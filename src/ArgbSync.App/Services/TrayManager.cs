using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace ArgbSync.App.Services;

/// <summary>
///     Ícone da bandeja do sistema: permite rodar o app em segundo plano.
///     Fechar a janela não encerra o processo enquanto o modo bandeja estiver ativo.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private static readonly Bitmap TrayBitmap = CreateTrayBitmap();
    private static readonly Icon TrayIcon = Icon.FromHandle(TrayBitmap.GetHicon());

    private readonly NotifyIcon _notifyIcon;
    private readonly Window _window;
    private bool _balloonShown;

    public TrayManager(Window window, Action onExit)
    {
        _window = window;
        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIcon,
            Text = "ArgbSync — rodando em segundo plano",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir ArgbSync", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) =>
        {
            onExit();
            Dispose();
            WpfApplication.Current.Shutdown();
        });
        _notifyIcon.ContextMenuStrip = menu;

        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    public void NotifyHidden()
    {
        if (_balloonShown)
            return;

        _balloonShown = true;
        try
        {
            _notifyIcon.ShowBalloonTip(
                3000,
                "ArgbSync",
                "Continuando em segundo plano. Clique no ícone para reabrir.",
                ToolTipIcon.Info);
        }
        catch
        {
        }
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch
        {
        }
    }

    private static Bitmap CreateTrayBitmap()
    {
        var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(20, 20, 26));
            g.FillEllipse(Brushes.Tomato, 4, 12, 8, 8);
            g.FillEllipse(Brushes.MediumSpringGreen, 12, 12, 8, 8);
            g.FillEllipse(Brushes.DeepSkyBlue, 20, 12, 8, 8);
        }

        return bitmap;
    }
}