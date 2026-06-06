using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace Mica.PowerModeTray.WinUI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        PowerModeLog.InitializeCleanLogLayout();
        PowerModeLog.Event("Power Mode started",
            "Version: " + PowerModeLog.GetVersionText(),
            "Session: " + PowerModeLog.SessionId);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            WriteCrashLog("Unhandled AppDomain exception.", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };

        try
        {
            PowerModeSafety.ApplyStartupGuard();
            PowerModeSafety.RegisterShutdownGuard();
            PowerModeSafety.StartPostLoginManualRestoreMonitor();

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start((p) => { _ = new App(); });
        }
        catch (Exception ex)
        {
            WriteCrashLog("Fatal exception during Power Mode startup.", ex);
            throw;
        }
    }

    internal static void WriteCrashLog(string message, Exception? ex = null)
    {
        PowerModeLog.Crash(message, ex);
    }
}
