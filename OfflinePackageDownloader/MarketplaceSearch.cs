using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfflinePackageDownloader;

public sealed class MarketplaceSearchResult
{
    public string ExtensionId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public long Installs { get; init; }
    public double Rating { get; init; }
}

public sealed class PackageSearchResult
{
    public string ProviderId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public long Downloads { get; init; }
    public string License { get; init; } = string.Empty;
    public string ProjectUrl { get; init; } = string.Empty;
    public string IconText => string.IsNullOrWhiteSpace(DisplayName) ? "P" : DisplayName[..1].ToUpperInvariant();
    public string DownloadsText => Downloads > 0 ? $"{Downloads:N0} downloads" : "downloads unavailable";
    public string LatestVersionText => string.IsNullOrWhiteSpace(LatestVersion) ? "latest" : $"{LatestVersion} (latest)";
}

public static class NuGetSearchClient
{
    private static readonly HttpClient Http = new();

    public static async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query, int pageSize, CancellationToken cancellationToken)
    {
        var url = $"https://azuresearch-usnc.nuget.org/query?q={Uri.EscapeDataString(query)}&take={pageSize}&prerelease=false&semVerLevel=2.0.0";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            return Array.Empty<PackageSearchResult>();
        }

        return data.EnumerateArray().Select(package =>
        {
            var id = package.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var version = package.TryGetProperty("version", out var versionElement) ? versionElement.GetString() ?? string.Empty : string.Empty;
            var description = package.TryGetProperty("description", out var descriptionElement) ? descriptionElement.GetString() ?? string.Empty : string.Empty;
            var authors = package.TryGetProperty("authors", out var authorsElement) ? authorsElement.GetString() ?? string.Empty : string.Empty;
            var license = package.TryGetProperty("licenseExpression", out var licenseElement) ? licenseElement.GetString() ?? string.Empty : string.Empty;
            var projectUrl = package.TryGetProperty("projectUrl", out var projectUrlElement) ? projectUrlElement.GetString() ?? string.Empty : string.Empty;
            var downloads = package.TryGetProperty("totalDownloads", out var downloadsElement) && downloadsElement.TryGetInt64(out var parsedDownloads) ? parsedDownloads : 0;

            return new PackageSearchResult
            {
                ProviderId = "nuget",
                PackageId = id,
                DisplayName = id,
                Description = description,
                Publisher = authors,
                LatestVersion = version,
                Downloads = downloads,
                License = string.IsNullOrWhiteSpace(license) ? "Unknown" : license,
                ProjectUrl = projectUrl
            };
        }).Where(item => !string.IsNullOrWhiteSpace(item.PackageId)).ToList();
    }
}

public static class MarketplaceSearchClient
{
    private static readonly HttpClient Http = new();

    public static async Task<IReadOnlyList<MarketplaceSearchResult>> SearchAsync(string query, int pageSize, CancellationToken cancellationToken)
    {
        var payload = new
        {
            filters = new[]
            {
                new
                {
                    criteria = new[] { new { filterType = 10, value = query } },
                    pageNumber = 1,
                    pageSize,
                    sortBy = 0,
                    sortOrder = 0
                }
            },
            flags = 914
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Accept", "application/json;api-version=7.2-preview.1");

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var extensions = document.RootElement.GetProperty("results")[0].GetProperty("extensions");
        return extensions.EnumerateArray().Select(ParseExtension).ToList();
    }

    private static MarketplaceSearchResult ParseExtension(JsonElement extension)
    {
        var publisher = extension.GetProperty("publisher");
        var publisherName = publisher.TryGetProperty("publisherName", out var publisherNameElement)
            ? publisherNameElement.GetString() ?? string.Empty
            : string.Empty;
        var extensionName = extension.GetProperty("extensionName").GetString() ?? string.Empty;
        var displayName = extension.TryGetProperty("displayName", out var displayNameElement)
            ? displayNameElement.GetString() ?? extensionName
            : extensionName;
        var shortDescription = extension.TryGetProperty("shortDescription", out var descriptionElement)
            ? descriptionElement.GetString() ?? string.Empty
            : string.Empty;
        var statistics = extension.TryGetProperty("statistics", out var statisticsElement)
            ? statisticsElement.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

        return new MarketplaceSearchResult
        {
            ExtensionId = $"{publisherName}.{extensionName}",
            DisplayName = displayName,
            Description = shortDescription,
            Publisher = publisherName,
            Installs = GetStatistic(statistics, "install"),
            Rating = GetStatistic(statistics, "averagerating")
        };
    }

    private static long GetStatistic(IEnumerable<JsonElement> statistics, string name)
    {
        var match = statistics.FirstOrDefault(item =>
            item.TryGetProperty("statisticName", out var statisticName)
            && string.Equals(statisticName.GetString(), name, StringComparison.OrdinalIgnoreCase));

        if (match.ValueKind == JsonValueKind.Undefined || !match.TryGetProperty("value", out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => (long)doubleValue,
            _ => 0
        };
    }
}
