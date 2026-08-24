using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ApiRouter.Actions;

/// <summary>
/// Downloads an image and sets it as the desktop wallpaper. This is the flagship
/// "action" target in the README: it shows the router doing real local work, and
/// it demonstrates thinking about platform boundaries — the same handler applies
/// the wallpaper natively on Windows, GNOME/Linux, and macOS.
/// </summary>
/// <remarks>
/// Expected parameters: <c>{ "imageUrl": "https://.../picture.jpg" }</c>.
/// </remarks>
public class SetWallpaperActionHandler : IActionHandler
{
    public string Name => "set-wallpaper";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SetWallpaperActionHandler> _logger;

    public SetWallpaperActionHandler(
        IHttpClientFactory httpClientFactory,
        ILogger<SetWallpaperActionHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ActionOutcome> ExecuteAsync(JsonElement parameters, CancellationToken ct)
    {
        if (!TryGetImageUrl(parameters, out var imageUrl))
        {
            return ActionOutcome.Fail("Missing required parameter 'imageUrl'.");
        }

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ActionOutcome.Fail("'imageUrl' must be an absolute http(s) URL.");
        }

        var (hostOk, reason) = await NetworkGuard.ValidatePublicHostAsync(uri, ct);
        if (!hostOk)
        {
            return ActionOutcome.Fail($"Image URL rejected: {reason}.");
        }

        string filePath;
        try
        {
            filePath = await DownloadAsync(uri, ct);
        }
        catch (Exception ex)
        {
            // Keep the detail server-side only; a reflected message would let a caller
            // probe internal hosts/ports via the difference between failure reasons.
            _logger.LogWarning(ex, "Failed to download wallpaper from {Url}", imageUrl);
            return ActionOutcome.Fail("Failed to download the image from the provided URL.");
        }

        try
        {
            return await ApplyAsync(filePath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply wallpaper from {Path}", filePath);
            return ActionOutcome.Fail($"Downloaded to '{filePath}' but failed to apply: {ex.Message}");
        }
    }

    private static bool TryGetImageUrl(JsonElement parameters, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("imageUrl", out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            imageUrl = prop.GetString() ?? string.Empty;
        }

        return !string.IsNullOrWhiteSpace(imageUrl);
    }

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    private async Task<string> DownloadAsync(Uri uri, CancellationToken ct)
    {
        // The named client validates every connection (initial host and each redirect hop)
        // against the SSRF guard and caps the response size, so a caller can neither reach an
        // internal host — directly or via redirect — nor exhaust memory with a huge body.
        var client = _httpClientFactory.CreateClient("wallpaper-download");
        var bytes = await client.GetByteArrayAsync(uri, ct);

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            extension = ".jpg";
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "wallpapers");
        Directory.CreateDirectory(directory);

        // Unique filename so parallel wallpaper steps don't clobber each other.
        var filePath = Path.Combine(directory, $"wallpaper-{Guid.NewGuid():N}{extension}");
        await File.WriteAllBytesAsync(filePath, bytes, ct);
        return filePath;
    }

    // Client-facing messages stay generic; server paths, exit codes, and command stderr are
    // logged rather than returned, so a caller can't harvest them via the step result.
    private async Task<ActionOutcome> ApplyAsync(string filePath, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            if (SetWindowsWallpaper(filePath))
            {
                return ActionOutcome.Ok("Wallpaper applied.");
            }

            _logger.LogWarning("SystemParametersInfo failed for {Path}", filePath);
            return ActionOutcome.Fail("Failed to apply the wallpaper.");
        }

        if (OperatingSystem.IsLinux())
        {
            // Best effort for GNOME-based desktops.
            var uri = new Uri(filePath).AbsoluteUri;
            var (code, err) = await RunAsync("gsettings",
                new[] { "set", "org.gnome.desktop.background", "picture-uri", uri }, ct);
            if (code == 0)
            {
                await RunAsync("gsettings",
                    new[] { "set", "org.gnome.desktop.background", "picture-uri-dark", uri }, ct);
                return ActionOutcome.Ok("Wallpaper applied.");
            }

            _logger.LogWarning("gsettings failed (exit {Code}) for {Path}: {Error}", code, filePath, err);
            return ActionOutcome.Fail("Failed to apply the wallpaper on this platform.");
        }

        if (OperatingSystem.IsMacOS())
        {
            var script = $"tell application \"System Events\" to set picture of every desktop to \"{filePath}\"";
            var (code, err) = await RunAsync("osascript", new[] { "-e", script }, ct);
            if (code == 0)
            {
                return ActionOutcome.Ok("Wallpaper applied.");
            }

            _logger.LogWarning("osascript failed (exit {Code}) for {Path}: {Error}", code, filePath, err);
            return ActionOutcome.Fail("Failed to apply the wallpaper on this platform.");
        }

        _logger.LogWarning("Wallpaper apply not implemented for this OS; file at {Path}", filePath);
        return ActionOutcome.Fail("Setting the wallpaper is not supported on this platform.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool SetWindowsWallpaper(string filePath)
    {
        const int SPI_SETDESKWALLPAPER = 0x0014;
        const int SPIF_UPDATEINIFILE = 0x01;
        const int SPIF_SENDCHANGE = 0x02;

        var result = SystemParametersInfo(
            SPI_SETDESKWALLPAPER, 0, filePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        return result != 0;
    }

    private static async Task<(int ExitCode, string StdErr)> RunAsync(
        string fileName, string[] arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = false, // stdout is unused; redirecting it risks a full-pipe deadlock
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return (-1, $"Could not start process '{fileName}'.");
        }

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, stderr.Trim());
    }
}
