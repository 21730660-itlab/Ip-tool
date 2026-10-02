namespace IpMonitor.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("Something went wrong:\n\n" + e.Exception.GetType().Name + ": " + e.Exception.Message + "\n\nYour data was saved after your last successful change.", "IP Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        var settings = Settings.Load();
        Theme.Init(app, settings.Theme == "dark");
        app.Run(new MainWindow(settings));
    }
}
