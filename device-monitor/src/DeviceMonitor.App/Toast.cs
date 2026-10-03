using System.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeviceMonitor.App;

/// <summary>
/// Pop-ups in the bottom-right corner of the screen, on top of every program, stacked (newest at the bottom).
/// Red = a device turned OFF, green = a device is back ON.
/// </summary>
public static class Toast
{
    const int MaxOpen = 6;
    static readonly List<Window> open = new();

    public static void Show(MonitorEvent e, bool down, bool sound, int seconds)
    {
        var accentKey = down ? "Sig" : "Ok";
        var accent = Theme.B(accentKey);
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            Width = 420, SizeToContent = SizeToContent.Height, AllowsTransparency = true, Background = Brushes.Transparent,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 15, Opacity = 0
        };

        // big round status icon
        var icon = new Grid { Width = 52, Height = 52, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 14, 0) };
        icon.Children.Add(new Shapes.Ellipse { Fill = Theme.B(down ? "SigSoft" : "OkSoft") });
        icon.Children.Add(new Shapes.Ellipse { Width = 36, Height = 36, Fill = accent });
        icon.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse(down ? "M0,0 L12,12 M12,0 L0,12" : "M0,6 L4.5,10.5 L13,1.5"),
            Stroke = Brushes.White, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });

        var close = new Button { Content = "✕", Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        close.SetResourceReference(Control.ForegroundProperty, "Muted");
        close.Click += (_, _) => Close(w);

        var sp = new StackPanel();
        var title = Ui.Text(down ? "DEVICE OFF" : "DEVICE BACK ON", 13, FontWeights.Bold, accentKey);
        sp.Children.Add(title);
        var name = Ui.Text(e.DeviceName, 18, FontWeights.SemiBold, "Ink"); name.Margin = new Thickness(0, 2, 0, 0);
        sp.Children.Add(name);
        sp.Children.Add(Ui.Text($"{e.Address}{(string.IsNullOrEmpty(e.Site) ? "" : "   ·   Site: " + e.Site)}", 14, null, "Ink2"));
        var detail = down
            ? $"{e.Detail}  ·  {e.At:HH:mm:ss}"
            : (e.Duration is TimeSpan t ? $"Was OFF for {EventLog.Duration(t)}  ·  " : "") + $"{e.Detail}  ·  {e.At:HH:mm:ss}";
        var dt = Ui.Muted(detail, 13); dt.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(dt);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var details = Ui.Btn("Show details", () => { Close(w); App.RequestShowDevice(e.DeviceId); }, "Primary"); details.MinHeight = 32; details.Padding = new Thickness(12, 4, 12, 4);
        var ok = Ui.Btn("OK", () => Close(w)); ok.MinHeight = 32; ok.Padding = new Thickness(16, 4, 16, 4); ok.Margin = new Thickness(8, 0, 0, 0);
        btns.Children.Add(details); btns.Children.Add(ok);
        sp.Children.Add(btns);

        var row = new DockPanel { Margin = new Thickness(18, 14, 10, 16) };
        DockPanel.SetDock(close, Dock.Right); row.Children.Add(close);
        DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        row.Children.Add(sp);

        var card = new Border
        {
            Child = row, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = accent, Margin = new Thickness(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.4 }
        };
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        var bar = new Border { Width = 6, CornerRadius = new CornerRadius(10, 0, 0, 10), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10) };
        var g = new Grid(); g.Children.Add(card); g.Children.Add(bar);

        // a thin bar showing the time left before it closes
        if (seconds > 0)
        {
            var timeBar = new Border { Height = 3, Background = accent, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 0, 16, 11), Opacity = 0.7, Width = 380 };
            g.Children.Add(timeBar);
            w.Loaded += (_, _) => timeBar.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(380, 0, TimeSpan.FromSeconds(seconds)));
        }
        w.Content = g;

        open.Add(w);
        w.Loaded += (_, _) => { Layout(); w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))); };
        w.Closed += (_, _) => { open.Remove(w); Layout(); };
        w.Show();
        if (sound) (down ? SystemSounds.Hand : SystemSounds.Asterisk).Play();
        if (seconds > 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) => { timer.Stop(); Close(w); };
            timer.Start();
        }
        while (open.Count > MaxOpen) Close(open[0]);
    }

    public static void CloseAll() { foreach (var w in open.ToList()) Close(w); }

    static void Close(Window w) { if (w.IsLoaded) w.Close(); }

    static void Layout()
    {
        var area = SystemParameters.WorkArea;
        double y = area.Bottom - 4;
        for (int i = open.Count - 1; i >= 0; i--)
        {
            var w = open[i];
            var h = w.ActualHeight > 0 ? w.ActualHeight : 170;
            y -= h;
            w.Left = area.Right - w.Width - 4; w.Top = y;
        }
    }
}
