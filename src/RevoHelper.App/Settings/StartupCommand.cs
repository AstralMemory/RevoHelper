using System.IO;

namespace RevoHelper.App.Settings;

public static class StartupCommand
{
    public const string StartupArgument = "--startup";

    public static string Build(string executablePath) =>
        $"\"{Path.GetFullPath(executablePath)}\" {StartupArgument}";

    public static bool IsStartupLaunch(IEnumerable<string> arguments) =>
        arguments.Any(argument =>
            string.Equals(argument, StartupArgument, StringComparison.OrdinalIgnoreCase));

    public static bool MatchesExecutable(string? registeredCommand, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(registeredCommand) || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var registeredPath = ExtractExecutablePath(registeredCommand);
        try
        {
            return Path.GetFullPath(registeredPath).Equals(
                Path.GetFullPath(executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string ExtractExecutablePath(string command)
    {
        var trimmed = command.Trim();
        if (!trimmed.StartsWith('"'))
        {
            return trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        }

        var closingQuote = trimmed.IndexOf('"', 1);
        return closingQuote > 1 ? trimmed[1..closingQuote] : trimmed.Trim('"');
    }
}
