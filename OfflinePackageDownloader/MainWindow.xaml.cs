using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OfflinePackageDownloader;

public partial class MainWindow : Window
{
    private readonly ProviderRegistry registry = new();
    private CancellationTokenSource? cancellation;

    public ObservableCollection<DownloadRecord> Results { get; } = new();
    public ObservableCollection<MarketplaceSearchResult> MarketplaceResults { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ConfigureSettingControls();
        ProviderList.ItemsSource = registry.Providers.Select(p => p.Definition).ToList();
        ProviderList.SelectedIndex = 0;
        OutputTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OfflinePackageDownloader_Output");
    }

    private ProviderDefinition CurrentDefinition => (ProviderDefinition)ProviderList.SelectedItem;
    private IOfflinePackageProvider CurrentProvider => registry.Get(CurrentDefinition.Id);

    private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderDefinition definition)
        {
            return;
        }

        TitleText.Text = definition.DisplayName;
        ProviderHintText.Text = definition.Description;
        RequestTextBox.Text = definition.DefaultRequests;
        MarketplaceSearchTextBox.Text = definition.Id == "vscode-extension" ? "python" : string.Empty;
        MarketplaceResults.Clear();
        ApplyProviderSettings(definition.Id);
        Results.Clear();
        StatusText.Text = "Ready";
        LogTextBox.Clear();
    }

    private async void SearchMarketplace_Click(object sender, RoutedEventArgs e)
    {
        await SearchMarketplaceAsync();
    }

    private async void MarketplaceSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SearchMarketplaceAsync();
        }
    }

    private void AddMarketplaceResult_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MarketplaceSearchResult result)
        {
            return;
        }

        AddRequestLine(result.ExtensionId);
        StatusText.Text = $"Added {result.ExtensionId}";
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

    private async Task RunCurrentProviderAsync(bool previewOnly)
    {
        SetBusy(true);
        Results.Clear();
        LogTextBox.Clear();
        cancellation = new CancellationTokenSource();

        try
        {
            var provider = CurrentProvider;
            var providerOutput = CommonOutput.ProviderOutputFolder(OutputTextBox.Text, provider.Definition.Id);
            var request = new ProviderRunRequest(
                provider.Definition.Id,
                RequestTextBox.Text,
                BuildTargetSettings(provider.Definition.Id),
                providerOutput,
                OverwriteCheckBox.IsChecked == true,
                previewOnly);

            StatusText.Text = previewOnly ? "Resolving..." : "Downloading...";
            var progress = new Progress<DownloadRecord>(UpsertRecord);
            var result = await provider.RunAsync(request, progress, cancellation.Token);
            CommonOutput.WriteCommonFiles(request, result);

            foreach (var record in result.Records)
            {
                UpsertRecord(record);
            }

            StatusText.Text = $"{result.OverallStatus}: {result.OutputFolder}";
            Log($"Generated files:{Environment.NewLine}{string.Join(Environment.NewLine, result.GeneratedFiles)}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Canceled";
            Log("Operation canceled.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed";
            Log(ex.ToString());
        }
        finally
        {
            SetBusy(false);
            cancellation?.Dispose();
            cancellation = null;
        }
    }

    private void UpsertRecord(DownloadRecord record)
    {
        var existing = Results.FirstOrDefault(r => r.ProviderId == record.ProviderId && r.Name == record.Name && r.Version == record.Version && r.Kind == record.Kind);
        if (existing == null)
        {
            Results.Add(record);
            return;
        }

        existing.Status = record.Status;
        existing.Message = record.Message;
        existing.FileName = record.FileName;
        existing.Source = record.Source;
    }

    private void SetBusy(bool busy)
    {
        ResolveButton.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        ProviderList.IsEnabled = !busy;
    }

    private void Log(string message)
    {
        LogTextBox.AppendText(message + Environment.NewLine);
        LogTextBox.ScrollToEnd();
    }

    private void ConfigureSettingControls()
    {
        NuGetSourceComboBox.ItemsSource = new[]
        {
            "https://api.nuget.org/v3/index.json"
        };
        NuGetSourceComboBox.Text = "https://api.nuget.org/v3/index.json";
        NuGetTargetFrameworkComboBox.ItemsSource = new[] { "net8.0", "net9.0", "net6.0", "netstandard2.0", "net472" };
        NuGetTargetFrameworkComboBox.Text = "net8.0";
        NuGetMaxParallelismComboBox.ItemsSource = new[] { "1", "2", "3", "5", "8" };
        NuGetMaxParallelismComboBox.Text = "5";

        PythonExecutableTextBox.Text = "python";
        PythonIndexUrlComboBox.ItemsSource = new[] { "https://pypi.org/simple" };
        PythonIndexUrlComboBox.Text = "https://pypi.org/simple";
        PythonPlatformComboBox.ItemsSource = new[] { "", "win_amd64", "win_arm64", "manylinux2014_x86_64", "manylinux2014_aarch64" };
        PythonPlatformComboBox.Text = "";
        PythonVersionComboBox.ItemsSource = new[] { "", "3.10", "3.11", "3.12", "3.13" };
        PythonVersionComboBox.Text = "";
        PythonAbiTextBox.Text = "";

        VSCodeVersionComboBox.ItemsSource = new[] { "1.91.0", "1.92.0", "1.93.0", "1.94.0" };
        VSCodeVersionComboBox.Text = "1.91.0";
        VSCodeTargetPlatformComboBox.ItemsSource = new[] { "win32-x64", "linux-x64", "win32-arm64", "linux-arm64", "web" };
        VSCodeTargetPlatformComboBox.Text = "win32-x64";

        UbuntuVersionComboBox.ItemsSource = new[] { "noble", "jammy", "focal", "bionic" };
        UbuntuVersionComboBox.Text = "noble";
        UbuntuArchitectureComboBox.ItemsSource = new[] { "amd64", "arm64" };
        UbuntuArchitectureComboBox.Text = "amd64";
        UbuntuComponentsComboBox.ItemsSource = new[] { "main", "main universe", "main universe multiverse" };
        UbuntuComponentsComboBox.Text = "main universe";
        UbuntuPocketsComboBox.ItemsSource = new[] { "release", "release updates security" };
        UbuntuPocketsComboBox.Text = "release updates security";
        UbuntuBaseUrlComboBox.ItemsSource = new[] { "http://archive.ubuntu.com/ubuntu", "http://mirror.kakao.com/ubuntu", "http://ports.ubuntu.com/ubuntu-ports" };
        UbuntuBaseUrlComboBox.Text = "http://archive.ubuntu.com/ubuntu";
        UbuntuMaxPackagesTextBox.Text = "80";
    }

    private void ApplyProviderSettings(string providerId)
    {
        NuGetSettingsPanel.Visibility = providerId == "nuget" ? Visibility.Visible : Visibility.Collapsed;
        PythonSettingsPanel.Visibility = providerId == "python" ? Visibility.Visible : Visibility.Collapsed;
        VSCodeSettingsPanel.Visibility = providerId == "vscode-extension" ? Visibility.Visible : Visibility.Collapsed;
        UbuntuSettingsPanel.Visibility = providerId == "ubuntu" ? Visibility.Visible : Visibility.Collapsed;
        VSCodeSearchPanel.Visibility = providerId == "vscode-extension" ? Visibility.Visible : Visibility.Collapsed;
        RequestHeaderText.Text = providerId == "vscode-extension" ? "Selected Extensions" : "Requests";
    }

    private string BuildTargetSettings(string providerId)
    {
        return providerId switch
        {
            "nuget" => string.Join(Environment.NewLine, new[]
            {
                $"source={ComboText(NuGetSourceComboBox)}",
                $"targetFramework={ComboText(NuGetTargetFrameworkComboBox)}",
                $"maxParallelism={ComboText(NuGetMaxParallelismComboBox)}"
            }),
            "python" => string.Join(Environment.NewLine, new[]
            {
                $"python={PythonExecutableTextBox.Text.Trim()}",
                $"indexUrl={ComboText(PythonIndexUrlComboBox)}",
                $"platform={ComboText(PythonPlatformComboBox)}",
                $"pythonVersion={ComboText(PythonVersionComboBox)}",
                "implementation=cp",
                $"abi={PythonAbiTextBox.Text.Trim()}"
            }),
            "vscode-extension" => string.Join(Environment.NewLine, new[]
            {
                $"vscodeVersion={ComboText(VSCodeVersionComboBox)}",
                $"targetPlatform={ComboText(VSCodeTargetPlatformComboBox)}"
            }),
            "ubuntu" => string.Join(Environment.NewLine, new[]
            {
                $"version={ComboText(UbuntuVersionComboBox)}",
                $"architecture={ComboText(UbuntuArchitectureComboBox)}",
                $"components={ComboText(UbuntuComponentsComboBox)}",
                $"pockets={ComboText(UbuntuPocketsComboBox)}",
                $"baseUrl={ComboText(UbuntuBaseUrlComboBox)}",
                $"maxPackages={UbuntuMaxPackagesTextBox.Text.Trim()}"
            }),
            _ => string.Empty
        };
    }

    private async Task SearchMarketplaceAsync()
    {
        var query = MarketplaceSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        try
        {
            StatusText.Text = $"Searching Marketplace: {query}";
            MarketplaceResults.Clear();
            var results = await MarketplaceSearchClient.SearchAsync(query, 25, CancellationToken.None);
            foreach (var result in results)
            {
                MarketplaceResults.Add(result);
            }

            StatusText.Text = $"Search returned {MarketplaceResults.Count} result(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Search failed";
            Log("Marketplace search failed: " + ex.Message);
        }
    }

    private void AddRequestLine(string value)
    {
        var lines = RequestTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (!lines.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(value);
        }

        RequestTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    private static string ComboText(ComboBox comboBox)
    {
        return comboBox.Text.Trim();
    }
}
