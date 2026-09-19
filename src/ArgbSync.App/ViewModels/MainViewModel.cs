using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
    private DispatcherTimer? _saveTimer;

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

    public string[] EffectOptions { get; } =
    {
        "Arco-íris (girando)",
        "Respiração",
        "Pulso",
        "Onda de cor",
        "Troca de cores",
        "Estroboscópio",
        "Aleatório",
        "Cor sólida",
    };

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

            ApplySavedDeviceStates();

            if (_settings.EffectWasOn && Devices.Count > 0 && !IsEffectRunning)
            {
                EffectOptionIndex = _settings.EffectOptionIndex;
                EffectIntensity = _settings.EffectIntensity;
                StartEffect();
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
        PersistSettings();
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
                Devices.Add(new DeviceViewModel(_service, device, StopEffect, ShowColorDialog, OnZonesResized, SaveDeviceState));
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

    private void SaveDeviceState(DeviceViewModel device)
    {
        _settings.DeviceStates[device.Name] = device.BuildPersistedState();
        ScheduleSave();
    }

    private int ApplySavedDeviceStates()
    {
        var restored = 0;
        foreach (var device in Devices)
        {
            if (_settings.DeviceStates.TryGetValue(device.Name, out var state))
            {
                device.RestoreState(state);
                restored++;
            }
        }

        if (restored > 0)
            SetStatus($"Configurações restauradas em {restored} dispositivo(s).", Colors.Green);

        return restored;
    }

    private void ScheduleSave()
    {
        _saveTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };

        _saveTimer.Stop();
        _saveTimer.Tick -= PersistSettings;
        _saveTimer.Tick += PersistSettings;
        _saveTimer.Start();
    }

    private void PersistSettings(object? sender, EventArgs e) => PersistSettings();

    private void PersistSettings()
    {
        _saveTimer?.Stop();
        _settingsService.Save(_settings);
    }

    private void SaveEffectState()
    {
        _settings.EffectWasOn = IsEffectRunning;
        _settings.EffectOptionIndex = EffectOptionIndex;
        _settings.EffectIntensity = EffectIntensity;
        ScheduleSave();
    }

    partial void OnEffectOptionIndexChanged(int value) => SaveEffectState();

    partial void OnEffectIntensityChanged(double value) => SaveEffectState();

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
        SaveEffectState();
        _ = Task.Run(() => EffectLoop(cts.Token));
    }

    public void StopEffect()
    {
        _effectCts?.Cancel();
        _effectCts = null;
        IsEffectRunning = false;
        SaveEffectState();
    }

    private void EffectLoop(CancellationToken ct)
    {
        List<(int Index, int Count)> targets = new();
        var preset = Math.Clamp(EffectOptionIndex, 0, EffectOptions.Length - 1);
        var rng = new Random();

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

            var dev = SelectedDevice;
            var baseColor = ColorConversion.Scale(
                dev?.SolidSdkColor ?? new SdkColor(255, 255, 255),
                dev?.Brightness ?? 1.0);

            var (baseHue, baseSat, baseVal) = baseColor.ToHsv();
            if (baseSat < 0.05) baseSat = 1.0;
            if (baseVal < 0.05) baseVal = 1.0;

            var delay = ComputeDelayMs();
            var step = RainbowStep();
            var hueStep = Math.Max(1, (int)Math.Round(EffectIntensity / 10.0));
            var frame = 0;

            while (!ct.IsCancellationRequested)
            {
                var sine = 0.5 + 0.5 * Math.Sin(2 * Math.PI * frame / 60.0);
                var quick = 0.5 + 0.5 * Math.Sin(2 * Math.PI * frame / 14.0);
                var strobeOn = preset == 5 && frame % 8 < 3;

                SdkColor baseLayer;
                switch (preset)
                {
                    case 1: // Respiração
                        baseLayer = ColorConversion.Scale(baseColor, 0.08 + 0.92 * sine);
                        break;
                    case 2: // Pulso
                        baseLayer = ColorConversion.Scale(baseColor, 0.15 + 0.85 * quick);
                        break;
                    case 5: // Estroboscópio
                        baseLayer = strobeOn ? ColorConversion.Scale(baseColor, 1.0) : new SdkColor(0, 0, 0);
                        break;
                    case 7: // Cor sólida
                        baseLayer = baseColor;
                        break;
                    default:
                        baseLayer = baseColor;
                        break;
                }

                foreach (var (index, count) in targets)
                {
                    SdkColor[] colors;
                    switch (preset)
                    {
                        case 0: // Arco-íris girando
                            colors = ColorUtils.GetHueRainbow(count, frame * step % 360.0, 1.0, 1.0, 1.0).ToArray();
                            break;
                        case 1: // Respiração
                        case 2: // Pulso
                        case 5: // Estroboscópio
                        case 7: // Cor sólida
                            colors = new SdkColor[count];
                            Array.Fill(colors, baseLayer);
                            break;
                        case 3: // Onda de cor
                            colors = new SdkColor[count];
                            for (var i = 0; i < count; i++)
                            {
                                var f = 0.15 + 0.85 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * (i / (double)count + frame / 50.0)));
                                colors[i] = ColorConversion.Scale(baseColor, f);
                            }
                            break;
                        case 4: // Troca de cores
                            colors = new SdkColor[count];
                            var uni = ColorUtils.FromHsv(baseHue + frame * hueStep, baseSat, baseVal);
                            Array.Fill(colors, uni);
                            break;
                        case 6: // Aleatório
                            colors = new SdkColor[count];
                            for (var i = 0; i < count; i++)
                                colors[i] = ColorUtils.FromHsv(rng.NextDouble() * 360.0, 1.0, 1.0);
                            break;
                        default:
                            colors = Array.Empty<SdkColor>();
                            break;
                    }

                    _service.UpdateLeds(index, colors);
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