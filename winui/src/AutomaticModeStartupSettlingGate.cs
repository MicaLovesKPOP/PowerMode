using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Mica.PowerModeTray.WinUI;

internal sealed class AutomaticModeStartupSettlingGate : IDisposable
{
    private const string ExtremeEnergySaver = "Extreme Energy Saver";
    private const string OptimizedPerformance = "Optimized Performance";

    private const int MinimumStartupGraceSeconds = 30;
    private const int SettledStableSecondsRequired = 12;
    private const int SettledMaxWaitSeconds = 5 * 60;
    private const double SettledCpuThresholdPercent = 15.0;
    private const double SettledDiskThresholdPercent = 20.0;

    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly PowerModeTrayApplication _app;
    private readonly Type _appType;
    private readonly Forms.Timer _timer;
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly Stopwatch _stable = new();
    private SystemSettledSampler? _sampler;
    private SystemSettledSample _lastSample = SystemSettledSample.Unavailable;
    private bool _released;
    private bool _disposed;
    private bool _loggedHold;
    private bool _loggedCounterFailure;

    private AutomaticModeStartupSettlingGate(PowerModeTrayApplication app)
    {
        _app = app;
        _appType = app.GetType();

        StopBuiltInAutomaticTimer();

        _sampler = SystemSettledSampler.TryCreate();
        if (_sampler == null)
        {
            _loggedCounterFailure = true;
            WriteDiagnostic("Automatic Mode EES startup gate could not create CPU/disk counters; using timeout fallback only.");
        }

        _timer = new Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
        WriteDiagnostic(
            "Automatic Mode EES startup gate started. " +
            $"minimumGrace={MinimumStartupGraceSeconds}s, thresholds=CPU<={SettledCpuThresholdPercent:0.#}%, Disk<={SettledDiskThresholdPercent:0.#}% for {SettledStableSecondsRequired}s, timeout={SettledMaxWaitSeconds}s.");
    }

    internal static AutomaticModeStartupSettlingGate Attach(PowerModeTrayApplication app) => new(app);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer.Stop(); _timer.Dispose(); } catch { }
        try { _sampler?.Dispose(); } catch { }
    }

    private void Tick()
    {
        if (_disposed) return;

        try
        {
            if (ShouldReleaseGate())
            {
                ReleaseGateIfNeeded();
                InvokeEvaluateAutomaticMode(force: false);
                return;
            }

            if (!ShouldHoldAutomaticExtremeEnergySaver())
            {
                InvokeEvaluateAutomaticMode(force: false);
                return;
            }

            HoldUsingPcProfileUntilSettled();
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup gate failed and released itself: " + ex.Message);
            ReleaseGateIfNeeded();
            InvokeEvaluateAutomaticMode(force: false);
        }
    }

    private bool ShouldReleaseGate()
    {
        if (_released) return true;
        if (_total.Elapsed >= TimeSpan.FromSeconds(SettledMaxWaitSeconds)) return true;
        if (_total.Elapsed < TimeSpan.FromSeconds(MinimumStartupGraceSeconds)) return false;

        if (_sampler == null) return false;

        _lastSample = _sampler.NextSample();
        if (!_lastSample.HasAnyMetric)
        {
            if (!_loggedCounterFailure)
            {
                _loggedCounterFailure = true;
                WriteDiagnostic("Automatic Mode EES startup gate could not read CPU/disk samples; using timeout fallback only.");
            }
            return false;
        }

        bool settled = (!_lastSample.CpuPercent.HasValue || _lastSample.CpuPercent.Value <= SettledCpuThresholdPercent)
            && (!_lastSample.DiskPercent.HasValue || _lastSample.DiskPercent.Value <= SettledDiskThresholdPercent);

        if (!settled)
        {
            _stable.Reset();
            return false;
        }

        if (!_stable.IsRunning) _stable.Start();
        return _stable.Elapsed >= TimeSpan.FromSeconds(SettledStableSecondsRequired);
    }

    private void ReleaseGateIfNeeded()
    {
        if (_released) return;
        _released = true;

        string reason = _total.Elapsed >= TimeSpan.FromSeconds(SettledMaxWaitSeconds)
            ? "timeout fallback"
            : "system settled";

        WriteDiagnostic(
            $"Automatic Mode EES startup gate released after {_total.Elapsed.TotalSeconds:0}s via {reason}. " +
            $"Last sample: {_lastSample.ToDiagnosticText()}.");

        LogAutomaticMode("Automatic Mode EES startup gate released",
            "Reason: " + reason + ".",
            "Last sample: " + _lastSample.ToDiagnosticText() + ".");
    }

    private bool ShouldHoldAutomaticExtremeEnergySaver()
    {
        if (!GetBoolField("_automaticModeEnabled")) return false;

        string awayProfile = GetStringField("_automaticAfkProfile");
        if (!PowerModeTrayApplication.PlanNamesEquivalent(awayProfile, ExtremeEnergySaver)) return false;

        int delaySeconds = Math.Max(1, GetIntField("_automaticAfkDelaySeconds"));
        long idleMs = InvokeStaticLong("GetIdleMilliseconds");
        if (idleMs < delaySeconds * 1000L) return false;

        if (IsForegroundFullscreenAppActive()) return false;
        return true;
    }

    private void HoldUsingPcProfileUntilSettled()
    {
        string normalProfile = GetStringField("_automaticNormalProfile");
        if (string.IsNullOrWhiteSpace(normalProfile) || PowerModeTrayApplication.PlanNamesEquivalent(normalProfile, ExtremeEnergySaver))
        {
            normalProfile = OptimizedPerformance;
        }

        string active = InvokeStaticString("GetActivePlanName");
        if (!PowerModeTrayApplication.PlanNamesEquivalent(active, normalProfile))
        {
            InvokeInstance("ApplyPlan", normalProfile, false, "Automatic Mode startup settling gate");
        }

        SetField("_automaticStateText", "Active");
        SetField("_automaticReason", "Waiting for system to settle before Away mode.");
        InvokeInstance("RecordAutomaticModeState", "Active", normalProfile, false, false, false, false, false, false);
        InvokeInstance("UpdateTrayState", true);
        RefreshFlyoutBestEffort();

        if (!_loggedHold)
        {
            _loggedHold = true;
            WriteDiagnostic("Automatic Mode wanted to enter Extreme Energy Saver, but startup/system load has not settled yet. Holding Using PC profile.");
            LogAutomaticMode("Automatic Mode Away delayed",
                "Reason: waiting for CPU/disk activity to settle after startup.",
                "Holding: " + normalProfile + ".");
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

    private void StopBuiltInAutomaticTimer()
    {
        try
        {
            if (GetField("_automaticTimer") is Forms.Timer timer)
            {
                timer.Stop();
                WriteDiagnostic("Automatic Mode EES startup gate took over Automatic Mode timer during startup settling window.");
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup gate could not stop built-in Automatic Mode timer: " + ex.Message);
        }
    }

    private void InvokeEvaluateAutomaticMode(bool force) => InvokeInstance("EvaluateAutomaticMode", force);

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

    private string InvokeStaticString(string methodName)
    {
        try { return _appType.GetMethod(methodName, StaticFlags)?.Invoke(null, null) as string ?? "Unknown"; }
        catch { return "Unknown"; }
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

            if (cpu == null && disk == null)
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
}
