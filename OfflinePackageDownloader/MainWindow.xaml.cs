using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OfflinePackageDownloader;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = MainWindowViewModel.CreateDesignMockup();
        DataContext = viewModel;
        ApplyInitialWindowBounds();
    }

    private void ApplyInitialWindowBounds()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1320, workArea.Width - 80);
        Height = Math.Min(875, workArea.Height - 80);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await viewModel.SearchAsync();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await viewModel.SearchAsync();
    private void ClearSearchButton_Click(object sender, RoutedEventArgs e) => viewModel.ClearSearch();
    private void FilterButton_Click(object sender, RoutedEventArgs e) => viewModel.ToggleFilterPopup();
    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string sortLabel }) viewModel.SortLabel = sortLabel;
    }
    private void PreviousPageButton_Click(object sender, RoutedEventArgs e) => viewModel.PreviousPage();
    private void NextPageButton_Click(object sender, RoutedEventArgs e) => viewModel.NextPage();
    private void SelectedPackageToggleButton_Click(object sender, RoutedEventArgs e) => viewModel.ToggleSelectedPackageExpanded();

    private void ClearAddedPackagesButton_Click(object sender, RoutedEventArgs e) => viewModel.ClearAddedPackages();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(viewModel.Settings, "nuget") { Owner = this };
        if (window.ShowDialog() == true)
        {
            AppSettingsStore.Save(viewModel.Settings);
            viewModel.NotifySettingsApplied();
        }
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e) => await RunNuGetAsync(true);
    private async void DownloadButton_Click(object sender, RoutedEventArgs e) => await RunNuGetAsync(false);

    private async System.Threading.Tasks.Task RunNuGetAsync(bool previewOnly)
    {
        try { await viewModel.RunNuGetAsync(previewOnly); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "NuGet operation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    public string SearchText { get; set; } = string.Empty;
    private string sortLabel = "Relevance";
    public string SortLabel
    {
        get => sortLabel;
        set
        {
            if (string.Equals(sortLabel, value, StringComparison.Ordinal)) return;
            sortLabel = value;
            OnPropertyChanged();
            ApplySearchResultView();
        }
    }
    public string ResultCountText { get; set; } = string.Empty;
    public string PageSummaryText { get; set; } = string.Empty;
    public ObservableCollection<string> SortOptions { get; } = new() { "Relevance", "Downloads", "Package ID" };
    public string AddedPackagesTitle => $"Added Packages ({AddedPackages.Count})";
    public ObservableCollection<ProviderTabMock> ProviderTabs { get; } = new();
    public ObservableCollection<PackageCardMock> SearchResults { get; } = new();
    public ObservableCollection<AddedPackageMock> AddedPackages { get; } = new();
    public ObservableCollection<SummaryMetricMock> SummaryMetrics { get; } = new();
    public SelectedPackageMock SelectedPackage { get; set; } = new();

    public static MainWindowViewModel CreateDesignMockup()
    {
        var model = new MainWindowViewModel
        {
            SearchText = "configuration json",
            ResultCountText = "1,234 results for “configuration json”",
            PageSummaryText = "Showing 1 – 12 of 1,234",
            SelectedPackage = new SelectedPackageMock
            {
                IconText = ".NET",
                IconBackground = "#5B2DD1",
                PackageId = "Microsoft.Extensions.Configuration.Json",
                PublisherText = "by Microsoft",
                LatestVersion = "8.0.0",
                DownloadsText = "241.3M",
                License = "MIT",
                Description = "JSON configuration provider implementation for Microsoft Extensions Configuration.",
                ProjectUrl = "https://github.com/dotnet/runtime",
                DependencyChips =
                {
                    "Microsoft.Extensions.Configuration.Abstractions (>= 8.0.0)",
                    "Microsoft.Extensions.Primitives (>= 8.0.0)",
                    "+ 1 more"
                }
            }
        };

        model.ProviderTabs.Add(new ProviderTabMock("NuGet", "●", "#167BD8", "#147BD1", "#147BD1", "SemiBold", 132, "7", 18));
        model.ProviderTabs.Add(new ProviderTabMock("Python", "P", "#2387C8", "#1F2937", "Transparent", "Normal", 132, "7", 17));
        model.ProviderTabs.Add(new ProviderTabMock("VS Code Extension", "◆", "#1685E5", "#1F2937", "Transparent", "Normal", 218, "0", 17));
        model.ProviderTabs.Add(new ProviderTabMock("Ubuntu", "U", "#E95420", "#1F2937", "Transparent", "Normal", 132, "14", 16));

        foreach (var item in PackageCardMock.CreateDefaults())
        {
            model.SearchResults.Add(item);
        }
        model.SearchResults[0].IsSelected = true;

        model.AddedPackages.Add(new AddedPackageMock(".NET", "#5B2DD1", null, "Microsoft.Extensions.Configuration.Json", "8.0.0", "8.0.0", 3, "Ready"));
        model.AddedPackages.Add(new AddedPackageMock("{}{ }", "#1468A8", null, "Newtonsoft.Json", "13.0.3", "13.0.3", 0, "Ready"));
        model.AddedPackages.Add(new AddedPackageMock("≡", "#0E8388", null, "System.Text.Json", "8.0.0", "8.0.0", 1, "Ready"));

        model.SummaryMetrics.Add(new SummaryMetricMock("⌘", "Roots", "3", "#111827", "34,0,0,0"));
        model.SummaryMetrics.Add(new SummaryMetricMock("⬡", "Dependencies", "4", "#111827", "58,0,0,0"));
        model.SummaryMetrics.Add(new SummaryMetricMock("▧", "Total Packages", "7", "#111827", "58,0,0,0"));
        model.SummaryMetrics.Add(new SummaryMetricMock("▰", "Estimated Size", "12.45 MB", "#111827", "58,0,0,0"));
        model.SummaryMetrics.Add(new SummaryMetricMock("✓", "Status", "Ready", "#16803A", "58,0,0,0"));

        return model;
    }
}

public sealed record ProviderTabMock(
    string DisplayName,
    string IconText,
    string IconBackground,
    string TextBrush,
    string UnderlineBrush,
    string FontWeight,
    int Width,
    string IconCornerRadius,
    int IconFontSize);

public sealed record SummaryMetricMock(
    string Icon,
    string Label,
    string Value,
    string ValueBrush,
    string Margin);

public sealed record AddedPackageMock(
    string IconText,
    string IconBackground,
    ImageSource? IconImage,
    string PackageId,
    string RequestedVersion,
    string ResolvedVersion,
    int DependencyCount,
    string Status)
{
    public string DisplayPackageId => string.Equals(PackageId, "Microsoft.Extensions.Configuration.Json", StringComparison.Ordinal)
        ? "Microsoft.Extensions.\nConfiguration.Json"
        : PackageId;
}

public sealed class SelectedPackageMock
{
    public string IconText { get; init; } = string.Empty;
    public string IconBackground { get; init; } = "#5B2DD1";
    public ImageSource? IconImage { get; init; }
    public string PackageId { get; init; } = string.Empty;
    public string PublisherText { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string DownloadsText { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ProjectUrl { get; init; } = string.Empty;
    public ObservableCollection<string> DependencyChips { get; init; } = new();
}

public sealed class PackageCardMock : INotifyPropertyChanged
{
    private const int DescriptionPreviewLength = 100;
    private bool isSelected;
    private ImageSource? iconImage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PackageId { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = "latest";
    public string DownloadsText { get; init; } = "downloads unavailable";
    public string License { get; init; } = "Unknown";
    public string ProjectUrl { get; init; } = string.Empty;
    public string TitleLine1 { get; init; } = string.Empty;
    public string TitleLine2 { get; init; } = string.Empty;
    public string PublisherText { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string DescriptionPreview => Description.Length <= DescriptionPreviewLength
        ? Description
        : $"{Description[..DescriptionPreviewLength].TrimEnd()}...";
    public string MetadataText { get; init; } = string.Empty;
    public string IconText { get; init; } = string.Empty;
    public string IconBackground { get; init; } = "#5B2DD1";
    public string IconUrl { get; init; } = string.Empty;
    public ImageSource? IconImage
    {
        get => iconImage;
        private set
        {
            if (ReferenceEquals(iconImage, value)) return;
            iconImage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconImage)));
        }
    }
    public string IconForeground { get; init; } = "White";
    public int IconFontSize { get; init; } = 18;
    public string BorderBrush { get; init; } = "#DFE6EF";
    public string BorderThickness { get; init; } = "1";
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBorderThickness)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBackground)));
        }
    }
    public string CardBorderBrush => IsSelected ? "#147BD1" : BorderBrush;
    public string CardBorderThickness => IsSelected ? "2" : BorderThickness;
    public string CardBackground => IsSelected ? "#F7FBFF" : "White";

    public static PackageCardMock FromSearchResult(PackageSearchResult result)
    {
        var title = string.IsNullOrWhiteSpace(result.DisplayName) ? result.PackageId : result.DisplayName;
        var splitAt = title.LastIndexOf('.', Math.Min(title.Length - 1, 32));
        var line1 = splitAt > 8 ? title[..(splitAt + 1)] : title;
        var line2 = splitAt > 8 ? title[(splitAt + 1)..] : string.Empty;
        return new PackageCardMock
        {
            PackageId = result.PackageId,
            TitleLine1 = line1,
            TitleLine2 = line2,
            PublisherText = string.IsNullOrWhiteSpace(result.Publisher) ? "by NuGet publisher" : $"by {result.Publisher}",
            Description = result.Description,
            MetadataText = $"{result.DownloadsText}     v{result.LatestVersion}",
            IconText = result.IconText,
            IconBackground = result.IconBackground,
            IconUrl = result.IconUrl,
            IconFontSize = result.IconText == ".NET" ? 16 : 26,
            LatestVersion = string.IsNullOrWhiteSpace(result.LatestVersion) ? "latest" : result.LatestVersion,
            DownloadsText = result.Downloads > 0 ? result.Downloads.ToString("N0") : "-",
            License = result.License,
            ProjectUrl = result.ProjectUrl
        };
    }

    public string EffectivePackageId => string.IsNullOrWhiteSpace(PackageId) ? string.Concat(TitleLine1, TitleLine2) : PackageId;

    public async Task LoadIconAsync(CancellationToken cancellationToken) => IconImage = await PackageIconLoader.LoadAsync(IconUrl, cancellationToken);

    public static IReadOnlyList<PackageCardMock> CreateDefaults()
    {
        return new[]
        {
            new PackageCardMock
            {
                TitleLine1 = "Microsoft.Extensions-",
                TitleLine2 = "Configuration.Json",
                PublisherText = "by Microsoft",
                Description = "JSON configuration provider implementation for Microsoft Extensions Configuration.",
                MetadataText = "⇩ 241.3M     ◇ v8.0.0",
                IconText = ".NET",
                IconBackground = "#5B2DD1"
            },
            new PackageCardMock
            {
                TitleLine1 = "JsonConfiguration",
                PublisherText = "by Marko Lahma",
                Description = "Simple JSON based configuration provider for .NET applications.",
                MetadataText = "⇩ 5.1M     ◇ v3.2.1",
                IconText = "J",
                IconBackground = "#0A858C",
                IconFontSize = 27
            },
            new PackageCardMock
            {
                TitleLine1 = "NJxonSchema",
                PublisherText = "by Rico Suter",
                Description = "Generates strongly typed clients and JSON schemas for .NET from OpenAPI (Swagger) specifications.",
                MetadataText = "⇩ 24.7M     ◇ v11.0.0",
                IconText = "⬡",
                IconBackground = "Transparent",
                IconForeground = "#0B5EA8",
                IconFontSize = 44
            },
            new PackageCardMock
            {
                TitleLine1 = "Microsoft.Extensions.Configuration",
                PublisherText = "by Microsoft",
                Description = "Abstractions and implementation of key-value pair based configuration.",
                MetadataText = "⇩ 1.18B     ◇ v8.0.0",
                IconText = "⬢",
                IconBackground = "#42A53B",
                IconFontSize = 26
            },
            new PackageCardMock
            {
                TitleLine1 = "Newtonsoft.Json",
                PublisherText = "by James Newton-King",
                Description = "Json.NET is a popular high-performance JSON framework for .NET.",
                MetadataText = "⇩ 2.22B     ◇ v13.0.3",
                IconText = "{}{ }",
                IconBackground = "#1468A8",
                IconFontSize = 21
            },
            new PackageCardMock
            {
                TitleLine1 = "System.Text.Json",
                PublisherText = "by Microsoft",
                Description = "High-performance JSON serialization and deserialization for .NET.",
                MetadataText = "⇩ 1.02B     ◇ v8.0.0",
                IconText = "≡",
                IconBackground = "#0E8388",
                IconFontSize = 34
            },
            new PackageCardMock
            {
                TitleLine1 = "Serilog.Settings.Configuration",
                PublisherText = "by Serilog Contributors",
                Description = "Configuration support for Serilog using Microsoft.Extensions.Configuration.",
                MetadataText = "⇩ 34.6M",
                IconText = "☁",
                IconBackground = "#2F73B7",
                IconFontSize = 27
            },
            new PackageCardMock
            {
                TitleLine1 = "Autofac.Configuration",
                PublisherText = "by Autofac Contributors",
                Description = "Integration between Autofac and configuration providers.",
                MetadataText = "⇩ 12.7M     ◇ v7.0.0",
                IconText = "○",
                IconBackground = "#D53B2F",
                IconFontSize = 31
            },
            new PackageCardMock
            {
                TitleLine1 = "FluentValidation.DependencyInjection...",
                PublisherText = "by Jeremy Skinner",
                Description = "Registers validators from assemblies using configuration.",
                MetadataText = "⇩ 8.3M     ◇ v11.8.1",
                IconText = "✣",
                IconBackground = "#6942B8",
                IconFontSize = 28
            }
        };
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
