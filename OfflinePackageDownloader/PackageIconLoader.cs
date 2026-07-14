using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OfflinePackageDownloader;

public static class PackageIconLoader
{
    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim DownloadGate = new(6, 6);

    public static Task<ImageSource?> LoadAsync(string iconUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        return Cache.GetOrAdd(uri.AbsoluteUri, static url => DownloadAsync(url));
    }

    private static async Task<ImageSource?> DownloadAsync(string iconUrl)
    {
        await DownloadGate.WaitAsync();
        try
        {
            var bytes = await Http.GetByteArrayAsync(iconUrl);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            DownloadGate.Release();
        }
    }
}
