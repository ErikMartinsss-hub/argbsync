using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace ArgbSync.App.Services;

/// <summary>
///     Gerencia o motor embutido (OpenRGB) usado como backend offline.
///     Localiza um executável do OpenRGB (no runtime gerenciado ou instalado no sistema),
///     faz o provisionamento via download quando necessário e sobe o servidor SDK em background.
/// </summary>
public sealed class OpenRgbRuntimeManager : IDisposable
{
    private const string CodebergLatestApi =
        "https://codeberg.org/api/v1/repos/OpenRGB/OpenRGB/releases/latest";

    private static readonly Uri FallbackWindowsZip = new(
        "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.0/OpenRGB_1.0_Windows_64_81bbe18.zip");

    private readonly string _runtimeRoot;
    private readonly string _embeddedDir;
    private readonly object _sync = new();
    private Process? _process;

    public OpenRgbRuntimeManager(string appDataBaseDir)
    {
        _runtimeRoot = Path.Combine(appDataBaseDir, "runtime");
        _embeddedDir = Path.Combine(_runtimeRoot, "OpenRGB");
    }

    public string? ExePath { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _process is { HasExited: false };
        }
    }

    /// <summary>
    ///     Garante que o servidor SDK esteja no ar. Retorna <c>true</c> se o processo foi iniciado ou já estava ativo.
    /// </summary>
    public async Task<bool> EnsureServerAsync(int port, bool allowDownload, IProgress<string>? progress, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_process is { HasExited: false })
                return true;
        }

        if (ExePath is null)
        {
            if (!TryLocateExe(out var located))
            {
                if (!allowDownload)
                {
                    progress?.Report("OpenRGB não encontrado.");
                    return false;
                }

                progress?.Report("Primeiro uso: baixando o OpenRGB (motor embutido)...");
                if (!await TryInstallFromWebAsync(progress, ct))
                {
                    progress?.Report("Não foi possível baixar o OpenRGB automaticamente.");
                    return false;
                }
            }
            else
            {
                ExePath = located;
            }
        }

        StartServer(port);
        return true;
    }

    private bool TryLocateExe(out string exePath)
    {
        var candidates = new[]
        {
            Path.Combine(_embeddedDir, "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenRGB", "OpenRGB.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                exePath = candidate;
                return true;
            }
        }

        exePath = string.Empty;
        return false;
    }

    private async Task<bool> TryInstallFromWebAsync(IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_embeddedDir);

            var url = await ResolveLatestWindowsZipUrlAsync(ct).ConfigureAwait(false);
            if (url is null)
                return false;

            var zipPath = Path.Combine(_runtimeRoot, "openrgb-download.zip");
            Directory.CreateDirectory(_runtimeRoot);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                var data = await client.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(zipPath, data, ct).ConfigureAwait(false);
            }

            progress?.Report("Extraindo o OpenRGB...");
            ExtractFlat(zipPath, _embeddedDir);
            TryDelete(zipPath);

            ExePath = FindExe(_embeddedDir);
            return ExePath is not null;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Uri?> ResolveLatestWindowsZipUrlAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            var json = await client.GetStringAsync(CodebergLatestApi, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.TryGetProperty("assets", out var assets))
            {
                Uri? windows64 = null;
                Uri? windowsAny = null;

                foreach (var asset in assets.EnumerateArray())
                {
                    var name = GetString(asset, "name");
                    var url = GetString(asset, "browser_download_url");
                    if (string.IsNullOrEmpty(url) || !url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var windows = name?.Contains("windows", StringComparison.OrdinalIgnoreCase) == true
                                  || url.Contains("windows", StringComparison.OrdinalIgnoreCase);
                    if (!windows)
                        continue;

                    var is64 = name?.Contains("64", StringComparison.OrdinalIgnoreCase) == true
                               || url.Contains("64", StringComparison.OrdinalIgnoreCase);

                    if (is64 && windows64 is null)
                        windows64 = new Uri(url);
                    windowsAny ??= new Uri(url);
                }

                return windows64 ?? windowsAny ?? FallbackWindowsZip;
            }
        }
        catch
        {
        }

        return FallbackWindowsZip;
    }

    private void StartServer(int port)
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            throw new InvalidOperationException("Executável do OpenRGB não encontrado.");

        var configDir = Path.Combine(_runtimeRoot, "config");
        Directory.CreateDirectory(configDir);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty
        };
        psi.ArgumentList.Add("--server");
        psi.ArgumentList.Add("--server-port");
        psi.ArgumentList.Add(port.ToString());
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(configDir);

        var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Falha ao iniciar o processo do OpenRGB.");

        lock (_sync)
            _process = process;
    }

    public void Shutdown()
    {
        lock (_sync)
        {
            if (_process is { HasExited: false })
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
                catch
                {
                }
            }

            _process = null;
        }
    }

    public void Dispose() => Shutdown();

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? FindExe(string root)
        => Directory.EnumerateFiles(root, "OpenRGB.exe", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3
        }).FirstOrDefault();

    private static void ExtractFlat(string zipPath, string destDir)
    {
        var temp = Path.Combine(Path.GetDirectoryName(zipPath) ?? destDir, "extract-tmp");
        if (Directory.Exists(temp))
            Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);

        ZipFile.ExtractToDirectory(zipPath, temp);

        var exe = FindExe(temp);
        var sourceRoot = Directory.GetParent(exe ?? destDir)?.FullName ?? temp;

        Directory.CreateDirectory(destDir);
        foreach (var dir in Directory.EnumerateDirectories(sourceRoot))
        {
            var target = Path.Combine(destDir, Path.GetFileName(dir));
            if (Directory.Exists(target))
                Directory.Delete(target, true);
            Directory.Move(dir, target);
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot))
        {
            var target = Path.Combine(destDir, Path.GetFileName(file));
            File.Move(file, target, overwrite: true);
        }

        TryDelete(temp);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}