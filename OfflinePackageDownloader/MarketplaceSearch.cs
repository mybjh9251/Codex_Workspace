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
