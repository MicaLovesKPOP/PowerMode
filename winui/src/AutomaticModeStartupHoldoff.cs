using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Forms = System.Windows.Forms;

namespace Mica.PowerModeTray.WinUI;

internal sealed class AutomaticModeStartupHoldoff : IDisposable
{
    private const string ExtremeEnergySaver = "Extreme Energy Saver";
    private const string OptimizedPerformance = "Optimized Performance";

    private const int MinimumStartupGraceSeconds = 30;
    private const int SettledStableSecondsRequired = 12;
    private const int CounterUnavailableFallbackSeconds = 120;
    private const int SettledMaxWaitSeconds = 5 * 60;
    private const double SettledCpuThresholdPercent = 15.0;

    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly PowerModeTrayApplication _app;
    private readonly Type _appType;
    private readonly Forms.Timer _gateTimer;
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly Stopwatch _stable = new();
    private SystemSettledSampler? _sampler;
    private SystemSettledSample _lastSample = SystemSettledSample.Unavailable;
    private bool _disposed;
    private bool _released;
    private bool _sessionEnding;
    private bool _active;
    private bool _loggedHold;
    private bool _loggedCounterFailure;
    private bool _loggedAwayBlocked;

    private AutomaticModeStartupHoldoff(PowerModeTrayApplication app)
    {
        _app = app;
        _appType = app.GetType();

        _gateTimer = new Forms.Timer { Interval = 1000 };
        _gateTimer.Tick += (_, _) => Tick();

        try
        {
            SystemEvents.SessionEnding += OnSessionEnding;
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup gate could not register session ending handler: " + ex.Message);
        }

        TryStart();
    }

    internal static AutomaticModeStartupHoldoff Attach(PowerModeTrayApplication app) => new(app);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
        try { _gateTimer.Stop(); _gateTimer.Dispose(); } catch { }
        try { _sampler?.Dispose(); } catch { }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        _sessionEnding = true;
        Dispose();
    }

    private void TryStart()
    {
        try
        {
            if (!GetBoolField("_automaticModeEnabled")) return;

            string awayProfile = GetStringField("_automaticAfkProfile");
            if (!PowerModeTrayApplication.PlanNamesEquivalent(awayProfile, ExtremeEnergySaver)) return;

            _active = true;
            StopBuiltInAutomaticTimer();
            HoldUsingPcProfile("startup gate initial hold");

            _sampler = SystemSettledSampler.TryCreate();
            if (_sampler == null)
            {
                _loggedCounterFailure = true;
                WriteDiagnostic("Automatic Mode EES startup gate could not create CPU counter; using fallback timeout only.");
            }

            _gateTimer.Start();

            WriteDiagnostic(
                "Automatic Mode EES startup gate started. " +
                $"minimumGrace={MinimumStartupGraceSeconds}s, CPU<={SettledCpuThresholdPercent:0.#}% for {SettledStableSecondsRequired}s, counterFallback={CounterUnavailableFallbackSeconds}s, maxWait={SettledMaxWaitSeconds}s.");
            LogAutomaticMode("Automatic Mode EES startup gate started",
                "Away profile is Extreme Energy Saver.",
                "Waiting for startup CPU activity to calm before allowing Away mode.");
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup gate failed to start: " + ex.Message);
            Release(runAutomaticEvaluation: true, reason: "startup failure");
        }
    }

    private void Tick()
    {
        if (_disposed || _sessionEnding) return;

        try
        {
            if (!GetBoolField("_automaticModeEnabled"))
            {
                Release(runAutomaticEvaluation: false, reason: "Automatic Mode disabled");
                return;
            }

            string awayProfile = GetStringField("_automaticAfkProfile");
            if (!PowerModeTrayApplication.PlanNamesEquivalent(awayProfile, ExtremeEnergySaver))
            {
                Release(runAutomaticEvaluation: true, reason: "Away profile no longer EES");
                return;
            }

            bool wantsEes = AutomaticModeCurrentlyWantsExtremeEnergySaver();
            bool gateReleased = ShouldReleaseGate();

            if (gateReleased)
            {
                Release(runAutomaticEvaluation: true, reason: GetReleaseReason());
                return;
            }

            if (wantsEes)
            {
                HoldUsingPcProfile("startup/system load has not settled");
                if (!_loggedAwayBlocked)
                {
                    _loggedAwayBlocked = true;
                    WriteDiagnostic("Automatic Mode wanted Away/EES, but the startup gate is still waiting for CPU activity to calm.");
                    LogAutomaticMode("Automatic Mode Away delayed",
                        "Reason: waiting for startup CPU activity to calm.",
                        "Holding Using PC profile.");
                }
            }
            else
            {
                InvokeInstance("EvaluateAutomaticMode", false);
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup gate failed and released itself: " + ex.Message);
            Release(runAutomaticEvaluation: true, reason: "gate failure");
        }
    }

    private bool AutomaticModeCurrentlyWantsExtremeEnergySaver()
    {
        int delaySeconds = Math.Max(1, GetIntField("_automaticAfkDelaySeconds"));
        long idleMs = InvokeStaticLong("GetIdleMilliseconds");
        if (idleMs < delaySeconds * 1000L) return false;
        if (IsForegroundFullscreenAppActive()) return false;
        return true;
    }

    private bool ShouldReleaseGate()
    {
        if (_released) return true;
        if (_total.Elapsed >= TimeSpan.FromSeconds(SettledMaxWaitSeconds)) return true;
        if (_sampler == null && _total.Elapsed >= TimeSpan.FromSeconds(CounterUnavailableFallbackSeconds)) return true;
        if (_total.Elapsed < TimeSpan.FromSeconds(MinimumStartupGraceSeconds)) return false;
        if (_sampler == null) return false;

        _lastSample = _sampler.NextSample();
        if (!_lastSample.CpuPercent.HasValue)
        {
            if (!_loggedCounterFailure)
            {
                _loggedCounterFailure = true;
                WriteDiagnostic("Automatic Mode EES startup gate could not read CPU samples; using fallback timeout only.");
            }
            return _total.Elapsed >= TimeSpan.FromSeconds(CounterUnavailableFallbackSeconds);
        }

        if (_lastSample.CpuPercent.Value > SettledCpuThresholdPercent)
        {
            _stable.Reset();
            return false;
        }

        if (!_stable.IsRunning) _stable.Start();
        return _stable.Elapsed >= TimeSpan.FromSeconds(SettledStableSecondsRequired);
    }

    private string GetReleaseReason()
    {
        if (_total.Elapsed >= TimeSpan.FromSeconds(SettledMaxWaitSeconds)) return "max wait fallback";
        if (_sampler == null || !_lastSample.CpuPercent.HasValue) return "counter fallback";
        return "CPU settled";
    }

    private void Release(bool runAutomaticEvaluation, string reason)
    {
        if (_released) return;
        _released = true;

        try
        {
            _gateTimer.Stop();
            RestartBuiltInAutomaticTimer();

            WriteDiagnostic(
                $"Automatic Mode EES startup gate released after {_total.Elapsed.TotalSeconds:0}s via {reason}. " +
                $"Last sample: {_lastSample.ToDiagnosticText()}.");
            LogAutomaticMode("Automatic Mode EES startup gate released",
                "Reason: " + reason + ".",
                "Last sample: " + _lastSample.ToDiagnosticText() + ".");

            if (!_sessionEnding && runAutomaticEvaluation)
            {
                InvokeInstance("EvaluateAutomaticMode", false);
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup gate release failed: " + ex.Message);
        }
        finally
        {
            Dispose();
        }
    }

    private void StopBuiltInAutomaticTimer()
    {
        try
        {
            if (GetField("_automaticTimer") is Forms.Timer timer)
            {
                timer.Stop();
                WriteDiagnostic("Automatic Mode EES startup gate paused built-in Automatic Mode timer.");
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup gate could not pause built-in Automatic Mode timer: " + ex.Message);
        }
    }

    private void RestartBuiltInAutomaticTimer()
    {
        if (_sessionEnding) return;

        try
        {
            if (GetField("_automaticTimer") is Forms.Timer timer)
            {
                timer.Start();
                WriteDiagnostic("Automatic Mode EES startup gate restarted built-in Automatic Mode timer.");
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup gate could not restart built-in Automatic Mode timer: " + ex.Message);
        }
    }

    private void HoldUsingPcProfile(string reason)
    {
        string normalProfile = GetStringField("_automaticNormalProfile");
        if (string.IsNullOrWhiteSpace(normalProfile) || PowerModeTrayApplication.PlanNamesEquivalent(normalProfile, ExtremeEnergySaver))
        {
            normalProfile = OptimizedPerformance;
        }

        string active = GetActivePlanName();
        if (PowerModeTrayApplication.PlanNamesEquivalent(active, ExtremeEnergySaver) && !_sessionEnding)
        {
            TrySetPlanByName(normalProfile, "Automatic Mode EES startup gate");
        }

        SetField("_automaticStateText", "Active");
        SetField("_automaticReason", "Waiting for startup CPU activity to calm before Away mode.");
        InvokeInstance("RecordAutomaticModeState", "Active", normalProfile, false, false, false, false, false, false);
        InvokeInstance("UpdateTrayState", true);
        RefreshFlyoutBestEffort();

        if (!_loggedHold)
        {
            _loggedHold = true;
            WriteDiagnostic("Automatic Mode EES startup gate holding Using PC profile. Reason: " + reason + ".");
        }
    }

    private bool IsForegroundFullscreenAppActive()
    {
        try
        {
            MethodInfo? method = _appType.GetMethod("TryGetForegroundFullscreenAppDescription", StaticFlags);
            if (method == null) return false;
            object?[] args = { "fullscreen app" };
            return method.Invoke(null, args) is bool result && result;
        }
        catch { return false; }
    }

    private static string GetActivePlanName()
    {
        SafetyPowerScheme? active = GetPowerSchemes().FirstOrDefault(x => x.Active);
        return active == null ? "Unknown" : ToKnownProfileDisplayName(active.Name);
    }

    private static bool TrySetPlanByName(string profileName, string source)
    {
        SafetyPowerScheme? scheme = GetPowerSchemes().FirstOrDefault(x => PowerModeTrayApplication.PlanNamesEquivalent(x.Name, profileName));
        if (scheme == null)
        {
            WriteDiagnostic($"{source}: could not find profile '{profileName}'.");
            return false;
        }

        RunResult first = RunHidden("powercfg.exe", "/setactive " + scheme.Guid);
        string activeAfterFirst = GetActivePlanName();
        if (!PowerModeTrayApplication.PlanNamesEquivalent(activeAfterFirst, profileName))
        {
            RunResult second = RunHidden("powercfg.exe", "/S " + scheme.Guid);
            string activeAfterSecond = GetActivePlanName();
            if (!PowerModeTrayApplication.PlanNamesEquivalent(activeAfterSecond, profileName))
            {
                WriteDiagnostic($"{source}: failed to switch to '{profileName}' ({scheme.Guid}). /setactive exit={first.ExitCode}, /S exit={second.ExitCode}. Active now: '{activeAfterSecond}'. Errors: {first.Error} {second.Error}");
                return false;
            }
        }

        return true;
    }

    private static string ToKnownProfileDisplayName(string name)
    {
        string cleaned = Regex.Replace(name ?? "", @"^Gaming PC\s*-\s*", "", RegexOptions.IgnoreCase).Trim();
        if (PowerModeTrayApplication.PlanNamesEquivalent(cleaned, "Cool & Quiet")) return "Cool & Quiet";
        if (PowerModeTrayApplication.PlanNamesEquivalent(cleaned, ExtremeEnergySaver)) return ExtremeEnergySaver;
        if (PowerModeTrayApplication.PlanNamesEquivalent(cleaned, OptimizedPerformance)) return OptimizedPerformance;
        return cleaned;
    }

    private static System.Collections.Generic.List<SafetyPowerScheme> GetPowerSchemes()
    {
        var list = new System.Collections.Generic.List<SafetyPowerScheme>();
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
            WriteDiagnostic("Automatic Mode EES startup gate could not read power schemes: " + ex.Message);
        }

        return list;
    }

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

    private object? GetField(string name) => _appType.GetField(name, InstanceFlags)?.GetValue(_app);

    private void SetField(string name, object value) => _appType.GetField(name, InstanceFlags)?.SetValue(_app, value);

    private bool GetBoolField(string name) => GetField(name) is bool value && value;

    private int GetIntField(string name) => GetField(name) is int value ? value : 0;

    private string GetStringField(string name) => GetField(name) as string ?? "";

    private object? InvokeInstance(string methodName, params object?[] args)
        => _appType.GetMethod(methodName, InstanceFlags)?.Invoke(_app, args);

    private long InvokeStaticLong(string methodName)
    {
        try { return _appType.GetMethod(methodName, StaticFlags)?.Invoke(null, null) is long value ? value : 0; }
        catch { return 0; }
    }

    private void RefreshFlyoutBestEffort()
    {
        try
        {
            object? flyout = GetField("_flyout");
            flyout?.GetType().GetMethod("RefreshContent", InstanceFlags)?.Invoke(flyout, null);
        }
        catch { }
    }

    private void LogAutomaticMode(string title, params string[] details)
    {
        try
        {
            MethodInfo? method = _appType.GetMethod("AutomaticModeLog", StaticFlags);
            method?.Invoke(null, new object?[] { title, details });
        }
        catch { }
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicaLovesKPOP", "PowerMode");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "PowerModeTray-diagnostic.log");
            File.AppendAllText(logPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
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

            if (cpu == null)
            {
                query.Dispose();
                return null;
            }

            _ = PdhCollectQueryData(queryHandle);
            System.Threading.Thread.Sleep(250);
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

    private readonly record struct RunResult(int ExitCode, string Output, string Error);

    private readonly record struct SystemSettledSample(double? CpuPercent, double? DiskPercent)
    {
        internal static readonly SystemSettledSample Unavailable = new(null, null);

        internal string ToDiagnosticText()
        {
            string cpu = CpuPercent.HasValue ? CpuPercent.Value.ToString("0.0") + "%" : "unavailable";
            string disk = DiskPercent.HasValue ? DiskPercent.Value.ToString("0.0") + "%" : "unavailable";
            return "CPU=" + cpu + ", Disk=" + disk;
        }
    }
}
