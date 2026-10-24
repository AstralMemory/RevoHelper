namespace RevoHelper.Core.Monitoring;

/// <summary>Follows foreground applications, not a game-name list. Clicking RevoHelper retains the last target.</summary>
public sealed class FrameTargetSelection(int ownProcessId)
{
    public ProcessTarget? PinnedTarget { get; private set; }
    public ProcessTarget? CurrentTarget { get; private set; }

    public void SetPinnedTarget(ProcessTarget? target)
    {
        PinnedTarget = target?.ProcessId == ownProcessId ? null : target;
        // Discard data immediately when unpinning; the next foreground observation chooses a new target.
        CurrentTarget = PinnedTarget;
    }

    public ProcessTarget? Refresh(IProcessTargetProvider provider)
    {
        if (PinnedTarget is not null)
        {
            CurrentTarget = provider.IsAlive(PinnedTarget) ? PinnedTarget : null;
            return CurrentTarget;
        }

        var foreground = provider.GetForegroundTarget();
        if (foreground?.ProcessId == ownProcessId)
        {
            if (CurrentTarget is not null && !provider.IsAlive(CurrentTarget))
            {
                CurrentTarget = null;
            }
            return CurrentTarget;
        }

        CurrentTarget = foreground is not null && provider.IsAlive(foreground)
            ? foreground
            : null;
        return CurrentTarget;
    }
}
