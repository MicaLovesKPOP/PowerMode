using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using WinRT.Interop;
using Windows.Graphics;
using Forms = System.Windows.Forms;
using WinPoint = Windows.Foundation.Point;
using UiColor = Windows.UI.Color;
using XamlFontFamily = Microsoft.UI.Xaml.Media.FontFamily;
using XamlEllipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace Mica.PowerModeTray.WinUI;

internal sealed class PlanInfo
{
    public string Name { get; }
    public string ColorHex { get; }
    public PlanInfo(string name, string colorHex) { Name = name; ColorHex = colorHex; }
}

internal sealed class PowerScheme
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

internal sealed class PowerModeTrayApplication : IDisposable
{
    private static readonly PlanInfo[] Plans = new[]
    {
        new PlanInfo("Unrestrained Performance", "#ff9e59"),
        new PlanInfo("Optimized Performance", "#ffd153"),
        new PlanInfo("Balanced Performance", "#8ebb57"),
        new PlanInfo("Cool & Quiet", "#5b99cc"),
        new PlanInfo("Extreme Energy Saver", "#8c79d1"),
    };

    private const string MutexName = "Mica.PowerModeTray.SingleInstance";
    private const string StartupRunName = "Power Mode";
    private const string StartupShortcutName = "Power Mode.lnk";
    private const string StartupRunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string GlobalBehaviorRegistryPath = @"Software\MicaLovesKPOP\PowerMode\GlobalBehavior";
    private const string ValueKeepPcAwake = "KeepPcAwake";
    private const string ValueKeepScreenOn = "KeepScreenOn";
    private const string ValueDisableScreensaver = "DisableScreensaver";
    private const string ValuePreviousSleepTimeouts = "PreviousSleepTimeouts";
    private const string ValuePreviousDisplayTimeouts = "PreviousDisplayTimeouts";
    private const string ValuePreviousScreenSaveActive = "PreviousScreenSaveActive";
    private const string ValuePreviousScreenSaveTimeout = "PreviousScreenSaveTimeOut";
    private const string ValuePreviousScreenSaverExe = "PreviousScreenSaverExe";
    private const string AutomaticModeRegistryPath = @"Software\MicaLovesKPOP\PowerMode\AutomaticMode";
    private const string ValueAutomaticEnabled = "Enabled";
    private const string ValueAutomaticNormalProfile = "NormalProfile";
    private const string ValueAutomaticAfkProfile = "AfkProfile";
    private const string ValueAutomaticAfkDelaySeconds = "AfkDelaySeconds";
    private const string DefaultAutomaticAfkProfile = "Extreme Energy Saver";
    private const int AutomaticLogMaxBytes = 256 * 1024;
    private const int AutomaticLogMaxFiles = 3;
    private const int AutomaticStatsDaysToKeep = 370;
    private const string AutomaticEventsLogFileName = "power-mode-events.log";
    private const string AutomaticSummaryFileName = "power-mode-summary.txt";
    private const string AutomaticStatsFileName = "power-mode-stats.json";
    private const string LegacyAutomaticEventsLogFileName = "automatic-mode-events.log";
    private const string LegacyAutomaticSummaryFileName = "automatic-mode-summary.txt";
    private const string LegacyAutomaticStatsFileName = "automatic-mode-stats.json";
    private const string LegacyAutomaticLogFileName = "automatic-mode.log";
    private static readonly int[] AutomaticAfkDelayOptionsSeconds = new[] { 1 * 60, 2 * 60, 3 * 60, 5 * 60, 10 * 60, 15 * 60, 30 * 60, 60 * 60 };

    private const uint SPI_GETSCREENSAVETIMEOUT = 0x000E;
    private const uint SPI_SETSCREENSAVETIMEOUT = 0x000F;
    private const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfoGetInt(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    private readonly Mutex _mutex;
    private readonly Forms.NotifyIcon _notifyIcon = null!;
    private readonly Forms.Timer _timer = null!;
    private readonly Forms.Timer _automaticTimer = null!;
    private readonly FlyoutWindow _flyout = null!;
    private readonly string _pidPath = "";
    private Icon? _currentIcon;
    private string _lastActivePlan = "";
    private bool? _lastLightMode;
    private int _promoteAttempts;
    private bool _disposed;
    private bool _automaticModeEnabled;
    private string _automaticNormalProfile = "Optimized Performance";
    private string _automaticAfkProfile = DefaultAutomaticAfkProfile;
    private int _automaticAfkDelaySeconds = 15 * 60;
    private string _automaticStateText = "Manual";
    private string _automaticReason = "Manual mode.";
    private bool _automaticFullscreenBlockActive;
    private string _automaticFullscreenBlockDescription = "";
    private string _automaticStatsState = "Manual";
    private string _automaticStatsProfile = "";
    private DateTime _automaticStatsSinceUtc = DateTime.UtcNow;

    private sealed class AutomaticModeStatsData
    {
        public int Version { get; set; } = 1;
        public string CurrentState { get; set; } = "Stopped";
        public string CurrentProfile { get; set; } = "";
        public DateTime CurrentSinceUtc { get; set; } = DateTime.UtcNow;
        public AutomaticModeStatsBucket AllTime { get; set; } = new();
        public Dictionary<string, AutomaticModeStatsBucket> Days { get; set; } = new();
    }

    private sealed class AutomaticModeStatsBucket
    {
        public long ManualSeconds { get; set; }
        public long ActiveSeconds { get; set; }
        public long AwaySeconds { get; set; }
        public long BlockedSeconds { get; set; }
        public int AwayEntries { get; set; }
        public int FullscreenBlocks { get; set; }
        public int ManualProfileChanges { get; set; }
        public int AutomaticSettingChanges { get; set; }
        public int AutomaticEnabled { get; set; }
        public int AutomaticDisabled { get; set; }
        public Dictionary<string, long> ProfileSeconds { get; set; } = new();
    }

    public PowerModeTrayApplication()
    {
        bool created;
        _mutex = new Mutex(true, MutexName, out created);
        if (!created)
        {
            CurrentApplicationExit();
            return;
        }

        _pidPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicaLovesKPOP", "PowerMode", "power-mode-tray-winui.pid");
        TryWritePidFile();

        LoadAutomaticModeSettings();

        _flyout = new FlyoutWindow(
            Plans,
            GetActivePlanName,
            SetPlan,
            IsLightMode,
            IsStartupEnabled,
            ToggleStartup,
            IsAutomaticModeEnabled,
            ToggleAutomaticMode,
            GetAutomaticStateText,
            GetAutomaticReason,
            GetAutomaticNormalProfile,
            GetAutomaticAfkProfile,
            GetAutomaticAfkDelaySeconds,
            () => CycleAutomaticNormalProfile(-1),
            () => CycleAutomaticNormalProfile(1),
            () => CycleAutomaticAfkProfile(-1),
            () => CycleAutomaticAfkProfile(1),
            () => CycleAutomaticAfkDelaySeconds(-1),
            () => CycleAutomaticAfkDelaySeconds(1),
            OpenAutomaticModeHistory,
            GetScreenSaverTimeoutSeconds,
            SetScreenSaverTimeoutSeconds,
            OpenScreenSaverSettings,
            GetDisplayTimeoutSeconds,
            SetDisplayTimeoutSeconds,
            GetSleepTimeoutSeconds,
            SetSleepTimeoutSeconds,
            OpenScreenAndSleepSettings,
            OpenPowerSettings,
            () => CurrentApplicationExit());

        string initialPlan = GetActivePlanName();
        bool initialLightMode = IsLightMode();
        string initialText = "Power Mode: " + initialPlan;
        _lastActivePlan = initialPlan;
        _lastLightMode = initialLightMode;
        InitializeAutomaticModeStats(initialPlan);
        _currentIcon = IconFactory.CreateTrayIcon(HexToDrawingColor(GetPlanColorHex(initialPlan)), initialLightMode);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = initialText.Length > 63 ? initialText[..63] : initialText,
            Icon = _currentIcon,
            Visible = true
        };
        Log("Tray icon created and made visible. Initial plan=" + initialPlan);
        _notifyIcon.MouseUp += (_, e) =>
        {
            // Match Windows tray behavior more closely: when our flyout is already
            // open, clicking the tray icon again simply closes the current surface.
            // Do not immediately swap/reopen to the other surface, as that produces
            // the visually odd reappear/disappear race the prototype had.
            if (_flyout.IsFlyoutVisible || _flyout.IsClosing)
            {
                _flyout.HideFlyout();
                return;
            }

            if (e.Button == Forms.MouseButtons.Left)
            {
                _flyout.ShowNear(Forms.Cursor.Position, FlyoutMode.Main);
            }
            else if (e.Button == Forms.MouseButtons.Right)
            {
                // Pass both anchors through. The visible taskbar case needs the
                // notification-icon rectangle for centering; the hidden-icons overflow
                // case needs the real cursor/click point so it does not inherit stale
                // direct-taskbar placement from Shell_NotifyIconGetRect.
                System.Drawing.Point clickPoint = Forms.Cursor.Position;
                System.Drawing.Rectangle? trayIconRect = TryGetNotifyIconRect(out System.Drawing.Rectangle rect) ? rect : null;
                _flyout.ShowNativeContextMenuNear(clickPoint, trayIconRect);
            }
        };

        ApplySavedGlobalBehaviorToggles();
        EnsureNotifyIconVisible();
        RequestTrayVisibility();

        _timer = new Forms.Timer { Interval = 3000 };
        _timer.Tick += (_, _) =>
        {
            UpdateTrayState(false);
            EnsureNotifyIconVisible();

            if (_promoteAttempts < 8)
            {
                _promoteAttempts++;
                RequestTrayVisibility();

                if (_promoteAttempts == 4 && !TryGetNotifyIconRect(out _))
                {
                    ReaddNotifyIcon("Shell did not report a tray icon rectangle after startup.");
                }
            }
        };
        _timer.Start();

        _automaticTimer = new Forms.Timer { Interval = 500 };
        _automaticTimer.Tick += (_, _) => EvaluateAutomaticMode(false);
        _automaticTimer.Start();
        EvaluateAutomaticMode(true);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
    }

    private void TryWritePidFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_pidPath)!);
            File.WriteAllText(_pidPath, Process.GetCurrentProcess().Id.ToString());
        }
        catch (Exception ex)
        {
            // The tray app must never crash just because a diagnostic pid file cannot be written.
            Log("PID file write failed: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { CloseAutomaticModeStatsSession(); } catch { }
        try { _timer.Stop(); _timer.Dispose(); } catch { }
        try { _automaticTimer.Stop(); _automaticTimer.Dispose(); } catch { }
        try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); } catch { }
        try { _currentIcon?.Dispose(); } catch { }
        try { if (File.Exists(_pidPath)) File.Delete(_pidPath); } catch { }
        try { _mutex.ReleaseMutex(); } catch { }
        try { _mutex.Dispose(); } catch { }
    }

    private void UpdateTrayState(bool force)
    {
        string active = GetActivePlanName();
        bool light = IsLightMode();
        if (!force && active == _lastActivePlan && light == _lastLightMode) return;
        _lastActivePlan = active;
        _lastLightMode = light;
        string text = _automaticModeEnabled
            ? "Power Mode: Auto - " + _automaticStateText + " - " + active
            : "Power Mode: " + active;
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        Icon old = _currentIcon!;
        _currentIcon = IconFactory.CreateTrayIcon(HexToDrawingColor(GetPlanColorHex(active)), light);
        _notifyIcon.Icon = _currentIcon;
        _notifyIcon.Visible = true;
        try { old?.Dispose(); } catch { }
    }

    private void EnsureNotifyIconVisible()
    {
        try
        {
            if (_notifyIcon.Icon == null)
            {
                _currentIcon ??= IconFactory.CreateTrayIcon(HexToDrawingColor(GetPlanColorHex(GetActivePlanName())), IsLightMode());
                _notifyIcon.Icon = _currentIcon;
            }

            if (!_notifyIcon.Visible)
            {
                _notifyIcon.Visible = true;
                Log("Tray icon visibility was off and has been restored.");
            }
        }
        catch (Exception ex)
        {
            Log("EnsureNotifyIconVisible failed: " + ex.Message);
        }
    }

    private void ReaddNotifyIcon(string reason)
    {
        try
        {
            Log("Re-adding tray icon. Reason: " + reason);
            _notifyIcon.Visible = false;
            Thread.Sleep(100);
            _notifyIcon.Visible = true;
        }
        catch (Exception ex)
        {
            Log("ReaddNotifyIcon failed: " + ex.Message);
        }
    }

    private static string GetPlanColorHex(string name)
    {
        return Plans.FirstOrDefault(x => PlanNamesEquivalent(x.Name, name))?.ColorHex ?? "#0078d4";
    }

    private static string NormalizePlanNameForCompare(string name)
    {
        string s = (name ?? "").Trim().ToLowerInvariant();
        s = s.Replace("&&", "&").Replace("_", "");
        s = Regex.Replace(s, @"\bcool\s+and\s+quiet\b", "cool & quiet");
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    internal static bool PlanNamesEquivalent(string a, string b)
        => string.Equals(NormalizePlanNameForCompare(a), NormalizePlanNameForCompare(b), StringComparison.OrdinalIgnoreCase);

    private static string ToDisplayPlanName(string name)
    {
        if (PlanNamesEquivalent(name, "Cool & Quiet")) return "Cool & Quiet";
        if (PlanNamesEquivalent(name, "Extreme Energy Saver")) return "Extreme Energy Saver";

        foreach (PlanInfo plan in Plans)
        {
            if (PlanNamesEquivalent(name, plan.Name)) return plan.Name;
        }

        return Regex.Replace(name ?? "", @"^Gaming PC\s*-\s*", "", RegexOptions.IgnoreCase).Trim();
    }

    private enum NativePowerModeOverlay
    {
        BestPowerEfficiency,
        Balanced,
        BestPerformance
    }

    private static NativePowerModeOverlay GetNativePowerModeOverlayForPlan(string name)
    {
        if (PlanNamesEquivalent(name, "Unrestrained Performance") || PlanNamesEquivalent(name, "Optimized Performance"))
            return NativePowerModeOverlay.BestPerformance;

        if (PlanNamesEquivalent(name, "Extreme Energy Saver"))
            return NativePowerModeOverlay.BestPowerEfficiency;

        return NativePowerModeOverlay.Balanced;
    }

    private static string GetNativePowerModeOverlayDisplayName(NativePowerModeOverlay overlay)
        => overlay switch
        {
            NativePowerModeOverlay.BestPerformance => "Best performance",
            NativePowerModeOverlay.BestPowerEfficiency => "Best power efficiency",
            _ => "Balanced"
        };

    private static string[] GetNativePowerModeOverlayCandidates(NativePowerModeOverlay overlay)
        => overlay switch
        {
            NativePowerModeOverlay.BestPerformance => new[] { "ded574b5-45a0-4f42-8737-46345c09c238", "OVERLAY_SCHEME_HIGH" },
            NativePowerModeOverlay.BestPowerEfficiency => new[] { "961cc777-2547-4f9d-8174-7d86181b8a7a", "OVERLAY_SCHEME_LOW" },
            _ => new[] { "00000000-0000-0000-0000-000000000000", "OVERLAY_SCHEME_NONE" }
        };

    private static void SetNativePowerModeOverlayBestEffort(string planName)
    {
        NativePowerModeOverlay overlay = GetNativePowerModeOverlayForPlan(planName);
        string display = GetNativePowerModeOverlayDisplayName(overlay);

        foreach (string candidate in GetNativePowerModeOverlayCandidates(overlay))
        {
            var result = RunHidden("powercfg.exe", "/setactiveoverlay " + candidate);
            if (result.ExitCode == 0)
            {
                WriteDiagnostic($"Native Windows Power mode overlay set to '{display}' for '{planName}' via powercfg ({candidate}).");
                return;
            }
        }

        WriteDiagnostic($"Could not set native Windows Power mode overlay to '{display}' for '{planName}'. This Windows build may not expose /setactiveoverlay.");
    }


    private static System.Drawing.Color HexToDrawingColor(string hex)
    {
        string h = hex.Trim().TrimStart('#');
        return System.Drawing.Color.FromArgb(255, Convert.ToInt32(h[..2], 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
    }

    private static bool IsLightMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? v = key?.GetValue("AppsUseLightTheme");
            if (v == null) return true;
            return Convert.ToInt32(v) != 0;
        }
        catch { return true; }
    }

    private static List<PowerScheme> GetPowerSchemes()
    {
        var list = new List<PowerScheme>();
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", "/list")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using Process? p = Process.Start(psi);
            if (p == null) return list;
            string output = p.StandardOutput.ReadToEnd() + "\n" + p.StandardError.ReadToEnd();
            p.WaitForExit(3000);
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match m = Regex.Match(line, @"([0-9a-fA-F-]{36}).*\((.*?)\)(\s*\*)?");
                if (m.Success)
                {
                    list.Add(new PowerScheme
                    {
                        Guid = m.Groups[1].Value,
                        Name = m.Groups[2].Value,
                        Active = line.Contains('*')
                    });
                }
            }
        }
        catch { }
        return list;
    }

    private static string GetActivePlanName()
    {
        string active = GetPowerSchemes().FirstOrDefault(x => x.Active)?.Name ?? "Unknown";
        return ToDisplayPlanName(active);
    }

    private static string? GetPlanGuidByName(string name)
    {
        return GetPowerSchemes().FirstOrDefault(x => PlanNamesEquivalent(x.Name, name))?.Guid;
    }

    private void SetPlan(string name)
    {
        bool disabledAutomatic = false;
        if (_automaticModeEnabled)
        {
            disabledAutomatic = true;
            _automaticModeEnabled = false;
            SaveAutomaticModeEnabled(false);
            _automaticStateText = "Manual";
            _automaticReason = "Manual profile selected.";
            Log("Automatic Mode disabled because a manual profile was selected.");
            AutomaticModeLog("Automatic Mode disabled", "Manual profile selected: " + name + ".");
        }

        ApplyPlan(name, refreshFlyout: true, source: "Manual");
        AutomaticModeLog("Manual profile selected",
            name + ".",
            disabledAutomatic ? "Automatic Mode turned off." : "");

        RecordAutomaticModeState("Manual", name, manualProfileChange: true, automaticDisabled: disabledAutomatic);
    }

    private void ApplyPlan(string name, bool refreshFlyout, string source)
    {
        string? guid = GetPlanGuidByName(name);
        if (string.IsNullOrWhiteSpace(guid))
        {
            WriteDiagnostic($"Could not switch to '{name}' because no matching power-plan GUID was found.");
            if (refreshFlyout) _flyout.RefreshContent();
            return;
        }

        var first = RunHidden("powercfg.exe", "/setactive " + guid);
        string activeAfterFirst = GetActivePlanName();
        if (!PlanNamesEquivalent(activeAfterFirst, name))
        {
            var second = RunHidden("powercfg.exe", "/S " + guid);
            string activeAfterSecond = GetActivePlanName();
            if (!PlanNamesEquivalent(activeAfterSecond, name))
            {
                WriteDiagnostic($"Failed to switch to '{name}' ({guid}). /setactive exit={first.ExitCode}, /S exit={second.ExitCode}. Active now: '{activeAfterSecond}'. Errors: {first.Error} {second.Error}");
            }
        }

        SetNativePowerModeOverlayBestEffort(name);
        UpdateTrayState(true);
        if (refreshFlyout) _flyout.RefreshContent();
        Log($"{source}: active profile target is '{name}'.");
    }



    private void LoadAutomaticModeSettings()
    {
        _automaticModeEnabled = GetAutomaticModeEnabledFromRegistry();

        string? storedProfile = GetAutomaticModeString(ValueAutomaticNormalProfile);
        if (!string.IsNullOrWhiteSpace(storedProfile) && IsValidAutomaticNormalProfile(storedProfile))
        {
            _automaticNormalProfile = ToDisplayPlanName(storedProfile);
        }
        else
        {
            string active = GetActivePlanName();
            _automaticNormalProfile = IsValidAutomaticNormalProfile(active) ? active : "Balanced Performance";
            SaveAutomaticModeString(ValueAutomaticNormalProfile, _automaticNormalProfile);
        }

        string? storedAfkProfile = GetAutomaticModeString(ValueAutomaticAfkProfile);
        _automaticAfkProfile = !string.IsNullOrWhiteSpace(storedAfkProfile) && Plans.Any(p => PlanNamesEquivalent(p.Name, storedAfkProfile))
            ? ToDisplayPlanName(storedAfkProfile)
            : DefaultAutomaticAfkProfile;

        EnsureAutomaticProfilePair(logCorrections: false);
        SaveAutomaticModeString(ValueAutomaticAfkProfile, _automaticAfkProfile);

        int storedDelay = GetAutomaticModeInt(ValueAutomaticAfkDelaySeconds, 15 * 60);
        _automaticAfkDelaySeconds = AutomaticAfkDelayOptionsSeconds.Contains(storedDelay) ? storedDelay : 15 * 60;
        SaveAutomaticModeInt(ValueAutomaticAfkDelaySeconds, _automaticAfkDelaySeconds);

        _automaticStateText = _automaticModeEnabled ? "Active" : "Manual";
        _automaticReason = _automaticModeEnabled ? "Automatic Mode ready." : "Manual mode.";
    }

    private bool IsAutomaticModeEnabled() => _automaticModeEnabled;
    private string GetAutomaticStateText() => _automaticStateText;
    private string GetAutomaticReason() => _automaticReason;
    private string GetAutomaticNormalProfile() => _automaticNormalProfile;
    private string GetAutomaticAfkProfile() => _automaticAfkProfile;
    private int GetAutomaticAfkDelaySeconds() => _automaticAfkDelaySeconds;

    private void ToggleAutomaticMode()
    {
        bool enable = !_automaticModeEnabled;
        if (enable)
        {
            string active = GetActivePlanName();
            _automaticNormalProfile = IsValidAutomaticNormalProfile(active) ? active : "Balanced Performance";
            EnsureAutomaticProfilePair(logCorrections: true);
            SaveAutomaticModeString(ValueAutomaticNormalProfile, _automaticNormalProfile);
            SaveAutomaticModeString(ValueAutomaticAfkProfile, _automaticAfkProfile);
            _automaticStateText = "Active";
            _automaticReason = "Using normal profile.";
            Log("Automatic Mode enabled. Normal profile='" + _automaticNormalProfile + "'. Away profile='" + _automaticAfkProfile + "'.");
            AutomaticModeLog("Automatic Mode enabled",
                "Using PC: " + _automaticNormalProfile + ".",
                "Away: " + _automaticAfkProfile + ".",
                "Delay: " + FormatDurationForReason(_automaticAfkDelaySeconds) + ".");
        }
        else
        {
            _automaticStateText = "Manual";
            _automaticReason = "Manual mode.";
            _automaticFullscreenBlockActive = false;
            _automaticFullscreenBlockDescription = "";
            Log("Automatic Mode disabled.");
            AutomaticModeLog("Automatic Mode disabled");
        }

        _automaticModeEnabled = enable;
        SaveAutomaticModeEnabled(enable);
        RecordAutomaticModeState(enable ? "Active" : "Manual", enable ? _automaticNormalProfile : GetActivePlanName(), automaticEnabled: enable, automaticDisabled: !enable);
        EvaluateAutomaticMode(force: true);
        _flyout.RefreshContent();
    }

    private void CycleAutomaticNormalProfile(int direction)
    {
        string oldNormal = _automaticNormalProfile;
        string newNormal = GetNextAutomaticNormalProfile(_automaticNormalProfile, direction);
        if (PlanNamesEquivalent(oldNormal, newNormal)) return;

        _automaticNormalProfile = newNormal;
        SaveAutomaticModeString(ValueAutomaticNormalProfile, _automaticNormalProfile);
        EnsureAutomaticProfilePair(logCorrections: true);
        SaveAutomaticModeString(ValueAutomaticAfkProfile, _automaticAfkProfile);
        AutomaticModeLog("Using PC profile changed",
            oldNormal + " -> " + _automaticNormalProfile + ".",
            "Away: " + _automaticAfkProfile + ".");
        RecordPowerHistoryCounter(automaticSettingChange: true);
        EvaluateAutomaticMode(force: true);
        _flyout.RefreshContent();
    }

    private void CycleAutomaticAfkProfile(int direction)
    {
        string oldAfk = _automaticAfkProfile;
        string newAfk = GetNextAutomaticAfkProfile(_automaticNormalProfile, _automaticAfkProfile, direction);
        if (PlanNamesEquivalent(oldAfk, newAfk)) return;

        _automaticAfkProfile = newAfk;
        SaveAutomaticModeString(ValueAutomaticAfkProfile, _automaticAfkProfile);
        AutomaticModeLog("Away profile changed", oldAfk + " -> " + _automaticAfkProfile + ".");
        RecordPowerHistoryCounter(automaticSettingChange: true);
        EvaluateAutomaticMode(force: true);
        _flyout.RefreshContent();
    }

    private void CycleAutomaticAfkDelaySeconds(int direction)
    {
        int oldDelay = _automaticAfkDelaySeconds;
        int index = Array.IndexOf(AutomaticAfkDelayOptionsSeconds, _automaticAfkDelaySeconds);
        if (index < 0) index = Array.IndexOf(AutomaticAfkDelayOptionsSeconds, 15 * 60);
        if (index < 0) index = 0;

        int nextIndex = ClampIndex(index + Math.Sign(direction == 0 ? 1 : direction), AutomaticAfkDelayOptionsSeconds.Length);
        _automaticAfkDelaySeconds = AutomaticAfkDelayOptionsSeconds[nextIndex];
        if (oldDelay == _automaticAfkDelaySeconds) return;

        SaveAutomaticModeInt(ValueAutomaticAfkDelaySeconds, _automaticAfkDelaySeconds);
        _automaticReason = "Away delay set to " + FormatDurationForReason(_automaticAfkDelaySeconds) + ".";
        Log("Automatic Mode away delay set to " + _automaticAfkDelaySeconds + " seconds.");
        AutomaticModeLog("Away delay changed", FormatDurationForReason(oldDelay) + " -> " + FormatDurationForReason(_automaticAfkDelaySeconds) + ".");
        RecordPowerHistoryCounter(automaticSettingChange: true);
        EvaluateAutomaticMode(force: true);
        _flyout.RefreshContent();
    }

    private void EvaluateAutomaticMode(bool force)
    {
        if (!_automaticModeEnabled) return;

        try
        {
            string previousState = _automaticStateText;
            string previousReason = _automaticReason;

            long idleMs = GetIdleMilliseconds();
            bool idleEnough = idleMs >= _automaticAfkDelaySeconds * 1000L;
            string fullscreenDescription = "";
            bool fullscreenBlocksAfk = idleEnough && TryGetForegroundFullscreenAppDescription(out fullscreenDescription);
            bool fullscreenBlockStarted = false;

            if (fullscreenBlocksAfk)
            {
                if (!_automaticFullscreenBlockActive || !string.Equals(_automaticFullscreenBlockDescription, fullscreenDescription, StringComparison.OrdinalIgnoreCase))
                {
                    fullscreenBlockStarted = true;
                    _automaticFullscreenBlockActive = true;
                    _automaticFullscreenBlockDescription = fullscreenDescription;
                    AutomaticModeLog("Away mode paused", "Fullscreen app: " + fullscreenDescription + ".");
                }
            }
            else if (_automaticFullscreenBlockActive)
            {
                AutomaticModeLog("Away mode resumed", "Previous fullscreen app: " + _automaticFullscreenBlockDescription + ".");
                _automaticFullscreenBlockActive = false;
                _automaticFullscreenBlockDescription = "";
            }

            string targetProfile;
            string stateText;
            string reason;

            if (idleEnough && !fullscreenBlocksAfk)
            {
                targetProfile = _automaticAfkProfile;
                stateText = "Away";
                reason = "No input for " + FormatDurationForReason(_automaticAfkDelaySeconds) + ".";
            }
            else
            {
                targetProfile = _automaticNormalProfile;
                stateText = "Active";
                reason = fullscreenBlocksAfk
                    ? "Fullscreen app paused away mode."
                    : "User is active.";
            }

            string statsState = fullscreenBlocksAfk ? "Blocked" : stateText;
            bool enteredAway = stateText == "Away" && !string.Equals(previousState, "Away", StringComparison.OrdinalIgnoreCase);
            RecordAutomaticModeState(statsState, targetProfile, awayEntry: enteredAway, fullscreenBlock: fullscreenBlockStarted);

            bool stateChanged = !string.Equals(previousState, stateText, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previousReason, reason, StringComparison.Ordinal);

            _automaticStateText = stateText;
            _automaticReason = reason;

            string active = GetActivePlanName();
            bool profileWillChange = !PlanNamesEquivalent(active, targetProfile);
            if (force || profileWillChange)
            {
                ApplyPlan(targetProfile, refreshFlyout: false, source: "Automatic Mode");
                stateChanged = true;
            }

            if (!string.Equals(previousState, stateText, StringComparison.OrdinalIgnoreCase))
            {
                if (stateText == "Away")
                {
                    AutomaticModeLog("Entered Away mode",
                        "Switched to " + targetProfile + ".",
                        "Reason: no input for " + FormatDurationForReason(_automaticAfkDelaySeconds) + ".");
                }
                else if (previousState == "Away")
                {
                    string exitReason = idleEnough && fullscreenBlocksAfk ? "fullscreen app active" : "user input detected";
                    AutomaticModeLog("Exited Away mode",
                        "Restored " + targetProfile + ".",
                        "Reason: " + exitReason + ".");
                }
            }
            else if (profileWillChange && stateText == "Active" && !force)
            {
                AutomaticModeLog("Restored automatic profile",
                    "Switched back to " + targetProfile + ".",
                    "Reason: " + reason);
            }

            if (stateChanged)
            {
                UpdateTrayState(true);
                _flyout.RefreshContent();
            }
        }
        catch (Exception ex)
        {
            Log("EvaluateAutomaticMode failed: " + ex.Message);
            AutomaticModeLog("Automatic Mode evaluation failed", ex.Message);
        }
    }

    private static bool GetAutomaticModeEnabledFromRegistry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutomaticModeRegistryPath);
            object? value = key?.GetValue(ValueAutomaticEnabled);
            return value is not null && Convert.ToInt32(value) != 0;
        }
        catch { return false; }
    }

    private static void SaveAutomaticModeEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AutomaticModeRegistryPath);
            key.SetValue(ValueAutomaticEnabled, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static string? GetAutomaticModeString(string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutomaticModeRegistryPath);
            return key?.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    private static void SaveAutomaticModeString(string valueName, string value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AutomaticModeRegistryPath);
            key.SetValue(valueName, value, RegistryValueKind.String);
        }
        catch { }
    }

    private static int GetAutomaticModeInt(string valueName, int fallback)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutomaticModeRegistryPath);
            object? value = key?.GetValue(valueName);
            return value is null ? fallback : Convert.ToInt32(value);
        }
        catch { return fallback; }
    }

    private static void SaveAutomaticModeInt(string valueName, int value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AutomaticModeRegistryPath);
            key.SetValue(valueName, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static int GetAutomaticProfileRank(string profile)
    {
        for (int i = 0; i < Plans.Length; i++)
        {
            if (PlanNamesEquivalent(Plans[i].Name, profile)) return i;
        }
        return Plans.Length - 1;
    }

    private static bool IsValidAutomaticNormalProfile(string profile)
        => GetAutomaticProfileRank(profile) < Plans.Length - 1;

    private static string GetNextAutomaticNormalProfile(string current, int direction)
    {
        var valid = Plans.Take(Plans.Length - 1).Select(p => p.Name).ToArray();
        int index = Array.FindIndex(valid, p => PlanNamesEquivalent(p, current));
        if (index < 0) index = 1;
        return valid[ClampIndex(index + Math.Sign(direction == 0 ? 1 : direction), valid.Length)];
    }

    private static string GetFirstValidAutomaticAfkProfile(string normalProfile)
    {
        int normalRank = GetAutomaticProfileRank(normalProfile);
        int afkRank = Math.Clamp(normalRank + 1, 1, Plans.Length - 1);
        return Plans[afkRank].Name;
    }

    private static string GetNextAutomaticAfkProfile(string normalProfile, string currentAfkProfile, int direction)
    {
        int normalRank = GetAutomaticProfileRank(normalProfile);
        string[] valid = Plans
            .Where(p => GetAutomaticProfileRank(p.Name) > normalRank)
            .Select(p => p.Name)
            .ToArray();

        if (valid.Length == 0) return DefaultAutomaticAfkProfile;

        int index = Array.FindIndex(valid, p => PlanNamesEquivalent(p, currentAfkProfile));
        if (index < 0) return valid[0];
        return valid[ClampIndex(index + Math.Sign(direction == 0 ? 1 : direction), valid.Length)];
    }

    private static int ClampIndex(int index, int length)
    {
        if (length <= 0) return 0;
        if (index < 0) return 0;
        if (index >= length) return length - 1;
        return index;
    }

    private void EnsureAutomaticProfilePair(bool logCorrections)
    {
        if (!IsValidAutomaticNormalProfile(_automaticNormalProfile))
        {
            string old = _automaticNormalProfile;
            _automaticNormalProfile = "Balanced Performance";
            if (logCorrections)
            {
                AutomaticModeLog("Using PC profile corrected", old + " -> " + _automaticNormalProfile + ".");
            }
        }

        int normalRank = GetAutomaticProfileRank(_automaticNormalProfile);
        int afkRank = GetAutomaticProfileRank(_automaticAfkProfile);
        if (afkRank <= normalRank)
        {
            string old = _automaticAfkProfile;
            _automaticAfkProfile = GetFirstValidAutomaticAfkProfile(_automaticNormalProfile);
            if (logCorrections)
            {
                AutomaticModeLog("Away profile corrected",
                    old + " -> " + _automaticAfkProfile + ".",
                    "Reason: Away must use a lower-power profile.");
            }
        }
    }

    private static long GetIdleMilliseconds()
    {
        try
        {
            LastInputInfo info = new() { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return 0;

            uint now = GetTickCount();
            unchecked { return Math.Max(0, (long)(now - info.dwTime)); }
        }
        catch { return 0; }
    }

    private static bool IsForegroundFullscreenAppActive()
        => TryGetForegroundFullscreenAppDescription(out _);

    private static bool TryGetForegroundFullscreenAppDescription(out string description)
    {
        description = "fullscreen app";
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return false;
            if (!GetAppWindowRect(hwnd, out AppNativeRect rect)) return false;

            System.Drawing.Rectangle window = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (window.Width <= 0 || window.Height <= 0) return false;

            Forms.Screen screen = Forms.Screen.FromHandle(hwnd);
            System.Drawing.Rectangle bounds = screen.Bounds;
            const int tolerance = 8;

            bool fullscreen = window.Left <= bounds.Left + tolerance
                && window.Top <= bounds.Top + tolerance
                && window.Right >= bounds.Right - tolerance
                && window.Bottom >= bounds.Bottom - tolerance
                && window.Width >= bounds.Width - tolerance
                && window.Height >= bounds.Height - tolerance;

            if (!fullscreen) return false;

            description = GetWindowProcessDescription(hwnd);
            return true;
        }
        catch { return false; }
    }

    private static string GetWindowProcessDescription(IntPtr hwnd)
    {
        try
        {
            _ = GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0) return "fullscreen app";

            using Process process = Process.GetProcessById((int)processId);
            string name = process.ProcessName;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
            return name;
        }
        catch { return "fullscreen app"; }
    }

    private static string FormatDurationForReason(int seconds)
    {
        int minutes = Math.Max(1, seconds / 60);
        if (minutes < 60) return minutes == 1 ? "1 minute" : minutes + " minutes";

        int hours = minutes / 60;
        int remainder = minutes % 60;
        string hourText = hours == 1 ? "1 hour" : hours + " hours";
        if (remainder == 0) return hourText;
        return hourText + " " + (remainder == 1 ? "1 minute" : remainder + " minutes");
    }

    private readonly record struct PowerTimeoutValues(int AcSeconds, int DcSeconds);

    private static bool GetGlobalBehaviorFlag(string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(GlobalBehaviorRegistryPath);
            object? value = key?.GetValue(valueName);
            return value is not null && Convert.ToInt32(value) != 0;
        }
        catch { return false; }
    }

    private static void SetGlobalBehaviorFlag(string valueName, bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(GlobalBehaviorRegistryPath);
            key.SetValue(valueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static string? GetGlobalBehaviorString(string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(GlobalBehaviorRegistryPath);
            return key?.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    private static void SetGlobalBehaviorString(string valueName, string? value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(GlobalBehaviorRegistryPath);
            if (value == null) key.DeleteValue(valueName, false);
            else key.SetValue(valueName, value, RegistryValueKind.String);
        }
        catch { }
    }

    private static bool IsKeepPcAwakeEnabled() => GetGlobalBehaviorFlag(ValueKeepPcAwake);
    private static bool IsKeepScreenOnEnabled() => GetGlobalBehaviorFlag(ValueKeepScreenOn);
    private static bool IsScreensaverDisabled() => GetGlobalBehaviorFlag(ValueDisableScreensaver);

    private void ToggleKeepPcAwake()
    {
        SetKeepPcAwakeEnabled(!IsKeepPcAwakeEnabled());
        _flyout.RefreshContent();
    }

    private void ToggleKeepScreenOn()
    {
        SetKeepScreenOnEnabled(!IsKeepScreenOnEnabled());
        _flyout.RefreshContent();
    }

    private void ToggleScreensaverDisabled()
    {
        SetScreensaverDisabled(!IsScreensaverDisabled());
        _flyout.RefreshContent();
    }

    private static int GetScreenSaverTimeoutSeconds()
    {
        try
        {
            using RegistryKey? desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            string active = desktop?.GetValue("ScreenSaveActive") as string ?? "0";
            if (active == "0") return 0;

            int systemTimeout = 0;
            if (SystemParametersInfoGetInt(SPI_GETSCREENSAVETIMEOUT, 0, ref systemTimeout, 0) && systemTimeout > 0)
            {
                return systemTimeout;
            }

            string? timeoutText = desktop?.GetValue("ScreenSaveTimeOut") as string;
            return int.TryParse(timeoutText, out int seconds) && seconds > 0 ? seconds : 600;
        }
        catch { return 0; }
    }

    private static void SetScreenSaverTimeoutSeconds(int seconds)
    {
        try
        {
            if (seconds <= 0)
            {
                SetScreensaverDisabled(true);
                return;
            }

            using RegistryKey desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true) ?? Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
            desktop.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
            desktop.SetValue("ScreenSaveTimeOut", seconds.ToString(), RegistryValueKind.String);
            desktop.Flush();

            SetGlobalBehaviorFlag(ValueDisableScreensaver, false);
            ApplyScreensaverDisabledState();
        }
        catch (Exception ex)
        {
            WriteDiagnostic("SetScreenSaverTimeoutSeconds failed: " + ex.Message);
        }
    }

    private static int GetDisplayTimeoutSeconds() => GetActivePowerTimeoutSeconds("SUB_VIDEO", "VIDEOIDLE");
    private static int GetSleepTimeoutSeconds() => GetActivePowerTimeoutSeconds("SUB_SLEEP", "STANDBYIDLE");

    private static void SetDisplayTimeoutSeconds(int seconds) => SetGlobalPowerTimeoutSeconds(
        seconds,
        "SUB_VIDEO",
        "VIDEOIDLE",
        ValueKeepScreenOn,
        ValuePreviousDisplayTimeouts,
        "SetDisplayTimeoutSeconds");

    private static void SetSleepTimeoutSeconds(int seconds) => SetGlobalPowerTimeoutSeconds(
        seconds,
        "SUB_SLEEP",
        "STANDBYIDLE",
        ValueKeepPcAwake,
        ValuePreviousSleepTimeouts,
        "SetSleepTimeoutSeconds");

    private static int GetActivePowerTimeoutSeconds(string subgroupAlias, string settingAlias)
    {
        try
        {
            string? activeGuid = GetPowerSchemes().FirstOrDefault(x => x.Active)?.Guid;
            if (string.IsNullOrWhiteSpace(activeGuid)) return 0;
            return ReadPowerTimeouts(activeGuid, subgroupAlias, settingAlias).AcSeconds;
        }
        catch { return 0; }
    }

    private static void SetGlobalPowerTimeoutSeconds(int seconds, string subgroupAlias, string settingAlias, string enabledFlagName, string previousValuesName, string diagnosticName)
    {
        try
        {
            if (seconds <= 0)
            {
                if (!GetGlobalBehaviorFlag(enabledFlagName))
                {
                    SetGlobalBehaviorString(previousValuesName, SerializeTimeouts(ReadPowerTimeoutsForAllSchemes(subgroupAlias, settingAlias)));
                }

                SetPowerTimeoutForAllSchemes(subgroupAlias, settingAlias, 0, 0);
                SetGlobalBehaviorFlag(enabledFlagName, true);
                return;
            }

            SetPowerTimeoutForAllSchemes(subgroupAlias, settingAlias, seconds, seconds);
            SetGlobalBehaviorFlag(enabledFlagName, false);
        }
        catch (Exception ex)
        {
            WriteDiagnostic(diagnosticName + " failed: " + ex.Message);
        }
    }

    private static void ApplySavedGlobalBehaviorToggles()
    {
        try
        {
            if (IsKeepPcAwakeEnabled())
            {
                SetPowerTimeoutForAllSchemes("SUB_SLEEP", "STANDBYIDLE", 0, 0);
            }

            if (IsKeepScreenOnEnabled())
            {
                SetPowerTimeoutForAllSchemes("SUB_VIDEO", "VIDEOIDLE", 0, 0);
            }

            if (IsScreensaverDisabled())
            {
                ApplyScreensaverDisabledState();
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("ApplySavedGlobalBehaviorToggles failed: " + ex.Message);
        }
    }

    private static void SetKeepPcAwakeEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                if (!IsKeepPcAwakeEnabled())
                {
                    SetGlobalBehaviorString(ValuePreviousSleepTimeouts, SerializeTimeouts(ReadPowerTimeoutsForAllSchemes("SUB_SLEEP", "STANDBYIDLE")));
                }

                SetPowerTimeoutForAllSchemes("SUB_SLEEP", "STANDBYIDLE", 0, 0);
                SetGlobalBehaviorFlag(ValueKeepPcAwake, true);
            }
            else
            {
                RestorePowerTimeouts(GetGlobalBehaviorString(ValuePreviousSleepTimeouts), "SUB_SLEEP", "STANDBYIDLE");
                SetGlobalBehaviorFlag(ValueKeepPcAwake, false);
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("SetKeepPcAwakeEnabled failed: " + ex.Message);
        }
    }

    private static void SetKeepScreenOnEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                if (!IsKeepScreenOnEnabled())
                {
                    SetGlobalBehaviorString(ValuePreviousDisplayTimeouts, SerializeTimeouts(ReadPowerTimeoutsForAllSchemes("SUB_VIDEO", "VIDEOIDLE")));
                }

                SetPowerTimeoutForAllSchemes("SUB_VIDEO", "VIDEOIDLE", 0, 0);
                SetGlobalBehaviorFlag(ValueKeepScreenOn, true);
            }
            else
            {
                RestorePowerTimeouts(GetGlobalBehaviorString(ValuePreviousDisplayTimeouts), "SUB_VIDEO", "VIDEOIDLE");
                SetGlobalBehaviorFlag(ValueKeepScreenOn, false);
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("SetKeepScreenOnEnabled failed: " + ex.Message);
        }
    }

    private static Dictionary<string, PowerTimeoutValues> ReadPowerTimeoutsForAllSchemes(string subgroupAlias, string settingAlias)
    {
        var values = new Dictionary<string, PowerTimeoutValues>(StringComparer.OrdinalIgnoreCase);
        foreach (PowerScheme scheme in GetPowerSchemes())
        {
            if (string.IsNullOrWhiteSpace(scheme.Guid)) continue;
            values[scheme.Guid] = ReadPowerTimeouts(scheme.Guid, subgroupAlias, settingAlias);
        }
        return values;
    }

    private static PowerTimeoutValues ReadPowerTimeouts(string schemeGuid, string subgroupAlias, string settingAlias)
    {
        var result = RunHidden("powercfg.exe", $"/query {schemeGuid} {subgroupAlias} {settingAlias}");
        string text = (result.Output + "\n" + result.Error).Trim();

        int ac = ParsePowerCfgSeconds(text, @"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)");
        int dc = ParsePowerCfgSeconds(text, @"Current DC Power Setting Index:\s*0x([0-9a-fA-F]+)");
        return new PowerTimeoutValues(ac, dc);
    }

    private static int ParsePowerCfgSeconds(string text, string pattern)
    {
        try
        {
            Match m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            return Convert.ToInt32(m.Groups[1].Value, 16);
        }
        catch { return 0; }
    }

    private static string SerializeTimeouts(Dictionary<string, PowerTimeoutValues> values)
    {
        return string.Join("\n", values.Select(kvp => $"{kvp.Key}|{kvp.Value.AcSeconds}|{kvp.Value.DcSeconds}"));
    }

    private static Dictionary<string, PowerTimeoutValues> DeserializeTimeouts(string? text)
    {
        var values = new Dictionary<string, PowerTimeoutValues>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return values;

        foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('|');
            if (parts.Length != 3) continue;
            if (!Guid.TryParse(parts[0], out _)) continue;
            if (!int.TryParse(parts[1], out int ac)) continue;
            if (!int.TryParse(parts[2], out int dc)) continue;
            values[parts[0]] = new PowerTimeoutValues(ac, dc);
        }

        return values;
    }

    private static void SetPowerTimeoutForAllSchemes(string subgroupAlias, string settingAlias, int acSeconds, int dcSeconds)
    {
        foreach (PowerScheme scheme in GetPowerSchemes())
        {
            if (string.IsNullOrWhiteSpace(scheme.Guid)) continue;
            SetPowerTimeout(scheme.Guid, subgroupAlias, settingAlias, acSeconds, dcSeconds);
        }

        RefreshActivePowerScheme();
    }

    private static void SetPowerTimeout(string schemeGuid, string subgroupAlias, string settingAlias, int acSeconds, int dcSeconds)
    {
        RunHidden("powercfg.exe", $"/setacvalueindex {schemeGuid} {subgroupAlias} {settingAlias} {acSeconds}");
        RunHidden("powercfg.exe", $"/setdcvalueindex {schemeGuid} {subgroupAlias} {settingAlias} {dcSeconds}");
    }

    private static void RestorePowerTimeouts(string? serializedValues, string subgroupAlias, string settingAlias)
    {
        var values = DeserializeTimeouts(serializedValues);
        foreach (var kvp in values)
        {
            SetPowerTimeout(kvp.Key, subgroupAlias, settingAlias, kvp.Value.AcSeconds, kvp.Value.DcSeconds);
        }

        RefreshActivePowerScheme();
    }

    private static void RefreshActivePowerScheme()
    {
        try
        {
            string? activeGuid = GetPowerSchemes().FirstOrDefault(x => x.Active)?.Guid;
            if (!string.IsNullOrWhiteSpace(activeGuid))
            {
                RunHidden("powercfg.exe", "/setactive " + activeGuid);
            }
        }
        catch { }
    }

    private static void SetScreensaverDisabled(bool disabled)
    {
        try
        {
            using RegistryKey desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true) ?? Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");

            if (disabled)
            {
                if (!IsScreensaverDisabled())
                {
                    SetGlobalBehaviorString(ValuePreviousScreenSaveActive, desktop.GetValue("ScreenSaveActive") as string);
                    SetGlobalBehaviorString(ValuePreviousScreenSaveTimeout, desktop.GetValue("ScreenSaveTimeOut") as string);
                    SetGlobalBehaviorString(ValuePreviousScreenSaverExe, desktop.GetValue("SCRNSAVE.EXE") as string);
                }

                desktop.SetValue("ScreenSaveActive", "0", RegistryValueKind.String);
                desktop.Flush();
                SetGlobalBehaviorFlag(ValueDisableScreensaver, true);
            }
            else
            {
                string? previousActive = GetGlobalBehaviorString(ValuePreviousScreenSaveActive);
                string? previousTimeout = GetGlobalBehaviorString(ValuePreviousScreenSaveTimeout);
                string? previousExe = GetGlobalBehaviorString(ValuePreviousScreenSaverExe);

                desktop.SetValue("ScreenSaveActive", string.IsNullOrWhiteSpace(previousActive) ? "1" : previousActive, RegistryValueKind.String);
                if (!string.IsNullOrWhiteSpace(previousTimeout)) desktop.SetValue("ScreenSaveTimeOut", previousTimeout, RegistryValueKind.String);
                if (!string.IsNullOrWhiteSpace(previousExe)) desktop.SetValue("SCRNSAVE.EXE", previousExe, RegistryValueKind.String);
                desktop.Flush();

                SetGlobalBehaviorFlag(ValueDisableScreensaver, false);
            }

            ApplyScreensaverDisabledState();
        }
        catch (Exception ex)
        {
            WriteDiagnostic("SetScreensaverDisabled failed: " + ex.Message);
        }
    }

    private static void ApplyScreensaverDisabledState()
    {
        try
        {
            using RegistryKey? desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            string active = desktop?.GetValue("ScreenSaveActive") as string ?? "1";
            string? timeoutText = desktop?.GetValue("ScreenSaveTimeOut") as string;
            int timeoutSeconds = int.TryParse(timeoutText, out int parsed) && parsed > 0 ? parsed : 600;

            uint flags = SPIF_UPDATEINIFILE | SPIF_SENDCHANGE;
            bool activeOk = SystemParametersInfoSet(SPI_SETSCREENSAVEACTIVE, active == "0" ? 0u : 1u, IntPtr.Zero, flags);
            if (!activeOk)
            {
                WriteDiagnostic("SPI_SETSCREENSAVEACTIVE failed. LastWin32Error=" + Marshal.GetLastWin32Error());
            }

            // Updating only HKCU\Control Panel\Desktop\ScreenSaveTimeOut is not enough on
            // modern Windows. The Control Panel/Settings value and the live user setting are
            // updated through SPI_SETSCREENSAVETIMEOUT.
            if (active != "0")
            {
                bool timeoutOk = SystemParametersInfoSet(SPI_SETSCREENSAVETIMEOUT, (uint)timeoutSeconds, IntPtr.Zero, flags);
                if (!timeoutOk)
                {
                    WriteDiagnostic("SPI_SETSCREENSAVETIMEOUT failed. LastWin32Error=" + Marshal.GetLastWin32Error());
                }
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic("ApplyScreensaverDisabledState failed: " + ex.Message);
        }
    }


    private readonly record struct RunResult(int ExitCode, string Output, string Error);

    private static RunResult RunHidden(string file, string args)
    {
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p == null) return new RunResult(-1, "", "Process.Start returned null.");
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return new RunResult(p.ExitCode, output.Trim(), error.Trim());
        }
        catch (Exception ex) { return new RunResult(-1, "", ex.Message); }
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MicaLovesKPOP", "PowerModeTray", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(System.IO.Path.Combine(dir, "power-mode-tray.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
        }
        catch { }
    }

    private static string ExePath => Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "PowerModeTray.exe";
    private static string StartupCommand => "\"" + ExePath + "\"";
    private static string StartupShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName);

    private static bool IsStartupEnabled()
    {
        try
        {
            if (!File.Exists(StartupShortcutPath)) return false;

            string? target = TryGetShortcutTargetPath(StartupShortcutPath);
            if (string.IsNullOrWhiteSpace(target))
            {
                // If Windows can see the shortcut but COM readback fails, show the user's
                // intended state instead of flickering unchecked.
                return true;
            }

            return PathsEqual(target, ExePath);
        }
        catch { return false; }
    }

    private void ToggleStartup()
    {
        SetStartupEnabled(!IsStartupEnabled());
        _flyout.RefreshContent();
    }

    private static void RepairStartupEntryIfNeeded()
    {
        // Kept for old call sites/documentation. We do not run startup repair during
        // normal launch anymore; the installer and explicit menu toggle own startup state.
    }

    private static void SetStartupEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                CreateStartupShortcut();
                DeleteStartupTask();
                EnsureStartupRunKeyRemoved();
                DeleteStartupApprovedValue();
            }
            else
            {
                DeleteStartupShortcut();
                DeleteStartupTask();
                EnsureStartupRunKeyRemoved();
                DeleteStartupApprovedValue();
            }
        }
        catch (Exception ex)
        {
            Log("SetStartupEnabled failed: " + ex.Message);
        }
    }

    private static void CreateStartupShortcut()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupShortcutPath)!);

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("WScript.Shell COM object is unavailable.");
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(StartupShortcutPath);
            shortcut.TargetPath = ExePath;
            shortcut.Arguments = "--startup";
            shortcut.WorkingDirectory = Path.GetDirectoryName(ExePath) ?? string.Empty;
            shortcut.Description = "Start Power Mode";
            shortcut.IconLocation = ExePath + ",0";
            shortcut.Save();

            Log("Start with Windows startup shortcut enabled.");
        }
        catch (Exception ex)
        {
            Log("Creating startup shortcut failed: " + ex.Message);
            throw;
        }
    }

    private static void DeleteStartupShortcut()
    {
        try
        {
            if (File.Exists(StartupShortcutPath))
            {
                File.Delete(StartupShortcutPath);
            }
        }
        catch (Exception ex)
        {
            Log("Deleting startup shortcut failed: " + ex.Message);
        }
    }

    private static string? TryGetShortcutTargetPath(string shortcutPath)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            return shortcut.TargetPath as string;
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteStartupTask()
    {
        try
        {
            using Process process = StartHiddenProcess("schtasks.exe", "/Delete /TN \"Power Mode\" /F");
            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
        }
        catch { }
    }

    private static Process StartHiddenProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        return Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + fileName);
    }

    private static void EnsureStartupRunKeyRemoved()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRunRegistryPath, true);
            key?.DeleteValue(StartupRunName, false);
        }
        catch { }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left).TrimEnd('\\'), Path.GetFullPath(right).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.TrimEnd('\\'), right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void DeleteStartupApprovedValue()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedRunRegistryPath, true);
            key?.DeleteValue(StartupRunName, false);
        }
        catch { }
    }

    private static void Log(string message)
    {
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicaLovesKPOP", "PowerMode");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "PowerModeTray.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }


    private static void MigrateLegacyAutomaticHistoryFiles()
    {
        try
        {
            string dir = GetAutomaticModeLogsDirectory();
            Directory.CreateDirectory(dir);

            string eventsPath = Path.Combine(dir, AutomaticEventsLogFileName);
            string statsPath = Path.Combine(dir, AutomaticStatsFileName);

            string legacyEventsPath = Path.Combine(dir, LegacyAutomaticEventsLogFileName);
            string legacyStatsPath = Path.Combine(dir, LegacyAutomaticStatsFileName);
            string legacyFlatLogPath = Path.Combine(dir, LegacyAutomaticLogFileName);

            if (!File.Exists(eventsPath))
            {
                if (File.Exists(legacyEventsPath))
                {
                    File.Copy(legacyEventsPath, eventsPath, overwrite: false);
                }
                else if (File.Exists(legacyFlatLogPath))
                {
                    File.Copy(legacyFlatLogPath, eventsPath, overwrite: false);
                }
            }

            if (!File.Exists(statsPath) && File.Exists(legacyStatsPath))
            {
                File.Copy(legacyStatsPath, statsPath, overwrite: false);
            }
        }
        catch { }
    }

    private static string GetAutomaticModeLogsDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicaLovesKPOP", "PowerMode", "logs");
    }

    private static string GetAutomaticModeEventsLogPath() => Path.Combine(GetAutomaticModeLogsDirectory(), AutomaticEventsLogFileName);
    private static string GetAutomaticModeSummaryPath() => Path.Combine(GetAutomaticModeLogsDirectory(), AutomaticSummaryFileName);
    private static string GetAutomaticModeStatsPath() => Path.Combine(GetAutomaticModeLogsDirectory(), AutomaticStatsFileName);

    private static string GetAutomaticModeLogPath() => GetAutomaticModeEventsLogPath();

    private static void AutomaticModeLog(string title, params string[] details)
    {
        try
        {
            string path = GetAutomaticModeEventsLogPath();
            string dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            RotateAutomaticModeLogIfNeeded(path);

            DateTime now = DateTime.Now;
            string dateHeader = now.ToString("yyyy-MM-dd");
            var builder = new StringBuilder();

            string? lastDate = GetLastAutomaticEventLogDate(path);
            if (!string.Equals(lastDate, dateHeader, StringComparison.Ordinal))
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0) builder.AppendLine();
                builder.AppendLine(dateHeader);
            }

            builder.Append(now.ToString("HH:mm:ss"));
            builder.Append("  ");
            builder.AppendLine(title.TrimEnd('.'));

            foreach (string detail in details.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                builder.Append("          ");
                builder.AppendLine(detail.Trim());
            }

            File.AppendAllText(path, builder.ToString());
        }
        catch { }
    }

    private static string? GetLastAutomaticEventLogDate(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            foreach (string line in File.ReadLines(path).Reverse())
            {
                string trimmed = line.Trim();
                if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}-\d{2}$")) return trimmed;
            }
        }
        catch { }
        return null;
    }

    private static void RotateAutomaticModeLogIfNeeded(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < AutomaticLogMaxBytes) return;

            string dir = file.DirectoryName!;
            string baseName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            for (int i = AutomaticLogMaxFiles - 1; i >= 1; i--)
            {
                string source = i == 1 ? path : Path.Combine(dir, baseName + "." + (i - 1) + extension);
                string destination = Path.Combine(dir, baseName + "." + i + extension);
                if (File.Exists(destination)) File.Delete(destination);
                if (File.Exists(source)) File.Move(source, destination);
            }
        }
        catch { }
    }

    private void InitializeAutomaticModeStats(string activeProfile)
    {
        try
        {
            MigrateLegacyAutomaticHistoryFiles();
            DateTime nowUtc = DateTime.UtcNow;
            string state = _automaticModeEnabled ? "Active" : "Manual";
            _automaticStatsState = state;
            _automaticStatsProfile = activeProfile;
            _automaticStatsSinceUtc = nowUtc;

            AutomaticModeStatsData data = LoadAutomaticModeStats();
            data.CurrentState = state;
            data.CurrentProfile = activeProfile;
            data.CurrentSinceUtc = nowUtc;
            TrimAutomaticStatsDays(data, nowUtc);
            SaveAutomaticModeStats(data);
            WriteAutomaticModeSummary(data, nowUtc);
        }
        catch { }
    }

    private void RecordAutomaticModeState(
        string state,
        string profile,
        bool awayEntry = false,
        bool fullscreenBlock = false,
        bool manualProfileChange = false,
        bool automaticSettingChange = false,
        bool automaticEnabled = false,
        bool automaticDisabled = false)
    {
        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool sameState = string.Equals(_automaticStatsState, state, StringComparison.OrdinalIgnoreCase);
            bool sameProfile = PlanNamesEquivalent(_automaticStatsProfile, profile);

            if (sameState && sameProfile && !awayEntry && !fullscreenBlock && !manualProfileChange && !automaticSettingChange && !automaticEnabled && !automaticDisabled)
            {
                return;
            }

            AutomaticModeStatsData data = LoadAutomaticModeStats();
            CloseAutomaticModeStatsSegment(data, nowUtc);

            if (awayEntry) IncrementAutomaticStatsCounter(data, nowUtc, b => b.AwayEntries++);
            if (fullscreenBlock) IncrementAutomaticStatsCounter(data, nowUtc, b => b.FullscreenBlocks++);
            if (manualProfileChange) IncrementAutomaticStatsCounter(data, nowUtc, b => b.ManualProfileChanges++);
            if (automaticSettingChange) IncrementAutomaticStatsCounter(data, nowUtc, b => b.AutomaticSettingChanges++);
            if (automaticEnabled) IncrementAutomaticStatsCounter(data, nowUtc, b => b.AutomaticEnabled++);
            if (automaticDisabled) IncrementAutomaticStatsCounter(data, nowUtc, b => b.AutomaticDisabled++);

            data.CurrentState = state;
            data.CurrentProfile = profile;
            data.CurrentSinceUtc = nowUtc;
            _automaticStatsState = state;
            _automaticStatsProfile = profile;
            _automaticStatsSinceUtc = nowUtc;

            TrimAutomaticStatsDays(data, nowUtc);
            SaveAutomaticModeStats(data);
            WriteAutomaticModeSummary(data, nowUtc);
        }
        catch { }
    }

    private void RecordPowerHistoryCounter(bool manualProfileChange = false, bool automaticSettingChange = false)
    {
        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            AutomaticModeStatsData data = LoadAutomaticModeStats();
            CloseAutomaticModeStatsSegment(data, nowUtc);

            if (manualProfileChange) IncrementAutomaticStatsCounter(data, nowUtc, b => b.ManualProfileChanges++);
            if (automaticSettingChange) IncrementAutomaticStatsCounter(data, nowUtc, b => b.AutomaticSettingChanges++);

            data.CurrentState = _automaticStatsState;
            data.CurrentProfile = _automaticStatsProfile;
            data.CurrentSinceUtc = nowUtc;
            _automaticStatsSinceUtc = nowUtc;

            TrimAutomaticStatsDays(data, nowUtc);
            SaveAutomaticModeStats(data);
            WriteAutomaticModeSummary(data, nowUtc);
        }
        catch { }
    }

    private void CloseAutomaticModeStatsSession()
    {
        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            AutomaticModeStatsData data = LoadAutomaticModeStats();
            CloseAutomaticModeStatsSegment(data, nowUtc);
            data.CurrentState = "Stopped";
            data.CurrentProfile = GetActivePlanName();
            data.CurrentSinceUtc = nowUtc;
            _automaticStatsState = "Stopped";
            _automaticStatsProfile = data.CurrentProfile;
            _automaticStatsSinceUtc = nowUtc;
            TrimAutomaticStatsDays(data, nowUtc);
            SaveAutomaticModeStats(data);
            WriteAutomaticModeSummary(data, nowUtc);
        }
        catch { }
    }

    private static AutomaticModeStatsData LoadAutomaticModeStats()
    {
        try
        {
            string path = GetAutomaticModeStatsPath();
            if (!File.Exists(path)) return new AutomaticModeStatsData();

            string json = File.ReadAllText(path);
            AutomaticModeStatsData? data = JsonSerializer.Deserialize<AutomaticModeStatsData>(json);
            if (data == null) return new AutomaticModeStatsData();
            data.AllTime ??= new AutomaticModeStatsBucket();
            data.Days ??= new Dictionary<string, AutomaticModeStatsBucket>();
            return data;
        }
        catch { return new AutomaticModeStatsData(); }
    }

    private static void SaveAutomaticModeStats(AutomaticModeStatsData data)
    {
        try
        {
            string path = GetAutomaticModeStatsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(data, options));
        }
        catch { }
    }

    private static void CloseAutomaticModeStatsSegment(AutomaticModeStatsData data, DateTime nowUtc)
    {
        try
        {
            DateTime startUtc = data.CurrentSinceUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(data.CurrentSinceUtc, DateTimeKind.Utc)
                : data.CurrentSinceUtc.ToUniversalTime();

            if (startUtc > nowUtc) startUtc = nowUtc;
            if ((nowUtc - startUtc).TotalSeconds < 1) return;

            AddAutomaticModeDuration(data, data.CurrentState, data.CurrentProfile, startUtc, nowUtc);
        }
        catch { }
    }

    private static void AddAutomaticModeDuration(AutomaticModeStatsData data, string state, string profile, DateTime startUtc, DateTime endUtc)
    {
        if (endUtc <= startUtc) return;

        DateTime startLocal = startUtc.ToLocalTime();
        DateTime endLocal = endUtc.ToLocalTime();
        DateTime cursor = startLocal;

        while (cursor < endLocal)
        {
            DateTime next = cursor.Date.AddDays(1);
            if (next > endLocal) next = endLocal;

            long seconds = Math.Max(0, (long)Math.Round((next - cursor).TotalSeconds));
            if (seconds > 0)
            {
                string dayKey = cursor.ToString("yyyy-MM-dd");
                AutomaticModeStatsBucket day = GetAutomaticStatsDay(data, dayKey);
                AddAutomaticModeDurationToBucket(day, state, profile, seconds);
                AddAutomaticModeDurationToBucket(data.AllTime, state, profile, seconds);
            }

            cursor = next;
        }
    }

    private static void AddAutomaticModeDurationToBucket(AutomaticModeStatsBucket bucket, string state, string profile, long seconds)
    {
        switch (state)
        {
            case "Manual":
                bucket.ManualSeconds += seconds;
                break;
            case "Active":
                bucket.ActiveSeconds += seconds;
                break;
            case "Away":
                bucket.AwaySeconds += seconds;
                break;
            case "Blocked":
                bucket.BlockedSeconds += seconds;
                break;
            default:
                break;
        }

        if (!string.IsNullOrWhiteSpace(profile) && !string.Equals(state, "Stopped", StringComparison.OrdinalIgnoreCase))
        {
            if (!bucket.ProfileSeconds.ContainsKey(profile)) bucket.ProfileSeconds[profile] = 0;
            bucket.ProfileSeconds[profile] += seconds;
        }
    }

    private static void IncrementAutomaticStatsCounter(AutomaticModeStatsData data, DateTime nowUtc, Action<AutomaticModeStatsBucket> increment)
    {
        string dayKey = nowUtc.ToLocalTime().ToString("yyyy-MM-dd");
        increment(GetAutomaticStatsDay(data, dayKey));
        increment(data.AllTime);
    }

    private static AutomaticModeStatsBucket GetAutomaticStatsDay(AutomaticModeStatsData data, string dayKey)
    {
        if (!data.Days.TryGetValue(dayKey, out AutomaticModeStatsBucket? bucket) || bucket == null)
        {
            bucket = new AutomaticModeStatsBucket();
            data.Days[dayKey] = bucket;
        }
        bucket.ProfileSeconds ??= new Dictionary<string, long>();
        return bucket;
    }

    private static void TrimAutomaticStatsDays(AutomaticModeStatsData data, DateTime nowUtc)
    {
        try
        {
            DateTime keepFrom = nowUtc.ToLocalTime().Date.AddDays(-AutomaticStatsDaysToKeep + 1);
            foreach (string key in data.Days.Keys.ToList())
            {
                if (DateTime.TryParse(key, out DateTime day) && day.Date < keepFrom)
                {
                    data.Days.Remove(key);
                }
            }
        }
        catch { }
    }

    private static void WriteAutomaticModeSummary(AutomaticModeStatsData data, DateTime nowUtc)
    {
        try
        {
            AutomaticModeStatsData snapshot = CloneAutomaticModeStats(data);
            CloseAutomaticModeStatsSegment(snapshot, nowUtc);

            var builder = new StringBuilder();
            builder.AppendLine("Power Mode History");
            builder.AppendLine("Updated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine("Current");
            builder.AppendLine("  State:   " + FormatAutomaticStatsState(snapshot.CurrentState));
            builder.AppendLine("  Profile: " + (string.IsNullOrWhiteSpace(snapshot.CurrentProfile) ? "Unknown" : snapshot.CurrentProfile));
            builder.AppendLine("  Since:   " + snapshot.CurrentSinceUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();

            AppendAutomaticStatsSection(builder, "Today", GetAutomaticStatsForDays(snapshot, 1));
            AppendAutomaticStatsSection(builder, "Last 7 days", GetAutomaticStatsForDays(snapshot, 7));
            AppendAutomaticStatsSection(builder, "Last 30 days", GetAutomaticStatsForDays(snapshot, 30));
            AppendAutomaticStatsSection(builder, "Last 365 days", GetAutomaticStatsForDays(snapshot, 365));
            AppendAutomaticStatsSection(builder, "All time", snapshot.AllTime);

            builder.AppendLine("Time by profile (all time)");
            foreach (var item in snapshot.AllTime.ProfileSeconds.OrderByDescending(kvp => kvp.Value))
            {
                builder.AppendLine("  " + item.Key.PadRight(28) + FormatDurationCompact(item.Value));
            }

            string path = GetAutomaticModeSummaryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, builder.ToString());
        }
        catch { }
    }

    private static AutomaticModeStatsData CloneAutomaticModeStats(AutomaticModeStatsData data)
    {
        var clone = new AutomaticModeStatsData
        {
            Version = data.Version,
            CurrentState = data.CurrentState,
            CurrentProfile = data.CurrentProfile,
            CurrentSinceUtc = data.CurrentSinceUtc,
            AllTime = CloneAutomaticStatsBucket(data.AllTime),
            Days = data.Days.ToDictionary(kvp => kvp.Key, kvp => CloneAutomaticStatsBucket(kvp.Value))
        };
        return clone;
    }

    private static AutomaticModeStatsBucket CloneAutomaticStatsBucket(AutomaticModeStatsBucket source)
    {
        return new AutomaticModeStatsBucket
        {
            ManualSeconds = source.ManualSeconds,
            ActiveSeconds = source.ActiveSeconds,
            AwaySeconds = source.AwaySeconds,
            BlockedSeconds = source.BlockedSeconds,
            AwayEntries = source.AwayEntries,
            FullscreenBlocks = source.FullscreenBlocks,
            ManualProfileChanges = source.ManualProfileChanges,
            AutomaticSettingChanges = source.AutomaticSettingChanges,
            AutomaticEnabled = source.AutomaticEnabled,
            AutomaticDisabled = source.AutomaticDisabled,
            ProfileSeconds = new Dictionary<string, long>(source.ProfileSeconds ?? new Dictionary<string, long>())
        };
    }

    private static AutomaticModeStatsBucket GetAutomaticStatsForDays(AutomaticModeStatsData data, int days)
    {
        DateTime from = DateTime.Now.Date.AddDays(-days + 1);
        var result = new AutomaticModeStatsBucket();

        foreach (var item in data.Days)
        {
            if (DateTime.TryParse(item.Key, out DateTime day) && day.Date >= from)
            {
                MergeAutomaticStatsBucket(result, item.Value);
            }
        }

        return result;
    }

    private static void MergeAutomaticStatsBucket(AutomaticModeStatsBucket target, AutomaticModeStatsBucket source)
    {
        target.ManualSeconds += source.ManualSeconds;
        target.ActiveSeconds += source.ActiveSeconds;
        target.AwaySeconds += source.AwaySeconds;
        target.BlockedSeconds += source.BlockedSeconds;
        target.AwayEntries += source.AwayEntries;
        target.FullscreenBlocks += source.FullscreenBlocks;
        target.ManualProfileChanges += source.ManualProfileChanges;
        target.AutomaticSettingChanges += source.AutomaticSettingChanges;
        target.AutomaticEnabled += source.AutomaticEnabled;
        target.AutomaticDisabled += source.AutomaticDisabled;

        foreach (var profile in source.ProfileSeconds)
        {
            if (!target.ProfileSeconds.ContainsKey(profile.Key)) target.ProfileSeconds[profile.Key] = 0;
            target.ProfileSeconds[profile.Key] += profile.Value;
        }
    }

    private static void AppendAutomaticStatsSection(StringBuilder builder, string title, AutomaticModeStatsBucket stats)
    {
        builder.AppendLine(title);
        builder.AppendLine("  Manual:              " + FormatDurationCompact(stats.ManualSeconds));
        builder.AppendLine("  Automatic active:    " + FormatDurationCompact(stats.ActiveSeconds));
        builder.AppendLine("  Away:                " + FormatDurationCompact(stats.AwaySeconds));
        builder.AppendLine("  Fullscreen paused:   " + FormatDurationCompact(stats.BlockedSeconds));
        builder.AppendLine("  Away switches:       " + stats.AwayEntries);
        builder.AppendLine("  Fullscreen pauses:   " + stats.FullscreenBlocks);
        builder.AppendLine("  Manual changes:      " + stats.ManualProfileChanges);
        builder.AppendLine("  Automatic changes:   " + stats.AutomaticSettingChanges);
        builder.AppendLine("  Automatic enabled:   " + stats.AutomaticEnabled);
        builder.AppendLine("  Automatic disabled:  " + stats.AutomaticDisabled);
        builder.AppendLine();
    }

    private static string FormatAutomaticStatsState(string state)
        => state switch
        {
            "Manual" => "Manual",
            "Active" => "Automatic active",
            "Away" => "Away",
            "Blocked" => "Fullscreen paused",
            "Stopped" => "App not running",
            _ => state
        };

    private static string FormatDurationCompact(long totalSeconds)
    {
        if (totalSeconds <= 0) return "0m";

        TimeSpan span = TimeSpan.FromSeconds(totalSeconds);
        int days = (int)span.TotalDays;
        int hours = span.Hours;
        int minutes = span.Minutes;

        if (days > 0)
        {
            return hours > 0 ? days + "d " + hours + "h" : days + "d";
        }

        if (hours > 0)
        {
            return minutes > 0 ? hours + "h " + minutes + "m" : hours + "h";
        }

        return Math.Max(1, minutes) + "m";
    }

    private static void OpenAutomaticModeHistory()
    {
        try
        {
            MigrateLegacyAutomaticHistoryFiles();
            string dir = GetAutomaticModeLogsDirectory();
            Directory.CreateDirectory(dir);

            AutomaticModeStatsData data = LoadAutomaticModeStats();
            WriteAutomaticModeSummary(data, DateTime.UtcNow);

            string eventsPath = GetAutomaticModeEventsLogPath();
            if (!File.Exists(eventsPath))
            {
                File.WriteAllText(eventsPath, DateTime.Now.ToString("yyyy-MM-dd") + Environment.NewLine);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + dir + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log("OpenAutomaticModeHistory failed: " + ex.Message);
        }
    }

    private static void RemoveFromSystemTray()
    {
        SetStartupEnabled(false);
        CurrentApplicationExit();
    }

    private static void OpenPowerSettings() => StartShell("ms-settings:powersleep");
    private static void OpenScreenAndSleepSettings() => StartShell("ms-settings:powersleep");

    private static void OpenScreenSaverSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "desk.cpl,,@screensaver",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static void OpenTaskbarSettings() => StartShell("ms-settings:taskbar");
    private static void StartShell(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } }

    private static void OpenPowerModeSettings()
    {
        try
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                DirectoryInfo? parent = Directory.GetParent(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar));
                if (parent == null) break;
                string candidate = System.IO.Path.Combine(parent.FullName, "Launch-TweakUI.ps1");
                if (File.Exists(candidate))
                {
                    Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + candidate + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    return;
                }
                dir = parent.FullName;
            }
            OpenPowerSettings();
        }
        catch { OpenPowerSettings(); }
    }

    private static bool IsTrayPromoted()
    {
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root == null) return false;
            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? k = root.OpenSubKey(name);
                if (k == null) continue;
                if (KeyMatchesThisIcon(k))
                {
                    object? v = k.GetValue("IsPromoted");
                    return v != null && Convert.ToInt32(v) == 1;
                }
            }
        }
        catch { }
        return false;
    }

    private bool RequestTrayVisibility()
    {
        bool changed = false;
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true);
            if (root == null) return false;
            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? k = root.OpenSubKey(name, true);
                if (k == null) continue;
                if (KeyMatchesThisIcon(k))
                {
                    k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    changed = true;
                }
            }
            _flyout.RefreshContent();
        }
        catch { }
        return changed;
    }

    private static bool KeyMatchesThisIcon(RegistryKey k)
    {
        string exe = Convert.ToString(k.GetValue("ExecutablePath", "")) ?? "";
        string tip = Convert.ToString(k.GetValue("InitialTooltip", "")) ?? "";
        if (string.IsNullOrWhiteSpace(tip)) tip = Convert.ToString(k.GetValue("ToolTip", "")) ?? "";
        return exe.IndexOf("PowerModeTray.exe", StringComparison.OrdinalIgnoreCase) >= 0 || (tip.StartsWith("Power Mode", StringComparison.OrdinalIgnoreCase) || tip.StartsWith("Power mode", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetNotifyIconRect(out System.Drawing.Rectangle rect)
    {
        rect = System.Drawing.Rectangle.Empty;
        try
        {
            object? window = GetInstanceMemberValue(_notifyIcon, "_window", "window");
            object? idValue = GetInstanceMemberValue(_notifyIcon, "_id", "id");
            if (window == null || idValue == null) return false;

            object? handleValue = GetInstanceMemberValue(window, "Handle", "_handle", "handle");
            if (handleValue is not IntPtr hwnd || hwnd == IntPtr.Zero) return false;

            uint id = Convert.ToUInt32(idValue);
            NotifyIconIdentifier identifier = new NotifyIconIdentifier
            {
                cbSize = Marshal.SizeOf<NotifyIconIdentifier>(),
                hWnd = hwnd,
                uID = id,
                guidItem = Guid.Empty
            };

            int hr = Shell_NotifyIconGetRect(ref identifier, out NotifyIconNativeRect nativeRect);
            if (hr != 0) return false;

            System.Drawing.Rectangle candidate = System.Drawing.Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
            if (candidate.Width <= 0 || candidate.Height <= 0) return false;
            rect = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? GetInstanceMemberValue(object source, params string[] names)
    {
        Type? type = source.GetType();
        while (type != null)
        {
            foreach (string name in names)
            {
                try
                {
                    var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (field != null) return field.GetValue(source);

                    var property = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (property != null) return property.GetValue(source);
                }
                catch { }
            }
            type = type.BaseType;
        }
        return null;
    }

    private static void CurrentApplicationExit()
    {
        try { Microsoft.UI.Xaml.Application.Current.Exit(); } catch { Environment.Exit(0); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppNativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    private static extern bool GetAppWindowRect(IntPtr hWnd, out AppNativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconNativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", SetLastError = false)]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NotifyIconNativeRect iconLocation);
}

internal enum FlyoutMode
{
    Main,
    Context
}

internal enum ContextMenuPage
{
    Main,
    ScreenSaverTimeout,
    DisplayTimeout,
    SleepTimeout
}

internal sealed class FlyoutWindow : Window
{
    private readonly PlanInfo[] _plans;
    private readonly Func<string> _getActivePlanName;
    private readonly Action<string> _setPlan;
    private readonly Func<bool> _isLightMode;
    private readonly Func<bool> _isStartupEnabled;
    private readonly Action _toggleStartup;
    private readonly Func<bool> _isAutomaticModeEnabled;
    private readonly Action _toggleAutomaticMode;
    private readonly Func<string> _getAutomaticStateText;
    private readonly Func<string> _getAutomaticReason;
    private readonly Func<string> _getAutomaticNormalProfile;
    private readonly Func<string> _getAutomaticAfkProfile;
    private readonly Func<int> _getAutomaticAfkDelaySeconds;
    private readonly Action _cycleAutomaticNormalProfilePrevious;
    private readonly Action _cycleAutomaticNormalProfileNext;
    private readonly Action _cycleAutomaticAfkProfilePrevious;
    private readonly Action _cycleAutomaticAfkProfileNext;
    private readonly Action _cycleAutomaticAfkDelaySecondsPrevious;
    private readonly Action _cycleAutomaticAfkDelaySecondsNext;
    private readonly Action _openAutomaticHistory;
    private readonly Func<int> _getScreenSaverTimeoutSeconds;
    private readonly Action<int> _setScreenSaverTimeoutSeconds;
    private readonly Action _openScreenSaverSettings;
    private readonly Func<int> _getDisplayTimeoutSeconds;
    private readonly Action<int> _setDisplayTimeoutSeconds;
    private readonly Func<int> _getSleepTimeoutSeconds;
    private readonly Action<int> _setSleepTimeoutSeconds;
    private readonly Action _openScreenAndSleepSettings;
    private readonly Action _openPowerSettings;
    private readonly Action _quit;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private DateTime _ignoreDeactivateUntilUtc = DateTime.MinValue;
    private readonly Forms.Timer _outsideClickTimer;
    private Forms.Timer? _closeAnimationTimer;
    private bool _isClosing;
    private double _openTranslateX;
    private double _openTranslateY = DefaultFlyoutMotionDistance;
    private ContextMenuPage _contextMenuPage = ContextMenuPage.Main;

    // Semantic layout tokens. These are intentionally shared instead of
    // one-off offsets: the title, description, power-mode row text, and
    // footer action all start on the same content column.
    private const double ContentInset = 14;
    private const double PlanRowLeftInset = 3;
    private const double PlanRowRightInset = 5;
    private const double PlanTextInset = ContentInset - PlanRowLeftInset;
    private const double ManualSummaryHeight = 56;
    private const double ManualMainUpperHeight = HeaderHeight + 34 + ManualSummaryHeight + (5 * PlanRowHeight);
    private const double AutomaticSummaryHeight = 76;
    private const double AutomaticMainUpperHeight = HeaderHeight + 34 + AutomaticSummaryHeight + (3 * PlanRowHeight);
    private const double HeaderHeight = 59;
    private const double TitleTopInset = 12;
    private const double DescriptionTopInset = 33;
    private const double PlanRowHeight = 44;
    private const double PlanHighlightHeight = 40;
    private const double PlanIndicatorHeight = 15;
    private const double FooterHeight = 49;

    // Right-click surface: compact, content-sized settings surface.
    private const double ContextMenuMinWidth = 260;
    private const double ContextMenuMaxWidth = 360;
    private const double ContextMenuHeaderHeight = 34;
    private const double ContextMenuRowHeight = 36;
    private const double ContextMenuSettingRowHeight = 38;
    private const double ContextMenuPickerHeaderHeight = 40;
    private const double ContextMenuSeparatorHeight = 9;
    private const double ContextMenuIconInset = 14;
    private const double ContextMenuTextGap = 10;
    private const double ContextMenuValueGap = 16;
    private const double ContextMenuRightInset = 12;
    private const double ContextMenuChevronWidth = 18;
    private const double ContextMenuPickerValueHeight = 38;
    private const double ContextMenuPickerSliderHeight = 54;
    // Windows taskbar flyouts visibly travel up/down from the taskbar, not just
    // fade in place. Keep the motion vector tied to the taskbar edge.
    private const double DefaultFlyoutMotionDistance = 56;

    public bool IsFlyoutVisible { get; private set; }
    public bool IsClosing => _isClosing;
    public FlyoutMode CurrentMode { get; private set; } = FlyoutMode.Main;

    public FlyoutWindow(
        PlanInfo[] plans,
        Func<string> getActivePlanName,
        Action<string> setPlan,
        Func<bool> isLightMode,
        Func<bool> isStartupEnabled,
        Action toggleStartup,
        Func<bool> isAutomaticModeEnabled,
        Action toggleAutomaticMode,
        Func<string> getAutomaticStateText,
        Func<string> getAutomaticReason,
        Func<string> getAutomaticNormalProfile,
        Func<string> getAutomaticAfkProfile,
        Func<int> getAutomaticAfkDelaySeconds,
        Action cycleAutomaticNormalProfilePrevious,
        Action cycleAutomaticNormalProfileNext,
        Action cycleAutomaticAfkProfilePrevious,
        Action cycleAutomaticAfkProfileNext,
        Action cycleAutomaticAfkDelaySecondsPrevious,
        Action cycleAutomaticAfkDelaySecondsNext,
        Action openAutomaticHistory,
        Func<int> getScreenSaverTimeoutSeconds,
        Action<int> setScreenSaverTimeoutSeconds,
        Action openScreenSaverSettings,
        Func<int> getDisplayTimeoutSeconds,
        Action<int> setDisplayTimeoutSeconds,
        Func<int> getSleepTimeoutSeconds,
        Action<int> setSleepTimeoutSeconds,
        Action openScreenAndSleepSettings,
        Action openPowerSettings,
        Action quit)
    {
        _plans = plans;
        _getActivePlanName = getActivePlanName;
        _setPlan = setPlan;
        _isLightMode = isLightMode;
        _isStartupEnabled = isStartupEnabled;
        _toggleStartup = toggleStartup;
        _isAutomaticModeEnabled = isAutomaticModeEnabled;
        _toggleAutomaticMode = toggleAutomaticMode;
        _getAutomaticStateText = getAutomaticStateText;
        _getAutomaticReason = getAutomaticReason;
        _getAutomaticNormalProfile = getAutomaticNormalProfile;
        _getAutomaticAfkProfile = getAutomaticAfkProfile;
        _getAutomaticAfkDelaySeconds = getAutomaticAfkDelaySeconds;
        _cycleAutomaticNormalProfilePrevious = cycleAutomaticNormalProfilePrevious;
        _cycleAutomaticNormalProfileNext = cycleAutomaticNormalProfileNext;
        _cycleAutomaticAfkProfilePrevious = cycleAutomaticAfkProfilePrevious;
        _cycleAutomaticAfkProfileNext = cycleAutomaticAfkProfileNext;
        _cycleAutomaticAfkDelaySecondsPrevious = cycleAutomaticAfkDelaySecondsPrevious;
        _cycleAutomaticAfkDelaySecondsNext = cycleAutomaticAfkDelaySecondsNext;
        _openAutomaticHistory = openAutomaticHistory;
        _getScreenSaverTimeoutSeconds = getScreenSaverTimeoutSeconds;
        _setScreenSaverTimeoutSeconds = setScreenSaverTimeoutSeconds;
        _openScreenSaverSettings = openScreenSaverSettings;
        _getDisplayTimeoutSeconds = getDisplayTimeoutSeconds;
        _setDisplayTimeoutSeconds = setDisplayTimeoutSeconds;
        _getSleepTimeoutSeconds = getSleepTimeoutSeconds;
        _setSleepTimeoutSeconds = setSleepTimeoutSeconds;
        _openScreenAndSleepSettings = openScreenAndSleepSettings;
        _openPowerSettings = openPowerSettings;
        _quit = quit;

        Title = "Power Mode";
        ExtendsContentIntoTitleBar = true;

        _hwnd = WindowNative.GetWindowHandle(this);
        WindowId id = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        _appWindow.IsShownInSwitchers = false;
        ApplyWindows11FlyoutChrome(_hwnd);
        _appWindow.Resize(new SizeInt32(300, ComputePreferredHeight(FlyoutMode.Main)));
        Activated += OnActivated;
        _outsideClickTimer = new Forms.Timer { Interval = 120 };
        _outsideClickTimer.Tick += (_, _) => CheckOutsideActivation();
        RefreshContent();
        HideFlyout(animated: false);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (IsFlyoutVisible && args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (DateTime.UtcNow < _ignoreDeactivateUntilUtc) return;
            HideFlyout();
        }
    }

    private void CheckOutsideActivation()
    {
        if (!IsFlyoutVisible) return;
        if (DateTime.UtcNow < _ignoreDeactivateUntilUtc) return;
        try
        {
            // Do not close just because the taskbar briefly keeps foreground focus
            // after the notification icon click. Close only after actual outside
            // activation or an outside mouse click.
            if (IsCursorInsideWindow()) return;

            bool mouseDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 ||
                             (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0 ||
                             (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
            if (!mouseDown) return;

            IntPtr fg = GetForegroundWindow();
            if (fg != _hwnd)
            {
                HideFlyout();
            }
        }
        catch { }
    }

    public void ShowNear(System.Drawing.Point anchor, FlyoutMode mode)
    {
        StopCloseAnimationTimer();
        _ignoreDeactivateUntilUtc = DateTime.UtcNow.AddMilliseconds(650);
        CurrentMode = mode;
        RefreshContent();

        Forms.Screen screen = Forms.Screen.FromPoint(anchor);
        int width = ComputePreferredWidth(mode);
        int height = ComputePreferredHeight(mode);
        System.Drawing.Rectangle work = screen.WorkingArea;
        System.Drawing.Rectangle bounds = screen.Bounds;

        int margin = 12;
        int gap = 13;
        int x = anchor.X - (width / 2);
        int y = work.Bottom - height - gap;
        ConfigureFlyoutMotion(TaskbarEdge.Bottom);

        if (TryGetNearestTaskbar(anchor, screen, out System.Drawing.Rectangle taskbar, out TaskbarEdge edge))
        {
            switch (edge)
            {
                case TaskbarEdge.Bottom:
                    ConfigureFlyoutMotion(TaskbarEdge.Bottom);
                    // Prefer the work-area edge like Windows shell flyouts. If the taskbar is
                    // auto-hidden and Windows reports only the thin hidden strip, estimate the
                    // visible taskbar edge from the tray-icon click position instead of assuming
                    // a hardcoded Windows 10/11 taskbar height.
                    x = anchor.X > work.Right - 260 ? work.Right - width - margin : anchor.X - (width / 2);
                    int bottomEdge = taskbar.Top;
                    bool taskbarLooksHidden = taskbar.Height <= 8;
                    bool taskbarLooksBogus = taskbar.Height > 140 || taskbar.Top < bounds.Bottom - 160;
                    if (taskbarLooksHidden || taskbarLooksBogus)
                    {
                        // Auto-hide and shell-skin taskbars can report misleading
                        // taskbar rectangles. When that happens, estimate the visible
                        // taskbar top from the actual tray-icon click position instead
                        // of assuming a Windows 10/11 taskbar height.
                        int distanceFromBottomToClick = Math.Max(1, bounds.Bottom - anchor.Y);
                        int estimatedVisibleHeight = Clamp(2 * distanceFromBottomToClick, 32, 96);
                        bottomEdge = bounds.Bottom - estimatedVisibleHeight;
                    }
                    y = bottomEdge - height - gap;
                    break;
                case TaskbarEdge.Top:
                    ConfigureFlyoutMotion(TaskbarEdge.Top);
                    x = anchor.X > work.Right - 260 ? work.Right - width - margin : anchor.X - (width / 2);
                    y = taskbar.Bottom + gap;
                    break;
                case TaskbarEdge.Left:
                    ConfigureFlyoutMotion(TaskbarEdge.Left);
                    x = taskbar.Right + gap;
                    y = anchor.Y - (height / 2);
                    break;
                case TaskbarEdge.Right:
                    ConfigureFlyoutMotion(TaskbarEdge.Right);
                    x = taskbar.Left - width - gap;
                    y = anchor.Y - (height / 2);
                    break;
            }
        }

        x = Clamp(x - 1, bounds.Left + margin, bounds.Right - width - margin);
        y = Clamp(y, bounds.Top + margin, bounds.Bottom - height - margin);

        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        ApplyRoundedWindowRegion(width, height, 8);
        IsFlyoutVisible = true;

        // Shell flyouts sit above the taskbar overflow/context surfaces. Make this
        // window topmost while visible so it doesn't open behind the tray menu,
        // then let normal outside-click logic hide it again.
        ShowWindow(_hwnd, 5);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
        SetForegroundWindow(_hwnd);
        Activate();
        BeginOpenAnimation();
        _outsideClickTimer.Start();
    }


    public void ShowNativeContextMenuNear(System.Drawing.Point anchor, System.Drawing.Rectangle? trayIconRect = null)
    {
        // The platform HMENU path follows dark mode now, but on customized
        // Windows 11/10 shells it still keeps the classic compact 22 px row
        // rhythm. Use our own WinUI command surface for the tray context menu
        // so row height, hover fill, separator rhythm, and click anchoring can
        // match the current shell menu more closely.
        ShowContextFlyoutNear(anchor, trayIconRect);
    }

    private void ShowContextFlyoutNear(System.Drawing.Point anchor, System.Drawing.Rectangle? trayIconRect = null)
    {
        StopCloseAnimationTimer();
        _ignoreDeactivateUntilUtc = DateTime.UtcNow.AddMilliseconds(650);
        CurrentMode = FlyoutMode.Context;
        _contextMenuPage = ContextMenuPage.Main;
        RefreshContent();

        Forms.Screen screen = Forms.Screen.FromPoint(anchor);
        System.Drawing.Rectangle bounds = screen.Bounds;
        System.Drawing.Rectangle work = screen.WorkingArea;
        int width = ComputePreferredWidth(FlyoutMode.Context);
        int height = ComputePreferredHeight(FlyoutMode.Context);
        int gap = 13; // Match the left-click taskbar flyout bottom gap.

        int x = Clamp(anchor.X - width / 2, bounds.Left, bounds.Right - width);
        int y = Clamp(anchor.Y - height, bounds.Top + 4, bounds.Bottom - height);
        ConfigureFlyoutMotion(TaskbarEdge.Bottom);

        bool useTaskbarEdgePlacement = false;
        TaskbarEdge edge = TaskbarEdge.Bottom;
        System.Drawing.Rectangle taskbar = System.Drawing.Rectangle.Empty;

        if (trayIconRect.HasValue && trayIconRect.Value.Width > 0 && trayIconRect.Value.Height > 0)
        {
            System.Drawing.Rectangle candidate = trayIconRect.Value;
            System.Drawing.Point candidateCenter = new System.Drawing.Point(candidate.Left + candidate.Width / 2, candidate.Top + candidate.Height / 2);
            Forms.Screen candidateScreen = Forms.Screen.FromPoint(candidateCenter);
            if (TryGetNearestTaskbar(candidateCenter, candidateScreen, out System.Drawing.Rectangle candidateTaskbar, out TaskbarEdge candidateEdge)
                && IsSourceActuallyOnTaskbar(candidate, candidateTaskbar, candidateEdge)
                && IsPointNearRectangle(anchor, candidate, 24))
            {
                taskbar = candidateTaskbar;
                edge = candidateEdge;
                useTaskbarEdgePlacement = true;
            }
        }

        if (useTaskbarEdgePlacement)
        {
            ConfigureFlyoutMotion(edge);
            switch (edge)
            {
                case TaskbarEdge.Bottom:
                    int bottomEdge = taskbar.Top;
                    bool bottomTaskbarLooksHidden = taskbar.Height <= 8;
                    bool bottomTaskbarLooksBogus = taskbar.Height > 140 || taskbar.Top < bounds.Bottom - 160;
                    if (bottomTaskbarLooksHidden || bottomTaskbarLooksBogus)
                    {
                        int distanceFromBottomToClick = Math.Max(1, bounds.Bottom - anchor.Y);
                        int estimatedVisibleHeight = Clamp(2 * distanceFromBottomToClick, 32, 96);
                        bottomEdge = bounds.Bottom - estimatedVisibleHeight;
                    }
                    if (bottomEdge <= bounds.Top || bottomEdge > bounds.Bottom) bottomEdge = work.Bottom;
                    y = bottomEdge - height - gap;
                    break;

                case TaskbarEdge.Top:
                    y = taskbar.Bottom + gap;
                    break;

                case TaskbarEdge.Left:
                    x = taskbar.Right + gap;
                    y = Clamp(anchor.Y - height / 2, bounds.Top + 4, bounds.Bottom - height - 4);
                    break;

                case TaskbarEdge.Right:
                    x = taskbar.Left - width - gap;
                    y = Clamp(anchor.Y - height / 2, bounds.Top + 4, bounds.Bottom - height - 4);
                    break;
            }
        }

        x = Clamp(x, bounds.Left, bounds.Right - width);
        y = Clamp(y, bounds.Top + 4, bounds.Bottom - height - 4);

        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        ApplyRoundedWindowRegion(width, height, 8);
        IsFlyoutVisible = true;
        ShowWindow(_hwnd, 5);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
        SetForegroundWindow(_hwnd);
        Activate();
        BeginOpenAnimation();
        _outsideClickTimer.Start();
    }

    private static System.Drawing.Point ComputeNativeContextMenuAnchor(System.Drawing.Point anchor, System.Drawing.Rectangle? trayIconRect, out uint alignmentFlags)
    {
        // The raw cursor/click position is the trustworthy anchor inside the hidden-icons
        // overflow. Shell_NotifyIconGetRect can report a stale/direct-taskbar rectangle
        // there, which makes the menu inherit the visible-taskbar placement behavior.
        System.Drawing.Rectangle clickSource = new System.Drawing.Rectangle(anchor.X, anchor.Y, 1, 1);
        System.Drawing.Rectangle source = clickSource;
        bool useTaskbarEdgePlacement = false;
        TaskbarEdge edge = TaskbarEdge.Bottom;
        System.Drawing.Rectangle taskbar = System.Drawing.Rectangle.Empty;

        if (trayIconRect.HasValue && trayIconRect.Value.Width > 0 && trayIconRect.Value.Height > 0)
        {
            System.Drawing.Rectangle candidate = trayIconRect.Value;
            System.Drawing.Point candidateCenter = new System.Drawing.Point(candidate.Left + candidate.Width / 2, candidate.Top + candidate.Height / 2);
            Forms.Screen candidateScreen = Forms.Screen.FromPoint(candidateCenter);
            if (TryGetNearestTaskbar(candidateCenter, candidateScreen, out System.Drawing.Rectangle candidateTaskbar, out TaskbarEdge candidateEdge)
                && IsSourceActuallyOnTaskbar(candidate, candidateTaskbar, candidateEdge)
                && IsPointNearRectangle(anchor, candidate, 24))
            {
                source = candidate;
                taskbar = candidateTaskbar;
                edge = candidateEdge;
                useTaskbarEdgePlacement = true;
            }
        }

        System.Drawing.Point center = new System.Drawing.Point(source.Left + source.Width / 2, source.Top + source.Height / 2);
        Forms.Screen screen = Forms.Screen.FromPoint(center);
        System.Drawing.Rectangle bounds = screen.Bounds;
        System.Drawing.Rectangle work = screen.WorkingArea;
        int margin = 8;
        int approximateNativeMenuWidth = EstimateNativeContextMenuWidth();

        if (!useTaskbarEdgePlacement)
        {
            // Hidden-icons overflow / in-panel right-click: center on the actual click
            // and open upward from that click point. This matches the user's observed
            // Windows overflow behavior much better than taskbar-edge alignment.
            int centeredX = anchor.X - approximateNativeMenuWidth / 2;
            int x = Clamp(centeredX, bounds.Left + margin, bounds.Right - approximateNativeMenuWidth - margin);
            int y = Clamp(anchor.Y, bounds.Top + margin, bounds.Bottom - margin);
            alignmentFlags = TPM_LEFTALIGN | TPM_BOTTOMALIGN;
            return new System.Drawing.Point(x, y);
        }

        int nativeX = ComputeCenteredNativeContextMenuX(source, bounds, work, margin, approximateNativeMenuWidth);
        int nativeY = Clamp(center.Y, bounds.Top + margin, bounds.Bottom - margin);
        alignmentFlags = TPM_LEFTALIGN | TPM_TOPALIGN;

        switch (edge)
        {
            case TaskbarEdge.Bottom:
                alignmentFlags = TPM_LEFTALIGN | TPM_BOTTOMALIGN;
                int bottomEdge = taskbar.Top;
                bool bottomTaskbarLooksHidden = taskbar.Height <= 8;
                bool bottomTaskbarLooksBogus = taskbar.Height > 140 || taskbar.Top < bounds.Bottom - 160;
                if (bottomTaskbarLooksHidden || bottomTaskbarLooksBogus)
                {
                    int distanceFromBottomToClick = Math.Max(1, bounds.Bottom - center.Y);
                    int estimatedVisibleHeight = Clamp(2 * distanceFromBottomToClick, 32, 96);
                    bottomEdge = bounds.Bottom - estimatedVisibleHeight;
                }
                if (bottomEdge <= bounds.Top || bottomEdge > bounds.Bottom) bottomEdge = work.Bottom;
                nativeY = Clamp(bottomEdge, bounds.Top + margin, bounds.Bottom - margin);
                break;

            case TaskbarEdge.Top:
                alignmentFlags = TPM_LEFTALIGN | TPM_TOPALIGN;
                int topEdge = taskbar.Bottom;
                if (topEdge < bounds.Top || topEdge >= bounds.Bottom) topEdge = work.Top;
                nativeY = Clamp(topEdge, bounds.Top + margin, bounds.Bottom - margin);
                break;

            case TaskbarEdge.Left:
                alignmentFlags = TPM_LEFTALIGN | TPM_TOPALIGN;
                nativeX = Clamp(taskbar.Right, bounds.Left + margin, bounds.Right - approximateNativeMenuWidth - margin);
                nativeY = ComputeNativeContextMenuY(source, bounds, work, margin, out uint verticalAlignment);
                alignmentFlags |= verticalAlignment;
                break;

            case TaskbarEdge.Right:
                alignmentFlags = TPM_RIGHTALIGN | TPM_TOPALIGN;
                nativeX = Clamp(taskbar.Left, bounds.Left + margin, bounds.Right - margin);
                nativeY = ComputeNativeContextMenuY(source, bounds, work, margin, out uint rightVerticalAlignment);
                alignmentFlags |= rightVerticalAlignment;
                break;
        }

        return new System.Drawing.Point(nativeX, nativeY);
    }

    private static bool IsSourceActuallyOnTaskbar(System.Drawing.Rectangle source, System.Drawing.Rectangle taskbar, TaskbarEdge edge)
    {
        int centerX = source.Left + source.Width / 2;
        int centerY = source.Top + source.Height / 2;

        switch (edge)
        {
            case TaskbarEdge.Bottom:
                return centerY >= taskbar.Top && centerY <= taskbar.Bottom + 4;
            case TaskbarEdge.Top:
                return centerY <= taskbar.Bottom && centerY >= taskbar.Top - 4;
            case TaskbarEdge.Left:
                return centerX <= taskbar.Right && centerX >= taskbar.Left - 4;
            case TaskbarEdge.Right:
                return centerX >= taskbar.Left && centerX <= taskbar.Right + 4;
            default:
                return false;
        }
    }

    private static int ComputeCenteredNativeContextMenuX(System.Drawing.Rectangle source, System.Drawing.Rectangle bounds, System.Drawing.Rectangle work, int margin, int approximateNativeMenuWidth)
    {
        int centerX = source.Left + source.Width / 2;
        int rightLimit = Math.Min(bounds.Right, work.Right) - approximateNativeMenuWidth - margin;
        int leftLimit = Math.Max(bounds.Left, work.Left) + margin;
        if (rightLimit < leftLimit) rightLimit = bounds.Right - approximateNativeMenuWidth - margin;
        return Clamp(centerX - approximateNativeMenuWidth / 2, leftLimit, rightLimit);
    }

    private static bool IsPointNearRectangle(System.Drawing.Point point, System.Drawing.Rectangle rectangle, int tolerance)
    {
        System.Drawing.Rectangle inflated = rectangle;
        inflated.Inflate(tolerance, tolerance);
        return inflated.Contains(point);
    }

    private static int EstimateNativeContextMenuWidth()
    {
        try
        {
            int textWidth = Forms.TextRenderer.MeasureText("Remove Power Mode from system tray", System.Drawing.SystemFonts.MenuFont).Width;
            int menuGutter = GetSystemMetrics(SM_CXMENUCHECK) + GetSystemMetrics(SM_CXMENUSIZE);
            return Clamp(textWidth + menuGutter + 64, 292, 380);
        }
        catch
        {
            return 304;
        }
    }

    private static int ComputeNativeContextMenuY(System.Drawing.Rectangle source, System.Drawing.Rectangle bounds, System.Drawing.Rectangle work, int margin, out uint verticalAlignment)
    {
        int centerY = source.Top + source.Height / 2;
        int lowerThreshold = Math.Min(bounds.Bottom, work.Bottom) - 180;
        if (centerY >= lowerThreshold)
        {
            verticalAlignment = TPM_BOTTOMALIGN;
            return Clamp(Math.Min(bounds.Bottom, work.Bottom) - margin, bounds.Top + margin, bounds.Bottom - margin);
        }

        verticalAlignment = TPM_TOPALIGN;
        return Clamp(source.Top, bounds.Top + margin, bounds.Bottom - margin);
    }

    private void ApplyNativeContextMenuTheme()
    {
        // Best-effort only. These uxtheme entry points are intentionally treated as
        // optional because Windows builds and shell skins differ. If Windows refuses
        // the opt-in, the native popup still opens normally.
        try
        {
            bool light = _isLightMode();
            int preferredMode = light ? PreferredAppModeForceLight : PreferredAppModeForceDark;
            try { _ = SetPreferredAppMode(preferredMode); } catch { }
            try { _ = AllowDarkModeForWindow(_hwnd, !light); } catch { }
            try { FlushMenuThemes(); } catch { }
        }
        catch { }
    }

    private static MenuFlyoutItem CreateNativeMenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private void ConfigureFlyoutMotion(TaskbarEdge edge)
    {
        _openTranslateX = 0;
        _openTranslateY = 0;

        switch (edge)
        {
            case TaskbarEdge.Top:
                _openTranslateY = -DefaultFlyoutMotionDistance;
                break;
            case TaskbarEdge.Left:
                _openTranslateX = -DefaultFlyoutMotionDistance;
                break;
            case TaskbarEdge.Right:
                _openTranslateX = DefaultFlyoutMotionDistance;
                break;
            case TaskbarEdge.Bottom:
            default:
                _openTranslateY = DefaultFlyoutMotionDistance;
                break;
        }
    }

    private void BeginOpenAnimation()
    {
        if (Content is not UIElement surface)
        {
            return;
        }

        UIElement payload = GetFlyoutPayloadElement(surface);

        if (!ShouldAnimate())
        {
            ResetFlyoutAnimationState(surface, payload, payloadOpacity: 1.0, translateX: 0.0, translateY: 0.0);
            return;
        }

        // Windows' own tray flyouts do not fade the whole top-level window from
        // transparent black. The shell surface appears as the flyout background
        // first, then the real contents fade/settle in. Mirror that by sliding
        // the outer surface while fading only the inner payload/content.
        _isClosing = false;
        ResetFlyoutAnimationState(surface, payload, payloadOpacity: 0.0, translateX: _openTranslateX, translateY: _openTranslateY);
        BeginFlyoutMotionAnimation(surface, payload, opacityTo: 1.0, translateXTo: 0.0, translateYTo: 0.0, milliseconds: 167, completed: null);
    }

    private static UIElement GetFlyoutPayloadElement(UIElement surface)
    {
        if (surface is Border border && border.Child is UIElement child)
        {
            return child;
        }

        return surface;
    }

    private static void ResetFlyoutAnimationState(UIElement surface, UIElement payload, double payloadOpacity, double translateX, double translateY)
    {
        // Keep the root flyout surface fully opaque and parked at its final
        // position. Only the inner menu payload moves/fades. This prevents the
        // WinUI window's default black backing surface from peeking out around
        // rounded corners or ahead of the menu during open/close animations.
        surface.Opacity = 1.0;
        payload.Opacity = payloadOpacity;

        var surfaceTransform = EnsureTranslateTransform(surface);
        surfaceTransform.X = 0.0;
        surfaceTransform.Y = 0.0;

        UIElement motionTarget = ReferenceEquals(surface, payload) ? surface : payload;
        var transform = EnsureTranslateTransform(motionTarget);
        transform.X = translateX;
        transform.Y = translateY;
    }

    private static TranslateTransform EnsureTranslateTransform(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform existing)
        {
            return existing;
        }

        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        return transform;
    }

    private static void BeginFlyoutMotionAnimation(UIElement surface, UIElement payload, double opacityTo, double translateXTo, double translateYTo, int milliseconds, Action? completed)
    {
        try
        {
            UIElement motionTarget = ReferenceEquals(surface, payload) ? surface : payload;
            var transform = EnsureTranslateTransform(motionTarget);
            surface.Opacity = 1.0;
            var storyboard = new Storyboard();

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation
            {
                To = opacityTo,
                Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
                EnableDependentAnimation = true,
                EasingFunction = ease
            };
            Storyboard.SetTarget(fade, payload);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);

            var slideX = new DoubleAnimation
            {
                To = translateXTo,
                Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
                EnableDependentAnimation = true,
                EasingFunction = ease
            };
            Storyboard.SetTarget(slideX, transform);
            Storyboard.SetTargetProperty(slideX, "X");
            storyboard.Children.Add(slideX);

            var slideY = new DoubleAnimation
            {
                To = translateYTo,
                Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
                EnableDependentAnimation = true,
                EasingFunction = ease
            };
            Storyboard.SetTarget(slideY, transform);
            Storyboard.SetTargetProperty(slideY, "Y");
            storyboard.Children.Add(slideY);

            if (completed != null)
            {
                storyboard.Completed += (_, _) => completed();
            }
            storyboard.Begin();
        }
        catch
        {
            ResetFlyoutAnimationState(surface, payload, opacityTo, translateXTo, translateYTo);
            completed?.Invoke();
        }
    }

    private void BeginWindowCloseSlideAnimation(double translateXTo, double translateYTo, int milliseconds, Action? completed)
    {
        try
        {
            StopCloseAnimationTimer();

            if (!GetWindowRect(_hwnd, out NativeRect startRect))
            {
                completed?.Invoke();
                return;
            }

            int startX = startRect.Left;
            int startY = startRect.Top;
            int targetX = startX + (int)Math.Round(translateXTo);
            int targetY = startY + (int)Math.Round(translateYTo);

            var stopwatch = Stopwatch.StartNew();
            var timer = new Forms.Timer { Interval = 15 };
            _closeAnimationTimer = timer;

            timer.Tick += (_, _) =>
            {
                double progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / milliseconds, 0.0, 1.0);
                // Cubic ease-in: starts calmly, then leaves toward the taskbar in one
                // continuous motion. The whole HWND moves, so the rounded background
                // travels with the content instead of lingering as a second layer.
                double eased = progress * progress * progress;
                int x = startX + (int)Math.Round((targetX - startX) * eased);
                int y = startY + (int)Math.Round((targetY - startY) * eased);

                SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);

                if (progress >= 1.0)
                {
                    StopCloseAnimationTimer();
                    completed?.Invoke();
                }
            };

            timer.Start();
        }
        catch
        {
            StopCloseAnimationTimer();
            completed?.Invoke();
        }
    }

    private void StopCloseAnimationTimer()
    {
        try
        {
            _closeAnimationTimer?.Stop();
            _closeAnimationTimer?.Dispose();
        }
        catch { }
        _closeAnimationTimer = null;
    }

    private static bool ShouldAnimate()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private int ComputePreferredWidth(FlyoutMode mode)
    {
        if (mode == FlyoutMode.Context) return ComputeContextMenuPreferredWidth();

        // Width is a minimum, not a fixed value. This keeps English compact while
        // allowing longer future translations to grow without clipping.
        int maxChars = _plans.Select(x => x.Name).Append("Balance performance, noise, and energy use.").Append("More power settings").Max(x => x.Length);

        // Keep the default English flyout compact. Grow only when strings are clearly
        // longer than the 300 px minimum can comfortably handle.
        int estimated = maxChars <= 45 ? 300 : 96 + (int)Math.Ceiling(maxChars * 7.4);
        return Clamp(Math.Max(300, estimated), 300, 460);
    }

    private int ComputeContextMenuPreferredWidth()
    {
        try
        {
            System.Drawing.Font font = System.Drawing.SystemFonts.MenuFont!;
            if (_contextMenuPage != ContextMenuPage.Main)
            {
                string title = GetTimeoutPickerTitle(_contextMenuPage);
                string[] valueSamples = { title, FormatTimeoutSeconds(60), FormatTimeoutSeconds(180), FormatTimeoutSeconds(18000), FormatTimeoutSeconds(GetTimeoutPickerMaxNormalSeconds(_contextMenuPage)), "Scroll for fine control" };
                int widestText = valueSamples.Select(x => Forms.TextRenderer.MeasureText(x, font).Width).Max();
                return Clamp(widestText + 72, (int)ContextMenuMinWidth, (int)ContextMenuMaxWidth);
            }

            string[] labels = { "Screen saver", "Start screen saver after", "Screen and sleep", "Turn screen off after", "Put PC to sleep after", "Start with Windows", "Exit Power Mode" };
            string[] values = { FormatTimeoutSeconds(_getScreenSaverTimeoutSeconds()), FormatTimeoutSeconds(_getDisplayTimeoutSeconds()), FormatTimeoutSeconds(_getSleepTimeoutSeconds()) };
            int labelWidth = labels.Select(x => Forms.TextRenderer.MeasureText(x, font).Width).Max();
            int valueWidth = values.Select(x => Forms.TextRenderer.MeasureText(x, font).Width).Max();
            int estimated = (int)(ContextMenuIconInset + labelWidth + ContextMenuValueGap + valueWidth + ContextMenuChevronWidth + ContextMenuRightInset + 22);
            return Clamp(estimated, (int)ContextMenuMinWidth, (int)ContextMenuMaxWidth);
        }
        catch
        {
            return (int)ContextMenuMinWidth;
        }
    }

    private int ComputePreferredHeight(FlyoutMode mode)
    {
        if (mode == FlyoutMode.Main)
        {
            double upperHeight = _isAutomaticModeEnabled() ? AutomaticMainUpperHeight : ManualMainUpperHeight;
            return (int)(upperHeight + 1 + FooterHeight);
        }

        if (_contextMenuPage != ContextMenuPage.Main)
            return (int)(ContextMenuPickerHeaderHeight + ContextMenuPickerValueHeight + ContextMenuPickerSliderHeight);
        return (int)(2 * ContextMenuHeaderHeight + 3 * ContextMenuSettingRowHeight + (2 * ContextMenuSeparatorHeight) + 3 * ContextMenuRowHeight);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    private bool IsCursorInsideWindow()
    {
        try
        {
            if (!GetWindowRect(_hwnd, out NativeRect r)) return false;
            System.Drawing.Point p = Forms.Cursor.Position;
            return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        }
        catch { return false; }
    }

    public void HideFlyout()
    {
        HideFlyout(animated: true);
    }

    private void HideFlyout(bool animated)
    {
        if (!animated) StopCloseAnimationTimer();
        if (_isClosing && animated) return;

        if (!IsFlyoutVisible && !_isClosing)
        {
            try { _outsideClickTimer.Stop(); } catch { }
            ShowWindow(_hwnd, 0);
            return;
        }

        IsFlyoutVisible = false;
        try { _outsideClickTimer.Stop(); } catch { }

        if (Content is not UIElement surface)
        {
            _isClosing = false;
            ShowWindow(_hwnd, 0);
            return;
        }

        UIElement payload = GetFlyoutPayloadElement(surface);

        if (!animated || !ShouldAnimate())
        {
            _isClosing = false;
            ResetFlyoutAnimationState(surface, payload, payloadOpacity: 0.0, translateX: _openTranslateX, translateY: _openTranslateY);
            ShowWindow(_hwnd, 0);
            return;
        }

        // Dismiss by moving the actual rounded HWND instead of sliding only the
        // payload. Sliding only the payload left the shell-colored background
        // behind as a separate layer; moving the window keeps background, border,
        // rounded clipping, and contents locked together.
        _isClosing = true;
        ResetFlyoutAnimationState(surface, payload, payloadOpacity: 1.0, translateX: 0.0, translateY: 0.0);
        BeginWindowCloseSlideAnimation(translateXTo: _openTranslateX, translateYTo: _openTranslateY, milliseconds: 125, completed: () =>
        {
            _isClosing = false;
            ResetFlyoutAnimationState(surface, payload, payloadOpacity: 0.0, translateX: _openTranslateX, translateY: _openTranslateY);
            ShowWindow(_hwnd, 0);
        });
    }

    public void RefreshContent()
    {
        bool light = _isLightMode();
        UIElement element = CurrentMode == FlyoutMode.Context ? BuildContextFlyout(light) : BuildMainFlyout(light);
        if (element is FrameworkElement frameworkElement)
        {
            frameworkElement.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        }
        Content = element;
        ResizeVisibleFlyoutToPreferredSize();
    }

    private void ResizeVisibleFlyoutToPreferredSize()
    {
        if (!IsFlyoutVisible) return;

        try
        {
            if (!GetWindowRect(_hwnd, out NativeRect rect)) return;

            int width = ComputePreferredWidth(CurrentMode);
            int height = ComputePreferredHeight(CurrentMode);
            int x = rect.Left;
            int y = rect.Bottom - height;

            Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            System.Drawing.Rectangle bounds = screen.Bounds;
            x = Clamp(x, bounds.Left, bounds.Right - width);
            y = Clamp(y, bounds.Top + 4, bounds.Bottom - height - 4);

            _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
            ApplyRoundedWindowRegion(width, height, 8);
        }
        catch { }
    }

    private UIElement BuildMainFlyout(bool light)
    {
        string active = _getActivePlanName();
        bool automatic = _isAutomaticModeEnabled();
        Palette p = Palette.For(light);

        var root = new Grid { MinWidth = 300, MaxWidth = 460 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(automatic ? AutomaticMainUpperHeight : ManualMainUpperHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FooterHeight) });

        var upper = new StackPanel { Spacing = 0 };

        var header = new Grid { Height = HeaderHeight };
        header.Children.Add(new TextBlock
        {
            Text = "Power Mode",
            FontFamily = new XamlFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Foreground = p.TextBrush,
            Margin = new Thickness(ContentInset, TitleTopInset, 19, 0),
            VerticalAlignment = VerticalAlignment.Top
        });
        header.Children.Add(new TextBlock
        {
            Text = automatic ? "Automatic power saving when you are away." : "Balance performance, noise, and energy use.",
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.15,
            Foreground = p.DescriptionTextBrush,
            Margin = new Thickness(ContentInset, DescriptionTopInset, 19, 0),
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        upper.Children.Add(header);
        upper.Children.Add(CreateModeRow(automatic, p));

        if (automatic)
        {
            upper.Children.Add(CreateAutomaticSummaryBlock(p));
            upper.Children.Add(CreateAutomaticStepperRow("Using PC", _getAutomaticNormalProfile(), _cycleAutomaticNormalProfilePrevious, CanMoveAutomaticNormalProfile(-1), _cycleAutomaticNormalProfileNext, CanMoveAutomaticNormalProfile(1), p));
            upper.Children.Add(CreateAutomaticStepperRow("Away", _getAutomaticAfkProfile(), _cycleAutomaticAfkProfilePrevious, CanMoveAutomaticAfkProfile(-1), _cycleAutomaticAfkProfileNext, CanMoveAutomaticAfkProfile(1), p));
            upper.Children.Add(CreateAutomaticStepperRow("Switch to away after", FormatTimeoutSeconds(_getAutomaticAfkDelaySeconds()), _cycleAutomaticAfkDelaySecondsPrevious, CanMoveAutomaticAfkDelay(-1), _cycleAutomaticAfkDelaySecondsNext, CanMoveAutomaticAfkDelay(1), p));
        }
        else
        {
            upper.Children.Add(CreateManualSummaryBlock(p));
            foreach (PlanInfo plan in _plans)
            {
                bool isActive = PowerModeTrayApplication.PlanNamesEquivalent(active, plan.Name);
                upper.Children.Add(CreatePlanRow(plan, isActive, p));
            }
        }

        upper.Children.Add(new Border { Height = 0 });
        Grid.SetRow(upper, 0);
        root.Children.Add(upper);

        var footerTopStroke = new Border { Height = 1, Background = CloneBrush(p.FooterTopStrokeBrush) };
        Grid.SetRow(footerTopStroke, 1);
        root.Children.Add(footerTopStroke);

        var footer = CreateFooterActionRow("More power settings", _openPowerSettings, p);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return new Border
        {
            Background = p.BackgroundBrush,
            BorderBrush = p.BorderBrush,
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Child = root
        };
    }



    private bool CanMoveModePrevious() => _isAutomaticModeEnabled();
    private bool CanMoveModeNext() => !_isAutomaticModeEnabled();

    private bool CanMoveAutomaticNormalProfile(int direction)
    {
        string[] valid = _plans.Take(Math.Max(0, _plans.Length - 1)).Select(p => p.Name).ToArray();
        if (valid.Length == 0) return false;

        int index = Array.FindIndex(valid, p => PowerModeTrayApplication.PlanNamesEquivalent(p, _getAutomaticNormalProfile()));
        if (index < 0) index = 0;

        return direction < 0 ? index > 0 : index < valid.Length - 1;
    }

    private bool CanMoveAutomaticAfkProfile(int direction)
    {
        int normalRank = GetFlyoutPlanRank(_getAutomaticNormalProfile());
        string[] valid = _plans
            .Where(p => GetFlyoutPlanRank(p.Name) > normalRank)
            .Select(p => p.Name)
            .ToArray();

        if (valid.Length <= 1) return false;

        int index = Array.FindIndex(valid, p => PowerModeTrayApplication.PlanNamesEquivalent(p, _getAutomaticAfkProfile()));
        if (index < 0) index = 0;

        return direction < 0 ? index > 0 : index < valid.Length - 1;
    }

    private bool CanMoveAutomaticAfkDelay(int direction)
    {
        int[] valid = new[] { 1 * 60, 2 * 60, 3 * 60, 5 * 60, 10 * 60, 15 * 60, 30 * 60, 60 * 60 };
        int index = Array.IndexOf(valid, _getAutomaticAfkDelaySeconds());
        if (index < 0) index = Array.IndexOf(valid, 15 * 60);
        if (index < 0) index = 0;

        return direction < 0 ? index > 0 : index < valid.Length - 1;
    }

    private int GetFlyoutPlanRank(string profile)
    {
        for (int i = 0; i < _plans.Length; i++)
        {
            if (PowerModeTrayApplication.PlanNamesEquivalent(_plans[i].Name, profile)) return i;
        }
        return _plans.Length - 1;
    }


    private UIElement BuildContextFlyout(bool light)
    {
        Palette p = Palette.For(light);
        UIElement content = _contextMenuPage == ContextMenuPage.Main
            ? BuildContextMainFlyout(p)
            : BuildTimeoutPickerFlyout(p);

        return new Border
        {
            Background = p.BackgroundBrush,
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Child = content
        };
    }

    private UIElement BuildContextMainFlyout(Palette p)
    {
        var stack = new StackPanel { Spacing = 0 };

        stack.Children.Add(CreateContextMenuSectionHeader("Screen saver", "\uE8A7", () => RunContextMenuAction(_openScreenSaverSettings), p));
        stack.Children.Add(CreateContextMenuSettingRow("Start screen saver after", FormatTimeoutSeconds(_getScreenSaverTimeoutSeconds()), () => ShowContextPicker(ContextMenuPage.ScreenSaverTimeout), p));

        stack.Children.Add(CreateContextMenuSectionHeader("Screen and sleep", "\uE713", () => RunContextMenuAction(_openScreenAndSleepSettings), p));
        stack.Children.Add(CreateContextMenuSettingRow("Turn screen off after", FormatTimeoutSeconds(_getDisplayTimeoutSeconds()), () => ShowContextPicker(ContextMenuPage.DisplayTimeout), p));
        stack.Children.Add(CreateContextMenuSettingRow("Put PC to sleep after", FormatTimeoutSeconds(_getSleepTimeoutSeconds()), () => ShowContextPicker(ContextMenuPage.SleepTimeout), p));

        stack.Children.Add(CreateContextMenuSeparator(p.SeparatorBrush));

        stack.Children.Add(CreateContextMenuExternalActionRow("Power history", "Open folder", "\uE8A7", () => RunContextMenuAction(_openAutomaticHistory), p));

        stack.Children.Add(CreateContextMenuSeparator(p.SeparatorBrush));

        stack.Children.Add(CreateContextMenuCheckRow("Start with Windows", _isStartupEnabled(), () => RunContextMenuAction(_toggleStartup), p));
        stack.Children.Add(CreateContextMenuRow("Exit Power Mode", () => RunContextMenuAction(_quit), p, " "));

        return stack;
    }

    private UIElement BuildTimeoutPickerFlyout(Palette p)
    {
        ContextMenuPage pickerPage = _contextMenuPage;
        int[] timeoutSequence = GetTimeoutWheelSequence(pickerPage);
        int pendingSeconds = GetCurrentTimeoutSecondsForPage(pickerPage);
        CancellationTokenSource? applyDebounce = null;

        void CommitPendingTimeoutValue()
        {
            applyDebounce?.Cancel();
            SetCurrentTimeoutSecondsForPage(pickerPage, pendingSeconds);
        }

        var stack = new StackPanel
        {
            Spacing = 0,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        stack.Children.Add(CreateContextMenuPickerHeader(GetTimeoutPickerTitle(pickerPage), () => { CommitPendingTimeoutValue(); ShowContextPicker(ContextMenuPage.Main); }, p));

        var valueText = new TextBlock
        {
            Text = FormatTimeoutSeconds(pendingSeconds),
            FontFamily = new XamlFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = CloneBrush(p.TextBrush),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(ContextMenuIconInset, 0, ContextMenuRightInset, 0)
        };
        stack.Children.Add(new Border { Height = ContextMenuPickerValueHeight, Child = valueText });

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = timeoutSequence.Length - 1,
            StepFrequency = 1,
            Value = GetTimeoutSequenceIndex(timeoutSequence, pendingSeconds),
            IsThumbToolTipEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(ContextMenuIconInset, 0, ContextMenuRightInset, 0)
        };
        stack.Children.Add(new Border { Height = ContextMenuPickerSliderHeight, Child = slider });

        bool suppressSliderChange = false;

        void ScheduleTimeoutApply(int seconds)
        {
            applyDebounce?.Cancel();
            var cts = new CancellationTokenSource();
            applyDebounce = cts;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(350, cts.Token).ConfigureAwait(false);
                    if (!cts.IsCancellationRequested)
                    {
                        SetCurrentTimeoutSecondsForPage(pickerPage, seconds);
                    }
                }
                catch (System.OperationCanceledException) { }
            });
        }

        void ApplyTimeoutValue(int seconds, bool updateSlider, bool commitImmediately = false)
        {
            pendingSeconds = seconds;
            valueText.Text = FormatTimeoutSeconds(seconds);

            if (updateSlider)
            {
                suppressSliderChange = true;
                slider.Value = GetTimeoutSequenceIndex(timeoutSequence, seconds);
                suppressSliderChange = false;
            }

            if (commitImmediately)
            {
                CommitPendingTimeoutValue();
            }
            else
            {
                ScheduleTimeoutApply(seconds);
            }
        }

        void HandleTimeoutPickerWheel(object sender, PointerRoutedEventArgs e)
        {
            if (e.Handled) return;

            int delta = e.GetCurrentPoint(stack).Properties.MouseWheelDelta;
            if (delta == 0) return;

            int next = GetTimeoutWheelAdjustedSeconds(pickerPage, pendingSeconds, delta);
            ApplyTimeoutValue(next, true);
            e.Handled = true;
        }

        slider.ValueChanged += (_, e) =>
        {
            if (suppressSliderChange) return;

            int index = Clamp((int)Math.Round(e.NewValue), 0, timeoutSequence.Length - 1);
            if (Math.Abs(slider.Value - index) > 0.001)
            {
                suppressSliderChange = true;
                slider.Value = index;
                suppressSliderChange = false;
            }

            ApplyTimeoutValue(timeoutSequence[index], false);
        };

        slider.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Home)
            {
                ApplyTimeoutValue(60, true, true);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.End)
            {
                ApplyTimeoutValue(0, true, true);
                e.Handled = true;
            }
        };

        slider.PointerWheelChanged += HandleTimeoutPickerWheel;
        stack.PointerWheelChanged += HandleTimeoutPickerWheel;

        return stack;
    }

    private int GetCurrentTimeoutSecondsForPage(ContextMenuPage page)
        => page switch
        {
            ContextMenuPage.ScreenSaverTimeout => _getScreenSaverTimeoutSeconds(),
            ContextMenuPage.DisplayTimeout => _getDisplayTimeoutSeconds(),
            ContextMenuPage.SleepTimeout => _getSleepTimeoutSeconds(),
            _ => 0
        };

    private static string GetTimeoutPickerTitle(ContextMenuPage page)
        => page switch
        {
            ContextMenuPage.ScreenSaverTimeout => "Start screen saver after",
            ContextMenuPage.DisplayTimeout => "Turn screen off after",
            ContextMenuPage.SleepTimeout => "Put PC to sleep after",
            _ => "Settings"
        };

    private void SelectTimeoutValue(int seconds)
    {
        SetCurrentTimeoutSecondsForPage(_contextMenuPage, seconds);
        ShowContextPicker(ContextMenuPage.Main);
    }

    private void SetCurrentTimeoutSecondsForPage(ContextMenuPage page, int seconds)
    {
        switch (page)
        {
            case ContextMenuPage.ScreenSaverTimeout:
                _setScreenSaverTimeoutSeconds(seconds);
                break;
            case ContextMenuPage.DisplayTimeout:
                _setDisplayTimeoutSeconds(seconds);
                break;
            case ContextMenuPage.SleepTimeout:
                _setSleepTimeoutSeconds(seconds);
                break;
        }
    }

    private static int GetTimeoutPickerMaxNormalSeconds(ContextMenuPage page)
        => page == ContextMenuPage.SleepTimeout ? 72 * 3600 : 24 * 3600;

    private static int[] GetTimeoutWheelSequence(ContextMenuPage page)
    {
        int maxSeconds = GetTimeoutPickerMaxNormalSeconds(page);
        var values = new List<int>();

        void AddRange(int startSeconds, int endSeconds, int stepSeconds)
        {
            int cappedEnd = Math.Min(endSeconds, maxSeconds);
            for (int seconds = startSeconds; seconds <= cappedEnd; seconds += stepSeconds)
            {
                if (values.Count == 0 || values[^1] != seconds)
                {
                    values.Add(seconds);
                }
            }
        }

        AddRange(60, 15 * 60, 60);
        AddRange(20 * 60, 3600, 5 * 60);
        AddRange(75 * 60, 6 * 3600, 15 * 60);
        AddRange(390 * 60, 12 * 3600, 30 * 60);
        AddRange(13 * 3600, 24 * 3600, 3600);

        if (maxSeconds > 24 * 3600)
        {
            AddRange(26 * 3600, maxSeconds, 2 * 3600);
        }

        if (values.Count == 0 || values[^1] != maxSeconds)
        {
            values.Add(maxSeconds);
        }

        values.Add(0);
        return values.ToArray();
    }

    private static int GetTimeoutSequenceIndex(int[] sequence, int seconds)
    {
        if (sequence.Length == 0) return 0;
        if (seconds <= 0) return sequence.Length - 1;

        int bestIndex = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < sequence.Length; i++)
        {
            int value = sequence[i];
            if (value <= 0) continue;

            int distance = Math.Abs(value - seconds);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static int GetTimeoutWheelAdjustedSeconds(ContextMenuPage page, int currentSeconds, int wheelDelta)
    {
        int[] sequence = GetTimeoutWheelSequence(page);
        if (sequence.Length == 0) return currentSeconds;

        int index = GetTimeoutSequenceIndex(sequence, currentSeconds);
        int direction = wheelDelta > 0 ? 1 : -1;

        if (direction > 0)
        {
            return sequence[Math.Min(sequence.Length - 1, index + 1)];
        }

        return sequence[Math.Max(0, index - 1)];
    }

    private void ShowContextPicker(ContextMenuPage page)
    {
        _contextMenuPage = page;
        RefreshContextContentAndResize();
    }

    private void RefreshContextContentAndResize()
    {
        int oldBottom = 0;
        int oldX = 0;
        try
        {
            if (GetWindowRect(_hwnd, out NativeRect rect))
            {
                oldBottom = rect.Bottom;
                oldX = rect.Left;
            }
        }
        catch { }

        RefreshContent();

        int width = ComputePreferredWidth(FlyoutMode.Context);
        int height = ComputePreferredHeight(FlyoutMode.Context);
        int x = oldX;
        int y = oldBottom > 0 ? oldBottom - height : 0;

        try
        {
            Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            System.Drawing.Rectangle bounds = screen.Bounds;
            x = Clamp(x, bounds.Left, bounds.Right - width);
            y = Clamp(y, bounds.Top + 4, bounds.Bottom - height - 4);
        }
        catch { }

        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        ApplyRoundedWindowRegion(width, height, 8);
    }

    private void RunContextMenuAction(Action action)
    {
        HideFlyout(animated: false);
        action();
    }

    private static void BeginIndicatorAnimation(CompositeTransform transform)
    {
        if (!ShouldAnimate())
        {
            transform.ScaleY = 1.0;
            return;
        }

        try
        {
            var storyboard = new Storyboard();
            var grow = new DoubleAnimation
            {
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(167)),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(grow, transform);
            Storyboard.SetTargetProperty(grow, "ScaleY");
            storyboard.Children.Add(grow);
            storyboard.Begin();
        }
        catch
        {
            transform.ScaleY = 1.0;
        }
    }


    private FrameworkElement CreateModeRow(bool automatic, Palette p)
    {
        var grid = new Grid
        {
            Height = 34,
            IsHitTestVisible = true
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        grid.Children.Add(new TextBlock
        {
            Text = "Mode",
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new XamlFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 13.4,
            Foreground = p.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(PlanTextInset, 0, 10, 0),
            IsHitTestVisible = false
        });

        var previousButton = CreateAutomaticSmallGlyphButton("\uE76B", _toggleAutomaticMode, p, CanMoveModePrevious());
        Grid.SetColumn(previousButton, 1);
        grid.Children.Add(previousButton);

        var pillText = new TextBlock
        {
            Text = automatic ? "Automatic" : "Manual",
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.0,
            Foreground = p.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var pill = new Border
        {
            MinWidth = 96,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = CloneBrush(automatic ? p.ActiveItemBrush : p.HighlightItemBrush),
            BorderBrush = p.FooterTopStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = pillText,
            Margin = new Thickness(0, 0, 0, 0),
            IsHitTestVisible = false
        };
        Grid.SetColumn(pill, 2);
        grid.Children.Add(pill);

        var nextButton = CreateAutomaticSmallGlyphButton("\uE76C", _toggleAutomaticMode, p, CanMoveModeNext());
        Grid.SetColumn(nextButton, 3);
        grid.Children.Add(nextButton);

        var fill = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(0),
            Margin = new Thickness(PlanRowLeftInset, 0, PlanRowRightInset, 0),
            Child = grid,
            IsHitTestVisible = true
        };

        var host = new Grid
        {
            Height = 34,
            Background = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = true
        };
        host.Children.Add(fill);
        host.PointerEntered += (_, _) => fill.Background = CloneBrush(p.HighlightItemBrush);
        host.PointerExited += (_, _) => fill.Background = new SolidColorBrush(Colors.Transparent);
        return host;
    }

    private FrameworkElement CreateManualSummaryBlock(Palette p)
    {
        var panel = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(PlanTextInset, 8, 12, 0)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Manual Mode",
            FontFamily = new XamlFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 14,
            Foreground = p.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Use a fixed power profile.",
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.1,
            Foreground = p.DescriptionTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        return new Border
        {
            Height = ManualSummaryHeight,
            Background = new SolidColorBrush(Colors.Transparent),
            Child = panel
        };
    }

    private FrameworkElement CreateAutomaticSummaryBlock(Palette p)
    {
        var panel = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(PlanTextInset, 8, 12, 0)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Automatic Mode",
            FontFamily = new XamlFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 14,
            Foreground = p.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Changes power profiles when you're away.",
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.1,
            Foreground = p.DescriptionTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Fullscreen apps pause away mode.",
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.1,
            Foreground = p.DescriptionTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        return new Border
        {
            Height = AutomaticSummaryHeight,
            Background = new SolidColorBrush(Colors.Transparent),
            Child = panel
        };
    }

    private FrameworkElement CreateAutomaticStepperRow(string label, string value, Action previousAction, bool canPrevious, Action nextAction, bool canNext, Palette p)
    {
        var grid = new Grid
        {
            Height = PlanRowHeight,
            IsHitTestVisible = true
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new XamlFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 13.5,
            Foreground = p.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(PlanTextInset, 0, 10, 0),
            RenderTransform = new TranslateTransform { Y = -1 },
            IsHitTestVisible = false
        });

        var previousButton = CreateAutomaticSmallGlyphButton("\uE76B", previousAction, p, canPrevious);
        Grid.SetColumn(previousButton, 1);
        grid.Children.Add(previousButton);

        var valueText = new TextBlock
        {
            Text = value,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.4,
            Foreground = p.DescriptionTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 2, 0),
            RenderTransform = new TranslateTransform { Y = -1 },
            IsHitTestVisible = false
        };
        Grid.SetColumn(valueText, 2);
        grid.Children.Add(valueText);

        var nextButton = CreateAutomaticSmallGlyphButton("\uE76C", nextAction, p, canNext);
        Grid.SetColumn(nextButton, 3);
        grid.Children.Add(nextButton);

        var fill = new Border
        {
            Height = PlanHighlightHeight,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(0),
            Margin = new Thickness(PlanRowLeftInset, 0, PlanRowRightInset, 0),
            Child = grid,
            IsHitTestVisible = true
        };

        var host = new Grid
        {
            Height = PlanRowHeight,
            Background = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = true
        };
        host.Children.Add(fill);
        host.PointerEntered += (_, _) => fill.Background = CloneBrush(p.HighlightItemBrush);
        host.PointerExited += (_, _) => fill.Background = new SolidColorBrush(Colors.Transparent);
        return host;
    }

    private FrameworkElement CreateAutomaticActionRow(string label, string value, string glyph, Action action, Palette p)
    {
        var grid = new Grid
        {
            Height = PlanRowHeight,
            IsHitTestVisible = false
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new XamlFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 13.5,
            Foreground = p.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(PlanTextInset, 0, 10, 0),
            RenderTransform = new TranslateTransform { Y = -1 },
            IsHitTestVisible = false
        });

        var valueText = new TextBlock
        {
            Text = value,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.4,
            Foreground = p.DescriptionTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 4, 0),
            RenderTransform = new TranslateTransform { Y = -1 },
            IsHitTestVisible = false
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);

        var externalGlyph = new TextBlock
        {
            Text = glyph,
            FontFamily = new XamlFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = p.DescriptionTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 10, 0),
            IsHitTestVisible = false
        };
        Grid.SetColumn(externalGlyph, 2);
        grid.Children.Add(externalGlyph);

        var fill = new Border
        {
            Height = PlanHighlightHeight,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(0),
            Margin = new Thickness(PlanRowLeftInset, 0, PlanRowRightInset, 0),
            Child = grid,
            IsHitTestVisible = false
        };

        var host = new Grid
        {
            Height = PlanRowHeight,
            Background = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = true
        };
        host.Children.Add(fill);
        host.PointerEntered += (_, _) => fill.Background = CloneBrush(p.HighlightItemBrush);
        host.PointerExited += (_, _) => fill.Background = new SolidColorBrush(Colors.Transparent);
        host.Tapped += (_, _) => action();
        return host;
    }

    private FrameworkElement CreateAutomaticSmallGlyphButton(string glyph, Action action, Palette p, bool enabled = true)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new XamlFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = p.DescriptionTextBrush,
            Opacity = enabled ? 1.0 : 0.0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
            IsHitTestVisible = false
        };

        var button = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = icon,
            IsHitTestVisible = enabled
        };

        if (enabled)
        {
            button.PointerEntered += (_, _) => button.Background = CloneBrush(p.ActiveHoverItemBrush);
            button.PointerExited += (_, _) => button.Background = new SolidColorBrush(Colors.Transparent);
            button.Tapped += (_, e) =>
            {
                e.Handled = true;
                action();
            };
        }

        return button;
    }

    private FrameworkElement CreatePlanRow(PlanInfo plan, bool isActive, Palette p)
    {
        UiColor planColor = HexToWinUIColor(plan.ColorHex);

        // Match the Windows shell pattern: the active indicator is a narrow
        // selection stripe at the far left of the row, while the row text aligns
        // with the flyout title and footer text. Do not reserve a permanent
        // indicator column; that makes inactive rows look indented compared with
        // Windows' own flyouts.
        var rowContent = new Grid
        {
            Height = PlanRowHeight,
            IsHitTestVisible = false
        };

        if (isActive)
        {
            var indicatorTransform = new CompositeTransform { ScaleX = 1.0, ScaleY = ShouldAnimate() ? 0.35 : 1.0, TranslateY = -2 };
            var indicator = new Border
            {
                Width = 3,
                Height = PlanIndicatorHeight,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(planColor),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
                RenderTransform = indicatorTransform,
                RenderTransformOrigin = new WinPoint(0.5, 0.5),
                IsHitTestVisible = false
            };
            indicator.Loaded += (_, _) => BeginIndicatorAnimation(indicatorTransform);
            rowContent.Children.Add(indicator);
        }

        rowContent.Children.Add(new TextBlock
        {
            Text = plan.Name,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new XamlFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 14,
            Foreground = p.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(PlanTextInset, 0, 12, 0),
            RenderTransform = new TranslateTransform { Y = -2 },
            IsHitTestVisible = false
        });

        var fill = new Border
        {
            Height = PlanHighlightHeight,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Background = isActive ? CloneBrush(p.ActiveItemBrush) : new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(0),
            Margin = new Thickness(PlanRowLeftInset, 0, PlanRowRightInset, 0),
            Child = rowContent,
            IsHitTestVisible = false
        };

        var host = new Grid
        {
            Height = PlanRowHeight,
            Background = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = true
        };
        host.Children.Add(fill);
        host.PointerEntered += (_, _) => fill.Background = isActive ? CloneBrush(p.ActiveHoverItemBrush) : CloneBrush(p.HighlightItemBrush);
        host.PointerExited += (_, _) => fill.Background = isActive ? CloneBrush(p.ActiveItemBrush) : new SolidColorBrush(Colors.Transparent);
        host.Tapped += (_, _) => _setPlan(plan.Name);
        return host;
    }

    private static string FormatTimeoutSeconds(int seconds)
    {
        if (seconds <= 0) return "Never";

        int totalMinutes = Math.Max(1, (int)Math.Round(seconds / 60.0));
        if (totalMinutes < 60)
        {
            return totalMinutes == 1 ? "1 minute" : $"{totalMinutes} minutes";
        }

        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        string hourText = hours == 1 ? "1 hour" : $"{hours} hours";
        if (minutes == 0) return hourText;
        string minuteText = minutes == 1 ? "1 minute" : $"{minutes} minutes";
        return hourText + " " + minuteText;
    }

    private static Border CreateContextMenuSectionHeader(string textValue, string iconGlyph, Action iconAction, Palette p)
    {
        var grid = new Grid { Height = ContextMenuHeaderHeight, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

        var text = new TextBlock
        {
            Text = textValue,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.6,
            Foreground = CloneBrush(p.MutedBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(ContextMenuIconInset, 2, 4, 0)
        };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var iconText = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new XamlFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = CloneBrush(p.TextBrush),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var iconButton = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.Transparent),
            Child = iconText,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 2, 2)
        };
        iconButton.PointerEntered += (_, _) => iconButton.Background = CloneBrush(p.HighlightItemBrush);
        iconButton.PointerExited += (_, _) => iconButton.Background = new SolidColorBrush(Colors.Transparent);
        iconButton.PointerPressed += (_, _) => iconButton.Background = CloneBrush(p.ActiveHoverItemBrush);
        iconButton.PointerReleased += (_, _) => iconButton.Background = CloneBrush(p.HighlightItemBrush);
        iconButton.Tapped += (_, _) => iconAction();
        Grid.SetColumn(iconButton, 1);
        grid.Children.Add(iconButton);

        return new Border { Height = ContextMenuHeaderHeight, Background = new SolidColorBrush(Colors.Transparent), Child = grid };
    }

    private static Border CreateContextMenuSettingRow(string label, string value, Action action, Palette p)
    {
        var grid = new Grid { Height = ContextMenuSettingRowHeight, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ContextMenuChevronWidth) });

        var labelText = new TextBlock
        {
            Text = label,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Foreground = CloneBrush(p.TextBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(ContextMenuIconInset, 0, ContextMenuValueGap, 0)
        };
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        var valueText = new TextBlock
        {
            Text = value,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Foreground = CloneBrush(p.MutedBrush),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 2, 0)
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);

        var chevron = new TextBlock
        {
            Text = "\uE974",
            FontFamily = new XamlFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = CloneBrush(p.MutedBrush),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, ContextMenuRightInset - 8, 0)
        };
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);

        return CreateContextInteractiveBorder(grid, ContextMenuSettingRowHeight, action, p);
    }

    private static Border CreateContextMenuPickerHeader(string title, Action backAction, Palette p)
    {
        var grid = new Grid { Height = ContextMenuPickerHeaderHeight, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var arrow = new TextBlock
        {
            Text = "\uE72B",
            FontFamily = new XamlFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = CloneBrush(p.TextBrush),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false
        };
        Grid.SetColumn(arrow, 0);
        grid.Children.Add(arrow);

        var titleText = new TextBlock
        {
            Text = title,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Foreground = CloneBrush(p.TextBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, ContextMenuRightInset, 0),
            IsHitTestVisible = false
        };
        Grid.SetColumn(titleText, 1);
        grid.Children.Add(titleText);

        return CreateContextInteractiveBorder(grid, ContextMenuPickerHeaderHeight, backAction, p);
    }

    private static Border CreateContextMenuCheckRow(string textValue, bool isChecked, Action action, Palette p)
        => CreateContextMenuRow(textValue, action, p, isChecked ? "\uE73E" : " ");

    private static Border CreateContextMenuRow(string textValue, Action action, Palette p, string? iconGlyph = null)
    {
        var grid = new Grid
        {
            Height = ContextMenuRowHeight,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new TextBlock
        {
            Text = iconGlyph ?? " ",
            FontFamily = new XamlFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = CloneBrush(p.TextBrush),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0)
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text = textValue,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Foreground = CloneBrush(p.TextBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, ContextMenuRightInset, 0)
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return CreateContextInteractiveBorder(grid, ContextMenuRowHeight, action, p);
    }

    private static Border CreateContextMenuExternalActionRow(string label, string value, string glyph, Action action, Palette p)
    {
        var grid = new Grid
        {
            Height = ContextMenuRowHeight,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ContextMenuChevronWidth) });

        var icon = new TextBlock
        {
            Text = " ",
            FontFamily = new XamlFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = CloneBrush(p.TextBrush),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0)
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var labelText = new TextBlock
        {
            Text = label,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Foreground = CloneBrush(p.TextBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, ContextMenuValueGap, 0)
        };
        Grid.SetColumn(labelText, 1);
        grid.Children.Add(labelText);

        var valueText = new TextBlock
        {
            Text = value,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Foreground = CloneBrush(p.MutedBrush),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 2, 0)
        };
        Grid.SetColumn(valueText, 2);
        grid.Children.Add(valueText);

        var externalGlyph = new TextBlock
        {
            Text = glyph,
            FontFamily = new XamlFontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = CloneBrush(p.MutedBrush),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, ContextMenuRightInset - 8, 0)
        };
        Grid.SetColumn(externalGlyph, 3);
        grid.Children.Add(externalGlyph);

        return CreateContextInteractiveBorder(grid, ContextMenuRowHeight, action, p);
    }

    private static Border CreateContextInteractiveBorder(UIElement child, double height, Action action, Palette p)
    {
        var border = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Transparent),
            Child = child
        };
        border.PointerEntered += (_, _) => border.Background = CloneBrush(p.HighlightItemBrush);
        border.PointerExited += (_, _) => border.Background = new SolidColorBrush(Colors.Transparent);
        border.PointerPressed += (_, _) => border.Background = CloneBrush(p.ActiveHoverItemBrush);
        border.PointerReleased += (_, _) => border.Background = CloneBrush(p.HighlightItemBrush);
        border.Tapped += (_, _) => action();
        return border;
    }

    private static Border CreateContextMenuSeparator(SolidColorBrush brush)
    {
        return new Border
        {
            Height = ContextMenuSeparatorHeight,
            Padding = new Thickness(0, 4, 0, 4),
            Child = new Border { Height = 1, Background = CloneBrush(brush) }
        };
    }

    private static Border CreateActionRow(string textValue, Action action, Palette p, bool subdued = false)
    {
        var tb = new TextBlock
        {
            Text = textValue,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = subdued ? p.MutedBrush : p.TextBrush
        };
        var border = new Border { Height = 36, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 0, 10, 0), Child = tb };
        border.PointerEntered += (_, _) => border.Background = CloneBrush(p.HighlightItemBrush);
        border.PointerExited += (_, _) => border.Background = new SolidColorBrush(Colors.Transparent);
        border.Tapped += (_, _) => action();
        return border;
    }

    private static Border CreateFooterActionRow(string textValue, Action action, Palette p)
    {
        var tb = new TextBlock
        {
            Text = textValue,
            FontFamily = new XamlFontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12.15,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = p.FooterTextBrush,
            Margin = new Thickness(ContentInset, 0, 12, 0),
            RenderTransform = new TranslateTransform { Y = -4 }
        };
        var footerContent = new Grid
        {
            Height = FooterHeight,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        footerContent.Children.Add(tb);

        var border = new Border
        {
            Height = FooterHeight,
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Background = p.FooterBrush,
            Padding = new Thickness(0),
            Child = footerContent
        };
        border.PointerEntered += (_, _) =>
        {
            border.Background = CloneBrush(p.FooterHoverBrush);
            tb.Foreground = CloneBrush(p.FooterTextBrush);
        };
        border.PointerExited += (_, _) =>
        {
            border.Background = CloneBrush(p.FooterBrush);
            tb.Foreground = CloneBrush(p.FooterTextBrush);
        };
        border.PointerPressed += (_, _) =>
        {
            border.Background = CloneBrush(p.FooterHoverBrush);
            tb.Foreground = CloneBrush(p.FooterPressedTextBrush);
        };
        border.PointerReleased += (_, _) =>
        {
            border.Background = CloneBrush(p.FooterHoverBrush);
            tb.Foreground = CloneBrush(p.FooterTextBrush);
        };
        border.Tapped += (_, _) => action();
        return border;
    }

    private Border CreateToggleRow(string textValue, bool isOn, Action action, Palette p)
    {
        var grid = new Grid { Height = 38 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tb = new TextBlock { Text = textValue, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = p.TextBrush };
        var toggle = new ToggleSwitch { IsOn = isOn, MinWidth = 0, Width = 46, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(tb);
        grid.Children.Add(toggle);
        var border = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 4, 0), Child = grid };
        border.PointerEntered += (_, _) => border.Background = CloneBrush(p.HighlightItemBrush);
        border.PointerExited += (_, _) => border.Background = new SolidColorBrush(Colors.Transparent);
        border.Tapped += (_, _) => action();
        return border;
    }

    private static Border CreateSeparator(SolidColorBrush brush)
    {
        return new Border { Height = 15, Padding = new Thickness(0, 7, 0, 7), Child = new Border { Height = 1, Background = CloneBrush(brush) } };
    }

    private static SolidColorBrush CloneBrush(SolidColorBrush brush) => new SolidColorBrush(brush.Color);

    private sealed class Palette
    {
        public SolidColorBrush BackgroundBrush { get; init; } = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush BorderBrush { get; init; } = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush TextBrush { get; init; } = new SolidColorBrush(Colors.White);
        public SolidColorBrush MutedBrush { get; init; } = new SolidColorBrush(Colors.Gray);
        public SolidColorBrush DescriptionTextBrush { get; init; } = new SolidColorBrush(Colors.Gray);
        public SolidColorBrush FooterTextBrush { get; init; } = new SolidColorBrush(Colors.LightGray);
        public SolidColorBrush HighlightItemBrush { get; init; } = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush ActiveItemBrush { get; init; } = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush ActiveHoverItemBrush { get; init; } = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush FooterBrush { get; init; } = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush FooterHoverBrush { get; init; } = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush FooterPressedTextBrush { get; init; } = new SolidColorBrush(Colors.White);
        public SolidColorBrush SeparatorBrush { get; init; } = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush FooterTopStrokeBrush { get; init; } = new SolidColorBrush(Colors.Transparent);

        public static Palette For(bool light)
        {
            // Light mode still prefers WinUI/Windows App SDK theme resources.
            // Dark mode below intentionally uses the sampled Windows 11 shell
            // palette directly so the custom tray flyout stays visually aligned
            // with the taskbar/flyout surface on customized desktops.
            if (light)
            {
                return new Palette
                {
                    BackgroundBrush = ThemeBrush(UiColor.FromArgb(255, 249, 249, 249), "FlyoutPresenterBackground", "LayerFillColorDefaultBrush", "SolidBackgroundFillColorBaseBrush"),
                    BorderBrush = ThemeBrush(UiColor.FromArgb(255, 218, 218, 218), "SurfaceStrokeColorFlyoutBrush", "SurfaceStrokeColorDefaultBrush", "ControlStrokeColorDefaultBrush"),
                    TextBrush = ThemeBrush(UiColor.FromArgb(255, 28, 28, 28), "TextFillColorPrimaryBrush", "SystemControlForegroundBaseHighBrush"),
                    MutedBrush = ThemeBrush(UiColor.FromArgb(255, 96, 96, 96), "TextFillColorTertiaryBrush", "TextFillColorSecondaryBrush", "SystemControlForegroundBaseMediumBrush"),
                    DescriptionTextBrush = ThemeBrush(UiColor.FromArgb(255, 96, 96, 96), "TextFillColorSecondaryBrush", "SystemControlForegroundBaseMediumBrush"),
                    FooterTextBrush = ThemeBrush(UiColor.FromArgb(255, 96, 96, 96), "TextFillColorSecondaryBrush", "SystemControlForegroundBaseMediumBrush"),
                    FooterPressedTextBrush = ThemeBrush(UiColor.FromArgb(255, 28, 28, 28), "TextFillColorPrimaryBrush", "SystemControlForegroundBaseHighBrush"),
                    HighlightItemBrush = ThemeBrush(UiColor.FromArgb(255, 238, 238, 238), "SubtleFillColorSecondaryBrush", "ControlFillColorSecondaryBrush"),
                    ActiveItemBrush = ThemeBrush(UiColor.FromArgb(255, 241, 241, 241), "SubtleFillColorSecondaryBrush", "ControlFillColorSecondaryBrush"),
                    ActiveHoverItemBrush = ThemeBrush(UiColor.FromArgb(255, 229, 229, 229), "SubtleFillColorTertiaryBrush", "ControlFillColorTertiaryBrush", "SubtleFillColorSecondaryBrush"),
                    FooterBrush = ThemeBrush(UiColor.FromArgb(255, 243, 243, 243), "LayerFillColorAltBrush", "CardBackgroundFillColorSecondaryBrush", "SolidBackgroundFillColorSecondaryBrush"),
                    FooterHoverBrush = ThemeBrush(UiColor.FromArgb(255, 236, 236, 236), "SubtleFillColorSecondaryBrush", "ControlFillColorSecondaryBrush"),
                    SeparatorBrush = ThemeBrush(UiColor.FromArgb(255, 226, 226, 226), "SurfaceStrokeColorDefaultBrush", "ControlStrokeColorDefaultBrush"),
                    FooterTopStrokeBrush = ThemeBrush(UiColor.FromArgb(255, 226, 226, 226), "SurfaceStrokeColorDefaultBrush", "ControlStrokeColorDefaultBrush"),
                };
            }

            // Dark-mode flyout uses the sampled Windows 11 shell colors as
            // the authoritative custom-flyout palette. This avoids WinUI theme
            // resources pulling the surface away from the shell/taskbar look on
            // customized desktops, while light mode still follows platform
            // resources until we have a sampled light palette.
            return new Palette
            {
                BackgroundBrush = DirectBrush(0x24, 0x24, 0x24),
                HighlightItemBrush = DirectBrush(0x31, 0x31, 0x31),
                ActiveItemBrush = DirectBrush(0x31, 0x31, 0x31),
                ActiveHoverItemBrush = DirectBrush(0x2D, 0x2D, 0x2D),
                FooterBrush = DirectBrush(0x1C, 0x1C, 0x1C),
                FooterHoverBrush = DirectBrush(0x1C, 0x1C, 0x1C),
                FooterPressedTextBrush = DirectBrush(0xFF, 0xFF, 0xFF),
                BorderBrush = DirectBrush(0x6E, 0x7A, 0x7A, 0x7A),
                TextBrush = DirectBrush(0xFF, 0xFF, 0xFF),
                MutedBrush = DirectBrush(0x78, 0x78, 0x78),
                DescriptionTextBrush = DirectBrush(0xCD, 0xCD, 0xCD),
                FooterTextBrush = DirectBrush(0xCB, 0xCB, 0xCB),
                SeparatorBrush = DirectBrush(0x19, 0x19, 0x19),
                FooterTopStrokeBrush = DirectBrush(0x19, 0x19, 0x19),
            };
        }

        private static SolidColorBrush DirectBrush(byte a, byte r, byte g, byte b)
            => new SolidColorBrush(UiColor.FromArgb(a, r, g, b));

        private static SolidColorBrush DirectBrush(byte r, byte g, byte b)
            => DirectBrush(255, r, g, b);

        private static SolidColorBrush ThemeBrush(UiColor fallback, params string[] keys)
        {
            try
            {
                ResourceDictionary? resources = Microsoft.UI.Xaml.Application.Current?.Resources;
                if (resources != null)
                {
                    foreach (string key in keys)
                    {
                        if (resources.TryGetValue(key, out object value))
                        {
                            if (value is SolidColorBrush solid)
                            {
                                // Clone to avoid mutating shared theme resources during hover changes.
                                return new SolidColorBrush(solid.Color);
                            }
                            if (value is UiColor color)
                            {
                                return new SolidColorBrush(color);
                            }
                        }
                    }
                }
            }
            catch { }
            return new SolidColorBrush(fallback);
        }
    }

    private static UiColor HexToWinUIColor(string hex)
    {
        string h = hex.Trim().TrimStart('#');
        return UiColor.FromArgb(255, Convert.ToByte(h[..2], 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16));
    }

    private enum TaskbarEdge { Left, Top, Right, Bottom }

    private static bool TryGetNearestTaskbar(System.Drawing.Point anchor, Forms.Screen screen, out System.Drawing.Rectangle taskbar, out TaskbarEdge edge)
    {
        taskbar = System.Drawing.Rectangle.Empty;
        edge = TaskbarEdge.Bottom;
        var candidates = new List<System.Drawing.Rectangle>();

        AddTaskbarWindows("Shell_TrayWnd", candidates);
        AddTaskbarWindows("Shell_SecondaryTrayWnd", candidates);

        System.Drawing.Rectangle screenBounds = screen.Bounds;
        candidates = candidates
            .Where(r => r.Width > 0 && r.Height > 0 && r.IntersectsWith(screenBounds))
            .ToList();

        if (candidates.Count == 0)
        {
            return false;
        }

        taskbar = candidates
            .OrderBy(r => DistanceSquared(anchor, new System.Drawing.Point(r.Left + r.Width / 2, r.Top + r.Height / 2)))
            .First();

        edge = ClassifyTaskbarEdge(taskbar, screenBounds);
        return true;
    }

    private static void AddTaskbarWindows(string className, List<System.Drawing.Rectangle> output)
    {
        IntPtr previous = IntPtr.Zero;
        while (true)
        {
            IntPtr hwnd = FindWindowEx(IntPtr.Zero, previous, className, null);
            if (hwnd == IntPtr.Zero) break;
            previous = hwnd;
            if (GetWindowRect(hwnd, out NativeRect r))
            {
                output.Add(System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            }
        }
    }

    private static int DistanceSquared(System.Drawing.Point a, System.Drawing.Point b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static TaskbarEdge ClassifyTaskbarEdge(System.Drawing.Rectangle r, System.Drawing.Rectangle screen)
    {
        bool horizontal = r.Width >= r.Height;
        if (horizontal)
        {
            return r.Top + r.Height / 2 < screen.Top + screen.Height / 2 ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }
        return r.Left + r.Width / 2 < screen.Left + screen.Width / 2 ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);


    private static void ApplyWindows11FlyoutChrome(IntPtr hwnd)
    {
        try
        {
            nint style = GetWindowLongPtr(hwnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
            SetWindowLongPtr(hwnd, GWL_STYLE, style);

            int preference = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            // We provide our own tray-flyout slide animations. Disable DWM's
            // top-level show/hide transition for this utility window so closing
            // does not stack into a second, minimize-like movement.
            int forceDisableTransitions = 1;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref forceDisableTransitions, sizeof(int));

            int noBorder = unchecked((int)0xFFFFFFFE);
            _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch { }
    }

    private void ApplyRoundedWindowRegion(int width, int height, int cornerRadius)
    {
        try
        {
            int diameter = Math.Max(1, cornerRadius * 2);
            IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region == IntPtr.Zero) return;

            // On success, Windows owns the region handle. Only delete it if the
            // region was rejected, otherwise the rounded HWND clipping would be
            // removed immediately.
            if (SetWindowRgn(_hwnd, region, true) == 0)
            {
                _ = DeleteObject(region);
            }
        }
        catch { }
    }

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    private static nint GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong)
    {
        if (IntPtr.Size == 8) return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        return SetWindowLong32(hWnd, nIndex, unchecked((int)dwNewLong));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;

    private const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_TOPALIGN = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RIGHTALIGN = 0x0008;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_WORKAREA = 0x10000;
    private const int CMD_MORE_POWER_SETTINGS = 1001;
    private const int CMD_REMOVE_FROM_TRAY = 1002;
    private const int CMD_QUIT = 1003;
    private const uint WM_NULL = 0x0000;
    private const int SM_CXMENUCHECK = 71;
    private const int SM_CXMENUSIZE = 54;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const int PreferredAppModeForceDark = 2;
    private const int PreferredAppModeForceLight = 3;

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = false)]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowDarkModeForWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = false)]
    private static extern void FlushMenuThemes();
}

internal static class IconFactory
{
    public static Icon CreateTrayIcon(System.Drawing.Color dotColor, bool lightMode)
    {
        using Bitmap bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(System.Drawing.Color.Transparent);
            System.Drawing.Color stroke = lightMode ? System.Drawing.Color.FromArgb(235, 32, 32, 32) : System.Drawing.Color.FromArgb(245, 245, 245, 245);
            System.Drawing.Color shadow = System.Drawing.Color.FromArgb(70, 0, 0, 0);
            using (Pen shadowPen = new Pen(shadow, 5.2f))
            using (Pen pen = new Pen(stroke, 4.6f))
            {
                shadowPen.StartCap = System.Drawing.Drawing2D.LineCap.Round; shadowPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round; pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawArc(shadowPen, new System.Drawing.Rectangle(4, 6, 24, 24), -215, 250);
                g.DrawLine(shadowPen, 16, 3, 16, 15);
                g.DrawArc(pen, new System.Drawing.Rectangle(4, 5, 24, 24), -215, 250);
                g.DrawLine(pen, 16, 2, 16, 14);
            }
            System.Drawing.Color borderColor = lightMode ? System.Drawing.Color.FromArgb(245, 255, 255, 255) : System.Drawing.Color.FromArgb(230, 24, 24, 24);
            using System.Drawing.Brush border = new SolidBrush(borderColor);
            using System.Drawing.Brush dot = new SolidBrush(dotColor);
            g.FillEllipse(border, 15, 15, 16, 16);
            g.FillEllipse(dot, 17, 17, 12, 12);
        }
        IntPtr hIcon = bmp.GetHicon();
        Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
