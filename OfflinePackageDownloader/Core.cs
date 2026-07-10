using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OfflinePackageDownloader;

public sealed record ProviderDefinition(string Id, string DisplayName, string Description, string DefaultRequests, string DefaultTarget);

public sealed record ProviderRunRequest(
    string ProviderId,
    string RequestText,
    string TargetText,
    string OutputFolder,
    bool OverwriteExisting,
    bool PreviewOnly);

public sealed class DownloadRecord : INotifyPropertyChanged
{
    private string status = "Pending";
    private string message = string.Empty;
    private string fileName = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Kind { get; init; } = "Requested";
    public string Source { get; set; } = string.Empty;

    public string Status
    {
        get => status;
        set => SetField(ref status, value);
    }

    public string Message
    {
        get => message;
        set => SetField(ref message, value);
    }

    public string FileName
    {
        get => fileName;
        set => SetField(ref fileName, value);
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

public sealed class ProviderRunResult
{
    public required string ProviderId { get; init; }
    public required string OutputFolder { get; init; }
    public List<DownloadRecord> Records { get; } = new();
    public List<string> GeneratedFiles { get; } = new();
    public List<string> Warnings { get; } = new();

    public string OverallStatus
    {
        get
        {
            if (Records.Count == 0)
            {
                return "Failed";
            }

            if (Records.Any(r => r.Status == "Canceled"))
            {
                return "Canceled";
            }

            if (Records.All(r => r.Status is "Downloaded" or "Skipped" or "Resolved"))
            {
                return "Complete";
            }

            if (Records.Any(r => r.Status is "Downloaded" or "Skipped" or "Resolved"))
            {
                return "PartialSuccess";
            }

            return "Failed";
        }
    }
}

public interface IOfflinePackageProvider
{
    ProviderDefinition Definition { get; }
    Task<ProviderRunResult> RunAsync(ProviderRunRequest request, IProgress<DownloadRecord> progress, CancellationToken cancellationToken);
}

public sealed class ProviderRegistry
{
    private readonly IReadOnlyList<IOfflinePackageProvider> providers;

    public ProviderRegistry()
    {
        providers = new IOfflinePackageProvider[]
        {
            new NuGetProvider(),
            new PythonProvider(),
            new VSCodeExtensionProvider(),
            new UbuntuProvider()
        };
    }

    public IReadOnlyList<IOfflinePackageProvider> Providers => providers;

    public IOfflinePackageProvider Get(string providerId)
    {
        return providers.First(p => string.Equals(p.Definition.Id, providerId, StringComparison.OrdinalIgnoreCase));
    }
}

public static class CommonOutput
{
    public static void WriteCommonFiles(ProviderRunRequest request, ProviderRunResult result)
    {
        Directory.CreateDirectory(result.OutputFolder);
        WriteReport(result);
        WriteManifest(request, result);
    }

    public static string ProviderOutputFolder(string root, string providerId)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(root, providerId, stamp);
    }

    public static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = Regex.Replace(value, @"://([^:/\s]+):([^@/\s]+)@", "://***:***@", RegexOptions.CultureInvariant);
        redacted = Regex.Replace(redacted, @"(?i)(token|apikey|api_key|password|passwd|pwd)=([^&\s]+)", "$1=***", RegexOptions.CultureInvariant);
        return redacted;
    }

    private static void WriteReport(ProviderRunResult result)
    {
        var path = Path.Combine(result.OutputFolder, "download-report.csv");
        var lines = new List<string> { "provider,name,version,kind,status,file_name,source,message" };
        lines.AddRange(result.Records.Select(r => string.Join(",", new[]
        {
            EscapeCsv(r.ProviderId),
            EscapeCsv(r.Name),
            EscapeCsv(r.Version),
            EscapeCsv(r.Kind),
            EscapeCsv(r.Status),
            EscapeCsv(r.FileName),
            EscapeCsv(Redact(r.Source)),
            EscapeCsv(Redact(r.Message))
        })));
        File.WriteAllLines(path, lines, Encoding.UTF8);
        AddGenerated(result, path);
    }

    private static void WriteManifest(ProviderRunRequest request, ProviderRunResult result)
    {
        var manifest = new
        {
            schemaVersion = "1.0",
            appVersion = "0.1.0",
            providerId = result.ProviderId,
            providerVersion = "0.1.0",
            createdAtUtc = DateTimeOffset.UtcNow,
            target = ParseKeyValueText(request.TargetText).ToDictionary(k => k.Key, v => Redact(v.Value)),
            requested = request.RequestText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Redact(x.Trim())).ToArray(),
            resolved = result.Records.Select(r => new
            {
                r.ProviderId,
                r.Name,
                r.Version,
                r.Kind,
                r.Status,
                r.FileName,
                Source = Redact(r.Source),
                Message = Redact(r.Message)
            }).ToArray(),
            outputs = result.GeneratedFiles.Select(p => Path.GetRelativePath(result.OutputFolder, p)).OrderBy(p => p).ToArray(),
            summary = new
            {
                downloaded = result.Records.Count(r => r.Status == "Downloaded"),
                skipped = result.Records.Count(r => r.Status == "Skipped"),
                failed = result.Records.Count(r => r.Status == "Failed"),
                canceled = result.Records.Count(r => r.Status == "Canceled"),
                overallStatus = result.OverallStatus
            },
            warnings = result.Warnings.Select(Redact).ToArray()
        };

        var path = Path.Combine(result.OutputFolder, "bundle.manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions()), Encoding.UTF8);
        AddGenerated(result, path);
    }

    public static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

    public static Dictionary<string, string> ParseKeyValueText(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var index = line.IndexOf('=');
            if (index < 0)
            {
                values[line] = "true";
                continue;
            }

            values[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        return values;
    }

    public static void AddGenerated(ProviderRunResult result, string path)
    {
        if (!result.GeneratedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            result.GeneratedFiles.Add(path);
        }
    }
}

public static class ProcessRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, (await stdout) + (await stderr));
    }
}
