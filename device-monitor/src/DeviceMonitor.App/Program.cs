namespace DeviceMonitor.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // one copy at a time: a second start brings the running one to the front
        using var single = new Mutex(true, @"Local\DeviceMonitor-single-instance", out bool first);
        using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DeviceMonitor-show");
        if (!first) { wake.Set(); return; }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("Something went wrong:\n\n" + e.Exception.GetType().Name + ": " + e.Exception.Message + "\n\nMonitoring keeps running.", App.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        App.Init();
        Theme.Init(app, App.Settings.DarkTheme);
        var win = new MainWindow(startHidden: args.Contains("--minimized"));

        // listen for "show yourself" from a second copy
        var t = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    wake.WaitOne();
                    app.Dispatcher.BeginInvoke(() => win.ShowFromTray());
                }
            }
            catch (ObjectDisposedException) { /* program is closing */ }
        }) { IsBackground = true };
        t.Start();

        app.Run();
    }
}
