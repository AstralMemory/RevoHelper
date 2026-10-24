namespace RevoHelper.Hardware.Windows.Discovery;

public static class OemControlPathResolver
{
    public static string? ResolveFromServiceImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var value = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        string executable;
        if (value.StartsWith('"'))
        {
            var closingQuote = value.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                return null;
            }
            executable = value[1..closingQuote];
        }
        else
        {
            var executableEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (executableEnd < 0)
            {
                return null;
            }
            executable = value[..(executableEnd + 4)];
        }

        if (!Path.IsPathFullyQualified(executable) ||
            !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var serviceDirectory = Path.GetDirectoryName(executable);
        return string.IsNullOrWhiteSpace(serviceDirectory)
            ? null
            : Path.Combine(serviceDirectory, "MyControlCenter");
    }
}
