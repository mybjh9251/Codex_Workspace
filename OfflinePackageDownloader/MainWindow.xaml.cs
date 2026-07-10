using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OfflinePackageDownloader;

public partial class MainWindow : Window
{
    private readonly ProviderRegistry registry = new();
    private CancellationTokenSource? cancellation;

    public ObservableCollection<DownloadRecord> Results { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
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
        TargetTextBox.Text = definition.DefaultTarget;
        Results.Clear();
        StatusText.Text = "Ready";
        LogTextBox.Clear();
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
                TargetTextBox.Text,
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
}
