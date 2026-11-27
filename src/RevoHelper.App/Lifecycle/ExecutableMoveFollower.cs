using System.IO;

namespace RevoHelper.App.Lifecycle;

/// <summary>
/// Keeps the logon task with this program file when the running file is moved (Windows allows
/// that on the same drive). It remembers every place the file has been seen at, and a task that
/// names one of them, now empty, may follow the file (see
/// <c>WindowsStartupRegistration.FollowMove</c>). So A to B to C and A to B and back to A are
/// both followed, whichever place the task was last pointed at.
/// </summary>
public sealed class ExecutableMoveFollower
{
    /// <summary>Tries for one place before it is left until the file moves again.</summary>
    public const int Attempts = 3;

    private readonly Func<string?> locate;
    private readonly Func<IReadOnlyCollection<string>, string, bool> follow;
    private readonly HashSet<string> places = new(StringComparer.OrdinalIgnoreCase);
    // The place where there was last nothing more to do: followed, or nothing to follow.
    private string? settled;
    private string? attempted;
    private int failures;

    /// <param name="started">Where this process started from; its task was settled at start.</param>
    /// <param name="locate">Where the file is now; null when that cannot be confirmed.</param>
    /// <param name="follow">
    /// Points a task that names one of the places at the place now: true when it did, false when
    /// there was nothing to do, an exception when Windows did not confirm it.
    /// </param>
    public ExecutableMoveFollower(string? started, Func<string?> locate,
        Func<IReadOnlyCollection<string>, string, bool> follow)
    {
        this.locate = locate;
        this.follow = follow;
        if (!string.IsNullOrWhiteSpace(started) && Path.IsPathFullyQualified(started))
            places.Add(settled = Path.GetFullPath(started));
    }

    /// <summary>One check. Returns a line for the log, or null when there is nothing to say.</summary>
    public string? Check()
    {
        if (locate() is not { } now || string.Equals(now, settled, StringComparison.OrdinalIgnoreCase)) return null;
        places.Add(now);
        if (!string.Equals(now, attempted, StringComparison.OrdinalIgnoreCase)) (attempted, failures) = (now, 0);
        try
        {
            var followed = follow(places, now);
            settled = now;
            return followed ? "logon task follows the moved program file" : null;
        }
        catch (Exception exception)
        {
            // Tried again at the next check, a few times, then left until the file moves again.
            if (++failures >= Attempts) settled = now;
            return $"follow-move {exception.GetType().Name}: {exception.Message}";
        }
    }
}
