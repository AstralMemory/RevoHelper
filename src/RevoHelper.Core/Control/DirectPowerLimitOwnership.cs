namespace RevoHelper.Core.Control;

/// <summary>
/// What this session left on a direct power-limit register and what the register held before
/// the session's first write (the root). The backend keeps one under its own lock and feeds it
/// the raw states it reads; nothing here touches hardware.
/// </summary>
/// <remarks>
/// <para>
/// Firmware may leave the register alone when the OEM mode changes: on the Core Ultra 9 275HX,
/// MSR 0x610 keeps the last value written until the next boot. A value read before a later
/// write can therefore be this session's own earlier write, so the root is carried across
/// writes for as long as the register still holds the state the session last left there.
/// Anything else changing it (firmware on a mode switch, another tool) ends that ownership and
/// the next write takes the new value as its root. Both firmware behaviours follow from the
/// readings; no model is singled out.
/// </para>
/// <para>
/// Only a state confirmed by readback authorizes a restore write over it. A write that landed
/// part-way (every field from either the state before it or the requested one) stays
/// unconfirmed under the same root: nothing is restored over it and it never becomes a root. A
/// field from neither means something else wrote, and the ownership ends. When nothing can be
/// read after a write, the ownership stays as it was; the requested state is never taken as
/// confirmed. Something else writing exactly the state this session left cannot be told apart
/// and counts as still owned.
/// </para>
/// </remarks>
/// <param name="sameState">Whether two raw states are equal in everything the backend controls.</param>
/// <param name="partOf">Whether a state (third) takes every controlled field from one of two others.</param>
public sealed class DirectPowerLimitOwnership<T>(Func<T, T, bool> sameState, Func<T, T, T, bool> partOf)
    where T : notnull
{
    private State? owned;

    public bool HasState => owned is not null;

    /// <summary>
    /// The root a write starting from <paramref name="current"/> would undo to. Ownership the
    /// register no longer reflects ends here.
    /// </summary>
    public T BeginWrite(T current)
    {
        if (owned is { } state && sameState(state.Last, current)) return state.Root;
        owned = null;
        return current;
    }

    /// <summary>A write from <paramref name="before"/> confirmed by readback.</summary>
    public void Confirmed(T root, T before, T written) => owned = new(root, written, true, before);

    /// <summary>
    /// A write that was not confirmed, from what a fresh read found afterwards: the state before
    /// it (nothing changed, or the backend put it back), the requested state (the write is on the
    /// register after all), a part-way write, or something else's value.
    /// </summary>
    public void WriteFailed(T root, T before, T requested, T after)
    {
        if (sameState(after, before)) return;
        if (sameState(after, requested)) Confirmed(root, before, after);
        else owned = partOf(before, requested, after) ? new(root, after, false, before) : null;
    }

    /// <summary>Who owns <paramref name="current"/>.</summary>
    public DirectPowerLimitClaim Claim(T current) => owned switch
    {
        null => DirectPowerLimitClaim.None,
        { } state when !sameState(state.Last, current) => DirectPowerLimitClaim.ChangedElsewhere,
        { Confirmed: true } => DirectPowerLimitClaim.Owned,
        _ => DirectPowerLimitClaim.Unconfirmed
    };

    /// <summary>
    /// Whether a restore point that captured <paramref name="original"/> may write over
    /// <paramref name="current"/>: only over a confirmed write that started from that very
    /// capture. A write that started from a value something else set after the capture belongs
    /// to a newer ownership the old restore point must not take over.
    /// </summary>
    public bool MayRestore(T original, T current) =>
        owned is { Confirmed: true } state && sameState(state.Last, current) && sameState(state.Before, original);

    public bool TryGetRoot(out T root)
    {
        root = owned is { } state ? state.Root : default!;
        return owned is not null;
    }

    /// <summary>
    /// A restore write over the owned <paramref name="before"/> that was not confirmed, from what
    /// a fresh read found: unchanged, the restore target after all (the register then belongs to
    /// <paramref name="restoring"/>, or to nobody for the session root), part-way, or something
    /// else's value.
    /// </summary>
    public void RestoreFailed(T before, T requested, T after, Snapshot? restoring)
    {
        if (sameState(after, before)) return;
        if (sameState(after, requested))
        {
            if (restoring is { } snapshot) Restored(snapshot, after);
            else owned = null;
            return;
        }
        owned = owned is { } state && partOf(before, requested, after)
            ? state with { Last = after, Confirmed = false }
            : null;
    }

    public void Clear() => owned = null;

    /// <summary>Ownership of <paramref name="current"/>, kept by a restore point.</summary>
    public Snapshot Capture(T current) =>
        new(owned is { } state && sameState(state.Last, current) ? state : null);

    /// <summary>
    /// A restore point wrote its state back and read it back: the register is owned again
    /// exactly as it was at capture, with the same root. Not for a restore point that found
    /// its state already there; that wrote nothing and the current ownership stands.
    /// </summary>
    public void Restored(Snapshot snapshot, T current) =>
        owned = snapshot.Owned is { } state ? state with { Last = current } : null;

    public readonly record struct Snapshot
    {
        internal Snapshot(State? owned) => Owned = owned;
        internal State? Owned { get; }
    }

    /// <param name="Before">What the write that left <paramref name="Last"/> replaced.</param>
    internal sealed record State(T Root, T Last, bool Confirmed, T Before);
}

public enum DirectPowerLimitClaim
{
    /// <summary>This session has not written, or its write was undone.</summary>
    None,
    /// <summary>The register holds the state this session confirmed.</summary>
    Owned,
    /// <summary>The register holds what a part-way write of this session left; nothing is restored over it.</summary>
    Unconfirmed,
    /// <summary>Something else changed the register after this session's write.</summary>
    ChangedElsewhere
}

/// <summary>A direct power-limit backend that can undo, at exit, what this session wrote.</summary>
public interface ICpuPolicySessionRoot
{
    /// <summary>
    /// Restores the value from before this session's first write when the register still holds
    /// the last state this session confirmed. An unconfirmed or externally changed register is
    /// kept as it is.
    /// </summary>
    ValueTask<ControlResult> RestoreSessionRootAsync(CancellationToken cancellationToken);
}
