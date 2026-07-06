using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace PythonPackageDownloaderWPF;

public partial class MainWindow : Window
{
    private static readonly Regex PackageNameRegex = new(@"^\s*([A-Za-z0-9][A-Za-z0-9._-]*)(?:\[[^\]]+\])?\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex ExactVersionRegex = new(@"(?:^|[, ]+)==\s*([A-Za-z0-9!._*+\-]+)", RegexOptions.Compiled);
    private static readonly Regex WheelNameRegex = new(@"^(?<name>.+?)-(?<version>[0-9][^-]*)-", RegexOptions.Compiled);

    private readonly ObservableCollection<ReportRow> _reportRows = [];
    private CancellationTokenSource? _downloadCts;
    private List<PackageRequest> _lastFailedRequests = [];
    private DownloadTarget _currentTarget = new("Windows", "3.11", "x64", "win_amd64", "cp311");

    public MainWindow()
    {
        InitializeComponent();
        ReportDataGrid.ItemsSource = _reportRows;
        InitializeDefaults();
        RefreshRuntimeStatus();
    }

    private void InitializeDefaults()
    {
        TargetOsComboBox.ItemsSource = new[] { "Windows", "Linux" };
        PythonVersionComboBox.ItemsSource = new[] { "3.10", "3.11", "3.12", "3.13" };
        ArchitectureComboBox.ItemsSource = new[] { "x64", "arm64" };
        TargetOsComboBox.SelectedItem = "Windows";
        PythonVersionComboBox.SelectedItem = "3.11";
        ArchitectureComboBox.SelectedItem = "x64";

        OutputFolderTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "PythonPackageBundle");

        UpdateTargetOptions();
        UpdateCounts();
    }

    private void RefreshRuntimeStatus()
    {
        var pythonPath = FindPythonExecutable();
        RuntimeStatusTextBlock.Text = pythonPath.Source == PythonSource.Bundled
            ? $"Bundled Python: {pythonPath.Path}"
            : $"Bundled Python not found. Development fallback will try: {pythonPath.Path}";
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDownloadAsync(null);
    }

    private async void RetryFailedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFailedRequests.Count == 0)
        {
            return;
        }

        await RunDownloadAsync(_lastFailedRequests);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        AppendLog("Cancel requested.");
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select output folder",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void TargetSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateTargetOptions();
    }

    private void UseFirstRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if ((TargetOsComboBox.SelectedItem as string) == "Linux")
        {
            SetTarget("Linux", "3.11", "x64");
        }
        else
        {
            SetTarget("Windows", DetectCurrentPythonVersion() ?? "3.11", "x64");
        }
    }

    private void UseSecondRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if ((TargetOsComboBox.SelectedItem as string) == "Linux")
        {
            SetTarget("Linux", "3.10", "x64");
        }
        else
        {
            SetTarget("Windows", "3.11", "x64");
        }
    }

    private void SetTarget(string os, string pythonVersion, string architecture)
    {
        TargetOsComboBox.SelectedItem = os;
        PythonVersionComboBox.SelectedItem = pythonVersion;
        ArchitectureComboBox.SelectedItem = architecture;
        UpdateTargetOptions();
    }

    private void UpdateTargetOptions()
    {
        var targetOs = TargetOsComboBox.SelectedItem as string ?? "Windows";
        var pythonVersion = PythonVersionComboBox.SelectedItem as string ?? "3.11";
        var architecture = ArchitectureComboBox.SelectedItem as string ?? "x64";
        var platformTags = targetOs == "Linux"
            ? new[] { "manylinux2014_x86_64", "manylinux2014_aarch64" }
            : new[] { "win_amd64", "win_arm64" };

        PlatformTagComboBox.ItemsSource = platformTags;
        PlatformTagComboBox.Text = GetRecommendedPlatformTag(targetOs, architecture);
        AbiComboBox.ItemsSource = new[] { GetAbiTag(pythonVersion), "abi3", "none" };
        AbiComboBox.Text = GetAbiTag(pythonVersion);

        _currentTarget = new DownloadTarget(targetOs, pythonVersion, architecture, PlatformTagComboBox.Text, AbiComboBox.Text);

        RecommendationTextBlock.Text = targetOs == "Linux"
            ? "1st: Linux x64 + Python 3.11 + manylinux2014_x86_64. 2nd: Linux x64 + Python 3.10 + manylinux2014_x86_64."
            : "1st: Windows x64 + current Python version + win_amd64. 2nd: Windows x64 + Python 3.11 + win_amd64.";
    }

    private static string GetRecommendedPlatformTag(string targetOs, string architecture)
    {
        return (targetOs, architecture) switch
        {
            ("Linux", "arm64") => "manylinux2014_aarch64",
            ("Linux", _) => "manylinux2014_x86_64",
            ("Windows", "arm64") => "win_arm64",
            _ => "win_amd64"
        };
    }

    private static string GetAbiTag(string pythonVersion)
    {
        return "cp" + pythonVersion.Replace(".", string.Empty, StringComparison.Ordinal);
    }

    private async Task RunDownloadAsync(List<PackageRequest>? retryRequests)
    {
        var requests = retryRequests ?? ParsePackageInput();
        if (requests.Count == 0)
        {
            MessageBox.Show(this, "Enter at least one package requirement.", "Package input", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var validationErrors = ValidateRequests(requests);
        if (validationErrors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, validationErrors), "Validation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdateTargetOptions();
        _currentTarget = new DownloadTarget(
            TargetOsComboBox.SelectedItem as string ?? "Windows",
            PythonVersionComboBox.SelectedItem as string ?? "3.11",
            ArchitectureComboBox.SelectedItem as string ?? "x64",
            PlatformTagComboBox.Text,
            AbiComboBox.Text);

        var outputFolder = OutputFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            MessageBox.Show(this, "Select an output folder.", "Output folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(outputFolder);
        Directory.CreateDirectory(Path.Combine(outputFolder, "packages"));

        _downloadCts = new CancellationTokenSource();
        _lastFailedRequests = [];
        _reportRows.Clear();
        DownloadProgressBar.Value = 0;
        DownloadProgressBar.Maximum = requests.Count;
        SetBusy(true);
        AppendLog($"Starting download for {requests.Count} package request(s).");

        var python = FindPythonExecutable();
        if (python.Source != PythonSource.Bundled)
        {
            AppendLog("Bundled Python was not found. Trying development fallback Python command.");
        }

        var pythonVersion = await TryGetProcessOutputAsync(python.Path, "--version", _downloadCts.Token);
        var pipVersion = await TryGetProcessOutputAsync(python.Path, "-m pip --version", _downloadCts.Token);
        AppendLog($"Python: {pythonVersion.Trim()}");
        AppendLog($"pip: {pipVersion.Trim()}");

        var downloadedBefore = SnapshotWheelFiles(outputFolder);
        var completed = 0;

        foreach (var request in requests)
        {
            if (_downloadCts.IsCancellationRequested)
            {
                AddReportRow(request, "Canceled", string.Empty, string.Empty, "Canceled before starting package.", string.Empty);
                continue;
            }

            AppendLog($"Downloading {request.OriginalSpec}");
            var command = BuildPipArguments(request, outputFolder);
            var result = await RunProcessAsync(python.Path, command, _downloadCts.Token);
            var currentWheels = SnapshotWheelFiles(outputFolder);
            var newOrChanged = currentWheels.Except(downloadedBefore, StringComparer.OrdinalIgnoreCase).ToList();
            downloadedBefore = currentWheels;

            if (_downloadCts.IsCancellationRequested)
            {
                AddReportRow(request, "Canceled", string.Empty, string.Empty, "Download canceled.", RedactCommand(python.Path, command));
            }
            else if (result.ExitCode == 0)
            {
                var wheel = FindBestWheelForPackage(outputFolder, request.PackageName, newOrChanged);
                var status = newOrChanged.Count == 0 && !OverwriteCheckBox.IsChecked.GetValueOrDefault()
                    ? "Skipped"
                    : OverwriteCheckBox.IsChecked.GetValueOrDefault() ? "Overwritten" : "Downloaded";
                AddReportRow(
                    request,
                    status,
                    wheel.Version,
                    wheel.FileName,
                    "pip download completed.",
                    RedactCommand(python.Path, command));
            }
            else
            {
                _lastFailedRequests.Add(request);
                AddReportRow(
                    request,
                    "Failed",
                    string.Empty,
                    string.Empty,
                    SummarizeFailure(result),
                    RedactCommand(python.Path, command));
            }

            completed++;
            DownloadProgressBar.Value = completed;
            UpdateCounts();
        }

        await WriteOutputsAsync(outputFolder, pythonVersion.Trim(), pipVersion.Trim());
        UpdateOverallStatus();
        SetBusy(false);
        _downloadCts.Dispose();
        _downloadCts = null;
        RetryFailedButton.IsEnabled = _lastFailedRequests.Count > 0;
        AppendLog("Download workflow finished.");
    }

    private List<PackageRequest> ParsePackageInput()
    {
        return PackageInputTextBox.Text
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select((line, index) => ParseRequirementLine(line, index + 1))
            .Where(request => request is not null)
            .Select(request => request!)
            .ToList();
    }

    private static PackageRequest? ParseRequirementLine(string line, int lineNumber)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
        {
            return null;
        }

        var hashIndex = trimmed.IndexOf(" #", StringComparison.Ordinal);
        if (hashIndex >= 0)
        {
            trimmed = trimmed[..hashIndex].Trim();
        }

        var match = PackageNameRegex.Match(trimmed);
        var packageName = match.Success ? NormalizePackageName(match.Groups[1].Value) : string.Empty;
        var exact = match.Success ? ExactVersionRegex.Match(match.Groups[2].Value) : Match.Empty;
        var exactVersion = exact.Success ? exact.Groups[1].Value : null;

        return new PackageRequest(lineNumber, trimmed, packageName, exactVersion);
    }

    private static List<string> ValidateRequests(List<PackageRequest> requests)
    {
        var errors = new List<string>();
        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.PackageName))
            {
                errors.Add($"Line {request.LineNumber}: cannot parse requirement '{request.OriginalSpec}'.");
            }
        }

        var exactVersionConflicts = requests
            .Where(request => !string.IsNullOrWhiteSpace(request.ExactVersion))
            .GroupBy(request => request.PackageName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(request => request.ExactVersion).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        foreach (var conflict in exactVersionConflicts)
        {
            errors.Add($"Package '{conflict.Key}' has conflicting exact versions: {string.Join(", ", conflict.Select(request => request.ExactVersion).Distinct())}.");
        }

        return errors;
    }

    private string BuildPipArguments(PackageRequest request, string outputFolder)
    {
        var args = new List<string>
        {
            "-m",
            "pip",
            "download",
            Quote(request.OriginalSpec),
            "--dest",
            Quote(Path.Combine(outputFolder, "packages")),
            "--index-url",
            Quote(IndexUrlTextBox.Text.Trim()),
            "--only-binary=:all:",
            "--platform",
            Quote(_currentTarget.PlatformTag),
            "--python-version",
            Quote(_currentTarget.PythonVersion),
            "--implementation",
            "cp",
            "--abi",
            Quote(_currentTarget.Abi),
            "--retries",
            "2",
            "--timeout",
            "30"
        };

        if (NoDependenciesCheckBox.IsChecked.GetValueOrDefault())
        {
            args.Add("--no-deps");
        }

        if (OverwriteCheckBox.IsChecked.GetValueOrDefault())
        {
            args.Add("--no-cache-dir");
        }

        return string.Join(' ', args);
    }

    private async Task WriteOutputsAsync(string outputFolder, string pythonVersion, string pipVersion)
    {
        await File.WriteAllLinesAsync(
            Path.Combine(outputFolder, "requirements.lock.txt"),
            BuildLockLines(outputFolder),
            Encoding.UTF8);

        await File.WriteAllLinesAsync(
            Path.Combine(outputFolder, "download-report.csv"),
            BuildReportLines(pythonVersion, pipVersion),
            Encoding.UTF8);

        var installScriptPath = _currentTarget.TargetOs == "Linux"
            ? Path.Combine(outputFolder, "install-offline.sh")
            : Path.Combine(outputFolder, "install-offline.ps1");

        await File.WriteAllTextAsync(installScriptPath, BuildInstallScript(), Encoding.UTF8);
    }

    private IEnumerable<string> BuildLockLines(string outputFolder)
    {
        var packageFolder = Path.Combine(outputFolder, "packages");
        var wheelFiles = Directory.Exists(packageFolder)
            ? Directory.GetFiles(packageFolder, "*.whl")
            : [];

        foreach (var wheel in wheelFiles.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var parsed = ParseWheelFile(Path.GetFileName(wheel));
            if (!string.IsNullOrWhiteSpace(parsed.Package) && !string.IsNullOrWhiteSpace(parsed.Version))
            {
                yield return $"{parsed.Package}=={parsed.Version}";
            }
        }
    }

    private IEnumerable<string> BuildReportLines(string pythonVersion, string pipVersion)
    {
        yield return "package,requested_spec,resolved_version,status,file_name,target_os,python_version,architecture,platform_tag,source_index,message,python_runtime,pip_runtime,pip_command";

        foreach (var row in _reportRows)
        {
            yield return string.Join(',', new[]
            {
                Csv(row.Package),
                Csv(row.RequestedSpec),
                Csv(row.ResolvedVersion),
                Csv(row.Status),
                Csv(row.FileName),
                Csv(_currentTarget.TargetOs),
                Csv(_currentTarget.PythonVersion),
                Csv(_currentTarget.Architecture),
                Csv(_currentTarget.PlatformTag),
                Csv(RedactUrl(IndexUrlTextBox.Text.Trim())),
                Csv(row.Message),
                Csv(pythonVersion),
                Csv(pipVersion),
                Csv(row.PipCommand)
            });
        }
    }

    private string BuildInstallScript()
    {
        var partial = _reportRows.Any(row => row.Status is "Failed" or "Canceled");
        if (_currentTarget.TargetOs == "Linux")
        {
            return $"""
                #!/usr/bin/env bash
                set -euo pipefail
                cd "$(dirname "$0")"
                {(partial ? "echo \"WARNING: This bundle was generated with failed or canceled package entries. Review download-report.csv before installing.\"" : string.Empty)}
                python3 -m pip install --no-index --find-links ./packages -r ./requirements.lock.txt
                """;
        }

        return $"""
            $ErrorActionPreference = "Stop"
            Set-Location -LiteralPath $PSScriptRoot
            {(partial ? "Write-Warning \"This bundle was generated with failed or canceled package entries. Review download-report.csv before installing.\"" : string.Empty)}
            python -m pip install --no-index --find-links .\packages -r .\requirements.lock.txt
            """;
    }

    private static PythonPath FindPythonExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "runtime", "python", "python.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "runtime", "python", "python.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new PythonPath(candidate, PythonSource.Bundled);
            }
        }

        return new PythonPath("python", PythonSource.DevelopmentFallback);
    }

    private async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); Dispatcher.Invoke(() => AppendLog(e.Data)); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); Dispatcher.Invoke(() => AppendLog(e.Data)); } };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, stdout.ToString(), ex.Message);
        }

        return new ProcessResult(process.HasExited ? process.ExitCode : -1, stdout.ToString(), stderr.ToString());
    }

    private static async Task<string> TryGetProcessOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var result = await RunProcessForOutputAsync(fileName, arguments, cancellationToken);
        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError
            : result.StandardOutput;
    }

    private static async Task<ProcessResult> RunProcessForOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessResult(-1, string.Empty, "Process did not start.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cancellation.
        }
    }

    private void AddReportRow(PackageRequest request, string status, string resolvedVersion, string fileName, string message, string pipCommand)
    {
        _reportRows.Add(new ReportRow
        {
            Package = request.PackageName,
            RequestedSpec = request.OriginalSpec,
            ResolvedVersion = resolvedVersion,
            Status = status,
            FileName = fileName,
            TargetSummary = $"{_currentTarget.TargetOs}/{_currentTarget.PythonVersion}/{_currentTarget.Architecture}",
            Message = message,
            PipCommand = pipCommand
        });
    }

    private void UpdateOverallStatus()
    {
        if (_reportRows.Count == 0)
        {
            OverallStatusTextBlock.Text = "Ready";
            return;
        }

        var failed = _reportRows.Count(row => row.Status == "Failed");
        var canceled = _reportRows.Count(row => row.Status == "Canceled");
        var successful = _reportRows.Count(row => row.Status is "Downloaded" or "Skipped" or "Overwritten");

        OverallStatusTextBlock.Text = canceled > 0
            ? "Canceled"
            : failed == 0 ? "Complete"
            : successful > 0 ? "Partial Success"
            : "Failed";
    }

    private void UpdateCounts()
    {
        CountStatusTextBlock.Text =
            $"Requested {_reportRows.Count} | Downloaded {_reportRows.Count(r => r.Status == "Downloaded")} | Skipped {_reportRows.Count(r => r.Status == "Skipped")} | Failed {_reportRows.Count(r => r.Status == "Failed")}";
    }

    private void SetBusy(bool isBusy)
    {
        DownloadButton.IsEnabled = !isBusy;
        CancelButton.IsEnabled = isBusy;
        RetryFailedButton.IsEnabled = !isBusy && _lastFailedRequests.Count > 0;
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private static HashSet<string> SnapshotWheelFiles(string outputFolder)
    {
        var packageFolder = Path.Combine(outputFolder, "packages");
        if (!Directory.Exists(packageFolder))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return Directory.GetFiles(packageFolder, "*.whl")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static WheelInfo FindBestWheelForPackage(string outputFolder, string packageName, IReadOnlyCollection<string> changedWheels)
    {
        var packageFolder = Path.Combine(outputFolder, "packages");
        var candidates = changedWheels.Count > 0
            ? changedWheels
            : Directory.Exists(packageFolder) ? Directory.GetFiles(packageFolder, "*.whl").Select(Path.GetFileName).Where(x => x is not null).Select(x => x!) : [];

        foreach (var fileName in candidates)
        {
            var parsed = ParseWheelFile(fileName);
            if (string.Equals(NormalizePackageName(parsed.Package), packageName, StringComparison.OrdinalIgnoreCase))
            {
                return new WheelInfo(fileName, parsed.Version);
            }
        }

        var first = candidates.FirstOrDefault() ?? string.Empty;
        return first.Length == 0 ? new WheelInfo(string.Empty, string.Empty) : new WheelInfo(first, ParseWheelFile(first).Version);
    }

    private static WheelInfo ParseWheelFile(string fileName)
    {
        var match = WheelNameRegex.Match(fileName);
        return match.Success
            ? new WheelInfo(match.Groups["name"].Value.Replace('_', '-'), match.Groups["version"].Value)
            : new WheelInfo(string.Empty, string.Empty);
    }

    private static string SummarizeFailure(ProcessResult result)
    {
        var text = string.Join(Environment.NewLine, result.StandardError, result.StandardOutput)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            ?? $"pip exited with code {result.ExitCode}.";
        return text.Length > 260 ? text[..260] : text;
    }

    private static string NormalizePackageName(string packageName)
    {
        return Regex.Replace(packageName.Trim(), "[-_.]+", "-").ToLowerInvariant();
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string RedactCommand(string fileName, string arguments)
    {
        return $"{fileName} {RedactUrl(arguments)}";
    }

    private static string RedactUrl(string value)
    {
        return Regex.Replace(value, @"(https?://)([^/@\s""]+):([^/@\s""]+)@", "$1$2:***@", RegexOptions.IgnoreCase);
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var outputFolder = OutputFolderTextBox.Text.Trim();
        Directory.CreateDirectory(outputFolder);
        Process.Start(new ProcessStartInfo(outputFolder) { UseShellExecute = true });
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        var reportPath = Path.Combine(OutputFolderTextBox.Text.Trim(), "download-report.csv");
        if (File.Exists(reportPath))
        {
            Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        }
    }

    private void CopyInstallCommand_Click(object sender, RoutedEventArgs e)
    {
        var command = _currentTarget.TargetOs == "Linux"
            ? "python3 -m pip install --no-index --find-links ./packages -r ./requirements.lock.txt"
            : "python -m pip install --no-index --find-links .\\packages -r .\\requirements.lock.txt";
        Clipboard.SetText(command);
    }

    private static string? DetectCurrentPythonVersion()
    {
        try
        {
            var path = FindPythonExecutable();
            var result = RunProcessForOutputAsync(path.Path, "--version", CancellationToken.None).GetAwaiter().GetResult();
            var match = Regex.Match(result.StandardOutput + result.StandardError, @"Python\s+(\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record PackageRequest(int LineNumber, string OriginalSpec, string PackageName, string? ExactVersion);

public sealed record DownloadTarget(string TargetOs, string PythonVersion, string Architecture, string PlatformTag, string Abi);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed record WheelInfo(string FileName, string Version)
{
    public string Package => FileName.Length == 0 ? string.Empty : FileName.Split('-').FirstOrDefault() ?? string.Empty;
}

public sealed record PythonPath(string Path, PythonSource Source);

public enum PythonSource
{
    Bundled,
    DevelopmentFallback
}

public sealed class ReportRow
{
    public string Package { get; set; } = string.Empty;
    public string RequestedSpec { get; set; } = string.Empty;
    public string ResolvedVersion { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string TargetSummary { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string PipCommand { get; set; } = string.Empty;
}
