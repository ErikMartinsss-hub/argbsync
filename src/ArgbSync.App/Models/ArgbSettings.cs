namespace ArgbSync.App.Models;

public sealed class ArgbSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6742;
    public string ClientName { get; set; } = "ArgbSync";
    public bool AutoStartRuntime { get; set; } = true;
    public bool AutoConnect { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    ///     Nº de LEDs definido pelo usuário para zonas (headers) que o OpenRGB não detecta sozinho.
    ///     Chave no formato "nomeDispositivo|nomeZona".
    /// </summary>
    public Dictionary<string, int> ZoneLedCounts { get; set; } = new();

    /// <summary>
    ///     Últimas cores/configurações aplicadas por dispositivo (chave = nome do dispositivo),
    ///     restauradas a cada conexão.
    /// </summary>
    public Dictionary<string, DeviceSavedState> DeviceStates { get; set; } = new();

    public bool EffectWasOn { get; set; }

    public int EffectOptionIndex { get; set; }

    public double EffectIntensity { get; set; } = 40;
}