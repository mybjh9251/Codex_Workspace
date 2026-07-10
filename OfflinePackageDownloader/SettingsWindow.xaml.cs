using System.Windows;

namespace OfflinePackageDownloader;

public partial class SettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly string providerId;

    public SettingsWindow(AppSettings settings, string providerId)
    {
        InitializeComponent();
        this.settings = settings;
        this.providerId = providerId;
        ConfigureControls();
        LoadValues();
        ApplyProviderVisibility();
    }

    private void ConfigureControls()
    {
        NuGetSourceComboBox.ItemsSource = new[] { "https://api.nuget.org/v3/index.json" };
        NuGetTargetFrameworkComboBox.ItemsSource = new[] { "net8.0", "net9.0", "net6.0", "netstandard2.0", "net472" };
        NuGetMaxParallelismComboBox.ItemsSource = new[] { "1", "2", "3", "5", "8" };

        PythonIndexUrlComboBox.ItemsSource = new[] { "https://pypi.org/simple" };
        PythonPlatformComboBox.ItemsSource = new[] { "", "win_amd64", "win_arm64", "manylinux2014_x86_64", "manylinux2014_aarch64" };
        PythonVersionComboBox.ItemsSource = new[] { "", "3.10", "3.11", "3.12", "3.13" };

        VSCodeVersionComboBox.ItemsSource = new[] { "1.91.0", "1.92.0", "1.93.0", "1.94.0" };
        VSCodeTargetPlatformComboBox.ItemsSource = new[] { "win32-x64", "linux-x64", "win32-arm64", "linux-arm64", "web" };

        UbuntuVersionComboBox.ItemsSource = new[] { "noble", "jammy", "focal", "bionic" };
        UbuntuArchitectureComboBox.ItemsSource = new[] { "amd64", "arm64" };
        UbuntuComponentsComboBox.ItemsSource = new[] { "main", "main universe", "main universe multiverse" };
        UbuntuPocketsComboBox.ItemsSource = new[] { "release", "release updates security" };
        UbuntuBaseUrlComboBox.ItemsSource = new[] { "http://archive.ubuntu.com/ubuntu", "http://mirror.kakao.com/ubuntu", "http://ports.ubuntu.com/ubuntu-ports" };
    }

    private void LoadValues()
    {
        OutputTextBox.Text = settings.OutputFolder;
        OverwriteCheckBox.IsChecked = settings.OverwriteExisting;

        NuGetSourceComboBox.Text = settings.NuGet.Source;
        NuGetTargetFrameworkComboBox.Text = settings.NuGet.TargetFramework;
        NuGetMaxParallelismComboBox.Text = settings.NuGet.MaxParallelism;

        PythonExecutableTextBox.Text = settings.Python.PythonExecutable;
        PythonIndexUrlComboBox.Text = settings.Python.IndexUrl;
        PythonPlatformComboBox.Text = settings.Python.Platform;
        PythonVersionComboBox.Text = settings.Python.PythonVersion;
        PythonAbiTextBox.Text = settings.Python.Abi;

        VSCodeVersionComboBox.Text = settings.VSCode.VSCodeVersion;
        VSCodeTargetPlatformComboBox.Text = settings.VSCode.TargetPlatform;

        UbuntuVersionComboBox.Text = settings.Ubuntu.Version;
        UbuntuArchitectureComboBox.Text = settings.Ubuntu.Architecture;
        UbuntuComponentsComboBox.Text = settings.Ubuntu.Components;
        UbuntuPocketsComboBox.Text = settings.Ubuntu.Pockets;
        UbuntuBaseUrlComboBox.Text = settings.Ubuntu.BaseUrl;
        UbuntuMaxPackagesTextBox.Text = settings.Ubuntu.MaxPackages;
    }

    private void ApplyProviderVisibility()
    {
        NuGetPanel.Visibility = providerId == "nuget" ? Visibility.Visible : Visibility.Collapsed;
        PythonPanel.Visibility = providerId == "python" ? Visibility.Visible : Visibility.Collapsed;
        VSCodePanel.Visibility = providerId == "vscode-extension" ? Visibility.Visible : Visibility.Collapsed;
        UbuntuPanel.Visibility = providerId == "ubuntu" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        settings.OutputFolder = OutputTextBox.Text.Trim();
        settings.OverwriteExisting = OverwriteCheckBox.IsChecked == true;

        settings.NuGet.Source = NuGetSourceComboBox.Text.Trim();
        settings.NuGet.TargetFramework = NuGetTargetFrameworkComboBox.Text.Trim();
        settings.NuGet.MaxParallelism = NuGetMaxParallelismComboBox.Text.Trim();

        settings.Python.PythonExecutable = PythonExecutableTextBox.Text.Trim();
        settings.Python.IndexUrl = PythonIndexUrlComboBox.Text.Trim();
        settings.Python.Platform = PythonPlatformComboBox.Text.Trim();
        settings.Python.PythonVersion = PythonVersionComboBox.Text.Trim();
        settings.Python.Abi = PythonAbiTextBox.Text.Trim();

        settings.VSCode.VSCodeVersion = VSCodeVersionComboBox.Text.Trim();
        settings.VSCode.TargetPlatform = VSCodeTargetPlatformComboBox.Text.Trim();

        settings.Ubuntu.Version = UbuntuVersionComboBox.Text.Trim();
        settings.Ubuntu.Architecture = UbuntuArchitectureComboBox.Text.Trim();
        settings.Ubuntu.Components = UbuntuComponentsComboBox.Text.Trim();
        settings.Ubuntu.Pockets = UbuntuPocketsComboBox.Text.Trim();
        settings.Ubuntu.BaseUrl = UbuntuBaseUrlComboBox.Text.Trim();
        settings.Ubuntu.MaxPackages = UbuntuMaxPackagesTextBox.Text.Trim();

        DialogResult = true;
    }
}
