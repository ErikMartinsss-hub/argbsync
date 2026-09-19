using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArgbSync.App.Services;
using ArgbSync.App.ViewModels;

namespace ArgbSync.App;

public partial class MainWindow : Window
{
    private readonly TrayManager _tray;
    private readonly MainViewModel _vm;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        _tray = new TrayManager(this, ExitApp);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _vm.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _tray.NotifyHidden();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ExitApp();

        base.OnClosed(e);
    }

    private void ExitApp()
    {
        if (_exiting)
            return;

        _exiting = true;
        _tray.Dispose();
        _vm.OnAppExit();
        Application.Current?.Shutdown();
    }

    private void OnQuickColorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm &&
            vm.SelectedDevice is { } device &&
            sender is Button { Tag: string hex } &&
            TryConvertHex(hex, out var color))
        {
            device.SolidColor = color;
        }
    }

    private static bool TryConvertHex(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            color = Colors.White;
            return false;
        }
    }
}