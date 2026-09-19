using OpenRGB.NET;

namespace ArgbSync.App.Services;

/// <summary>
///     Wraps the OpenRGB SDK client in a thread-safe facade.
/// </summary>
public sealed class OpenRgbService : IDisposable
{
    private readonly object _sync = new();
    private OpenRgbClient? _client;

    public event EventHandler? DeviceListChanged;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
                return _client?.Connected == true;
        }
    }

    public string ProtocolVersionText
    {
        get
        {
            lock (_sync)
                return _client is null ? "-" : $"v{_client.CommonProtocolVersion.Number}";
        }
    }

    public void Connect(string host, int port, string clientName)
    {
        lock (_sync)
        {
            DisconnectCore();

            var client = new OpenRgbClient(host, port, clientName, autoConnect: true, timeoutMs: 2000);
            client.DeviceListUpdated += OnDeviceListUpdated;
            _client = client;
        }
    }

    public void Disconnect()
    {
        lock (_sync)
            DisconnectCore();
    }

    private void DisconnectCore()
    {
        if (_client is null)
            return;

        _client.DeviceListUpdated -= OnDeviceListUpdated;
        _client.Dispose();
        _client = null;
    }

    private void OnDeviceListUpdated(object? sender, EventArgs e)
    {
        try
        {
            DeviceListChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
        }
    }

    public Device[] GetDevices() => WithLock(c => c.GetAllControllerData());

    public string[] GetProfiles() => WithLock(c => c.GetProfiles());

    public bool SupportsProfiles => GetProfilesOrNull() is not null;

    private string[]? GetProfilesOrNull()
    {
        try
        {
            return WithLock(c => c.GetProfiles());
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public void LoadProfile(string name) => WithLock(c => c.LoadProfile(name));

    public void SaveProfile(string name) => WithLock(c => c.SaveProfile(name));

    public void DeleteProfile(string name) => WithLock(c => c.DeleteProfile(name));

    public void SetCustomMode(int deviceId) => WithLock(c => c.SetCustomMode(deviceId));

    public void ResizeZone(int deviceId, int zoneId, int newSize) => WithLock(c => c.ResizeZone(deviceId, zoneId, newSize));

    public void SetSolid(int deviceId, Color[] colors)
    {
        WithLock(c =>
        {
            c.SetCustomMode(deviceId);
            c.UpdateLeds(deviceId, colors);
        });
    }

    public void UpdateLeds(int deviceId, Color[] colors) => WithLock(c => c.UpdateLeds(deviceId, colors));

    public void ApplyMode(int deviceId, int modeId, uint? speed, Direction? direction, Color[]? colors)
        => WithLock(c => c.UpdateMode(deviceId, modeId, speed, direction, colors));

    public void SaveMode(int deviceId, int modeId) => WithLock(c => c.SaveMode(deviceId, modeId));

    private T WithLock<T>(Func<OpenRgbClient, T> action)
    {
        lock (_sync)
        {
            var client = _client;
            if (client is null || !client.Connected)
                throw new InvalidOperationException("Não conectado ao servidor OpenRGB.");

            return action(client);
        }
    }

    private void WithLock(Action<OpenRgbClient> action)
    {
        lock (_sync)
        {
            var client = _client;
            if (client is null || !client.Connected)
                throw new InvalidOperationException("Não conectado ao servidor OpenRGB.");

            action(client);
        }
    }

    public void Dispose()
    {
        lock (_sync)
            DisconnectCore();
    }
}