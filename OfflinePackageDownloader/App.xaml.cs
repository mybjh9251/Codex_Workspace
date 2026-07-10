using System;
using System.Threading.Tasks;
using System.Windows;

namespace OfflinePackageDownloader;

public partial class App : Application
{
    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--smoke", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await SmokeRunner.RunAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
