using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace UsbToC64.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Any unexpected error is shown with its details (to report it) instead of closing the program silently.
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Report(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Report(a.Exception); a.SetObserved(); };

        base.OnStartup(e);
        try
        {
            // Follow the Windows light/dark theme.
            ApplicationThemeManager.ApplySystemTheme();
            var w = new MainWindow();
            MainWindow = w;
            SystemThemeWatcher.Watch(w);
            w.Show();
        }
        catch (Exception ex)
        {
            Report(ex);
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception);
        e.Handled = true;
    }

    private static void Report(Exception? ex)
    {
        if (ex == null) return;
        var text = ex.ToString();
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "USBtoC64Config-errore.txt");
            File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{text}");
            text += $"\n\nDettagli salvati in: {path}";
        }
        catch (Exception) { /* folder not writable */ }
        MessageBox.Show(text, "USBtoC64 Config - errore imprevisto", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
