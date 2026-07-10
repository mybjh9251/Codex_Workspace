using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OfflinePackageDownloader;

public sealed partial class MainWindowViewModel
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Settings { get; } = AppSettingsStore.Load();
    public ICommand AddPackageCommand => new DelegateCommand(parameter =>
    {
        if (parameter is PackageCardMock package) Add(package);
    });
    public ICommand SelectPackageCommand => new DelegateCommand(parameter =>
    {
        if (parameter is PackageCardMock package) Select(package);
    });

    public async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ClearSearch();
            return;
        }

        ResultCountText = $"Searching NuGet for \"{query}\"...";
        PageSummaryText = string.Empty;
        RaiseDisplayProperties();

        try
        {
            var results = await NuGetSearchClient.SearchAsync(query, 24, CancellationToken.None);
            SearchResults.Clear();
            foreach (var result in results)
            {
                SearchResults.Add(PackageCardMock.FromSearchResult(result));
            }

            ResultCountText = $"{results.Count:N0} results for \"{query}\"";
            PageSummaryText = results.Count == 0 ? "No packages found" : $"Showing 1 - {results.Count:N0}";
            if (SearchResults.Count > 0)
            {
                Select(SearchResults[0]);
            }
        }
        catch (Exception ex)
        {
            ResultCountText = "NuGet search could not be completed";
            PageSummaryText = ex.Message;
        }

        RaiseDisplayProperties();
    }

    public void ClearSearch()
    {
        SearchText = string.Empty;
        ResultCountText = "Enter a package name to search NuGet";
        PageSummaryText = string.Empty;
        SearchResults.Clear();
        RaiseDisplayProperties();
    }

    public void Select(PackageCardMock package)
    {
        SelectedPackage = new SelectedPackageMock
        {
            IconText = package.IconText,
            IconBackground = package.IconBackground,
            PackageId = package.EffectivePackageId,
            PublisherText = package.PublisherText,
            LatestVersion = package.LatestVersion,
            DownloadsText = package.DownloadsText,
            License = package.License,
            Description = package.Description,
            ProjectUrl = package.ProjectUrl,
            DependencyChips = new ObservableCollection<string>
            {
                "Dependencies are resolved during Preview or Download."
            }
        };
        OnPropertyChanged(nameof(SelectedPackage));
    }

    public void Add(PackageCardMock package)
    {
        Select(package);
        var packageId = package.EffectivePackageId;
        if (AddedPackages.Any(item => string.Equals(item.PackageId, packageId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        AddedPackages.Add(new AddedPackageMock(package.IconText, package.IconBackground, packageId, package.LatestVersion, package.LatestVersion, 0, "Ready"));
        UpdateSummary("Ready");
        OnPropertyChanged(nameof(AddedPackagesTitle));
    }

    public void ClearAddedPackages()
    {
        AddedPackages.Clear();
        UpdateSummary("Ready");
        OnPropertyChanged(nameof(AddedPackagesTitle));
    }

    public void NotifySettingsApplied() => UpdateSummary("Settings saved");

    public async Task RunNuGetAsync(bool previewOnly)
    {
        if (AddedPackages.Count == 0)
        {
            throw new InvalidOperationException("Add at least one NuGet package before running Preview or Download.");
        }

        UpdateSummary(previewOnly ? "Previewing" : "Downloading");
        var requestText = string.Join(Environment.NewLine, AddedPackages.Select(item => $"{item.PackageId} {item.RequestedVersion}"));
        var targetText = $"source={Settings.NuGet.Source}{Environment.NewLine}targetFramework={Settings.NuGet.TargetFramework}{Environment.NewLine}maxParallelism={Settings.NuGet.MaxParallelism}";
        var outputFolder = CommonOutput.ProviderOutputFolder(Settings.OutputFolder, "nuget");
        var request = new ProviderRunRequest("nuget", requestText, targetText, outputFolder, Settings.OverwriteExisting, previewOnly);
        var provider = new ProviderRegistry().Get("nuget");
        var progress = new Progress<DownloadRecord>(record => UpdateAddedPackage(record));

        var result = await provider.RunAsync(request, progress, CancellationToken.None);
        CommonOutput.WriteCommonFiles(request, result);
        UpdateSummary(
            result.OverallStatus,
            result.Records.Count(record => record.Kind == "Requested"),
            result.Records.Count(record => record.Kind == "Dependency"),
            result.Records.Count);
    }

    private void UpdateAddedPackage(DownloadRecord record)
    {
        for (var index = 0; index < AddedPackages.Count; index++)
        {
            var item = AddedPackages[index];
            if (!string.Equals(item.PackageId, record.Name, StringComparison.OrdinalIgnoreCase)) continue;
            AddedPackages[index] = item with
            {
                ResolvedVersion = string.IsNullOrWhiteSpace(record.Version) ? item.RequestedVersion : record.Version,
                Status = record.Status
            };
            return;
        }
    }

    private void UpdateSummary(string status, int? rootCount = null, int? dependencyCount = null, int? totalCount = null)
    {
        SummaryMetrics.Clear();
        var roots = rootCount ?? AddedPackages.Count;
        var dependencies = dependencyCount ?? 0;
        var total = totalCount ?? roots + dependencies;
        SummaryMetrics.Add(new SummaryMetricMock("R", "Roots", roots.ToString(), "#111827", "34,0,0,0"));
        SummaryMetrics.Add(new SummaryMetricMock("D", "Dependencies", dependencies.ToString(), "#111827", "58,0,0,0"));
        SummaryMetrics.Add(new SummaryMetricMock("P", "Total Packages", total.ToString(), "#111827", "58,0,0,0"));
        SummaryMetrics.Add(new SummaryMetricMock("S", "Estimated Size", "-", "#111827", "58,0,0,0"));
        SummaryMetrics.Add(new SummaryMetricMock("OK", "Status", status, status == "Failed" ? "#DC2626" : "#16803A", "58,0,0,0"));
    }

    private void RaiseDisplayProperties()
    {
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(PageSummaryText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class DelegateCommand : ICommand
{
    private readonly Action<object?> execute;

    public DelegateCommand(Action<object?> execute) => this.execute = execute;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}
