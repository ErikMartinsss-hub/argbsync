using CommunityToolkit.Mvvm.ComponentModel;
using OpenRGB.NET;

namespace ArgbSync.App.ViewModels;

public partial class ModeViewModel : ObservableObject
{
    private static readonly Dictionary<string, Direction> DirectionByLabel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Padrão"] = Direction.None,
        ["Esquerda"] = Direction.Left,
        ["Direita"] = Direction.Right,
        ["Cima"] = Direction.Up,
        ["Baixo"] = Direction.Down,
        ["Horizontal"] = Direction.Horizontal,
        ["Vertical"] = Direction.Vertical
    };

    private readonly Mode _mode;

    public ModeViewModel(Mode mode)
    {
        _mode = mode;
        _speed = mode.Speed;
        _brightness = mode.Brightness;
        _directionLabel = ToDirectionLabel(mode.Direction);
    }

    public int Index => _mode.Index;

    public string Name => _mode.Name;

    public string DisplayName => _mode.Name;

    public bool SupportsSpeed => _mode.SupportsSpeed;

    public bool SupportsBrightness => _mode.SupportsBrightness;

    public bool SupportsDirection => _mode.SupportsDirection;

    public bool SupportsColor => _mode.Flags.HasFlag(ModeFlags.HasModeSpecificColor);

    public double SpeedMin => _mode.SpeedMin;

    public double SpeedMax => _mode.SpeedMax;

    public double BrightnessMin => _mode.BrightnessMin;

    public double BrightnessMax => _mode.BrightnessMax;

    public int ColorCount => _mode.Colors.Length;

    [ObservableProperty]
    private double _speed;

    [ObservableProperty]
    private double _brightness;

    public string[] DirectionOptions => BuildDirectionOptions(_mode);

    [ObservableProperty]
    private string _directionLabel;

    public uint? GetSpeedOrNull() => SupportsSpeed ? (uint)Math.Round(Speed) : null;

    public uint? GetBrightnessOrNull() => SupportsBrightness ? (uint)Math.Round(Brightness) : null;

    public Direction? GetDirectionOrNull() =>
        SupportsDirection && DirectionByLabel.TryGetValue(DirectionLabel, out var dir) ? dir : null;

    private static string ToDirectionLabel(Direction d) => d switch
    {
        Direction.Left => "Esquerda",
        Direction.Right => "Direita",
        Direction.Up => "Cima",
        Direction.Down => "Baixo",
        Direction.Horizontal => "Horizontal",
        Direction.Vertical => "Vertical",
        _ => "Padrão"
    };

    private static string[] BuildDirectionOptions(Mode mode)
    {
        if (mode.Flags.HasFlag(ModeFlags.HasDirectionLR))
            return new[] { "Esquerda", "Direita" };
        if (mode.Flags.HasFlag(ModeFlags.HasDirectionUD))
            return new[] { "Cima", "Baixo" };
        if (mode.Flags.HasFlag(ModeFlags.HasDirectionHV))
            return new[] { "Horizontal", "Vertical" };
        return new[] { "Padrão" };
    }
}