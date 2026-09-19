namespace ArgbSync.App.Models;

/// <summary>
///     Tipo da última configuração aplicada pelo usuário em um dispositivo.
///     Define qual aspecto deve ser restaurado na próxima conexão.
/// </summary>
public enum DeviceLastAction
{
    None,
    Solid,
    Mode,
    Leds
}

/// <summary>
///     Estado salvo de um dispositivo, usado para restaurar cores/configurações
///     após o app reiniciar. Chave no dicionário = nome do dispositivo.
/// </summary>
public sealed class DeviceSavedState
{
    public DeviceLastAction LastAction { get; set; } = DeviceLastAction.None;

    public string SolidColorHex { get; set; } = "#FFFFFF";

    public double Brightness { get; set; } = 1.0;

    public int ModeIndex { get; set; } = -1;

    public double ModeSpeed { get; set; }

    public double ModeBrightness { get; set; }

    public string ModeDirectionLabel { get; set; } = string.Empty;

    public List<string> LedColorsHex { get; set; } = new();
}