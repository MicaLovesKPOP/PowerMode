using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Forms = System.Windows.Forms;

namespace Mica.PowerModeTray.WinUI;

internal sealed class AutomaticModeStartupHoldoff : IDisposable
{
    private const string ExtremeEnergySaver = "Extreme Energy Saver";
    private const string OptimizedPerformance = "Optimized Performance";
    private const int StartupHoldoffSeconds = 120;

    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly PowerModeTrayApplication _app;
    private readonly Type _appType;
    private readonly Forms.Timer _releaseTimer;
    private bool _disposed;
    private bool _active;

    private AutomaticModeStartupHoldoff(PowerModeTrayApplication app)
    {
        _app = app;
        _appType = app.GetType();

        _releaseTimer = new Forms.Timer { Interval = StartupHoldoffSeconds * 1000 };
        _releaseTimer.Tick += (_, _) => Release();

        try
        {
            SystemEvents.SessionEnding += OnSessionEnding;
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode startup holdoff could not register session ending handler: " + ex.Message);
        }

        TryStart();
    }

    internal static AutomaticModeStartupHoldoff Attach(PowerModeTrayApplication app) => new(app);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
        try { _releaseTimer.Stop(); _releaseTimer.Dispose(); } catch { }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
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
            HoldUsingPcProfile();
            _releaseTimer.Start();

            WriteDiagnostic($"Automatic Mode EES startup holdoff started for {StartupHoldoffSeconds}s.");
            LogAutomaticMode("Automatic Mode Away delayed",
                "Reason: startup holdoff before allowing Extreme Energy Saver.",
                "Duration: " + StartupHoldoffSeconds + " seconds.");
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup holdoff failed to start: " + ex.Message);
            Release();
        }
    }

    private void Release()
    {
        if (_disposed) return;

        try
        {
            _releaseTimer.Stop();
            if (_active)
            {
                _active = false;
                WriteDiagnostic("Automatic Mode EES startup holdoff released.");
                LogAutomaticMode("Automatic Mode startup holdoff released");
                InvokeInstance("EvaluateAutomaticMode", false);
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode EES startup holdoff release failed: " + ex.Message);
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
                WriteDiagnostic("Automatic Mode startup holdoff paused Automatic Mode timer.");
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Automatic Mode startup holdoff could not pause Automatic Mode timer: " + ex.Message);
        }
    }

    private void HoldUsingPcProfile()
    {
        string normalProfile = GetStringField("_automaticNormalProfile");
        if (string.IsNullOrWhiteSpace(normalProfile) || PowerModeTrayApplication.PlanNamesEquivalent(normalProfile, ExtremeEnergySaver))
        {
            normalProfile = OptimizedPerformance;
        }

        string active = GetActivePlanName();
        if (PowerModeTrayApplication.PlanNamesEquivalent(active, ExtremeEnergySaver))
        {
            TrySetPlanByName(normalProfile, "Automatic Mode startup holdoff");
        }

        SetField("_automaticStateText", "Active");
        SetField("_automaticReason", "Startup holdoff before Away mode.");
        InvokeInstance("RecordAutomaticModeState", "Active", normalProfile, false, false, false, false, false, false);
        InvokeInstance("UpdateTrayState", true);
        RefreshFlyoutBestEffort();
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
            WriteDiagnostic("Automatic Mode startup holdoff could not read power schemes: " + ex.Message);
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

    private string GetStringField(string name) => GetField(name) as string ?? "";

    private object? InvokeInstance(string methodName, params object?[] args)
        => _appType.GetMethod(methodName, InstanceFlags)?.Invoke(_app, args);

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

    private sealed class SafetyPowerScheme
    {
        public string Guid { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Active { get; set; }
    }

    private readonly record struct RunResult(int ExitCode, string Output, string Error);
}
