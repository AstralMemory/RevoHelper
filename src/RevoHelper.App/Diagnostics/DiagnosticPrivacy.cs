using System.Globalization;
using System.Text.RegularExpressions;

namespace RevoHelper.App.Diagnostics;

/// <summary>
/// Last pass over a report before anyone sees it. The report is built from model names, readings
/// and RevoHelper's own log lines; this catches what can still slip in through an exception
/// message: paths in any form, user, computer and domain names, e-mail and network addresses.
/// When in doubt it removes more, not less.
/// </summary>
public sealed class DiagnosticPrivacy
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options = RegexOptions.CultureInvariant;

    private static readonly Regex Email = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", Options, Timeout);
    // A stack frame's location: "in D:\any\folder, even O'Connor or 家、人\File.cs:line 88". It is
    // found by its ":line N" ending, so whatever the folders are called, only the file and line remain.
    private static readonly Regex StackLocation = new(
        @"(?:(?<![A-Za-z0-9])[A-Za-z]:[\\/]|\\\\)[^\r\n]*?[\\/](?<file>[^\\/\r\n]*?):(?:line|行号) (?<line>[0-9]+)", Options, Timeout);
    // A quoted stretch: exception messages quote the path they are about ('C:\Users\Jane Doe\a.json').
    private static readonly Regex Quoted = new(@"(['""“‘])([^'""“”‘’\r\n]{1,400}?)(['""”’])", Options, Timeout);
    private static readonly Regex PathStart = new(
        @"(?:%[A-Za-z]+%|\\\\\?\\|(?<![A-Za-z0-9])[A-Za-z]:(?=[\\/])|(?<![^\s'""(=:])\\\\(?=[^\\\s]))", Options, Timeout);
    // An unquoted path cannot tell where it ends (C:\Users\Jane Doe\a b.xlsx), so it runs to the end
    // of the clause: a quote, a Chinese or closing punctuation mark, the next stack frame or the line end.
    private static readonly Regex UnquotedPath = new(
        @"(?:%[A-Za-z]+%|\\\\\?\\|(?<![A-Za-z0-9])[A-Za-z]:(?=[\\/])|(?<![^\s'""(=:])\\\\(?=[^\\\s]))" +
        @"[^'""<>|\r\n，。；、！？]*?(?=$|['""<>|\r\n，。；、！？]|[,;](?:\s|$)| at | ---> |\.\s|\.$)",
        Options | RegexOptions.Multiline, Timeout);
    // Only RevoHelper's own files and its source files keep their names; any other name can be personal.
    private static readonly Regex KnownFile = new(
        @"^(?:(?:[A-Za-z][A-Za-z0-9]*\.)+(?:cs|xaml)(?::line [0-9]+)?|hold-log-[0-9]{8}-[0-9]{6}\.jsonl|" +
        @"runtime\.log(?:\.previous)?|settings\.json(?:\.bak)?|hold-status\.json|manual-frequency-lease\.json|" +
        @"gpu-power-budgets\.json|performance-monitor\.json|lease\.json|status\.json|journal\.jsonl|" +
        @"RevoHelper\.exe|nvidia-smi\.exe)$", Options, Timeout);
    private static readonly Regex SourceFile = new(@"^(?:[A-Za-z][A-Za-z0-9]*\.)+(?:cs|xaml)$", Options, Timeout);
    // No word boundary: Windows writes Bluetooth IDs as BluetoothLEe0:d4:…, letters right against the address.
    private static readonly Regex Mac = new(@"(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}", Options, Timeout);
    private static readonly Regex Ipv6 = new(@"(?<![0-9A-Za-z:])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?![0-9A-Za-z:])", Options, Timeout);
    // ASCII digits only: \d would also take full-width digits that int parsing does not accept.
    private static readonly Regex Ipv4 = new(@"(?<![0-9.])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![0-9.])", Options, Timeout);
    // Names are swapped for this before paths are looked at, so a path still reads as one piece.
    private const string NameMark = "\u0001";

    private readonly (string Value, Regex? Word)[] names;

    public DiagnosticPrivacy(string? userName, string? machineName, string? userDomain, string? profilePath)
    {
        // Longest first, so a computer name such as NAME-LAPTOP goes as a whole before the user name NAME.
        // Latin names count as whole words only, so a short name does not eat parts of other words;
        // a Chinese name has no word boundaries next to Chinese text, so it goes wherever it appears.
        names = new[] { userName, machineName, userDomain, LastFolder(profilePath) }
            .Where(name => !string.IsNullOrWhiteSpace(name) && name!.Trim().Length >= 2)
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(name => name.Length)
            .Select(name => (name, name.All(character => character < 0x80)
                ? new Regex($@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_])", Options | RegexOptions.IgnoreCase, Timeout)
                : null))
            .ToArray();
    }

    public static DiagnosticPrivacy ForCurrentUser() => new(
        Environment.UserName,
        Environment.MachineName,
        Environment.UserDomainName,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            text = text.Replace(NameMark, "", StringComparison.Ordinal);
            // Whole addresses first: a domain name inside one must not break it up before it is found.
            text = Email.Replace(text, "<邮箱>");
            foreach (var (value, word) in names)
                text = word is null ? text.Replace(value, NameMark, StringComparison.OrdinalIgnoreCase) : word.Replace(text, NameMark);
            text = StackLocation.Replace(text, match =>
                (SourceFile.IsMatch(match.Groups["file"].Value) ? @"<路径>\" + match.Groups["file"].Value : "<路径>") + ":line " + match.Groups["line"].Value);
            text = Quoted.Replace(text, match => PathStart.IsMatch(match.Groups[2].Value) || match.Groups[2].Value.Contains('\\')
                ? match.Groups[1].Value + KeepFileName(match.Groups[2].Value) + match.Groups[3].Value
                : match.Value);
            text = UnquotedPath.Replace(text, match => KeepFileName(match.Value));
            text = Mac.Replace(text, "<MAC>");
            text = Ipv6.Replace(text, match => LooksLikeIpv6(match.Value) ? "<IP>" : match.Value);
            text = Ipv4.Replace(text, match => LooksLikeIpv4(match.Value) ? "<IP>" : match.Value);
            return text.Replace(NameMark, "<已隐藏>", StringComparison.Ordinal);
        }
        catch (RegexMatchTimeoutException)
        {
            // Never hand out text that could not be checked.
            return "（这段内容无法完成隐私检查，已省略）";
        }
    }

    // "C:\Users\name\AppData\Local\RevoHelper\settings.json" → "<路径>\settings.json"; every other
    // name, and every folder, goes: "D:\Cases\Jane.Doe.json" → "<路径>".
    private static string KeepFileName(string path)
    {
        var trimmed = path.TrimEnd(' ', '.', ',', ';', ')', '）');
        var tail = path[trimmed.Length..];
        var name = trimmed.Split('\\', '/').LastOrDefault(part => part.Length > 0) ?? "";
        return (name.Contains(NameMark, StringComparison.Ordinal) || !KnownFile.IsMatch(name) ? "<路径>" : @"<路径>\" + name) + tail;
    }

    private static string? LastFolder(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault();

    // Clock times such as 23:25:03 have two colons and no "::"; an IPv6 address has "::" or at least four.
    private static bool LooksLikeIpv6(string value) =>
        value.Any(char.IsAsciiHexDigit) && (value.Contains("::", StringComparison.Ordinal) || value.Count(character => character == ':') >= 4);

    private static bool LooksLikeIpv4(string value) =>
        value.Split('.').All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number <= 255);
}
