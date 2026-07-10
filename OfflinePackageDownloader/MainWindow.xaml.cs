using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace OfflinePackageDownloader;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
