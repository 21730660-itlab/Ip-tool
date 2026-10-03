using System.Windows.Controls.Primitives;

namespace IpMonitor.App;

/// <summary>A text box with a list of suggestions under it that narrows down while typing (like a search drop-down).</summary>
public class SuggestBox : Grid
{
    public record Item(string Value, string Title, string Sub, string Tag, object Data);

    public readonly TextBox Box;
    readonly Popup pop;
    readonly ListBox list;
    readonly Func<string, IEnumerable<Item>> source;
    bool picking;
    public event Action<Item> Picked;
    public event Action<string> Typed;

    public SuggestBox(string text, Func<string, IEnumerable<Item>> source, string placeholderHint = null)
    {
        this.source = source;
        Box = Ui.Box(text);
        Box.Padding = new Thickness(8, 6, 36, 6);
        if (placeholderHint != null) Box.ToolTip = placeholderHint;
        Children.Add(Box);
        var arrow = new Button { Width = 32, MinHeight = 0, Height = 30, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Focusable = false, ToolTip = "Show the list",
            Content = new Shapes.Path { Data = Geometry.Parse("M0,0 L5,5 L10,0"), StrokeThickness = 1.8, Stroke = Theme.B("Muted") } };
        arrow.Click += (_, _) => { if (pop.IsOpen) pop.IsOpen = false; else { Fill(""); Box.Focus(); } };
        Children.Add(arrow);

        list = new ListBox { MaxHeight = 340, BorderThickness = new Thickness(0), Focusable = false };
        list.SetResourceReference(Control.BackgroundProperty, "Panel");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem li && li.Tag is Item it) Pick(it);
        };
        var border = new Border { Child = list, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 2, 0, 0) }
            .Res(Border.BackgroundProperty, "Panel").Res(Border.BorderBrushProperty, "Frame");
        pop = new Popup { Child = border, PlacementTarget = Box, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        Children.Add(pop);

        Box.TextChanged += (_, _) => { if (picking) return; Typed?.Invoke(Box.Text); if (Box.IsKeyboardFocused) Fill(Box.Text); };
        Box.GotKeyboardFocus += (_, _) => { if (!picking) Fill(Box.Text); };
        Box.PreviewKeyDown += (_, e) =>
        {
            if (!pop.IsOpen) { if (e.Key == Key.Down) { Fill(Box.Text); e.Handled = true; } return; }
            int n = list.Items.Count;
            if (e.Key == Key.Down && n > 0) { list.SelectedIndex = Math.Min(n - 1, list.SelectedIndex + 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Up && n > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
            else if ((e.Key == Key.Enter || e.Key == Key.Tab) && list.SelectedItem is ListBoxItem li && li.Tag is Item it) { Pick(it); e.Handled = e.Key == Key.Enter; }
            else if (e.Key == Key.Escape) { pop.IsOpen = false; e.Handled = true; }
        };
    }

    public string Text { get => Box.Text; set { picking = true; Box.Text = value ?? ""; picking = false; } }

    void Fill(string typed)
    {
        list.Items.Clear();
        foreach (var it in source(typed).Take(60))
        {
            var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            if (!string.IsNullOrEmpty(it.Tag))
            {
                var tag = Ui.Badge(it.Tag, Theme.B("Acc"), Theme.B("AccSoft")); tag.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(tag, Dock.Right); row.Children.Add(tag);
            }
            var t = new StackPanel();
            t.Children.Add(Ui.Text(it.Title, 14, FontWeights.SemiBold, "Ink", false));
            if (!string.IsNullOrEmpty(it.Sub)) t.Children.Add(Ui.Muted(it.Sub, 12));
            row.Children.Add(t);
            list.Items.Add(new ListBoxItem { Content = row, Tag = it, Padding = new Thickness(8, 4, 8, 4), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        if (list.Items.Count == 0) { pop.IsOpen = false; return; }
        list.SelectedIndex = 0;
        pop.MinWidth = Box.ActualWidth; pop.Width = Math.Max(Box.ActualWidth, 420);
        pop.IsOpen = true;
    }

    void Pick(Item it)
    {
        picking = true; Box.Text = it.Value; Box.CaretIndex = Box.Text.Length; picking = false;
        pop.IsOpen = false;
        Picked?.Invoke(it);
    }
}
