using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VSCodeExtensionDownloaderWPF;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new();
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VSCodeExtensionDownloaderWPF",
        "settings.json");
    private readonly string _defaultOutputFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "VSCodeExtensionBundle");
    private CancellationTokenSource? _downloadCts;
    private AppSettings _settings = new();

    public ObservableCollection<ExtensionSearchResult> SearchResults { get; } = [];
    public ObservableCollection<BundleItem> BundleItems { get; } = [];
    public ObservableCollection<DownloadReportRow> ReportRows { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        TargetPlatformComboBox.ItemsSource = new[] { "win32-x64", "linux-x64" };
        SearchFilterComboBox.ItemsSource = new[] { "Recommended", "Most Popular" };
        SearchFilterComboBox.SelectedItem = "Recommended";
        LoadSettings();
        _ = SearchAsync();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
            }
        }
        catch
        {
            _settings = new AppSettings();
        }

        VSCodeVersionTextBox.Text = string.IsNullOrWhiteSpace(_settings.VSCodeVersion) ? "1.95.0" : _settings.VSCodeVersion;
        TargetPlatformComboBox.SelectedItem = string.IsNullOrWhiteSpace(_settings.TargetPlatform) ? "win32-x64" : _settings.TargetPlatform;
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        _settings.VSCodeVersion = VSCodeVersionTextBox.Text.Trim();
        _settings.TargetPlatform = TargetPlatformComboBox.SelectedItem as string ?? "win32-x64";
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async void SearchFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await SearchAsync();
        }
    }

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SearchAsync();
        }
    }

    private async Task SearchAsync()
    {
        var query = SearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        try
        {
            AppendLog($"Searching Marketplace: {query}");
            SearchResults.Clear();
            var results = await MarketplaceClient.SearchAsync(query, 25, CancellationToken.None);
            if ((SearchFilterComboBox.SelectedItem as string) == "Most Popular")
            {
                results = results.OrderByDescending(result => result.Installs).ToList();
            }
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }
            AppendLog($"Search returned {SearchResults.Count} result(s). Filter: {SearchFilterComboBox.SelectedItem}.");
        }
        catch (Exception ex)
        {
            AppendLog("Search failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Search failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddToBundle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExtensionSearchResult result)
        {
            return;
        }

        AddBundleItem(result, "Requested");
    }

    private void AddBundleItem(ExtensionSearchResult result, string dependencyType)
    {
        if (BundleItems.Any(item => string.Equals(item.ExtensionId, result.ExtensionId, StringComparison.OrdinalIgnoreCase)))
        {
            AppendLog($"Already in bundle: {result.ExtensionId}");
            return;
        }

        BundleItems.Add(new BundleItem
        {
            ExtensionId = result.ExtensionId,
            ExtensionName = result.ExtensionName,
            PublisherName = result.PublisherName,
            PublisherDisplayName = result.PublisherDisplayName,
            DisplayName = result.DisplayName,
            Version = result.Version,
            IconUrl = result.IconUrl,
            Engine = result.Engine,
            DependencyType = dependencyType,
            Dependencies = result.Dependencies,
            ExtensionPack = result.ExtensionPack
        });
        AppendLog($"Added to bundle: {result.ExtensionId}");
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (QueueDataGrid.SelectedItem is BundleItem item)
        {
            BundleItems.Remove(item);
        }
    }

    private async void DetectCodeVersion_Click(object sender, RoutedEventArgs e)
    {
        var version = await TryDetectCodeVersionAsync();
        if (!string.IsNullOrWhiteSpace(version))
        {
            VSCodeVersionTextBox.Text = version;
            SaveSettings();
            AppendLog($"Detected VS Code version: {version}");
        }
        else
        {
            MessageBox.Show(this, "Could not run code --version.", "Detect VS Code", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Settings_Changed(object sender, EventArgs e)
    {
        if (IsLoaded)
        {
            SaveSettings();
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        await DownloadBundleAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        AppendLog("Cancel requested.");
    }

    private async Task DownloadBundleAsync()
    {
        if (BundleItems.Count == 0)
        {
            MessageBox.Show(this, "Add at least one extension to the bundle.", "Bundle queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveSettings();
        _downloadCts = new CancellationTokenSource();
        DownloadButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ReportRows.Clear();
        OverallStatusTextBlock.Text = "Downloading";

        var target = TargetPlatformComboBox.SelectedItem as string ?? "win32-x64";
        var vscodeVersion = VSCodeVersionTextBox.Text.Trim();
        var outputFolder = _defaultOutputFolder;
        var extensionsFolder = Path.Combine(outputFolder, "extensions");
        Directory.CreateDirectory(extensionsFolder);

        try
        {
            await ExpandDependenciesAsync(_downloadCts.Token);
            foreach (var item in BundleItems.ToList())
            {
                if (_downloadCts.IsCancellationRequested)
                {
                    AddReport(item, target, vscodeVersion, "Canceled", string.Empty, string.Empty, "Canceled before download.");
                    continue;
                }

                var warnings = IsLikelyCompatible(item.Engine, vscodeVersion) ? string.Empty : $"Engine {item.Engine} may not match VS Code {vscodeVersion}.";
                if (!string.IsNullOrWhiteSpace(warnings))
                {
                    AppendLog($"{item.ExtensionId}: {warnings}");
                }

                var url = BuildVsixUrl(item.PublisherName, item.ExtensionName, item.Version, target, includeTarget: true);
                var fileName = SafeFileName($"{item.ExtensionId}-{item.Version}-{target}.vsix");
                var filePath = Path.Combine(extensionsFolder, fileName);
                try
                {
                    var finalUrl = await DownloadVsixWithFallbackAsync(url, item, target, filePath, _downloadCts.Token);
                    AddReport(item, target, vscodeVersion, "Downloaded", fileName, finalUrl, string.IsNullOrWhiteSpace(warnings) ? "Downloaded." : warnings);
                    AppendLog($"Downloaded {item.ExtensionId}");
                }
                catch (OperationCanceledException)
                {
                    AddReport(item, target, vscodeVersion, "Canceled", string.Empty, url, "Canceled.");
                }
                catch (Exception ex)
                {
                    AddReport(item, target, vscodeVersion, "Failed", string.Empty, url, ex.Message);
                    AppendLog($"Failed {item.ExtensionId}: {ex.Message}");
                }
            }

            await WriteOutputsAsync(outputFolder, target, vscodeVersion);
            UpdateOverallStatus();
        }
        finally
        {
            DownloadButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _downloadCts.Dispose();
            _downloadCts = null;
        }
    }

    private async Task ExpandDependenciesAsync(CancellationToken cancellationToken)
    {
        var pendingIds = BundleItems
            .SelectMany(item => item.Dependencies.Concat(item.ExtensionPack))
            .Select(id => id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var id in pendingIds)
        {
            if (BundleItems.Any(item => string.Equals(item.ExtensionId, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var metadata = await MarketplaceClient.GetByExtensionIdAsync(id, cancellationToken);
            if (metadata is not null)
            {
                AddBundleItem(metadata, metadata.ExtensionPack.Count > 0 ? "ExtensionPack" : "Dependency");
            }
        }
    }

    private static async Task<string> DownloadVsixWithFallbackAsync(string url, BundleItem item, string target, string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await DownloadFileAsync(url, filePath, cancellationToken);
            return url;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            var fallbackUrl = BuildVsixUrl(item.PublisherName, item.ExtensionName, item.Version, target, includeTarget: false);
            await DownloadFileAsync(fallbackUrl, filePath, cancellationToken);
            return fallbackUrl;
        }
    }

    private static async Task DownloadFileAsync(string url, string filePath, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(filePath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private async Task WriteOutputsAsync(string outputFolder, string target, string vscodeVersion)
    {
        var lockRows = ReportRows.Select(row => new LockEntry
        {
            Provider = "vscode-extension",
            ExtensionId = row.ExtensionId,
            DisplayName = row.DisplayName,
            Publisher = row.Publisher,
            Version = row.Version,
            TargetPlatform = target,
            VSCodeVersion = vscodeVersion,
            DependencyType = row.DependencyType,
            SourceUrl = row.SourceUrl,
            FileName = row.FileName,
            Status = row.Status,
            Message = row.Message
        }).ToList();

        await File.WriteAllTextAsync(
            Path.Combine(outputFolder, "vscode-extensions.lock.json"),
            JsonSerializer.Serialize(lockRows, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        var csv = new List<string>
        {
            "extension_id,display_name,publisher,version,target_platform,vscode_version,dependency_type,status,file_name,source_url,message"
        };
        csv.AddRange(ReportRows.Select(row => string.Join(',', new[]
        {
            Csv(row.ExtensionId),
            Csv(row.DisplayName),
            Csv(row.Publisher),
            Csv(row.Version),
            Csv(row.TargetPlatform),
            Csv(vscodeVersion),
            Csv(row.DependencyType),
            Csv(row.Status),
            Csv(row.FileName),
            Csv(row.SourceUrl),
            Csv(row.Message)
        })));
        await File.WriteAllLinesAsync(Path.Combine(outputFolder, "download-report.csv"), csv, Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "install-offline.ps1"), BuildInstallScript(windows: true, vscodeVersion), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "install-offline.sh"), BuildInstallScript(windows: false, vscodeVersion), Encoding.UTF8);
    }

    private string BuildInstallScript(bool windows, string vscodeVersion)
    {
        var files = ReportRows.Where(row => row.Status == "Downloaded").Select(row => row.FileName).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        if (windows)
        {
            var commands = files.Select(file => $"code --install-extension .\\extensions\\{file}");
            return "$ErrorActionPreference = \"Stop\"\n"
                + "Set-Location -LiteralPath $PSScriptRoot\n"
                + $"$ExpectedVSCodeVersion = \"{vscodeVersion}\"\n"
                + "try { $ActualVSCodeVersion = (code --version)[0]; if ($ActualVSCodeVersion -ne $ExpectedVSCodeVersion) { Write-Warning \"VS Code version is $ActualVSCodeVersion but bundle target is $ExpectedVSCodeVersion.\" } } catch { Write-Warning \"Could not run code --version.\" }\n"
                + string.Join('\n', commands)
                + "\n";
        }

        var shellCommands = files.Select(file => $"code --install-extension ./extensions/{file}");
        return "#!/usr/bin/env bash\nset -euo pipefail\ncd \"$(dirname \"$0\")\"\n"
            + $"EXPECTED_VSCODE_VERSION=\"{vscodeVersion}\"\n"
            + "if command -v code >/dev/null 2>&1; then ACTUAL_VSCODE_VERSION=$(code --version | head -n 1); if [ \"$ACTUAL_VSCODE_VERSION\" != \"$EXPECTED_VSCODE_VERSION\" ]; then echo \"WARNING: VS Code version is $ACTUAL_VSCODE_VERSION but bundle target is $EXPECTED_VSCODE_VERSION.\"; fi; else echo \"WARNING: code command was not found.\"; fi\n"
            + string.Join('\n', shellCommands)
            + "\n";
    }

    private void AddReport(BundleItem item, string target, string vscodeVersion, string status, string fileName, string sourceUrl, string message)
    {
        ReportRows.Add(new DownloadReportRow
        {
            ExtensionId = item.ExtensionId,
            DisplayName = item.DisplayName,
            Publisher = item.PublisherDisplayName,
            Version = item.Version,
            TargetPlatform = target,
            VSCodeVersion = vscodeVersion,
            DependencyType = item.DependencyType,
            Status = status,
            FileName = fileName,
            SourceUrl = sourceUrl,
            Message = message
        });
    }

    private void UpdateOverallStatus()
    {
        var failed = ReportRows.Count(row => row.Status == "Failed");
        var canceled = ReportRows.Count(row => row.Status == "Canceled");
        var downloaded = ReportRows.Count(row => row.Status == "Downloaded");
        OverallStatusTextBlock.Text = canceled > 0
            ? "Canceled"
            : failed == 0 ? "Complete"
            : downloaded > 0 ? "Partial Success"
            : "Failed";
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_defaultOutputFolder);
        Process.Start(new ProcessStartInfo(_defaultOutputFolder) { UseShellExecute = true });
    }

    private static string BuildVsixUrl(string publisher, string extensionName, string version, string target, bool includeTarget)
    {
        var baseUrl = $"https://marketplace.visualstudio.com/_apis/public/gallery/publishers/{Uri.EscapeDataString(publisher)}/vsextensions/{Uri.EscapeDataString(extensionName)}/{Uri.EscapeDataString(version)}/vspackage";
        return includeTarget ? $"{baseUrl}?targetPlatform={Uri.EscapeDataString(target)}" : baseUrl;
    }

    private static bool IsLikelyCompatible(string engine, string vscodeVersion)
    {
        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(vscodeVersion))
        {
            return true;
        }

        var minimum = engine.Trim().TrimStart('^', '>', '=').Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Version.TryParse(NormalizeVersion(minimum), out var min)
            && Version.TryParse(NormalizeVersion(vscodeVersion), out var current)
            && current >= min;
    }

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0.0.0";
        }

        var parts = value.Split('.', '-').Take(3).ToList();
        while (parts.Count < 3)
        {
            parts.Add("0");
        }
        return string.Join('.', parts);
    }

    private static async Task<string?> TryDetectCodeVersionAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "code",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private static string SafeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '-');
        }
        return fileName;
    }

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public static class MarketplaceClient
{
    private static readonly HttpClient Http = new();

    public static async Task<List<ExtensionSearchResult>> SearchAsync(string query, int pageSize, CancellationToken cancellationToken)
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

        return await QueryAsync(payload, cancellationToken);
    }

    public static async Task<ExtensionSearchResult?> GetByExtensionIdAsync(string extensionId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            filters = new[]
            {
                new
                {
                    criteria = new[] { new { filterType = 7, value = extensionId } },
                    pageNumber = 1,
                    pageSize = 1,
                    sortBy = 0,
                    sortOrder = 0
                }
            },
            flags = 914
        };

        return (await QueryAsync(payload, cancellationToken)).FirstOrDefault();
    }

    private static async Task<List<ExtensionSearchResult>> QueryAsync(object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json;api-version=7.1-preview.1");
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var extensions = document.RootElement.GetProperty("results")[0].GetProperty("extensions");
        var results = new List<ExtensionSearchResult>();
        foreach (var extension in extensions.EnumerateArray())
        {
            results.Add(ParseExtension(extension));
        }
        return results;
    }

    private static ExtensionSearchResult ParseExtension(JsonElement extension)
    {
        var publisher = extension.GetProperty("publisher");
        var versions = extension.GetProperty("versions");
        var version = versions[0];
        var files = version.TryGetProperty("files", out var filesElement) ? filesElement : default;
        var properties = version.TryGetProperty("properties", out var propertiesElement) ? propertiesElement : default;

        var publisherName = GetString(publisher, "publisherName");
        var extensionName = GetString(extension, "extensionName");
        var extensionId = $"{publisherName}.{extensionName}";

        return new ExtensionSearchResult
        {
            ExtensionId = extensionId,
            ExtensionName = extensionName,
            PublisherName = publisherName,
            PublisherDisplayName = GetString(publisher, "displayName", publisherName),
            DisplayName = GetString(extension, "displayName", extensionName),
            ShortDescription = GetString(extension, "shortDescription"),
            Version = GetString(version, "version", "latest"),
            IconUrl = GetAssetUrl(files, "Microsoft.VisualStudio.Services.Icons.Default"),
            Installs = GetStatistic(extension, "install"),
            Rating = GetStatistic(extension, "averagerating"),
            IsVerified = GetBool(publisher, "isVerified"),
            Engine = GetProperty(properties, "Microsoft.VisualStudio.Code.Engine"),
            Dependencies = SplitIds(GetProperty(properties, "Microsoft.VisualStudio.Code.ExtensionDependencies")),
            ExtensionPack = SplitIds(GetProperty(properties, "Microsoft.VisualStudio.Code.ExtensionPack")),
            IsPreRelease = string.Equals(GetProperty(properties, "Microsoft.VisualStudio.Code.PreRelease"), "true", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string GetString(JsonElement element, string name, string fallback = "")
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static double GetStatistic(JsonElement extension, string name)
    {
        if (!extension.TryGetProperty("statistics", out var stats))
        {
            return 0;
        }
        foreach (var stat in stats.EnumerateArray())
        {
            if (string.Equals(GetString(stat, "statisticName"), name, StringComparison.OrdinalIgnoreCase)
                && stat.TryGetProperty("value", out var value)
                && value.TryGetDouble(out var number))
            {
                return number;
            }
        }
        return 0;
    }

    private static string GetAssetUrl(JsonElement files, string assetType)
    {
        if (files.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }
        foreach (var file in files.EnumerateArray())
        {
            if (string.Equals(GetString(file, "assetType"), assetType, StringComparison.OrdinalIgnoreCase))
            {
                return GetString(file, "source");
            }
        }
        return string.Empty;
    }

    private static string GetProperty(JsonElement properties, string key)
    {
        if (properties.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }
        foreach (var property in properties.EnumerateArray())
        {
            if (string.Equals(GetString(property, "key"), key, StringComparison.OrdinalIgnoreCase))
            {
                return GetString(property, "value");
            }
        }
        return string.Empty;
    }

    private static List<string> SplitIds(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => id.Contains('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public class ExtensionSearchResult
{
    public string ExtensionId { get; set; } = string.Empty;
    public string ExtensionName { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;
    public string PublisherDisplayName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public double Installs { get; set; }
    public double Rating { get; set; }
    public bool IsVerified { get; set; }
    public string Engine { get; set; } = string.Empty;
    public bool IsPreRelease { get; set; }
    public List<string> Dependencies { get; set; } = [];
    public List<string> ExtensionPack { get; set; } = [];
    public string InstallsText => Installs > 1000000 ? $"{Installs / 1000000:0.#}M" : Installs > 1000 ? $"{Installs / 1000:0.#}K" : Installs.ToString("0");
    public string RatingText => Rating > 0 ? $"Star {Rating:0.0}" : string.Empty;
}

public sealed class BundleItem : ExtensionSearchResult
{
    public string DependencyType { get; set; } = "Requested";
}

public sealed class DownloadReportRow
{
    public string ExtensionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TargetPlatform { get; set; } = string.Empty;
    public string VSCodeVersion { get; set; } = string.Empty;
    public string DependencyType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class LockEntry
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;
    [JsonPropertyName("extensionId")]
    public string ExtensionId { get; set; } = string.Empty;
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = string.Empty;
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
    [JsonPropertyName("targetPlatform")]
    public string TargetPlatform { get; set; } = string.Empty;
    [JsonPropertyName("vscodeVersion")]
    public string VSCodeVersion { get; set; } = string.Empty;
    [JsonPropertyName("dependencyType")]
    public string DependencyType { get; set; } = string.Empty;
    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = string.Empty;
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class AppSettings
{
    public string VSCodeVersion { get; set; } = "1.95.0";
    public string TargetPlatform { get; set; } = "win32-x64";
}
