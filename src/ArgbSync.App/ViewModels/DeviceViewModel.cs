using System.Windows.Media;
using ArgbSync.App.Helpers;
using ArgbSync.App.Models;
using ArgbSync.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenRGB.NET;
using OpenRGB.NET.Utils;
using Color = System.Windows.Media.Color;
using SdkColor = OpenRGB.NET.Color;

namespace ArgbSync.App.ViewModels;

public partial class DeviceViewModel : ObservableObject
{
    private readonly OpenRgbService _service;
    private readonly Action _onManualChange;
    private readonly Func<Color, Color?> _pickColor;
    private readonly Action<IReadOnlyList<(string ZoneName, int LedCount)>> _onZonesResized;
    private readonly Action<DeviceViewModel>? _onStateChanged;
    private bool _restoring;

    public DeviceViewModel(
        OpenRgbService service,
        Device device,
        Action onManualChange,
        Func<Color, Color?> pickColor,
        Action<IReadOnlyList<(string ZoneName, int LedCount)>>? onZonesResized = null,
        Action<DeviceViewModel>? onStateChanged = null)
    {
        _service = service;
        Device = device;
        _onManualChange = onManualChange;
        _pickColor = pickColor;
        _onZonesResized = onZonesResized ?? (_ => { });
        _onStateChanged = onStateChanged;

        _solidColor = Colors.White;
        _brightness = 1.0;

        Leds = new System.Collections.ObjectModel.ObservableCollection<LedViewModel>(
            device.Leds.Select((led, i) => new LedViewModel(i, led.Name, ColorConversion.ToMedia(device.Colors[i]))));

        Modes = new System.Collections.ObjectModel.ObservableCollection<ModeViewModel>(
            device.Modes.Select(m => new ModeViewModel(m)));

        ZoneViewModel[] zones = device.Zones.Select(z => new ZoneViewModel(z.Index, z.Name, (int)z.LedCount)).ToArray();
        Zones = new System.Collections.ObjectModel.ObservableCollection<ZoneViewModel>(zones);

        SelectedMode = Modes.FirstOrDefault(m => m.Index == device.ActiveModeIndex)
                       ?? Modes.FirstOrDefault();
    }

    public Device Device { get; }

    public int Index => Device.Index;

    public string Name => Device.Name;

    public string Vendor => string.IsNullOrWhiteSpace(Device.Vendor) ? string.Empty : Device.Vendor;

    public string Description => Device.Description;

    public string DeviceTypeLabel => GetDeviceTypeLabel(Device.Type);

    public string LedCountText => $"{Device.Leds.Length} LEDs";

    public string TypeAndLeds => $"{DeviceTypeLabel}  ·  {LedCountText}";

    public string Summary => $"{Name} ({DeviceTypeLabel}) · {LedCountText}";

    public string DetailHeader => string.IsNullOrWhiteSpace(Vendor)
        ? $"{Name} — {DeviceTypeLabel}"
        : $"{Vendor} {Name} — {DeviceTypeLabel}";

    public System.Collections.ObjectModel.ObservableCollection<LedViewModel> Leds { get; }

    public System.Collections.ObjectModel.ObservableCollection<ModeViewModel> Modes { get; }

    public System.Collections.ObjectModel.ObservableCollection<ZoneViewModel> Zones { get; }

    public bool HasZones => Zones.Count > 0;

    public bool HasNoZones => !HasZones;

    [ObservableProperty]
    private ModeViewModel? _selectedMode;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SolidBrush))]
    private Color _solidColor;

    partial void OnSolidColorChanged(Color value)
    {
        if (_restoring)
            return;

        LastAction = DeviceLastAction.Solid;
        _onStateChanged?.Invoke(this);
    }

    [ObservableProperty]
    private double _brightness;

    partial void OnBrightnessChanged(double value)
    {
        if (_restoring)
            return;

        LastAction = DeviceLastAction.Solid;
        _onStateChanged?.Invoke(this);
    }

    /// <summary>
    ///     Última configuração aplicada pelo usuário neste dispositivo.
    /// </summary>
    public DeviceLastAction LastAction { get; private set; } = DeviceLastAction.None;

    public Brush SolidBrush => new SolidColorBrush(SolidColor);

    public Brush DeviceGlowBrush
    {
        get
        {
            var c = Device.Colors.Length > 0 ? ColorConversion.ToMedia(Device.Colors[0]) : Colors.White;
            return new SolidColorBrush(c);
        }
    }

    public SdkColor SolidSdkColor => ColorConversion.ToSdk(SolidColor);

    public SdkColor[] ComputeSolidColors()
    {
        var scaled = ColorConversion.Scale(SolidSdkColor, Brightness);
        var result = new SdkColor[Device.Leds.Length];
        Array.Fill(result, scaled);
        return result;
    }

    public SdkColor[] LedSdkColors()
    {
        var result = new SdkColor[Device.Leds.Length];
        foreach (var led in Leds)
        {
            result[led.Index] = ColorConversion.ToSdk(led.Color);
        }
        return result;
    }

    [RelayCommand]
    private void ApplySolid()
    {
        _onManualChange();
        LastAction = DeviceLastAction.Solid;
        ApplySolidNoStatus();
        _onStateChanged?.Invoke(this);
    }

    public void ApplySolidNoStatus()
    {
        _service.SetSolid(Index, ComputeSolidColors());
        SyncLedColorsFromSolid();
    }

    [RelayCommand]
    private void ResizeZones()
    {
        _service.SetCustomMode(Index);

        var applied = new List<(string ZoneName, int LedCount)>(Zones.Count);
        foreach (var zone in Zones)
        {
            var size = Math.Max(0, zone.LedCount);
            _service.ResizeZone(Index, zone.ZoneIndex, size);
            applied.Add((zone.Name, size));
        }

        _onZonesResized(applied);
        _onManualChange();
    }

    [RelayCommand]
    private void ApplyMode()
    {
        _onManualChange();
        ApplyModeToHardware();
        LastAction = DeviceLastAction.Mode;
        _onStateChanged?.Invoke(this);
    }

    public void ApplyModeToHardware()
    {
        var mode = SelectedMode;
        if (mode is null)
            return;

        _service.ApplyMode(
            Index,
            mode.Index,
            mode.GetSpeedOrNull(),
            mode.GetDirectionOrNull(),
            null);
    }

    [RelayCommand]
    private void SaveModeToDevice()
    {
        var mode = SelectedMode;
        if (mode is null)
            return;

        _service.SaveMode(Index, mode.Index);
    }

    [RelayCommand]
    private void ApplyLeds()
    {
        _onManualChange();
        ApplyLedsToHardware();
        LastAction = DeviceLastAction.Leds;
        _onStateChanged?.Invoke(this);
    }

    public void ApplyLedsToHardware()
    {
        _service.SetCustomMode(Index);
        _service.UpdateLeds(Index, LedSdkColors());
    }

    /// <summary>
    ///     Restaura o estado salvo (cor, modo ou LEDs) e aplica no hardware.
    ///     Não dispara gravação de configurações durante a restauração.
    /// </summary>
    public void RestoreState(DeviceSavedState state)
    {
        if (state is null)
            return;

        _restoring = true;
        try
        {
            if (ColorConversion.TryFromHex(state.SolidColorHex, out var color))
                SolidColor = color;

            Brightness = state.Brightness;
            SelectedMode = Modes.FirstOrDefault(m => m.Index == state.ModeIndex);
        }
        finally
        {
            _restoring = false;
        }

        switch (state.LastAction)
        {
            case DeviceLastAction.Mode when SelectedMode is { } mode:
                mode.Speed = state.ModeSpeed;
                mode.Brightness = state.ModeBrightness;
                if ((state.ModeDirectionLabel?.Length ?? 0) > 0)
                    mode.DirectionLabel = state.ModeDirectionLabel ?? string.Empty;

                LastAction = DeviceLastAction.Mode;
                ApplyModeToHardware();
                break;

            case DeviceLastAction.Leds when state.LedColorsHex.Count == Leds.Count:
                for (var i = 0; i < Leds.Count; i++)
                {
                    if (ColorConversion.TryFromHex(state.LedColorsHex[i], out var ledColor))
                        Leds[i].Color = ledColor;
                }

                LastAction = DeviceLastAction.Leds;
                ApplyLedsToHardware();
                break;

            case DeviceLastAction.Solid:
                LastAction = DeviceLastAction.Solid;
                ApplySolidNoStatus();
                break;
        }
    }

    /// <summary>
    ///     Constrói o estado persistível atual do dispositivo.
    /// </summary>
    public DeviceSavedState BuildPersistedState()
    {
        var state = new DeviceSavedState
        {
            LastAction = LastAction,
            SolidColorHex = ColorConversion.ToHex(SolidColor),
            Brightness = Brightness
        };

        if (SelectedMode is { } mode)
        {
            state.ModeIndex = mode.Index;
            state.ModeSpeed = mode.Speed;
            state.ModeBrightness = mode.Brightness;
            state.ModeDirectionLabel = mode.DirectionLabel;
        }

        if (Leds.Count > 0)
        {
            foreach (var led in Leds)
                state.LedColorsHex.Add(ColorConversion.ToHex(led.Color));
        }

        return state;
    }

    [RelayCommand]
    private void OpenLedColor(LedViewModel? led)
    {
        if (led is null)
            return;

        var picked = _pickColor(led.Color);
        if (picked is null)
            return;

        led.Color = picked.Value;
        ApplyLeds();
    }

    [RelayCommand]
    private void RainbowLeds()
    {
        var rainbow = ColorUtils.GetHueRainbow(Device.Leds.Length).ToArray();
        for (var i = 0; i < Leds.Count; i++)
            Leds[i].Color = ColorConversion.ToMedia(rainbow[i]);

        ApplyLeds();
    }

    [RelayCommand]
    private void RandomLeds()
    {
        var rng = new Random();
        foreach (var led in Leds)
            led.Color = Color.FromRgb((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));

        ApplyLeds();
    }

    [RelayCommand]
    private void OffLeds()
    {
        foreach (var led in Leds)
            led.Color = Colors.Black;

        ApplyLeds();
    }

    private void SyncLedColorsFromSolid()
    {
        var colors = ComputeSolidColors();
        for (var i = 0; i < Leds.Count; i++)
            Leds[i].Color = ColorConversion.ToMedia(colors[i]);
    }

    public static string GetDeviceTypeLabel(DeviceType type) => type switch
    {
        DeviceType.Motherboard => "Placa-mãe",
        DeviceType.Dram => "Memória RAM",
        DeviceType.Gpu => "Placa de vídeo",
        DeviceType.Cooler => "CPU cooler / água",
        DeviceType.Ledstrip => "Faixa de LED",
        DeviceType.Keyboard => "Teclado",
        DeviceType.Mouse => "Mouse",
        DeviceType.Mousemat => "Mousepad",
        DeviceType.Headset => "Headset",
        DeviceType.HeadsetStand => "Suporte headset",
        DeviceType.Gamepad => "Gamepad",
        DeviceType.Light => "Lâmpada",
        DeviceType.Speaker => "Caixa de som",
        DeviceType.Virtual => "Virtual",
        _ => "Outro"
    };
}