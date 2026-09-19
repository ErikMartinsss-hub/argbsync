using System.Windows.Media;
using ArgbSync.App.Helpers;
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

    public DeviceViewModel(
        OpenRgbService service,
        Device device,
        Action onManualChange,
        Func<Color, Color?> pickColor,
        Action<IReadOnlyList<(string ZoneName, int LedCount)>>? onZonesResized = null)
    {
        _service = service;
        Device = device;
        _onManualChange = onManualChange;
        _pickColor = pickColor;
        _onZonesResized = onZonesResized ?? (_ => { });

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

    [ObservableProperty]
    private double _brightness;

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
        ApplySolidNoStatus();
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
        var mode = SelectedMode;
        if (mode is null)
            return;

        _onManualChange();
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
        _service.SetCustomMode(Index);
        _service.UpdateLeds(Index, LedSdkColors());
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