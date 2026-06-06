using System.Collections.Generic;
using System.Text;

namespace Mica.PowerModeTray.WinUI;

internal static class LegacyLogFile
{
    internal static bool Exists(string path) => global::System.IO.File.Exists(path);

    internal static void Delete(string path) => global::System.IO.File.Delete(path);

    internal static string ReadAllText(string path) => global::System.IO.File.ReadAllText(path);

    internal static string ReadAllText(string path, Encoding encoding) => global::System.IO.File.ReadAllText(path, encoding);

    internal static IEnumerable<string> ReadLines(string path) => global::System.IO.File.ReadLines(path);

    internal static IEnumerable<string> ReadLines(string path, Encoding encoding) => global::System.IO.File.ReadLines(path, encoding);

    internal static void WriteAllText(string path, string? contents)
    {
        if (TryRedirectLegacyRootLogWrite(path, contents ?? string.Empty)) return;
        global::System.IO.File.WriteAllText(path, contents);
    }

    internal static void WriteAllText(string path, string? contents, Encoding encoding)
    {
        if (TryRedirectLegacyRootLogWrite(path, contents ?? string.Empty)) return;
        global::System.IO.File.WriteAllText(path, contents, encoding);
    }

    internal static void AppendAllText(string path, string? contents)
    {
        if (TryRedirectLegacyRootLogWrite(path, contents ?? string.Empty)) return;
        global::System.IO.File.AppendAllText(path, contents);
    }

    internal static void AppendAllText(string path, string? contents, Encoding encoding)
    {
        if (TryRedirectLegacyRootLogWrite(path, contents ?? string.Empty)) return;
        global::System.IO.File.AppendAllText(path, contents, encoding);
    }

    internal static void Copy(string sourceFileName, string destFileName)
        => global::System.IO.File.Copy(sourceFileName, destFileName);

    internal static void Copy(string sourceFileName, string destFileName, bool overwrite)
        => global::System.IO.File.Copy(sourceFileName, destFileName, overwrite);

    internal static void Move(string sourceFileName, string destFileName)
        => global::System.IO.File.Move(sourceFileName, destFileName);

    internal static void Move(string sourceFileName, string destFileName, bool overwrite)
        => global::System.IO.File.Move(sourceFileName, destFileName, overwrite);

    private static bool TryRedirectLegacyRootLogWrite(string path, string contents)
    {
        string fileName;
        try
        {
            fileName = global::System.IO.Path.GetFileName(path);
        }
        catch
        {
            return false;
        }

        string message = NormalizeLegacyLogMessage(contents);

        if (string.Equals(fileName, "PowerModeTray.log", System.StringComparison.OrdinalIgnoreCase))
        {
            PowerModeLog.Diagnostic("Tray", message);
            return true;
        }

        if (string.Equals(fileName, "PowerModeTray-diagnostic.log", System.StringComparison.OrdinalIgnoreCase))
        {
            PowerModeLog.Diagnostic("Safety", message);
            return true;
        }

        if (string.Equals(fileName, "PowerModeTray-crash.log", System.StringComparison.OrdinalIgnoreCase))
        {
            PowerModeLog.Crash(message);
            return true;
        }

        return false;
    }

    private static string NormalizeLegacyLogMessage(string contents)
    {
        string text = (contents ?? string.Empty).Trim();
        if (text.Length == 0) return "Legacy log entry.";

        if (text.Length > 22 && text[0] == '[' && text[20] == ']' && text[21] == ' ')
        {
            return text[22..].Trim();
        }

        return text;
    }
}
