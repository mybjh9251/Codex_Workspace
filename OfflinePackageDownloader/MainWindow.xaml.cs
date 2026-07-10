using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OfflinePackageDownloader;

public partial class MainWindow : Window
{
    private readonly ProviderRegistry registry = new();
    private readonly AppSettings settings;
    private CancellationTokenSource? cancellation;
    private PackageSearchResult? selectedPackage;
    private bool initialSearchCompleted;

    public ObservableCollection<PackageSearchResult> SearchResults { get; } = new();
    public ObservableCollection<AddedPackageItem> AddedPackages { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        settings = AppSettingsStore.Load();
        ProviderList.ItemsSource = registry.Providers.Select(p => p.Definition).ToList();
        ProviderList.SelectedIndex = 0;
        AddedPackages.CollectionChanged += (_, _) => UpdateSummary();
        Loaded += MainWindow_Loaded;
    }

    private ProviderDefinition CurrentDefinition => (ProviderDefinition)ProviderList.SelectedItem;
    private IOfflinePackageProvider CurrentProvider => registry.Get(CurrentDefinition.Id);

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialSearchCompleted)
        {
            return;
        }

        initialSearchCompleted = true;
        await SearchAsync();
    }

    private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderDefinition definition)
        {
            return;
        }

        SearchTextBox.Text = definition.Id switch
        {
            "nuget" => "configuration json",
            "vscode-extension" => "python",
            "python" => "requests",
            "ubuntu" => "hello",
            _ => string.Empty
        };
        SearchResults.Clear();
        AddedPackages.Clear();
        selectedPackage = null;
        UpdateSelectedPackage(null);
        ResultsCountText.Text = "Search packages to begin";
        UpdateStatus("Status: Ready", "Log: Ready.");
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SearchAsync();
        }
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedPackage = SearchResultsList.SelectedItem as PackageSearchResult;
        UpdateSelectedPackage(selectedPackage);
    }

    private void AddSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PackageSearchResult result)
        {
            AddPackage(result);
        }
    }

    private void AddSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedPackage != null)
        {
            AddPackage(selectedPackage);
        }
    }

    private void RemoveAddedButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AddedPackageItem item)
        {
            AddedPackages.Remove(item);
        }
    }

    private void ClearAddedButton_Click(object sender, RoutedEventArgs e)
    {
        AddedPackages.Clear();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(settings, CurrentDefinition.Id) { Owner = this };
        if (window.ShowDialog() == true)
        {
            AppSettingsStore.Save(settings);
            UpdateStatus("Status: Settings saved", "Log: Target settings were saved.");
        }
    }

    private async void ResolveButton_Click(object sender, RoutedEventArgs e)
    {
        await RunCurrentProviderAsync(previewOnly: true);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        await RunCurrentProviderAsync(previewOnly: false);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
    }

    private async Task SearchAsync()
    {
        var query = SearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        SearchResults.Clear();
        UpdateStatus($"Status: Searching {CurrentDefinition.DisplayName}", $"Log: Query '{query}'.");

        try
        {
            var results = CurrentDefinition.Id switch
            {
                "nuget" => await NuGetSearchClient.SearchAsync(query, 25, CancellationToken.None),
                "vscode-extension" => (await MarketplaceSearchClient.SearchAsync(query, 25, CancellationToken.None)).Select(ToPackageSearchResult).ToList(),
                _ => LocalSearch(query)
            };

            foreach (var result in results)
            {
                SearchResults.Add(result);
            }

            SearchResultsList.SelectedIndex = SearchResults.Count > 0 ? 0 : -1;
            ResultsCountText.Text = SearchResults.Count == 0
                ? $"No results for \"{query}\""
                : $"{SearchResults.Count:N0} results for \"{query}\"";
            UpdateStatus($"Status: {SearchResults.Count} result(s)", $"Log: Search completed for {CurrentDefinition.DisplayName}.");
        }
        catch (Exception ex)
        {
            var fallback = FallbackSearchResults(query);
            foreach (var result in fallback)
            {
                SearchResults.Add(result);
            }

            SearchResultsList.SelectedIndex = SearchResults.Count > 0 ? 0 : -1;
            ResultsCountText.Text = SearchResults.Count == 0
                ? "Search failed"
                : $"{SearchResults.Count:N0} fallback results for \"{query}\"";
            UpdateStatus("Status: Search fallback", "Log: " + ex.Message);
        }
    }

    private async Task RunCurrentProviderAsync(bool previewOnly)
    {
        if (AddedPackages.Count == 0)
        {
            AddDefaultRequestIfNeeded();
        }

        SetBusy(true);
        cancellation = new CancellationTokenSource();

        try
        {
            var provider = CurrentProvider;
            var providerOutput = CommonOutput.ProviderOutputFolder(settings.OutputFolder, provider.Definition.Id);
            var request = new ProviderRunRequest(
                provider.Definition.Id,
                BuildRequestText(provider.Definition.Id),
                BuildTargetSettings(provider.Definition.Id),
                providerOutput,
                settings.OverwriteExisting,
                previewOnly);

            UpdateStatus(previewOnly ? "Status: Resolving..." : "Status: Downloading...", "Log: Provider is running.");
            var progress = new Progress<DownloadRecord>(UpsertAddedPackage);
            var result = await provider.RunAsync(request, progress, cancellation.Token);
            CommonOutput.WriteCommonFiles(request, result);

            foreach (var record in result.Records)
            {
                UpsertAddedPackage(record);
            }

            UpdateStatus($"Status: {result.OverallStatus}", $"Log: {result.OutputFolder}");
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Status: Canceled", "Log: Operation canceled.");
        }
        catch (Exception ex)
        {
            UpdateStatus("Status: Failed", "Log: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            cancellation?.Dispose();
            cancellation = null;
        }
    }

    private void AddPackage(PackageSearchResult result)
    {
        if (AddedPackages.Any(item => string.Equals(item.PackageId, result.PackageId, StringComparison.OrdinalIgnoreCase)))
        {
            UpdateStatus("Status: Already added", $"Log: {result.PackageId} is already in the queue.");
            return;
        }

        AddedPackages.Add(new AddedPackageItem
        {
            ProviderId = result.ProviderId,
            PackageId = result.PackageId,
            RequestedVersion = string.IsNullOrWhiteSpace(result.LatestVersion) ? "latest" : result.LatestVersion,
            ResolvedVersion = string.IsNullOrWhiteSpace(result.LatestVersion) ? "-" : result.LatestVersion,
            DependencyCount = 0,
            Status = "Ready"
        });
        UpdateStatus("Status: Ready", $"Log: Added {result.PackageId}.");
    }

    private void AddDefaultRequestIfNeeded()
    {
        var firstLine = CurrentDefinition.DefaultRequests
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return;
        }

        var parts = firstLine.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var packageId = parts[0];
        var version = parts.Length > 1 ? parts[1] : "latest";
        AddedPackages.Add(new AddedPackageItem
        {
            ProviderId = CurrentDefinition.Id,
            PackageId = packageId,
            RequestedVersion = version,
            ResolvedVersion = version == "latest" ? "-" : version,
            Status = "Ready"
        });
    }

    private void UpsertAddedPackage(DownloadRecord record)
    {
        var existing = AddedPackages.FirstOrDefault(item => string.Equals(item.PackageId, record.Name, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new AddedPackageItem
            {
                ProviderId = record.ProviderId,
                PackageId = record.Name,
                RequestedVersion = record.Version,
                ResolvedVersion = record.Version,
                DependencyCount = record.Kind == "Dependency" ? 0 : 1,
                Status = record.Status
            };
            AddedPackages.Add(existing);
            return;
        }

        existing.ResolvedVersion = string.IsNullOrWhiteSpace(record.Version) ? existing.ResolvedVersion : record.Version;
        existing.Status = record.Status;
        existing.DependencyCount = record.Kind == "Requested"
            ? AddedPackages.Count(item => !string.Equals(item.PackageId, existing.PackageId, StringComparison.OrdinalIgnoreCase))
            : existing.DependencyCount;
    }

    private void UpdateSelectedPackage(PackageSearchResult? item)
    {
        if (item == null)
        {
            SelectedIconText.Text = CurrentDefinition.DisplayName[..1].ToUpperInvariant();
            SelectedNameText.Text = "Select a package";
            SelectedPublisherText.Text = string.Empty;
            SelectedDownloadsText.Text = "-";
            SelectedVersionText.Text = "-";
            SelectedLicenseText.Text = "-";
            SelectedProviderText.Text = CurrentDefinition.DisplayName;
            SelectedDescriptionText.Text = "Search for a package, then select a result to inspect it before adding it to the queue.";
            SelectedDependenciesText.Text = "Resolve Preview updates exact dependencies.";
            return;
        }

        SelectedIconText.Text = item.IconText;
        SelectedNameText.Text = item.PackageId;
        SelectedPublisherText.Text = item.Publisher;
        SelectedDownloadsText.Text = item.Downloads > 0 ? item.Downloads.ToString("N0") : "-";
        SelectedVersionText.Text = string.IsNullOrWhiteSpace(item.LatestVersion) ? "latest" : item.LatestVersion;
        SelectedLicenseText.Text = string.IsNullOrWhiteSpace(item.License) ? "-" : item.License;
        SelectedProviderText.Text = CurrentDefinition.DisplayName;
        SelectedDescriptionText.Text = item.Description;
        SelectedDependenciesText.Text = item.ProviderId == "nuget"
            ? "NuGet dependency closure will be resolved for the saved target framework."
            : "Provider-specific dependencies will be resolved during Preview or Download.";
    }

    private void UpdateSummary()
    {
        var roots = AddedPackages.Count(item => item.DependencyCount == 0 || item.Status == "Ready");
        var dependencies = Math.Max(0, AddedPackages.Count - roots);
        RootsSummaryText.Text = $"Roots: {roots}";
        DependenciesSummaryText.Text = $"Dependencies: {dependencies}";
        TotalSummaryText.Text = $"Total Packages: {AddedPackages.Count}";
        SizeSummaryText.Text = "Estimated Size: -";
    }

    private void UpdateStatus(string status, string log)
    {
        StatusText.Text = status;
        LogTextBox.Text = log;
        UpdateSummary();
    }

    private void SetBusy(bool busy)
    {
        ResolveButton.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        ProviderList.IsEnabled = !busy;
    }

    private string BuildRequestText(string providerId)
    {
        return providerId switch
        {
            "nuget" => string.Join(Environment.NewLine, AddedPackages.Where(item => item.ProviderId == providerId).Select(item => $"{item.PackageId} {VersionOrDefault(item.RequestedVersion, "8.0.0")}")),
            _ => string.Join(Environment.NewLine, AddedPackages.Where(item => item.ProviderId == providerId).Select(item => item.PackageId))
        };
    }

    private string BuildTargetSettings(string providerId)
    {
        return providerId switch
        {
            "nuget" => string.Join(Environment.NewLine, new[]
            {
                $"source={settings.NuGet.Source}",
                $"targetFramework={settings.NuGet.TargetFramework}",
                $"maxParallelism={settings.NuGet.MaxParallelism}"
            }),
            "python" => string.Join(Environment.NewLine, new[]
            {
                $"python={settings.Python.PythonExecutable}",
                $"indexUrl={settings.Python.IndexUrl}",
                $"platform={settings.Python.Platform}",
                $"pythonVersion={settings.Python.PythonVersion}",
                "implementation=cp",
                $"abi={settings.Python.Abi}"
            }),
            "vscode-extension" => string.Join(Environment.NewLine, new[]
            {
                $"vscodeVersion={settings.VSCode.VSCodeVersion}",
                $"targetPlatform={settings.VSCode.TargetPlatform}"
            }),
            "ubuntu" => string.Join(Environment.NewLine, new[]
            {
                $"version={settings.Ubuntu.Version}",
                $"architecture={settings.Ubuntu.Architecture}",
                $"components={settings.Ubuntu.Components}",
                $"pockets={settings.Ubuntu.Pockets}",
                $"baseUrl={settings.Ubuntu.BaseUrl}",
                $"maxPackages={settings.Ubuntu.MaxPackages}"
            }),
            _ => string.Empty
        };
    }

    private IReadOnlyList<PackageSearchResult> LocalSearch(string query)
    {
        var providerId = CurrentDefinition.Id;
        var packageId = providerId == "python" ? query : query.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? query;
        return new[]
        {
            new PackageSearchResult
            {
                ProviderId = providerId,
                PackageId = packageId,
                DisplayName = packageId,
                Publisher = CurrentDefinition.DisplayName,
                LatestVersion = "latest",
                License = "-",
                Description = $"Add '{packageId}' to the {CurrentDefinition.DisplayName} download queue."
            }
        };
    }

    private IReadOnlyList<PackageSearchResult> FallbackSearchResults(string query)
    {
        if (CurrentDefinition.Id != "nuget")
        {
            return LocalSearch(query);
        }

        return new[]
        {
            new PackageSearchResult
            {
                ProviderId = "nuget",
                PackageId = "Microsoft.Extensions.Configuration.Json",
                DisplayName = "Microsoft.Extensions.Configuration.Json",
                Publisher = "Microsoft",
                LatestVersion = "8.0.0",
                Downloads = 241_300_000,
                License = "MIT",
                ProjectUrl = "https://github.com/dotnet/runtime",
                Description = "JSON configuration provider implementation for Microsoft Extensions Configuration."
            },
            new PackageSearchResult
            {
                ProviderId = "nuget",
                PackageId = "Newtonsoft.Json",
                DisplayName = "Newtonsoft.Json",
                Publisher = "James Newton-King",
                LatestVersion = "13.0.3",
                Downloads = 2_220_000_000,
                License = "MIT",
                Description = "Json.NET is a popular high-performance JSON framework for .NET."
            },
            new PackageSearchResult
            {
                ProviderId = "nuget",
                PackageId = "System.Text.Json",
                DisplayName = "System.Text.Json",
                Publisher = "Microsoft",
                LatestVersion = "8.0.0",
                Downloads = 1_020_000_000,
                License = "MIT",
                Description = "High-performance JSON serialization and deserialization for .NET."
            },
            new PackageSearchResult
            {
                ProviderId = "nuget",
                PackageId = "Serilog.Settings.Configuration",
                DisplayName = "Serilog.Settings.Configuration",
                Publisher = "Serilog Contributors",
                LatestVersion = "8.0.0",
                Downloads = 34_600_000,
                License = "Apache-2.0",
                Description = "Configuration support for Serilog using Microsoft.Extensions.Configuration."
            }
        };
    }

    private static PackageSearchResult ToPackageSearchResult(MarketplaceSearchResult result)
    {
        return new PackageSearchResult
        {
            ProviderId = "vscode-extension",
            PackageId = result.ExtensionId,
            DisplayName = result.DisplayName,
            Description = result.Description,
            Publisher = result.Publisher,
            LatestVersion = "latest",
            Downloads = result.Installs,
            License = "-",
            ProjectUrl = "https://marketplace.visualstudio.com"
        };
    }

    private static string VersionOrDefault(string version, string fallback)
    {
        return string.IsNullOrWhiteSpace(version) || version.Equals("latest", StringComparison.OrdinalIgnoreCase) ? fallback : version;
    }
}

public sealed class AddedPackageItem : INotifyPropertyChanged
{
    private string resolvedVersion = string.Empty;
    private int dependencyCount;
    private string status = "Ready";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string RequestedVersion { get; init; } = string.Empty;

    public string ResolvedVersion
    {
        get => resolvedVersion;
        set => SetField(ref resolvedVersion, value);
    }

    public int DependencyCount
    {
        get => dependencyCount;
        set => SetField(ref dependencyCount, value);
    }

    public string Status
    {
        get => status;
        set => SetField(ref status, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AppSettings
{
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OfflinePackageDownloader_Output");
    public bool OverwriteExisting { get; set; }
    public NuGetTargetSettings NuGet { get; set; } = new();
    public PythonTargetSettings Python { get; set; } = new();
    public VSCodeTargetSettings VSCode { get; set; } = new();
    public UbuntuTargetSettings Ubuntu { get; set; } = new();
}

public sealed class NuGetTargetSettings
{
    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";
    public string TargetFramework { get; set; } = "net8.0";
    public string MaxParallelism { get; set; } = "5";
}

public sealed class PythonTargetSettings
{
    public string PythonExecutable { get; set; } = "python";
    public string IndexUrl { get; set; } = "https://pypi.org/simple";
    public string Platform { get; set; } = string.Empty;
    public string PythonVersion { get; set; } = string.Empty;
    public string Abi { get; set; } = string.Empty;
}

public sealed class VSCodeTargetSettings
{
    public string VSCodeVersion { get; set; } = "1.91.0";
    public string TargetPlatform { get; set; } = "win32-x64";
}

public sealed class UbuntuTargetSettings
{
    public string Version { get; set; } = "noble";
    public string Architecture { get; set; } = "amd64";
    public string Components { get; set; } = "main universe";
    public string Pockets { get; set; } = "release updates security";
    public string BaseUrl { get; set; } = "http://archive.ubuntu.com/ubuntu";
    public string MaxPackages { get; set; } = "80";
}

public static class AppSettingsStore
{
    private static readonly string SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OfflinePackageDownloader");
    private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), CommonOutput.JsonOptions()) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, CommonOutput.JsonOptions()));
    }
}
