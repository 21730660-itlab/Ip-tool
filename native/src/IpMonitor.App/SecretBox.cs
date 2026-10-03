namespace IpMonitor.App;

/// <summary>A password field with an eye button to show or hide what is typed.</summary>
public class SecretBox : Grid
{
    readonly PasswordBox pw = new();
    readonly TextBox plain = new() { Visibility = Visibility.Collapsed };
    readonly Button eye;
    bool shown, syncing;
    public event Action PasswordChanged;

    public SecretBox(string value = "")
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        pw.Password = value ?? ""; plain.Text = value ?? "";
        pw.Padding = plain.Padding = new Thickness(8, 6, 40, 6);
        Children.Add(pw); Children.Add(plain);
        eye = new Button
        {
            Width = 34, MinHeight = 0, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Focusable = false, ToolTip = "Show password"
        };
        eye.SetResourceReference(Control.ForegroundProperty, "Muted");
        SetEye();
        eye.Click += (_, _) => Toggle();
        Children.Add(eye);
        pw.PasswordChanged += (_, _) => { if (syncing) return; syncing = true; plain.Text = pw.Password; syncing = false; PasswordChanged?.Invoke(); };
        plain.TextChanged += (_, _) => { if (syncing) return; syncing = true; pw.Password = plain.Text; syncing = false; PasswordChanged?.Invoke(); };
    }

    void SetEye()
    {
        if (Ui.HasIcons) { eye.FontFamily = Ui.Icons; eye.FontSize = 15; eye.Content = shown ? "" : ""; }
        else { eye.FontSize = 11.5; eye.Content = shown ? "Hide" : "Show"; eye.Width = 44; }
        eye.ToolTip = shown ? "Hide password" : "Show password";
    }

    void Toggle()
    {
        shown = !shown;
        pw.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        plain.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        SetEye();
        var target = shown ? (Control)plain : pw;
        target.Focus();
        if (shown) plain.CaretIndex = plain.Text.Length;
    }

    public string Password { get => pw.Password; set { syncing = true; pw.Password = plain.Text = value ?? ""; syncing = false; } }
    public void Clear() => Password = "";
    public new bool Focus() => shown ? plain.Focus() : pw.Focus();
}
