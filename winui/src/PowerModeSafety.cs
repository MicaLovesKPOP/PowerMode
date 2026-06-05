using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mica.PowerModeTray.WinUI;

internal static class PowerModeSafety
{
    private const string AutomaticModeRegistryPath = @"Software\MicaLovesKPOP\PowerMode\AutomaticMode";
    private const string SafetyRegistryPath = @"Software\MicaLovesKPOP\PowerMode\Safety";
    private const string ValueAutomaticEnabled = "Enabled";
    private const string ValueAutomaticNormalProfile = "NormalProfile";
    private const string ValuePendingManualRestoreProfile = "PendingManualRestoreProfile";
    private const string ValuePendingSafeProfile = "PendingSafeProfile";
    private const string ValuePendingReason = "PendingReason";
    private const string ValuePendingSinceUtc = "PendingSinceUtc";

    private const string UnrestrainedPerformance = "Unrestrained Performance";
    private const string OptimizedPerformance = "Optimized Performance";
    private const string BalancedPerformance = "Balanced Performance";
    private const string CoolAndQuiet = "Cool & Quiet";
    private const string ExtremeEnergySaver = "Extreme Energy Saver";

    private const int MinimumPostLoginGraceSeconds = 20;
    private const int SettledStableSecondsRequired = 12;
    private const int SettledMaxWaitSeconds = 5 * 60;
    private const int SettledSampleIntervalMilliseconds = 2000;
    private const double SettledCpuThresholdPercent = 15.0;
    private const double SettledDiskThresholdPercent = 20.0;

    private static readonly string[] KnownProfiles =
    {
        UnrestrainedPerformance,
        OptimizedPerformance,
        BalancedPerformance,
        CoolAndQuiet,
        ExtremeEnergySaver,
    };

    private static bool _shutdownGuardRegistered;
    private static int _postLoginRestoreStarted;

    internal static void ApplyStartupGuard()
    {
        try
        {
            string active = GetActivePlanName();
            string? target = GetStartupGuardTarget(active);
            if (string.IsNullOrWhiteSpace(target)) return;

            bool pendingManualRestore = !IsAutomaticModeEnabled() && PlanNamesEquivalent(active, ExtremeEnergySaver);
            if (TrySetPlanByName(target, "Startup guard"))
            {
                if (pendingManualRestore)
                {
                    SavePendingManualRestore(ExtremeEnergySaver, target, "startup guard");
                    WriteDiagnostic($"Startup guard moved manual '{active}' to '{target}' and queued post-login restore.");
                }
                else
                {
                    ClearPendingManualRestore("startup guard used automatic normal profile");
                    WriteDiagnostic($"Startup guard moved active profile from '{active}' to '{target}'.");
                }
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Startup guard failed: " + ex.Message);
        }
    }

    internal static void RegisterShutdownGuard()
    {
        if (_shutdownGuardRegistered) return;
        _shutdownGuardRegistered = true;

        try
        {
            SystemEvents.SessionEnding += OnSessionEnding;
            WriteDiagnostic("Shutdown guard registered.");
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Shutdown guard registration failed: " + ex.Message);
        }
    }

    internal static void StartPostLoginManualRestoreMonitor()
    {
        PendingManualRestore? pending = LoadPendingManualRestore();
        if (pending == null) return;

        if (Interlocked.CompareExchange(ref _postLoginRestoreStarted, 1, 0) != 0)
        {
            WriteDiagnostic("Post-login manual restore monitor is already running.");
            return;
        }

        _ = Task.Run(() => MonitorAndRestoreManualExtremeEnergySaverAsync(pending.Value));
    }

    private static void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        try
        {
            string active = GetActivePlanName();
            string? target = GetShutdownGuardTarget(active);
            if (string.IsNullOrWhiteSpace(target)) return;

            bool pendingManualRestore = !IsAutomaticModeEnabled() && PlanNamesEquivalent(active, ExtremeEnergySaver);
            if (TrySetPlanByName(target, "Shutdown guard"))
            {
                if (pendingManualRestore)
                {
                    SavePendingManualRestore(ExtremeEnergySaver, target, "shutdown guard");
                    WriteDiagnostic($"Shutdown guard moved manual '{active}' to '{target}' before session end ({e.Reason}) and queued post-login restore.");

                    // If shutdown/restart continues, the app exits before this monitor can restore anything.
                    // If another app or Windows policy cancels shutdown, the live session remains on the
                    // safe profile, so start the same settled-system restore monitor for the surviving session.
                    StartPostLoginManualRestoreMonitor();
                }
                else
                {
                    ClearPendingManualRestore("shutdown guard used automatic normal profile");
                    WriteDiagnostic($"Shutdown guard moved active profile from '{active}' to '{target}' before session end ({e.Reason}).");
                }
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Shutdown guard failed: " + ex.Message);
        }
    }

    private static string? GetStartupGuardTarget(string activeProfile)
    {
        if (IsAutomaticModeEnabled())
        {
            string normalProfile = GetAutomaticNormalProfile();
            return GetProfileRank(activeProfile) > GetProfileRank(normalProfile) ? normalProfile : null;
        }

        return PlanNamesEquivalent(activeProfile, ExtremeEnergySaver) ? CoolAndQuiet : null;
    }

    private static string? GetShutdownGuardTarget(string activeProfile)
    {
        if (IsAutomaticModeEnabled())
        {
            string normalProfile = GetAutomaticNormalProfile();
            return PlanNamesEquivalent(activeProfile, normalProfile) ? null : normalProfile;
        }

        return PlanNamesEquivalent(activeProfile, ExtremeEnergySaver) ? CoolAndQuiet : null;
    }

    private static async Task MonitorAndRestoreManualExtremeEnergySaverAsync(PendingManualRestore pending)
    {
        Stopwatch total = Stopwatch.StartNew();
        Stopwatch stable = new();
        SystemSettledSampler? sampler = null;
        SystemSettledSample lastSample = SystemSettledSample.Unavailable;
        bool loggedSamplerUnavailable = false;

        WriteDiagnostic(
            "Post-login manual restore monitor started. " +
            $"Pending='{pending.Profile}', safe='{pending.SafeProfile}', reason='{pending.Reason}', " +
            $"thresholds=CPU<={SettledCpuThresholdPercent:0.#}%, Disk<={SettledDiskThresholdPercent:0.#}% for {SettledStableSecondsRequired}s, timeout={SettledMaxWaitSeconds}s.");

        try
        {
            sampler = SystemSettledSampler.TryCreate();
            if (sampler == null)
            {
                loggedSamplerUnavailable = true;
                WriteDiagnostic("Post-login manual restore monitor could not create CPU/disk counters; using timeout fallback only.");
            }

            while (total.Elapsed < TimeSpan.FromSeconds(SettledMaxWaitSeconds))
            {
                await Task.Delay(SettledSampleIntervalMilliseconds).ConfigureAwait(false);

                string active = GetActivePlanName();
                if (PlanNamesEquivalent(active, pending.Profile))
                {
                    ClearPendingManualRestore("manual profile already active");
                    WriteDiagnostic("Post-login manual restore monitor stopped because the pending profile is already active.");
                    return;
                }

                if (IsAutomaticModeEnabled())
                {
                    ClearPendingManualRestore("automatic mode enabled before restore");
                    WriteDiagnostic("Post-login manual restore monitor cancelled because Automatic Mode is enabled.");
                    return;
                }

                if (!PlanNamesEquivalent(active, pending.SafeProfile))
                {
                    ClearPendingManualRestore("active profile changed by user or another tool");
                    WriteDiagnostic($"Post-login manual restore monitor cancelled because active profile changed from expected safe profile '{pending.SafeProfile}' to '{active}'.");
                    return;
                }

                if (total.Elapsed < TimeSpan.FromSeconds(MinimumPostLoginGraceSeconds))
                {
                    continue;
                }

                if (sampler == null)
                {
                    continue;
                }

                lastSample = sampler.NextSample();
                if (!lastSample.HasAnyMetric)
                {
                    if (!loggedSamplerUnavailable)
                    {
                        loggedSamplerUnavailable = true;
                        WriteDiagnostic("Post-login manual restore monitor could not read CPU/disk samples; using timeout fallback only.");
                    }
                    continue;
                }

                bool settled = (!lastSample.CpuPercent.HasValue || lastSample.CpuPercent.Value <= SettledCpuThresholdPercent)
                    && (!lastSample.DiskPercent.HasValue || lastSample.DiskPercent.Value <= SettledDiskThresholdPercent);

                if (settled)
                {
                    if (!stable.IsRunning) stable.Start();
                    if (stable.Elapsed >= TimeSpan.FromSeconds(SettledStableSecondsRequired))
                    {
                        RestorePendingManualProfile(pending, "system settled", total.Elapsed, lastSample);
                        return;
                    }
                }
                else
                {
                    stable.Reset();
                }
            }

            RestorePendingManualProfile(pending, "timeout fallback", total.Elapsed, lastSample);
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Post-login manual restore monitor failed: " + ex.Message);
        }
        finally
        {
            sampler?.Dispose();
            Interlocked.Exchange(ref _postLoginRestoreStarted, 0);
        }
    }

    private static void RestorePendingManualProfile(PendingManualRestore pending, string trigger, TimeSpan elapsed, SystemSettledSample sample)
    {
        string active = GetActivePlanName();
        if (!PlanNamesEquivalent(active, pending.SafeProfile))
        {
            ClearPendingManualRestore("active profile changed before restore");
            WriteDiagnostic($"Post-login manual restore skipped because active profile changed from expected safe profile '{pending.SafeProfile}' to '{active}'.");
            return;
        }

        if (TrySetPlanByName(pending.Profile, "Post-login manual restore"))
        {
            ClearPendingManualRestore("restored");
            WriteDiagnostic(
                $"Post-login manual restore reapplied '{pending.Profile}' after {elapsed.TotalSeconds:0}s via {trigger}. " +
                $"Last sample: {sample.ToDiagnosticText()}.");
        }
    }

    private static bool IsAutomaticModeEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutomaticModeRegistryPath);
            object? value = key?.GetValue(ValueAutomaticEnabled);
            return value is not null && Convert.ToInt32(value) != 0;
        }
        catch { return false; }
    }

    private static string GetAutomaticNormalProfile()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutomaticModeRegistryPath);
            string? storedProfile = key?.GetValue(ValueAutomaticNormalProfile) as string;
            if (!string.IsNullOrWhiteSpace(storedProfile))
            {
                string displayName = ToKnownProfileDisplayName(storedProfile);
                if (!PlanNamesEquivalent(displayName, ExtremeEnergySaver)) return displayName;
            }
        }
        catch { }

        return OptimizedPerformance;
    }

    private static string GetActivePlanName()
    {
        SafetyPowerScheme? active = GetPowerSchemes().FirstOrDefault(x => x.Active);
        return active == null ? "Unknown" : ToKnownProfileDisplayName(active.Name);
    }

    private static bool TrySetPlanByName(string profileName, string source)
    {
        SafetyPowerScheme? scheme = GetPowerSchemes().FirstOrDefault(x => PlanNamesEquivalent(x.Name, profileName));
        if (scheme == null)
        {
            WriteDiagnostic($"{source}: could not find profile '{profileName}'.");
            return false;
        }

        RunResult first = RunHidden("powercfg.exe", "/setactive " + scheme.Guid);
        string activeAfterFirst = GetActivePlanName();
        if (!PlanNamesEquivalent(activeAfterFirst, profileName))
        {
            RunResult second = RunHidden("powercfg.exe", "/S " + scheme.Guid);
            string activeAfterSecond = GetActivePlanName();
            if (!PlanNamesEquivalent(activeAfterSecond, profileName))
            {
                WriteDiagnostic($"{source}: failed to switch to '{profileName}' ({scheme.Guid}). /setactive exit={first.ExitCode}, /S exit={second.ExitCode}. Active now: '{activeAfterSecond}'. Errors: {first.Error} {second.Error}");
                return false;
            }
        }

        SetNativePowerModeOverlayBestEffort(profileName);
        return true;
    }

    private static List<SafetyPowerScheme> GetPowerSchemes()
    {
        var list = new List<SafetyPowerScheme>();
        try
        {
            RunResult result = RunHidden("powercfg.exe", "/list");
            string output = result.Output + "\n" + result.Error;
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = Regex.Match(line, @"([0-9a-fA-F-]{36}).*\((.*?)\)(\s*\*)?");
                if (!match.Success) continue;

                list.Add(new SafetyPowerScheme
                {
                    Guid = match.Groups[1].Value,
                    Name = match.Groups[2].Value,
                    Active = line.Contains('*')
                });
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Could not read power schemes: " + ex.Message);
        }

        return list;
    }

    private static string ToKnownProfileDisplayName(string name)
    {
        foreach (string profile in KnownProfiles)
        {
            if (PlanNamesEquivalent(name, profile)) return profile;
        }

        return Regex.Replace(name ?? "", @"^Gaming PC\s*-\s*", "", RegexOptions.IgnoreCase).Trim();
    }

    private static int GetProfileRank(string profileName)
    {
        for (int i = 0; i < KnownProfiles.Length; i++)
        {
            if (PlanNamesEquivalent(KnownProfiles[i], profileName)) return i;
        }

        return KnownProfiles.Length - 1;
    }

    private static bool PlanNamesEquivalent(string a, string b)
        => string.Equals(NormalizePlanNameForCompare(a), NormalizePlanNameForCompare(b), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePlanNameForCompare(string name)
    {
        string normalized = (name ?? "").Trim().ToLowerInvariant();
        normalized = normalized.Replace("&&", "&").Replace("_", "");
        normalized = Regex.Replace(normalized, @"\bcool\s+and\s+quiet\b", "cool & quiet");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }

    private static void SavePendingManualRestore(string profile, string safeProfile, string reason)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(SafetyRegistryPath);
            key.SetValue(ValuePendingManualRestoreProfile, profile, RegistryValueKind.String);
            key.SetValue(ValuePendingSafeProfile, safeProfile, RegistryValueKind.String);
            key.SetValue(ValuePendingReason, reason, RegistryValueKind.String);
            key.SetValue(ValuePendingSinceUtc, DateTime.UtcNow.ToString("O"), RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Could not save pending manual restore: " + ex.Message);
        }
    }

    private static PendingManualRestore? LoadPendingManualRestore()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SafetyRegistryPath);
            string? profile = key?.GetValue(ValuePendingManualRestoreProfile) as string;
            string? safeProfile = key?.GetValue(ValuePendingSafeProfile) as string;
            string? reason = key?.GetValue(ValuePendingReason) as string;
            string? sinceText = key?.GetValue(ValuePendingSinceUtc) as string;

            if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(safeProfile)) return null;
            if (!PlanNamesEquivalent(profile, ExtremeEnergySaver)) return null;

            DateTime sinceUtc = DateTime.TryParse(sinceText, out DateTime parsed) ? parsed.ToUniversalTime() : DateTime.UtcNow;
            return new PendingManualRestore(ToKnownProfileDisplayName(profile), ToKnownProfileDisplayName(safeProfile), reason ?? "unknown", sinceUtc);
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Could not load pending manual restore: " + ex.Message);
            return null;
        }
    }

    private static void ClearPendingManualRestore(string reason)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SafetyRegistryPath, writable: true);
            key?.DeleteValue(ValuePendingManualRestoreProfile, false);
            key?.DeleteValue(ValuePendingSafeProfile, false);
            key?.DeleteValue(ValuePendingReason, false);
            key?.DeleteValue(ValuePendingSinceUtc, false);
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Could not clear pending manual restore after " + reason + ": " + ex.Message);
        }
    }

    private enum NativePowerModeOverlay
    {
        BestPowerEfficiency,
        Balanced,
        BestPerformance
    }

    private static void SetNativePowerModeOverlayBestEffort(string profileName)
    {
        NativePowerModeOverlay overlay = GetNativePowerModeOverlayForPlan(profileName);
        foreach (string candidate in GetNativePowerModeOverlayCandidates(overlay))
        {
            RunResult result = RunHidden("powercfg.exe", "/setactiveoverlay " + candidate);
            if (result.ExitCode == 0) return;
        }
    }

    private static NativePowerModeOverlay GetNativePowerModeOverlayForPlan(string profileName)
    {
        if (PlanNamesEquivalent(profileName, UnrestrainedPerformance) || PlanNamesEquivalent(profileName, OptimizedPerformance))
        {
            return NativePowerModeOverlay.BestPerformance;
        }

        if (PlanNamesEquivalent(profileName, ExtremeEnergySaver))
        {
            return NativePowerModeOverlay.BestPowerEfficiency;
        }

        return NativePowerModeOverlay.Balanced;
    }

    private static string[] GetNativePowerModeOverlayCandidates(NativePowerModeOverlay overlay)
        => overlay switch
        {
            NativePowerModeOverlay.BestPerformance => new[] { "ded574b5-45a0-4f42-8737-46345c09c238", "OVERLAY_SCHEME_HIGH" },
            NativePowerModeOverlay.BestPowerEfficiency => new[] { "961cc777-2547-4f9d-8174-7d86181b8a7a", "OVERLAY_SCHEME_LOW" },
            _ => new[] { "00000000-0000-0000-0000-000000000000", "OVERLAY_SCHEME_NONE" }
        };

    private static RunResult RunHidden(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(psi);
            if (process == null) return new RunResult(-1, "", "Process did not start.");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(); } catch { }
                return new RunResult(-2, output, "Timed out. " + error);
            }

            return new RunResult(process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return new RunResult(-1, "", ex.Message);
        }
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicaLovesKPOP", "PowerMode");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "PowerModeTray-diagnostic.log");
            TrimLogFileIfNeeded(logPath, maxBytes: 256 * 1024);
            File.AppendAllText(logPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
        }
        catch { }
    }

    private static void TrimLogFileIfNeeded(string logPath, int maxBytes)
    {
        try
        {
            var file = new FileInfo(logPath);
            if (!file.Exists || file.Length <= maxBytes) return;

            string text = File.ReadAllText(logPath);
            int keepChars = Math.Min(text.Length, maxBytes / 2);
            File.WriteAllText(logPath, text[^keepChars..]);
        }
        catch { }
    }

    private sealed class SystemSettledSampler : IDisposable
    {
        private readonly PdhQuery? _query;
        private readonly PdhCounter? _cpuCounter;
        private readonly PdhCounter? _diskCounter;

        private SystemSettledSampler(PdhQuery query, PdhCounter? cpuCounter, PdhCounter? diskCounter)
        {
            _query = query;
            _cpuCounter = cpuCounter;
            _diskCounter = diskCounter;
        }

        internal static SystemSettledSampler? TryCreate()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out IntPtr queryHandle) != 0 || queryHandle == IntPtr.Zero)
            {
                return null;
            }

            var query = new PdhQuery(queryHandle);
            PdhCounter? cpu = query.TryAddEnglishCounter(@"\Processor(_Total)\% Processor Time");
            PdhCounter? disk = query.TryAddEnglishCounter(@"\PhysicalDisk(_Total)\% Disk Time");

            if (cpu == null && disk == null)
            {
                query.Dispose();
                return null;
            }

            _ = PdhCollectQueryData(queryHandle);
            Thread.Sleep(250);
            _ = PdhCollectQueryData(queryHandle);
            return new SystemSettledSampler(query, cpu, disk);
        }

        internal SystemSettledSample NextSample()
        {
            if (_query == null) return SystemSettledSample.Unavailable;

            if (PdhCollectQueryData(_query.Handle) != 0)
            {
                return SystemSettledSample.Unavailable;
            }

            double? cpu = _cpuCounter?.ReadValue();
            double? disk = _diskCounter?.ReadValue();
            if (cpu.HasValue) cpu = Math.Clamp(cpu.Value, 0, 100);
            if (disk.HasValue) disk = Math.Max(0, disk.Value);
            return new SystemSettledSample(cpu, disk);
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _diskCounter?.Dispose();
            _query?.Dispose();
        }
    }

    private sealed class PdhQuery : IDisposable
    {
        internal IntPtr Handle { get; }
        private bool _disposed;

        internal PdhQuery(IntPtr handle) => Handle = handle;

        internal PdhCounter? TryAddEnglishCounter(string path)
        {
            int status = PdhAddEnglishCounter(Handle, path, IntPtr.Zero, out IntPtr counterHandle);
            return status == 0 && counterHandle != IntPtr.Zero ? new PdhCounter(counterHandle) : null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = PdhCloseQuery(Handle);
        }
    }

    private sealed class PdhCounter : IDisposable
    {
        private IntPtr _handle;

        internal PdhCounter(IntPtr handle) => _handle = handle;

        internal double? ReadValue()
        {
            if (_handle == IntPtr.Zero) return null;
            int status = PdhGetFormattedCounterValue(_handle, PdhFmtDouble, out _, out PdhFmtCounterValue value);
            if (status != 0 || value.CStatus != 0) return null;
            return value.DoubleValue;
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            _ = PdhRemoveCounter(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll")]
    private static extern int PdhRemoveCounter(IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);

    private const uint PdhFmtDouble = 0x00000200;

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    private sealed class SafetyPowerScheme
    {
        public string Guid { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Active { get; set; }
    }

    private readonly record struct PendingManualRestore(string Profile, string SafeProfile, string Reason, DateTime SinceUtc);

    private readonly record struct SystemSettledSample(double? CpuPercent, double? DiskPercent)
    {
        internal static readonly SystemSettledSample Unavailable = new(null, null);
        internal bool HasAnyMetric => CpuPercent.HasValue || DiskPercent.HasValue;

        internal string ToDiagnosticText()
        {
            string cpu = CpuPercent.HasValue ? CpuPercent.Value.ToString("0.0") + "%" : "unavailable";
            string disk = DiskPercent.HasValue ? DiskPercent.Value.ToString("0.0") + "%" : "unavailable";
            return "CPU=" + cpu + ", Disk=" + disk;
        }
    }

    private readonly record struct RunResult(int ExitCode, string Output, string Error);
}
