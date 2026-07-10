using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace OfflinePackageDownloader;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = MainWindowViewModel.CreateDesignMockup();
        ApplyInitialWindowBounds();
    }

    private void ApplyInitialWindowBounds()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(workArea.Width * 0.94, workArea.Width - 80);
        Height = Math.Min(workArea.Height * 0.90, workArea.Height - 80);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }
}

public sealed class MainWindowViewModel
{
    public string SearchText { get; init; } = string.Empty;
    public string SortLabel { get; init; } = "Relevance";
    public string ResultCountText { get; init; } = string.Empty;
    public string PageSummaryText { get; init; } = string.Empty;
    public string AddedPackagesTitle => $"Added Packages ({AddedPackages.Count})";
    public ObservableCollection<ProviderTabMock> ProviderTabs { get; } = new();
    public ObservableCollection<PackageCardMock> SearchResults { get; } = new();
    public ObservableCollection<AddedPackageMock> AddedPackages { get; } = new();
    public ObservableCollection<SummaryMetricMock> SummaryMetrics { get; } = new();
    public SelectedPackageMock SelectedPackage { get; init; } = new();

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

        model.AddedPackages.Add(new AddedPackageMock(".NET", "#5B2DD1", "Microsoft.Extensions.\nConfiguration.Json", "8.0.0", "8.0.0", 3, "Ready"));
        model.AddedPackages.Add(new AddedPackageMock("{}{ }", "#1468A8", "Newtonsoft.Json", "13.0.3", "13.0.3", 0, "Ready"));
        model.AddedPackages.Add(new AddedPackageMock("≡", "#0E8388", "System.Text.Json", "8.0.0", "8.0.0", 1, "Ready"));

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
    string PackageId,
    string RequestedVersion,
    string ResolvedVersion,
    int DependencyCount,
    string Status);

public sealed class SelectedPackageMock
{
    public string IconText { get; init; } = string.Empty;
    public string IconBackground { get; init; } = "#5B2DD1";
    public string PackageId { get; init; } = string.Empty;
    public string PublisherText { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string DownloadsText { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ProjectUrl { get; init; } = string.Empty;
    public ObservableCollection<string> DependencyChips { get; } = new();
}

public sealed class PackageCardMock
{
    public string TitleLine1 { get; init; } = string.Empty;
    public string TitleLine2 { get; init; } = string.Empty;
    public string PublisherText { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string MetadataText { get; init; } = string.Empty;
    public string IconText { get; init; } = string.Empty;
    public string IconBackground { get; init; } = "#5B2DD1";
    public string IconForeground { get; init; } = "White";
    public int IconFontSize { get; init; } = 18;
    public string BorderBrush { get; init; } = "#DFE6EF";
    public string BorderThickness { get; init; } = "1";

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
                IconBackground = "#5B2DD1",
                BorderBrush = "#2E8BEF",
                BorderThickness = "2"
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
