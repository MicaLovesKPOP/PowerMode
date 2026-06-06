using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Mica.PowerModeTray.WinUI;

internal static class PowerModeLog
{
    private const string LoggingRegistryPath = @"Software\MicaLovesKPOP\PowerMode\Logging";
    private const string CleanLogLayoutInitializedValue = "CleanLogLayoutInitialized";
    private const string LegacyBackupPathValue = "LegacyBackupPath";

    private const int EventLogMaxBytes = 256 * 1024;
    private const int EventLogMaxFiles = 3;
    private const int DiagnosticLogMaxBytes = 512 * 1024;
    private const int DiagnosticLogMaxFiles = 5;
    private const int CrashLogMaxBytes = 256 * 1024;
    private const int CrashLogMaxFiles = 3;

    private static readonly object Sync = new();
    private static bool _initialized;

    internal static string SessionId { get; } = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Environment.ProcessId;

    private static string LocalAppDataDirectory => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    internal static string DataDirectory => Path.Combine(LocalAppDataDirectory, "PowerMode");
    internal static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    private static string EventLogPath => Path.Combine(LogsDirectory, "power-mode-events.log");
    private static string DiagnosticLogPath => Path.Combine(LogsDirectory, "power-mode-diagnostic.log");
    private static string CrashLogPath => Path.Combine(LogsDirectory, "power-mode-crash.log");

    internal static void InitializeCleanLogLayout()
    {
        lock (Sync)
        {
            if (_initialized) return;

            try
            {
                Directory.CreateDirectory(LocalAppDataDirectory);

                if (!IsCleanLogLayoutInitialized())
                {
                    string? backupPath = MigrateLegacyPowerModeDirectoryIfNeeded();
                    Directory.CreateDirectory(LogsDirectory);
                    MarkCleanLogLayoutInitialized(backupPath);
                }
                else
                {
                    Directory.CreateDirectory(LogsDirectory);
                }

                _initialized = true;
            }
            catch
            {
                try
                {
                    Directory.CreateDirectory(LogsDirectory);
                    _initialized = true;
                }
                catch { }
            }
        }
    }

    internal static void Event(string title, params string[] details)
    {
        try
        {
            InitializeCleanLogLayout();

            lock (Sync)
            {
                RotateFileIfNeeded(EventLogPath, EventLogMaxBytes, EventLogMaxFiles);

                DateTime now = DateTime.Now;
                string dateHeader = now.ToString("yyyy-MM-dd");
                string? lastDate = GetLastEventLogDate(EventLogPath);

                using var writer = new StreamWriter(EventLogPath, append: true);
                if (!string.Equals(lastDate, dateHeader, StringComparison.Ordinal))
                {
                    if (new FileInfo(EventLogPath).Length > 0) writer.WriteLine();
                    writer.WriteLine(dateHeader);
                }

                writer.Write(now.ToString("HH:mm:ss"));
                writer.Write("  ");
                writer.WriteLine((title ?? "").TrimEnd('.'));

                foreach (string detail in details.Where(d => !string.IsNullOrWhiteSpace(d)))
                {
                    writer.Write("          ");
                    writer.WriteLine(detail.Trim());
                }
            }
        }
        catch { }
    }

    internal static void Diagnostic(string component, string message) => Diagnostic("INFO", component, message);

    internal static void Warning(string component, string message) => Diagnostic("WARN", component, message);

    internal static void Error(string component, string message, Exception? exception = null)
    {
        string text = exception == null ? message : message + " " + exception;
        Diagnostic("ERROR", component, text);
    }

    internal static void Crash(string message, Exception? exception = null)
    {
        try
        {
            InitializeCleanLogLayout();

            lock (Sync)
            {
                RotateFileIfNeeded(CrashLogPath, CrashLogMaxBytes, CrashLogMaxFiles);
                using var writer = new StreamWriter(CrashLogPath, append: true);
                writer.Write('[');
                writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                writer.Write("] [FATAL] [Crash] [session=");
                writer.Write(SessionId);
                writer.Write("] ");
                writer.WriteLine(message);

                if (exception != null)
                {
                    writer.WriteLine(exception);
                }

                writer.WriteLine();
            }
        }
        catch { }
    }

    private static void Diagnostic(string level, string component, string message)
    {
        try
        {
            InitializeCleanLogLayout();

            lock (Sync)
            {
                RotateFileIfNeeded(DiagnosticLogPath, DiagnosticLogMaxBytes, DiagnosticLogMaxFiles);
                using var writer = new StreamWriter(DiagnosticLogPath, append: true);
                writer.Write('[');
                writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                writer.Write("] [");
                writer.Write(level);
                writer.Write("] [");
                writer.Write(string.IsNullOrWhiteSpace(component) ? "General" : component.Trim());
                writer.Write("] [session=");
                writer.Write(SessionId);
                writer.Write("] ");
                writer.WriteLine(message ?? "");
            }
        }
        catch { }
    }

    private static bool IsCleanLogLayoutInitialized()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(LoggingRegistryPath);
            object? value = key?.GetValue(CleanLogLayoutInitializedValue);
            return value is not null && Convert.ToInt32(value) != 0;
        }
        catch { return false; }
    }

    private static void MarkCleanLogLayoutInitialized(string? backupPath)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(LoggingRegistryPath);
            key.SetValue(CleanLogLayoutInitializedValue, 1, RegistryValueKind.DWord);
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                key.SetValue(LegacyBackupPathValue, backupPath, RegistryValueKind.String);
            }
        }
        catch { }
    }

    private static string? MigrateLegacyPowerModeDirectoryIfNeeded()
    {
        try
        {
            if (!Directory.Exists(DataDirectory)) return null;
            if (!ShouldBackupExistingPowerModeDirectory(DataDirectory)) return null;

            string backupPath = GetAvailableBackupPath(Path.Combine(LocalAppDataDirectory, "PowerMode.bak"));
            Directory.Move(DataDirectory, backupPath);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldBackupExistingPowerModeDirectory(string path)
    {
        try
        {
            var rootEntries = Directory.EnumerateFileSystemEntries(path).ToList();
            if (rootEntries.Count == 0) return false;

            foreach (string entry in rootEntries)
            {
                string name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    if (!string.Equals(name, "logs", StringComparison.OrdinalIgnoreCase)) return true;
                    if (DirectoryHasAnyEntries(entry)) return true;
                    continue;
                }

                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool DirectoryHasAnyEntries(string path)
    {
        try { return Directory.EnumerateFileSystemEntries(path).Any(); }
        catch { return true; }
    }

    private static string GetAvailableBackupPath(string preferredPath)
    {
        if (!Directory.Exists(preferredPath) && !File.Exists(preferredPath)) return preferredPath;

        for (int i = 2; i < 100; i++)
        {
            string candidate = preferredPath + "." + i;
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }

        return preferredPath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    private static void RotateFileIfNeeded(string path, int maxBytes, int maxFiles)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < maxBytes) return;

            string dir = file.DirectoryName!;
            string baseName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            string oldest = Path.Combine(dir, baseName + "." + (maxFiles - 1) + extension);
            if (File.Exists(oldest)) File.Delete(oldest);

            for (int i = maxFiles - 2; i >= 1; i--)
            {
                string source = Path.Combine(dir, baseName + "." + i + extension);
                string target = Path.Combine(dir, baseName + "." + (i + 1) + extension);
                if (File.Exists(source)) File.Move(source, target, overwrite: true);
            }

            string first = Path.Combine(dir, baseName + ".1" + extension);
            File.Move(path, first, overwrite: true);
        }
        catch { }
    }

    private static string? GetLastEventLogDate(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            foreach (string line in File.ReadLines(path).Reverse())
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 10 && trimmed[4] == '-' && trimmed[7] == '-') return trimmed;
            }
        }
        catch { }

        return null;
    }

    internal static string GetVersionText()
    {
        try
        {
            AssemblyInformationalVersionAttribute? info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (!string.IsNullOrWhiteSpace(info?.InformationalVersion)) return info.InformationalVersion;

            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "unknown";
        }
        catch { return "unknown"; }
    }
}
