using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ArgbSync.App.Helpers;
using ArgbSync.App.Models;
using ArgbSync.App.Services;
using ArgbSync.App.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenRGB.NET;
using OpenRGB.NET.Utils;
using Color = System.Windows.Media.Color;
using SdkColor = OpenRGB.NET.Color;

namespace ArgbSync.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string AllTypes = "Todos";

    private readonly OpenRgbService _service = new();
    private readonly SettingsService _settingsService;
    private readonly OpenRgbRuntimeManager _runtime;
    private ArgbSettings _settings = new();
    private CancellationTokenSource? _effectCts;

    private static readonly Dictionary<string, DeviceType> TypeFilterMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Placa-mãe"] = DeviceType.Motherboard,
        ["Memória RAM"] = DeviceType.Dram,
        ["Placa de vídeo"] = DeviceType.Gpu,
        ["CPU cooler / água"] = DeviceType.Cooler,
        ["Faixa de LED"] = DeviceType.Ledstrip,
        ["Teclado"] = DeviceType.Keyboard,
        ["Mouse"] = DeviceType.Mouse,
        ["Mousepad"] = DeviceType.Mousemat,
        ["Headset"] = DeviceType.Headset,
        ["Suporte headset"] = DeviceType.HeadsetStand,
        ["Gamepad"] = DeviceType.Gamepad,
        ["Lâmpada"] = DeviceType.Light,
        ["Caixa de som"] = DeviceType.Speaker,
        ["Virtual"] = DeviceType.Virtual
    };

    public MainViewModel()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ArgbSync");
        _settingsService = new SettingsService(baseDir);
        _runtime = new OpenRgbRuntimeManager(baseDir);

        _settings = _settingsService.Load();
        _host = _settings.Host;
        _port = _settings.Port.ToString();
        _clientName = _settings.ClientName;
        _autoStartRuntime = _settings.AutoStartRuntime;
        _autoConnect = _settings.AutoConnect;
        _closeToTray = _settings.CloseToTray;

        _service.DeviceListChanged += OnDeviceListChanged;

        if (AutoConnect)
            _ = ConnectInternalAsync(allowRuntimeStart: true);
    }

    public string[] TypeFilters { get; } =
        new[] { AllTypes, "Placa-mãe", "Memória RAM", "Placa de vídeo", "CPU cooler / água",
            "Faixa de LED", "Teclado", "Mouse", "Mousepad", "Headset", "Suporte headset",
            "Gamepad", "Lâmpada", "Caixa de som", "Virtual", "Outros" };

    public string[] EffectOptions { get; } = { "Arco-íris (girando)", "Respiração" };

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    public ObservableCollection<DeviceViewModel> VisibleDevices { get; } = new();

    public ObservableCollection<string> Profiles { get; } = new();

    [ObservableProperty]
    private string _host = "127.0.0.1";

    [ObservableProperty]
    private string _port = "6742";

    [ObservableProperty]
    private string _clientName = "ArgbSync";

    [ObservableProperty]
    private bool _autoStartRuntime = true;

    [ObservableProperty]
    private bool _autoConnect = true;

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Desconectado";

    [ObservableProperty]
    private SolidColorBrush _statusBrush = new(Colors.Gray);

    [ObservableProperty]
    private DeviceViewModel? _selectedDevice;

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        foreach (var device in Devices)
            device.IsSelected = ReferenceEquals(device, value);
    }

    [ObservableProperty]
    private string _typeFilter = AllTypes;

    [ObservableProperty]
    private int _effectOptionIndex;

    [ObservableProperty]
    private double _effectIntensity = 40;

    [ObservableProperty]
    private bool _isEffectRunning;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfilesNotSupported))]
    private bool _profilesSupported = true;

    public bool ProfilesNotSupported => !ProfilesSupported;

    partial void OnTypeFilterChanged(string value) => ApplyFilter();

    private void OnDeviceListChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() =>
        {
            if (IsConnected)
                RefreshDevicesInternal();
        });
    }

    [RelayCommand]
    private Task ConnectAsync() => ConnectInternalAsync(allowRuntimeStart: true);

    private async Task ConnectInternalAsync(bool allowRuntimeStart)
    {
        if (IsConnected && allowRuntimeStart)
            Disconnect();

        if (!int.TryParse(Port, out var port) || port <= 0)
        {
            SetStatus("Porta inválida.", Colors.Red);
            return;
        }

        var host = Host.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            SetStatus("Informe o endereço do servidor.", Colors.Red);
            return;
        }

        var name = string.IsNullOrWhiteSpace(ClientName) ? "ArgbSync" : ClientName.Trim();

        var settings = _settings ?? new ArgbSettings();
        settings.Host = host;
        settings.Port = port;
        settings.ClientName = name;
        settings.AutoStartRuntime = AutoStartRuntime;
        settings.AutoConnect = AutoConnect;
        settings.CloseToTray = CloseToTray;
        _settings = settings;
        _settingsService.Save(_settings);

        SetStatus($"Conectando a {host}:{port}...", Colors.Orange);

        if (allowRuntimeStart && AutoStartRuntime && !await ProbeServerAsync(host, port))
        {
            SetStatus("Servidor SDK não detectado — iniciando o motor embutido...", Colors.Orange);
            var progress = new Progress<string>(s => SetStatus(s, Colors.Orange));
            var started = await _runtime.EnsureServerAsync(port, allowDownload: true, progress);
            if (!started)
            {
                SetStatus("Falha ao preparar o motor embutido. Verifique o acesso à internet ou instale o OpenRGB manualmente.", Colors.Red);
                return;
            }
            SetStatus("Motor embutido iniciado — aguardando o servidor SDK...", Colors.Orange);

            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                if (await ProbeServerAsync(host, port))
                    break;
            }
        }

        try
        {
            await Task.Run(() => _service.Connect(host, port, name));

            IsConnected = true;
            SetStatus($"Conectado a {host}:{port} (protocolo SDK v{_service.ProtocolVersionText})", Colors.Green);

            RefreshDevicesInternal();
            RefreshProfilesInternal();

            if (ApplySavedZoneSizes())
            {
                SetStatus("Tamanhos das zonas/headers aplicados.", Colors.Green);
                RefreshDevicesInternal();
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            SetStatus($"Falha na conexão: {ConnectionErrorHint(host, port, ex, engineAttempted: allowRuntimeStart && AutoStartRuntime)}", Colors.Red);
        }
    }

    private static async Task<bool> ProbeServerAsync(string host, int port)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            using var cts = new CancellationTokenSource(750);
            await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ConnectionErrorHint(string host, int port, Exception ex, bool engineAttempted)
    {
        var inner = ex;
        while (inner.InnerException is not null)
            inner = inner.InnerException;

        if (inner is System.Net.Sockets.SocketException
            {
                SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused
            })
        {
            return engineAttempted
                ? $"O motor embutido foi iniciado, mas {host}:{port} recusou a conexão. Verifique se outro software usa a porta 6742."
                : $"Não há servidor respondendo em {host}:{port}. Abra o OpenRGB e ative o servidor SDK " +
                  $"(botão 'SDK Server' na janela do OpenRGB) antes de conectar.";
        }

        if (inner is TimeoutException || inner is System.Net.Sockets.SocketException
            {
                SocketErrorCode: System.Net.Sockets.SocketError.TimedOut
            })
        {
            return $"{host}:{port} não respondeu a tempo. Verifique se o servidor SDK está ativo.";
        }

        return inner.Message;
    }

    public void OnAppExit()
    {
        StopEffect();
        _service.Disconnect();
        _runtime.Shutdown();
        _settings.AutoStartRuntime = AutoStartRuntime;
        _settings.AutoConnect = AutoConnect;
        _settings.CloseToTray = CloseToTray;
        _settingsService.Save(_settings);
    }

    [RelayCommand]
    private void Disconnect()
    {
        StopEffect();
        _service.Disconnect();
        Devices.Clear();
        VisibleDevices.Clear();
        Profiles.Clear();
        SelectedDevice = null;
        IsConnected = false;
        SetStatus("Desconectado", Colors.Gray);
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        if (!IsConnected)
            return;

        RefreshDevicesInternal();
    }

    private void RefreshDevicesInternal()
    {
        try
        {
            var keepIndex = SelectedDevice?.Index;
            var devices = _service.GetDevices();

            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(new DeviceViewModel(_service, device, StopEffect, ShowColorDialog, OnZonesResized));
            }

            SelectedDevice = Devices.FirstOrDefault(d => d.Index == keepIndex) ?? Devices.FirstOrDefault();
            ApplyFilter();

            if (Devices.Count == 0)
                SetStatus("Conectado, mas nenhum dispositivo RGB foi detectado.", Colors.Orange);
        }
        catch (Exception ex)
        {
            if (!_service.IsConnected)
            {
                IsConnected = false;
                SetStatus($"Conexão perdida: {ExceptionMessage(ex)}", Colors.Red);
            }
            else
            {
                SetStatus($"Erro ao listar dispositivos: {ExceptionMessage(ex)}", Colors.Red);
            }
        }
    }

    private void ApplyFilter()
    {
        VisibleDevices.Clear();

        if (TypeFilter == AllTypes)
        {
            foreach (var d in Devices)
                VisibleDevices.Add(d);
            return;
        }

        if (TypeFilter == "Outros")
        {
            foreach (var d in Devices)
            {
                if (TypeFilterMap.Values.Contains(d.Device.Type) is false)
                    VisibleDevices.Add(d);
            }
            return;
        }

        if (TypeFilterMap.TryGetValue(TypeFilter, out var target))
        {
            foreach (var d in Devices)
            {
                if (d.Device.Type == target)
                    VisibleDevices.Add(d);
            }
        }
    }

    [RelayCommand]
    private void SelectDevice(DeviceViewModel? device)
    {
        if (device is not null)
            SelectedDevice = device;
    }

    [RelayCommand]
    private void ApplySolidToAll()
    {
        if (Devices.Count == 0)
            return;

        StopEffect();
        try
        {
            foreach (var device in Devices)
            {
                device.ApplySolidNoStatus();
            }
            SetStatus($"Cor aplicada em {Devices.Count} dispositivo(s).", Colors.Green);
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao aplicar: {ExceptionMessage(ex)}", Colors.Red);
        }
    }

    private void SetStatus(string text, Color color)
    {
        StatusText = text;
        StatusBrush = new SolidColorBrush(color);
    }

    private bool ApplySavedZoneSizes()
    {
        if (_settings.ZoneLedCounts.Count == 0)
            return false;

        var resized = false;
        foreach (var device in Devices)
        {
            foreach (var zone in device.Zones)
            {
                var key = $"{device.Device.Name}|{zone.Name}";
                if (_settings.ZoneLedCounts.TryGetValue(key, out var saved) && saved != zone.LedCount)
                {
                    _service.ResizeZone(device.Index, zone.ZoneIndex, saved);
                    resized = true;
                }
            }
        }

        return resized;
    }

    private void OnZonesResized(IReadOnlyList<(string ZoneName, int LedCount)> applied)
    {
        if (SelectedDevice is not { } device)
            return;

        foreach (var (zoneName, count) in applied)
            _settings.ZoneLedCounts[$"{device.Device.Name}|{zoneName}"] = count;

        _settingsService.Save(_settings);
        SetStatus("Tamanhos das zonas/headers aplicados e salvos.", Colors.Green);
        RefreshDevicesInternal();
    }

    private static string ExceptionMessage(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex.Message;
    }

    #region Efeitos ao vivo

    [RelayCommand]
    private void StartStopEffect()
    {
        if (IsEffectRunning)
            StopEffect();
        else
            StartEffect();
    }

    private void StartEffect()
    {
        if (Devices.Count == 0)
        {
            SetStatus("Nenhum dispositivo disponível para o efeito.", Colors.Red);
            return;
        }

        _effectCts?.Cancel();
        var cts = new CancellationTokenSource();
        _effectCts = cts;
        IsEffectRunning = true;
        _ = Task.Run(() => EffectLoop(cts.Token));
    }

    public void StopEffect()
    {
        _effectCts?.Cancel();
        _effectCts = null;
        IsEffectRunning = false;
    }

    private void EffectLoop(CancellationToken ct)
    {
        List<(int Index, int Count)> targets = new();
        var breathing = EffectOptionIndex == 1;
        var baseColor = new SdkColor(255, 255, 255);

        try
        {
            foreach (var device in _service.GetDevices())
            {
                if (device.Leds.Length > 0)
                    targets.Add((device.Index, device.Leds.Length));
            }

            if (targets.Count == 0)
            {
                Dispatch(() =>
                {
                    IsEffectRunning = false;
                    SetStatus("Nenhum dispositivo com LEDs para o efeito.", Colors.Red);
                });
                return;
            }

            if (breathing)
            {
                var dev = SelectedDevice;
                baseColor = ColorConversion.Scale(
                    dev?.SolidSdkColor ?? new SdkColor(255, 255, 255),
                    dev?.Brightness ?? 1.0);
            }

            var delay = ComputeDelayMs();
            var step = RainbowStep();
            var frame = 0;

            while (!ct.IsCancellationRequested)
            {
                if (breathing)
                {
                    var factor = 0.08 + 0.92 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * frame / 60.0));
                    var breathe = ColorConversion.Scale(baseColor, factor);
                    foreach (var (index, count) in targets)
                    {
                        var colors = new SdkColor[count];
                        Array.Fill(colors, breathe);
                        _service.UpdateLeds(index, colors);
                    }
                }
                else
                {
                    var offset = frame * step;
                    foreach (var (index, count) in targets)
                    {
                        var rainbow = ColorUtils.GetHueRainbow(count, offset % 360.0, 1.0, 1.0, 1.0).ToArray();
                        _service.UpdateLeds(index, rainbow);
                    }
                }

                frame++;
                Thread.Sleep(delay);
            }
        }
        catch (Exception ex)
        {
            Dispatch(() =>
            {
                IsEffectRunning = false;
                SetStatus($"Efeito interrompido: {ExceptionMessage(ex)}", Colors.Red);
            });
        }
        finally
        {
            Dispatch(() =>
            {
                if (IsEffectRunning)
                    IsEffectRunning = false;
            });
        }
    }

    private int ComputeDelayMs()
    {
        var fps = 5 + EffectIntensity * 0.55;
        return (int)Math.Max(10, 1000.0 / fps);
    }

    private int RainbowStep()
    {
        return Math.Max(1, (int)Math.Round(EffectIntensity / 20.0));
    }

    private static void Dispatch(Action action)
    {
        Application.Current?.Dispatcher.BeginInvoke(action);
    }

    #endregion

    #region Perfis

    [RelayCommand]
    private void RefreshProfiles()
    {
        if (!IsConnected)
            return;

        RefreshProfilesInternal();
    }

    private void RefreshProfilesInternal()
    {
        try
        {
            var profiles = _service.GetProfiles();
            ProfilesSupported = true;
            Profiles.Clear();
            foreach (var profile in profiles)
                Profiles.Add(profile);
        }
        catch (NotSupportedException)
        {
            ProfilesSupported = false;
            SetStatus("Perfis não suportados pela versão do protocolo SDK.", Colors.Orange);
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao listar perfis: {ExceptionMessage(ex)}", Colors.Red);
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            _service.SaveProfile(name);
            NewProfileName = string.Empty;
            RefreshProfilesInternal();
            SetStatus($"Perfil '{name}' salvo.", Colors.Green);
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao salvar perfil: {ExceptionMessage(ex)}", Colors.Red);
        }
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (SelectedProfile is null)
            return;

        try
        {
            StopEffect();
            _service.LoadProfile(SelectedProfile);
            SetStatus($"Perfil '{SelectedProfile}' carregado.", Colors.Green);
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao carregar perfil: {ExceptionMessage(ex)}", Colors.Red);
        }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null)
            return;

        try
        {
            _service.DeleteProfile(SelectedProfile);
            SelectedProfile = null;
            RefreshProfilesInternal();
            SetStatus("Perfil excluído.", Colors.Green);
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao excluir perfil: {ExceptionMessage(ex)}", Colors.Red);
        }
    }

    #endregion

    private Color? ShowColorDialog(Color initial)
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new ColorPickerDialog(initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedColor : null;
    }
}