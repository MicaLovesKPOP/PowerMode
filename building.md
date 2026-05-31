using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Mica.PowerModeTray.WinUI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicaLovesKPOP", "PowerMode");
            Directory.CreateDirectory(logDir);

            string logPath = Path.Combine(logDir, "PowerModeTray-crash.log");
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine;
            if (ex != null)
            {
                line += ex.ToString() + Environment.NewLine;
            }

            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch { }
    }
}
