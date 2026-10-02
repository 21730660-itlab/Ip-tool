using System.Media;
using System.Windows.Threading;

namespace IpMonitor.App;

/// <summary>Pop-up notifications in the bottom-right corner of the screen (on top of other programs), stacked.</summary>
public static class Notifier
{
    static readonly List<Window> open = new();

    /// <param name="down">true = red "device down" alert (stays until closed), false = green "back up" (closes by itself).</param>
    public static void Show(string title, string line1, string line2, bool down, Action onOpen, bool sound)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            Width = 400, SizeToContent = SizeToContent.Height, AllowsTransparency = true, Background = Brushes.Transparent,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 14
        };
        var accent = Theme.B(down ? "Sig" : "Ok");
        var body = new DockPanel { Margin = new Thickness(16, 12, 12, 14) };
        var close = new Button { Content = "✕", Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        close.SetResourceReference(Control.ForegroundProperty, "Muted");
        close.Click += (_, _) => Close(w);
        DockPanel.SetDock(close, Dock.Right); body.Children.Add(close);
        var sp = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = accent, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 15.5, Foreground = accent });
        sp.Children.Add(head);
        var t1 = Ui.Text(line1, 15, FontWeights.SemiBold, "Ink"); t1.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(t1);
        if (!string.IsNullOrEmpty(line2)) { var t2 = Ui.Muted(line2, 13); t2.Margin = new Thickness(0, 2, 0, 0); sp.Children.Add(t2); }
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var openB = Ui.Btn("Show on map", () => { Close(w); onOpen?.Invoke(); }, "Primary"); openB.MinHeight = 32; openB.Padding = new Thickness(12, 4, 12, 4);
        var ok = Ui.Btn("OK", () => Close(w)); ok.MinHeight = 32; ok.Padding = new Thickness(14, 4, 14, 4); ok.Margin = new Thickness(8, 0, 0, 0);
        btns.Children.Add(openB); btns.Children.Add(ok);
        sp.Children.Add(btns);
        body.Children.Add(sp);
        var card = new Border { Child = body, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1, 1, 1, 1), Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35 } };
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        card.BorderBrush = accent;
        var outer = new Border { Child = card, BorderThickness = new Thickness(0) };
        var bar = new Border { Width = 5, CornerRadius = new CornerRadius(8, 0, 0, 8), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8) };
        var g = new Grid(); g.Children.Add(outer); g.Children.Add(bar);
        w.Content = g;
        open.Add(w);
        w.Loaded += (_, _) => Layout();
        w.Closed += (_, _) => { open.Remove(w); Layout(); };
        w.Show();
        if (sound) (down ? SystemSounds.Exclamation : SystemSounds.Asterisk).Play();
        if (!down)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            t.Tick += (_, _) => { t.Stop(); Close(w); };
            t.Start();
        }
        // many at once: keep the newest 5
        while (open.Count > 5) Close(open[0]);
    }

    static void Close(Window w) { if (w.IsLoaded) w.Close(); }

    static void Layout()
    {
        var area = SystemParameters.WorkArea;
        double y = area.Bottom - 6;
        for (int i = open.Count - 1; i >= 0; i--)
        {
            var w = open[i];
            var h = w.ActualHeight > 0 ? w.ActualHeight : 150;
            y -= h;
            w.Left = area.Right - w.Width - 6; w.Top = y;
        }
    }
}
