using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OfflinePackageDownloader;

public static class PackageIconLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly ConcurrentDictionary<string, ImageSource> SuccessfulCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim DownloadGate = new(6, 6);

    public static async Task<ImageSource?> LoadAsync(string packageId, string version, string iconUrl, CancellationToken cancellationToken)
    {
        foreach (var candidateUrl in GetCandidateUrls(packageId, version, iconUrl))
        {
            if (SuccessfulCache.TryGetValue(candidateUrl, out var cached)) return cached;

            var pending = InFlight.GetOrAdd(candidateUrl, static url => DownloadAsync(url));
            var image = await pending;
            InFlight.TryRemove(candidateUrl, out _);
            if (image is null) continue;

            SuccessfulCache.TryAdd(candidateUrl, image);
            return image;
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateUrls(string packageId, string version, string iconUrl)
    {
        if (IsSupportedUrl(iconUrl)) yield return iconUrl;

        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version)) yield break;
        var fallbackUrl = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/{version.ToLowerInvariant()}/icon";
        if (!string.Equals(iconUrl, fallbackUrl, StringComparison.OrdinalIgnoreCase)) yield return fallbackUrl;
    }

    private static bool IsSupportedUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static async Task<ImageSource?> DownloadAsync(string iconUrl)
    {
        await DownloadGate.WaitAsync();
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var response = await Http.GetAsync(iconUrl);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400));
                    continue;
                }

                if (!response.IsSuccessStatusCode) return null;
                var bytes = await response.Content.ReadAsByteArrayAsync();
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            DownloadGate.Release();
        }

        return null;
    }
}
