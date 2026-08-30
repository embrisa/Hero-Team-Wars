using System.Text;
using System.Text.RegularExpressions;

namespace Wc3MapEngine.Core.Scripts;

/// <summary>
/// The first release keeps GUI/custom-text/JASS ownership with World Editor.
/// This class contains only conservative, read-only checks used by the build
/// validator; it deliberately does not compile or inject script source.
/// </summary>
public static class ScriptOwnership
{
    public const string Strategy = "editor_owned_gui_custom_text";
    public const string Version = "1.0";

    private static readonly Regex TriggerString = new(
        @"STRING\s+(?<id>\d+)\s*\{(?<value>[\s\S]*?)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JassMain = new(
        @"\bfunction\s+main\s+takes\s+nothing\s+returns\s+nothing\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LuaMain = new(
        @"\bfunction\s+main\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string> ParseTriggerStrings(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TriggerString.Matches(text))
        {
            var token = $"TRIGSTR_{match.Groups["id"].Value.PadLeft(3, '0')}";
            if (!values.TryAdd(token, match.Groups["value"].Value.Trim()))
            {
                throw new InvalidDataException($"Duplicate trigger string '{token}'.");
            }
        }

        var starts = Regex.Matches(text, @"\bSTRING\s+\d+\s*\{", RegexOptions.CultureInvariant).Count;
        if (starts != values.Count)
        {
            throw new InvalidDataException("One or more trigger-string blocks are malformed or unterminated.");
        }

        return values;
    }

    public static bool HasEntryPoint(string archivePath, byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return archivePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? LuaMain.IsMatch(text)
            : JassMain.IsMatch(text);
    }

    public static string DescribeLanguage(string archivePath)
        => archivePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? "Lua" : "Jass";
}
