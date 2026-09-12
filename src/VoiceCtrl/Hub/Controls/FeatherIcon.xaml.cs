using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VoiceCtrl.Hub.Controls;

/// <summary>
/// Renders one Feather icon (github.com/feathericons/feather, MIT) from a Geometry resource
/// defined in HubTheme.xaml. Feather's SVGs are stroked, not filled, at a 24x24 viewBox, so this
/// hosts the geometry in a Viewbox at that native size and lets the Viewbox scale stroke width
/// along with the path, matching how the browser prototype rendered them.
/// </summary>
public partial class FeatherIcon : UserControl
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(FeatherIcon),
        new PropertyMetadata(null, (d, e) => ((FeatherIcon)d).IconPath.Data = (Geometry?)e.NewValue));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(FeatherIcon),
        new PropertyMetadata(null, (d, e) => ((FeatherIcon)d).IconPath.Stroke = (Brush?)e.NewValue));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(FeatherIcon),
        new PropertyMetadata(16.0, (d, e) =>
        {
            var icon = (FeatherIcon)d;
            double size = (double)e.NewValue;
            icon.Root.Width = size;
            icon.Root.Height = size;
        }));

    public FeatherIcon()
    {
        InitializeComponent();
        Root.Width = Size;
        Root.Height = Size;
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }
}
