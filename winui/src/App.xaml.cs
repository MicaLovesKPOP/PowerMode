using Microsoft.UI.Xaml;

namespace Mica.PowerModeTray.WinUI;

public sealed class App : Application
{
    private PowerModeTrayApplication? _trayApplication;

    public App()
    {
        UnhandledException += (_, e) =>
        {
            Program.WriteCrashLog("Unhandled WinUI exception.", e.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _trayApplication = new PowerModeTrayApplication();
    }
}
