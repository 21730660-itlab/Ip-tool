using System.Collections;
using System.Windows.Controls.Primitives;

namespace IpMonitor.App;

/// <summary>Small builders so the screens read like a layout.</summary>
public static class Ui
{
    public const string Mono = "Consolas";

    public static T Res<T>(this T e, DependencyProperty dp, string key) where T : FrameworkElement { e.SetResourceReference(dp, key); return e; }

    public static TextBlock Text(string t, double size = 14, FontWeight? weight = null, string brush = "Ink", bool wrap = true, bool mono = false)
    {
        var tb = new TextBlock { Text = t ?? "", FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (mono) tb.FontFamily = new FontFamily(Mono);
        if (!wrap) tb.TextTrimming = TextTrimming.CharacterEllipsis;
        return tb.Res(TextBlock.ForegroundProperty, brush);
    }
    public static TextBlock Muted(string t, double size = 13) => Text(t, size, null, "Muted");
    public static TextBlock Error() => Text("", 14, FontWeights.SemiBold, "Sig");

    public static Button Btn(string text, Action click, string style = null, string tip = null)
    {
        var b = new Button { Content = text };
        if (style != null) b.Style = (Style)Application.Current.Resources[style];
        if (tip != null) b.ToolTip = tip;
        b.Click += (_, _) => click();
        return b;
    }
    /// <summary>A button with a Segoe icon in front of its text.</summary>
    public static Button IconBtn(string glyph, string text, Action click, string style = null, string tip = null)
    {
        var b = Btn("", click, style, tip);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (!HasIcons) { b.Content = text == "" ? (tip ?? "•") : text; return b; }
        sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, text == "" ? 0 : 8, 0) });
        if (text != "") sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        b.Content = sp;
        return b;
    }
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    /// <summary>Windows 10 and 11 have an icon font; without it, buttons show their text only.</summary>
    public static readonly bool HasIcons = Fonts.SystemFontFamilies.Any(f => f.Source is "Segoe MDL2 Assets" or "Segoe Fluent Icons");

    public static TextBox Box(string text = "", bool multi = false, bool mono = false, string tip = null)
    {
        var t = new TextBox { Text = text ?? "" };
        if (multi) { t.AcceptsReturn = true; t.TextWrapping = TextWrapping.Wrap; t.MinHeight = 74; t.VerticalContentAlignment = VerticalAlignment.Top; t.MaxHeight = 160; }
        if (mono) t.FontFamily = new FontFamily(Mono);
        if (tip != null) t.ToolTip = tip;
        return t;
    }

    /// <summary>A drop-down of (value, label) pairs.</summary>
    public static ComboBox Choice(IEnumerable<(string value, string label)> items, string selected)
    {
        var c = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Value", MaxDropDownHeight = 360 };
        c.ItemsSource = items.Select(i => new Opt(i.value, i.label)).ToList();
        c.SelectedValue = selected ?? "";
        if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }
    public static string Val(this ComboBox c) => c.SelectedValue as string ?? "";
    public static ComboBox Choice(Dictionary<string, string> map, string selected) => Choice(map.Select(kv => (kv.Key, kv.Value)), selected);

    /// <summary>A drop-down that also accepts typed text (interface names, for example).</summary>
    public static ComboBox Editable(IEnumerable<string> items, string text)
    {
        var c = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, MaxDropDownHeight = 360 };
        c.ItemsSource = items.ToList();
        c.Text = text ?? "";
        return c;
    }

    public static Border Card(UIElement child, double pad = 18) => new Border
    {
        Child = child, Padding = new Thickness(pad), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
    }.Res(Border.BackgroundProperty, "Panel").Res(Border.BorderBrushProperty, "Line");

    /// <summary>Label above a field, with an optional hint below.</summary>
    public static FrameworkElement Field(string label, UIElement field, string hint = null)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        if (!string.IsNullOrEmpty(label)) { var l = Text(label, 13.5, FontWeights.SemiBold, "Ink2"); l.Margin = new Thickness(0, 0, 0, 5); sp.Children.Add(l); }
        if (field is FrameworkElement fe && !double.IsNaN(fe.Width) && fe.HorizontalAlignment == HorizontalAlignment.Stretch) fe.HorizontalAlignment = HorizontalAlignment.Left;   // fixed-width fields sit on the left
        sp.Children.Add(field);
        if (!string.IsNullOrEmpty(hint)) { var h = Muted(hint, 12.5); h.Margin = new Thickness(1, 4, 0, 0); sp.Children.Add(h); }
        return sp;
    }

    /// <summary>Side by side, equal widths.</summary>
    public static Grid Cols(params UIElement[] items)
    {
        var g = new Grid();
        for (int i = 0; i < items.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (items[i] is FrameworkElement fe) fe.Margin = new Thickness(i == 0 ? 0 : 7, fe.Margin.Top, i == items.Length - 1 ? 0 : 7, fe.Margin.Bottom);
            Grid.SetColumn(items[i], i); g.Children.Add(items[i]);
        }
        return g;
    }

    public static StackPanel Row(double gap, params UIElement[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) { if (i is FrameworkElement fe && sp.Children.Count > 0) fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom); sp.Children.Add(i); }
        return sp;
    }

    public static TextBlock Section(string t)
    {
        var tb = Text(t, 15, FontWeights.Bold);
        tb.Margin = new Thickness(0, 8, 0, 10);
        return tb;
    }

    /// <summary>A coloured tag such as a status.</summary>
    public static Border Badge(string text, Brush fg, Brush bg) => new Border
    {
        Background = bg, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, Foreground = fg, FontWeight = FontWeights.SemiBold, FontSize = 12.5 }
    };

    // ------------------------------------------------------------------ tables
    public static DataGrid Table()
    {
        var g = new DataGrid();
        VirtualizingPanel.SetIsVirtualizing(g, true);
        return g;
    }

    /// <param name="width">0 = fit the content, negative = share of the free space, positive = pixels.</param>
    public static DataGridTextColumn Col(string header, string path, double width = 0, bool mono = false, string sortPath = null, bool wrap = false)
    {
        var c = new DataGridTextColumn { Header = header, Binding = new Binding(path), SortMemberPath = sortPath ?? path };
        c.Width = width == 0 ? DataGridLength.Auto : width < 0 ? new DataGridLength(-width, DataGridLengthUnitType.Star) : new DataGridLength(width);
        var st = new Style(typeof(TextBlock));
        st.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        st.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        if (wrap) st.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        if (mono) st.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily(Mono)));
        c.ElementStyle = st;
        return c;
    }

    /// <summary>A column showing a coloured tag: the row needs Text, Fg and Bg properties named by the paths.</summary>
    public static DataGridTemplateColumn BadgeCol(string header, string textPath, string fgPath, string bgPath)
    {
        var b = new FrameworkElementFactory(typeof(Border));
        b.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        b.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 2));
        b.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        b.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        b.SetBinding(Border.BackgroundProperty, new Binding(bgPath));
        var t = new FrameworkElementFactory(typeof(TextBlock));
        t.SetBinding(TextBlock.TextProperty, new Binding(textPath));
        t.SetBinding(TextBlock.ForegroundProperty, new Binding(fgPath));
        t.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        t.SetValue(TextBlock.FontSizeProperty, 12.5);
        b.AppendChild(t);
        return new DataGridTemplateColumn { Header = header, CellTemplate = new DataTemplate { VisualTree = b }, SortMemberPath = textPath, Width = DataGridLength.Auto };
    }

    /// <summary>Runs the action when a row (not the header) is double-clicked.</summary>
    public static void OnRowDoubleClick(DataGrid g, Action<object> act)
    {
        g.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(g, d) is DataGridRow row) act(row.Item);
        };
        g.KeyDown += (_, e) => { if (e.Key == Key.Enter && g.SelectedItem != null) { act(g.SelectedItem); e.Handled = true; } };
    }

    /// <summary>Sorts IPs by number, not as text ("10.0.0.2" before "10.0.0.10").</summary>
    public static string IpKey(string ip)
    {
        var p = IpMath.ParseRaw(ip);
        return p == null ? "~" + (ip ?? "") : $"{p.V}{p.Addr.ToString().PadLeft(40, '0')}{(p.Prefix ?? 999):000}";
    }

    // ------------------------------------------------------------------ logo
    /// <summary>The blue hexagon with the network nodes, as in the web version.</summary>
    public static Viewbox Logo(double size)
    {
        var c = new Canvas { Width = 40, Height = 40 };
        var grad = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#3B93FF"), (Color)ColorConverter.ConvertFromString("#0B4FB3"), new Point(0, 0), new Point(1, 1));
        c.Children.Add(new Shapes.Path { Data = Geometry.Parse("M20 2.5 35.2 11.25v17.5L20 37.5 4.8 28.75v-17.5z"), Fill = grad });
        c.Children.Add(new Shapes.Path { Data = Geometry.Parse("M20 20 12.3 14.6M20 20l7.7-5.4M20 20v8.8"), Stroke = Brushes.White, StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        void Dot(double x, double y, double r, Brush fill, double stroke = 0)
        {
            var e = new Shapes.Ellipse { Width = r * 2, Height = r * 2, Fill = fill };
            if (stroke > 0) { e.Stroke = Brushes.White; e.StrokeThickness = stroke; }
            Canvas.SetLeft(e, x - r); Canvas.SetTop(e, y - r); c.Children.Add(e);
        }
        Dot(12.3, 14.6, 2.9, Brushes.White); Dot(27.7, 14.6, 2.9, Brushes.White); Dot(20, 28.8, 2.9, Brushes.White);
        Dot(20, 20, 3.7, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3DD68C")), 1.6);
        return new Viewbox { Width = size, Height = size, Child = c };
    }

    // ------------------------------------------------------------------ messages
    public static bool Ask(string message, string title = "IP Monitor", string ok = "OK", bool danger = false, string cancel = "Cancel")
    {
        var d = new Dlg(title, 480);
        d.Body.Children.Add(Text(message, 14.5));
        d.Ok(ok, () => true, danger ? "Danger" : "Primary");
        if (cancel != null) d.Cancel(cancel);
        return d.Open();
    }
    public static void Info(string message, string title = "IP Monitor") => Ask(message, title, "OK", false, null);
}

public record Opt(string Value, string Label) { public override string ToString() => Label; }

/// <summary>A dialog in the app's look: title bar, scrolling form, error line and buttons.</summary>
public class Dlg : Window
{
    public readonly StackPanel Body = new() { Margin = new Thickness(22, 18, 22, 8) };
    readonly TextBlock err = Ui.Error();
    readonly StackPanel right = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly StackPanel left = new() { Orientation = Orientation.Horizontal };

    public Dlg(string title, double width = 600)
    {
        Title = title; Width = width; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        var owner = Application.Current.MainWindow;
        if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        this.SetResourceReference(BackgroundProperty, "Bg");
        this.SetResourceReference(ForegroundProperty, "Ink");

        var dock = new DockPanel();
        var head = new Border { Padding = new Thickness(22, 14, 22, 14), BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.Text(title, 19, FontWeights.Bold) }
            .Res(Border.BackgroundProperty, "Panel").Res(Border.BorderBrushProperty, "Line");
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head);

        var foot = new Grid();
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition());
        foot.Children.Add(left); Grid.SetColumn(right, 1); foot.Children.Add(right);
        var footWrap = new StackPanel();
        err.Margin = new Thickness(0, 0, 0, 10); err.Visibility = Visibility.Collapsed;
        footWrap.Children.Add(err); footWrap.Children.Add(foot);
        var footBorder = new Border { Padding = new Thickness(22, 12, 22, 14), BorderThickness = new Thickness(0, 1, 0, 0), Child = footWrap }
            .Res(Border.BackgroundProperty, "Panel").Res(Border.BorderBrushProperty, "Line");
        DockPanel.SetDock(footBorder, Dock.Bottom); dock.Children.Add(footBorder);

        dock.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
    }

    public string ErrorText
    {
        get => err.Text;
        set { err.Text = value ?? ""; err.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; }
    }

    /// <summary>Runs act; a broken rule is shown in red on the dialog instead of closing it.</summary>
    public bool Attempt(Func<bool> act)
    {
        ErrorText = "";
        try { return act(); }
        catch (RuleException e) { ErrorText = e.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException) { ErrorText = e.Message; }
        return false;
    }

    public Button Ok(string text, Func<bool> act, string style = "Primary")
    {
        var b = Ui.Btn(text, () => { if (Attempt(act)) DialogResult = true; }, style);
        b.IsDefault = true; b.MinWidth = 96; b.Margin = new Thickness(10, 0, 0, 0);
        right.Children.Add(b);
        return b;
    }
    public Button Cancel(string text = "Cancel")
    {
        var b = Ui.Btn(text, () => DialogResult = false);
        b.IsCancel = true; b.MinWidth = 96; b.Margin = new Thickness(10, 0, 0, 0);
        right.Children.Insert(0, b);
        return b;
    }
    /// <summary>A button on the left of the footer (Delete, for example).</summary>
    public Button Extra(string text, Action act, string style = null)
    {
        var b = Ui.Btn(text, act, style); b.Margin = new Thickness(0, 0, 10, 0);
        left.Children.Add(b);
        return b;
    }
    public bool Open() => ShowDialog() == true;
}
