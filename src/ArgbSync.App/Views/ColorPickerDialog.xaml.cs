using System.Windows;
using System.Windows.Media;

namespace ArgbSync.App.Views;

public partial class ColorPickerDialog : Window
{
    public ColorPickerDialog(Color initialColor)
    {
        InitializeComponent();
        Picker.Color = initialColor;
        OkButton.Focus();
    }

    public Color SelectedColor => Picker.Color;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}