using System.Windows.Markup;

namespace IpMonitor.App;

/// <summary>The look of the web version ("network vendor console"): navy top bar, white panels, blue buttons, light and dark.</summary>
public static class Theme
{
    public static bool Dark { get; private set; }

    static readonly Dictionary<string, string> LightColors = new()
    {
        ["Bg"] = "#F3F5F8", ["Panel"] = "#FFFFFF", ["Panel2"] = "#F7F9FB", ["Sunk"] = "#EBEFF3",
        ["Ink"] = "#1B2533", ["Ink2"] = "#334155", ["Muted"] = "#5E6B7B", ["Line"] = "#E4E8ED", ["Frame"] = "#CDD5DE",
        ["Acc"] = "#0B5CD5", ["AccHover"] = "#0A4FB6", ["AccInk"] = "#FFFFFF", ["AccSoft"] = "#EAF2FE", ["AccLine"] = "#9EC2F5",
        ["Ok"] = "#1E7F45", ["OkSoft"] = "#E5F4EB", ["Warn"] = "#9A5B00", ["WarnSoft"] = "#FDF3E1", ["Sig"] = "#C0262D", ["SigSoft"] = "#FCE9EA",
        ["Nav"] = "#0B2240", ["Nav2"] = "#12305A", ["NavInk"] = "#E8EEF7", ["NavMute"] = "#9DB0C9", ["NavLine"] = "#1C3A63", ["NavHi"] = "#4DA3FF",
        ["CSrv"] = "#6B47C7", ["CCam"] = "#0B7A70", ["Grid"] = "#E9EDF2",
    };
    static readonly Dictionary<string, string> DarkColors = new()
    {
        ["Bg"] = "#0D1520", ["Panel"] = "#131E2B", ["Panel2"] = "#182535", ["Sunk"] = "#0F1823",
        ["Ink"] = "#E4EAF2", ["Ink2"] = "#C3CEDB", ["Muted"] = "#93A2B4", ["Line"] = "#22324A", ["Frame"] = "#2E425C",
        ["Acc"] = "#5BA3FF", ["AccHover"] = "#7BB5FF", ["AccInk"] = "#061C3A", ["AccSoft"] = "#14294A", ["AccLine"] = "#2F5A93",
        ["Ok"] = "#4CC27E", ["OkSoft"] = "#10291C", ["Warn"] = "#F0B44C", ["WarnSoft"] = "#2A2010", ["Sig"] = "#F07A7F", ["SigSoft"] = "#30171B",
        ["Nav"] = "#081A31", ["Nav2"] = "#0F2747", ["NavInk"] = "#E8EEF7", ["NavMute"] = "#9DB0C9", ["NavLine"] = "#183255", ["NavHi"] = "#4DA3FF",
        ["CSrv"] = "#A88BF5", ["CCam"] = "#3CC9B8", ["Grid"] = "#172231",
    };

    public static void Init(Application app, bool dark)
    {
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
        Apply(dark);
    }

    public static void Apply(bool dark)
    {
        Dark = dark;
        foreach (var (k, v) in dark ? DarkColors : LightColors)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(v)); b.Freeze();
            Application.Current.Resources[k] = b;
        }
    }

    public static Brush B(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>Colour pair (text, background) of a status.</summary>
    public static (Brush fg, Brush bg) Status(string status) => Store.StatusOf(status) switch
    {
        "active" => (B("Ok"), B("OkSoft")),
        "planned" => (B("Acc"), B("AccSoft")),
        "reserved" => (B("Warn"), B("WarnSoft")),
        "offline" => (B("Sig"), B("SigSoft")),
        _ => (B("Muted"), B("Sunk")),
    };

    const string Styles = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- Buttons -->
  <Style TargetType="Button">
    <Setter Property="Background" Value="{DynamicResource Panel}"/>
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource Frame}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="14,7"/>
    <Setter Property="MinHeight" Value="36"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" SnapsToDevicePixels="True">
            <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="False"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Acc}"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Acc}"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.85"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.45"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="Primary" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="{DynamicResource Acc}"/>
    <Setter Property="Foreground" Value="{DynamicResource AccInk}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource Acc}"/>
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{DynamicResource AccHover}"/></Trigger>
    </Style.Triggers>
  </Style>
  <Style x:Key="Danger" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Foreground" Value="{DynamicResource Sig}"/>
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{DynamicResource SigSoft}"/><Setter Property="BorderBrush" Value="{DynamicResource Sig}"/></Trigger>
    </Style.Triggers>
  </Style>
  <Style x:Key="Link" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Foreground" Value="{DynamicResource Acc}"/>
    <Setter Property="Padding" Value="4,2"/>
    <Setter Property="MinHeight" Value="0"/>
  </Style>
  <Style x:Key="Top" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{DynamicResource NavInk}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource NavLine}"/>
    <Setter Property="MinHeight" Value="34"/>
    <Setter Property="Padding" Value="12,5"/>
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{DynamicResource Nav2}"/></Trigger>
    </Style.Triggers>
  </Style>
  <Style x:Key="NavItem" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{DynamicResource Ink2}"/>
    <Setter Property="BorderThickness" Value="3,0,0,0"/>
    <Setter Property="BorderBrush" Value="Transparent"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="Padding" Value="14,10"/>
    <Setter Property="MinHeight" Value="44"/>
    <Setter Property="Margin" Value="8,2"/>
    <Setter Property="FontSize" Value="15"/>
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{DynamicResource Sunk}"/><Setter Property="BorderBrush" Value="Transparent"/></Trigger>
      <Trigger Property="Tag" Value="on">
        <Setter Property="Background" Value="{DynamicResource AccSoft}"/>
        <Setter Property="Foreground" Value="{DynamicResource Acc}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Acc}"/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- Text fields -->
  <Style TargetType="TextBox">
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="Background" Value="{DynamicResource Panel}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource Frame}"/>
    <Setter Property="CaretBrush" Value="{DynamicResource Ink}"/>
    <Setter Property="SelectionBrush" Value="{DynamicResource Acc}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="8,6"/>
    <Setter Property="MinHeight" Value="36"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" SnapsToDevicePixels="True">
            <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Auto"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Acc}"/><Setter TargetName="b" Property="BorderThickness" Value="2"/></Trigger>
            <Trigger Property="IsReadOnly" Value="True"><Setter TargetName="b" Property="Background" Value="{DynamicResource Sunk}"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.55"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="PasswordBox">
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="Background" Value="{DynamicResource Panel}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource Frame}"/>
    <Setter Property="CaretBrush" Value="{DynamicResource Ink}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="8,6"/>
    <Setter Property="MinHeight" Value="36"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="PasswordBox">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" SnapsToDevicePixels="True">
            <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" Focusable="False"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Acc}"/><Setter TargetName="b" Property="BorderThickness" Value="2"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Drop-down lists -->
  <ControlTemplate x:Key="ComboToggle" TargetType="ToggleButton">
    <Border x:Name="b" Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Frame}" BorderThickness="1" CornerRadius="6" SnapsToDevicePixels="True">
      <Path HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0" Data="M0,0 L5,5 L10,0" Stroke="{DynamicResource Muted}" StrokeThickness="1.8"/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Acc}"/></Trigger>
      <Trigger Property="IsChecked" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Acc}"/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="MinHeight" Value="36"/>
    <Setter Property="Padding" Value="10,6,32,6"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="ScrollViewer.CanContentScroll" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid>
            <ToggleButton x:Name="tg" Template="{StaticResource ComboToggle}" Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
            <ContentPresenter x:Name="cp" IsHitTestVisible="False" Margin="{TemplateBinding Padding}" VerticalAlignment="Center" HorizontalAlignment="Left"
                              Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                              ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"/>
            <TextBox x:Name="PART_EditableTextBox" Style="{x:Null}" Visibility="Hidden" Margin="2,2,30,2" Padding="7,4" VerticalContentAlignment="Center"
                     Background="Transparent" BorderThickness="0" Foreground="{DynamicResource Ink}" CaretBrush="{DynamicResource Ink}"
                     IsReadOnly="{TemplateBinding IsReadOnly}"/>
            <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="None"
                   IsOpen="{TemplateBinding IsDropDownOpen}">
              <Border MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}" Margin="0,2,0,0"
                      Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Frame}" BorderThickness="1" CornerRadius="6">
                <ScrollViewer SnapsToDevicePixels="True">
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsEditable" Value="True">
              <Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/>
              <Setter TargetName="cp" Property="Visibility" Value="Hidden"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.55"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Border x:Name="b" Background="Transparent" Padding="10,7" SnapsToDevicePixels="True">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="{DynamicResource AccSoft}"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter Property="FontWeight" Value="SemiBold"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="CheckBox">
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
  </Style>
  <Style TargetType="RadioButton">
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="Margin" Value="0,0,18,0"/>
  </Style>
  <Style TargetType="ToolTip">
    <Setter Property="Background" Value="{DynamicResource Nav}"/>
    <Setter Property="Foreground" Value="{DynamicResource NavInk}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource NavLine}"/>
    <Setter Property="Padding" Value="8,5"/>
  </Style>

  <!-- Tables -->
  <Style TargetType="DataGrid">
    <Setter Property="Background" Value="{DynamicResource Panel}"/>
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
    <Setter Property="RowBackground" Value="{DynamicResource Panel}"/>
    <Setter Property="AlternatingRowBackground" Value="{DynamicResource Panel2}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource Line}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource Line}"/>
    <Setter Property="GridLinesVisibility" Value="Horizontal"/>
    <Setter Property="HeadersVisibility" Value="Column"/>
    <Setter Property="AutoGenerateColumns" Value="False"/>
    <Setter Property="CanUserAddRows" Value="False"/>
    <Setter Property="CanUserDeleteRows" Value="False"/>
    <Setter Property="CanUserResizeRows" Value="False"/>
    <Setter Property="IsReadOnly" Value="True"/>
    <Setter Property="SelectionMode" Value="Single"/>
    <Setter Property="SelectionUnit" Value="FullRow"/>
    <Setter Property="MinRowHeight" Value="36"/>
    <Setter Property="VerticalGridLinesBrush" Value="Transparent"/>
  </Style>
  <Style TargetType="DataGridColumnHeader">
    <Setter Property="Background" Value="{DynamicResource Panel2}"/>
    <Setter Property="Foreground" Value="{DynamicResource Muted}"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="DataGridColumnHeader">
          <Grid>
            <Border Background="{TemplateBinding Background}" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="10,9">
              <StackPanel Orientation="Horizontal">
                <ContentPresenter VerticalAlignment="Center"/>
                <Path x:Name="arrow" Visibility="Collapsed" Margin="6,0,0,0" VerticalAlignment="Center" Data="M0,0 L4,4 L8,0" Stroke="{DynamicResource Muted}" StrokeThickness="1.6"/>
              </StackPanel>
            </Border>
            <Thumb x:Name="PART_RightHeaderGripper" HorizontalAlignment="Right" Width="6" Cursor="SizeWE" Opacity="0"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="SortDirection" Value="Ascending"><Setter TargetName="arrow" Property="Visibility" Value="Visible"/><Setter TargetName="arrow" Property="Data" Value="M0,4 L4,0 L8,4"/></Trigger>
            <Trigger Property="SortDirection" Value="Descending"><Setter TargetName="arrow" Property="Visibility" Value="Visible"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="DataGridCell">
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="DataGridCell">
          <Border Background="{TemplateBinding Background}" Padding="10,6" SnapsToDevicePixels="True">
            <ContentPresenter VerticalAlignment="Center"/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property="IsSelected" Value="True">
        <Setter Property="Background" Value="{DynamicResource AccSoft}"/>
        <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>
""";
}
