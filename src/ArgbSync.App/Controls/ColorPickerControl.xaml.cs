using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArgbSync.App.Controls;

public partial class ColorPickerControl : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color),
        typeof(Color),
        typeof(ColorPickerControl),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorPropertyChanged));

    public ColorPickerControl()
    {
        InitializeComponent();
        SyncToColor(Color);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private static void OnColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ColorPickerControl)d).SyncToColor((Color)e.NewValue);
    }

    private void SyncToColor(Color c)
    {
        RedSlider.Value = c.R;
        GreenSlider.Value = c.G;
        BlueSlider.Value = c.B;
        HexBox.Text = ToHex(c);
        Preview.Background = new SolidColorBrush(c);
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static bool TryParseHex(string? text, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hex = text.Trim();
        if (hex.StartsWith("#"))
            hex = hex[1..];

        if (hex.Length != 6)
            return false;

        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = Color.FromRgb(r, g, b);
        return true;
    }

    private void OnChannelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var color = Color.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);
        HexBox.Text = ToHex(color);
        Preview.Background = new SolidColorBrush(color);
        Color = color;
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (TryParseHex(HexBox.Text, out var color))
        {
            Color = color;
        }
    }
}