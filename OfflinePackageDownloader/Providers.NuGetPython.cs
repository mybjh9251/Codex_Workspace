using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OfflinePackageDownloader;

public sealed class NuGetProvider : IOfflinePackageProvider
{
    public ProviderDefinition Definition { get; } = new(
        "nuget",
        "NuGet",
        "Download NuGet packages and transitive dependencies into an offline local feed.",
        "Microsoft.Extensions.Configuration.Json 8.0.0",
        "source=https://api.nuget.org/v3/index.json\ntargetFramework=net8.0\nmaxParallelism=5");

    public async Task<ProviderRunResult> RunAsync(ProviderRunRequest request, IProgress<DownloadRecord> progress, CancellationToken cancellationToken)
    {
        var settings = CommonOutput.ParseKeyValueText(request.TargetText);
        var source = settings.GetValueOrDefault("source", "https://api.nuget.org/v3/index.json");
        var targetFramework = settings.GetValueOrDefault("targetFramework", "net8.0");
        var maxParallelism = int.TryParse(settings.GetValueOrDefault("maxParallelism", "5"), out var parsed) ? parsed : 5;
        var output = request.OutputFolder;
        var packagesFolder = Path.Combine(output, "packages");
        Directory.CreateDirectory(packagesFolder);

        var result = new ProviderRunResult { ProviderId = Definition.Id, OutputFolder = output };
        var roots = ParseRequests(request.RequestText).ToList();
        var repo = Repository.Factory.GetCoreV3(source);
        using var cache = new SourceCacheContext();
        var framework = NuGetFramework.ParseFolder(targetFramework);
        var dependencyResource = await repo.GetResourceAsync<DependencyInfoResource>(cancellationToken);
        var findResource = await repo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var resolved = new ConcurrentDictionary<string, DownloadRecord>(StringComparer.OrdinalIgnoreCase);
        var queue = new ConcurrentQueue<DownloadRecord>();

        foreach (var root in roots)
        {
            var record = new DownloadRecord { ProviderId = Definition.Id, Name = root.Id, Version = root.Version, Kind = "Requested", Source = source, Status = "Pending" };
            if (resolved.TryAdd(Key(root.Id, root.Version), record))
            {
                queue.Enqueue(record);
                result.Records.Add(record);
                progress.Report(record);
            }
        }

        while (queue.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            current.Status = "Resolving";
            progress.Report(current);
            try
            {
                var identity = new PackageIdentity(current.Name, NuGetVersion.Parse(current.Version));
                var metadata = await dependencyResource.ResolvePackage(identity, framework, cache, NullLogger.Instance, cancellationToken);
                if (metadata == null)
                {
                    current.Status = "Failed";
                    current.Message = "Package metadata not found";
                    progress.Report(current);
                    continue;
                }

                foreach (var dependency in metadata.Dependencies)
                {
                    var versions = await findResource.GetAllVersionsAsync(dependency.Id, cache, NullLogger.Instance, cancellationToken);
                    var candidates = versions.Where(dependency.VersionRange.Satisfies);
                    if (!AllowsPrerelease(dependency.VersionRange))
                    {
                        candidates = candidates.Where(v => !v.IsPrerelease);
                    }

                    var best = candidates.OrderBy(v => v).FirstOrDefault();
                    if (best == null)
                    {
                        result.Warnings.Add($"Could not resolve dependency {dependency.Id} {dependency.VersionRange}");
                        continue;
                    }

                    var depRecord = new DownloadRecord { ProviderId = Definition.Id, Name = dependency.Id, Version = best.ToNormalizedString(), Kind = "Dependency", Source = source, Status = "Pending" };
                    if (resolved.TryAdd(Key(depRecord.Name, depRecord.Version), depRecord))
                    {
                        queue.Enqueue(depRecord);
                        result.Records.Add(depRecord);
                        progress.Report(depRecord);
                    }
                }

                current.Status = "Resolved";
                current.Message = $"{metadata.Dependencies.Count()} dependency item(s)";
                progress.Report(current);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                current.Status = "Failed";
                current.Message = ex.Message;
                progress.Report(current);
            }
        }

        if (!request.PreviewOnly)
        {
            await ParallelForEachAsync(result.Records.Where(r => r.Status != "Failed"), maxParallelism, async record =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = $"{record.Name}.{record.Version}.nupkg";
                var filePath = Path.Combine(packagesFolder, fileName);
                if (File.Exists(filePath) && !request.OverwriteExisting)
                {
                    record.Status = "Skipped";
                    record.FileName = Path.Combine("packages", fileName);
                    record.Message = "Already exists";
                    progress.Report(record);
                    return;
                }

                record.Status = "Downloading";
                progress.Report(record);
                await using var stream = new MemoryStream();
                var success = await findResource.CopyNupkgToStreamAsync(record.Name, NuGetVersion.Parse(record.Version), stream, cache, NullLogger.Instance, cancellationToken);
                if (!success)
                {
                    record.Status = "Failed";
                    record.Message = "Download failed";
                    progress.Report(record);
                    return;
                }

                await File.WriteAllBytesAsync(filePath, stream.ToArray(), cancellationToken);
                record.Status = "Downloaded";
                record.FileName = Path.Combine("packages", fileName);
                record.Message = fileName;
                progress.Report(record);
            });
        }

        WriteNuGetConfig(packagesFolder, result);
        WriteProviderLock(result, "nuget-lock.json");
        return result;
    }

    private static IEnumerable<(string Id, string Version)> ParseRequests(string text)
    {
        foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new InvalidOperationException($"NuGet request requires 'id version': {line}");
            yield return (parts[0], parts[1]);
        }
    }

    private static string Key(string id, string version) => $"{id}/{version}";

    private static bool AllowsPrerelease(VersionRange range) => (range.MinVersion?.IsPrerelease ?? false) || (range.MaxVersion?.IsPrerelease ?? false) || (range.OriginalString?.Contains('-', StringComparison.Ordinal) ?? false);

    private static void WriteNuGetConfig(string packagesFolder, ProviderRunResult result)
    {
        var path = Path.Combine(result.OutputFolder, "nuget.config");
        var xml = $"""
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="offline-bundle" value="{System.Security.SecurityElement.Escape(Path.GetFullPath(packagesFolder))}" />
  </packageSources>
</configuration>
""";
        File.WriteAllText(path, xml, Encoding.UTF8);
        CommonOutput.AddGenerated(result, path);
    }

    internal static async Task ParallelForEachAsync<T>(IEnumerable<T> items, int maxParallelism, Func<T, Task> action)
    {
        using var semaphore = new SemaphoreSlim(Math.Max(1, maxParallelism));
        await Task.WhenAll(items.Select(async item =>
        {
            await semaphore.WaitAsync();
            try { await action(item); }
            finally { semaphore.Release(); }
        }));
    }

    internal static void WriteProviderLock(ProviderRunResult result, string fileName)
    {
        var path = Path.Combine(result.OutputFolder, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(result.Records.Select(r => new
        {
            r.ProviderId,
            r.Name,
            r.Version,
            r.Kind,
            r.Status,
            r.FileName,
            Source = CommonOutput.Redact(r.Source),
            Message = CommonOutput.Redact(r.Message)
        }), CommonOutput.JsonOptions()), Encoding.UTF8);
        CommonOutput.AddGenerated(result, path);
    }
}

public sealed class PythonProvider : IOfflinePackageProvider
{
    public ProviderDefinition Definition { get; } = new(
        "python",
        "Python",
        "Run pip download for Python wheel/offline install bundles.",
        "requests",
        "python=python\nindexUrl=https://pypi.org/simple\nplatform=\npythonVersion=\nimplementation=cp\nabi=");

    public async Task<ProviderRunResult> RunAsync(ProviderRunRequest request, IProgress<DownloadRecord> progress, CancellationToken cancellationToken)
    {
        var settings = CommonOutput.ParseKeyValueText(request.TargetText);
        var python = settings.GetValueOrDefault("python", "python");
        var indexUrl = settings.GetValueOrDefault("indexUrl", "https://pypi.org/simple");
        var platform = settings.GetValueOrDefault("platform", string.Empty);
        var pythonVersion = settings.GetValueOrDefault("pythonVersion", string.Empty);
        var implementation = settings.GetValueOrDefault("implementation", "cp");
        var abi = settings.GetValueOrDefault("abi", string.Empty);
        var packagesFolder = Path.Combine(request.OutputFolder, "packages");
        Directory.CreateDirectory(packagesFolder);
        var result = new ProviderRunResult { ProviderId = Definition.Id, OutputFolder = request.OutputFolder };

        foreach (var requirement in RequestLines(request.RequestText))
        {
            var record = new DownloadRecord { ProviderId = Definition.Id, Name = requirement, Kind = "Requested", Source = indexUrl, Status = request.PreviewOnly ? "Resolved" : "Pending" };
            result.Records.Add(record);
            progress.Report(record);
            if (request.PreviewOnly) continue;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                record.Status = "Downloading";
                progress.Report(record);
                var args = new StringBuilder($"-m pip download {Quote(requirement)} --dest {Quote(packagesFolder)} --index-url {Quote(indexUrl)} --only-binary=:all:");
                if (!string.IsNullOrWhiteSpace(platform)) args.Append($" --platform {Quote(platform)}");
                if (!string.IsNullOrWhiteSpace(pythonVersion)) args.Append($" --python-version {Quote(pythonVersion)}");
                if (!string.IsNullOrWhiteSpace(platform)) args.Append($" --implementation {Quote(implementation)}");
                if (!string.IsNullOrWhiteSpace(abi)) args.Append($" --abi {Quote(abi)}");
                if (request.OverwriteExisting) args.Append(" --exists-action=w");
                var before = Directory.GetFiles(packagesFolder).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var run = await ProcessRunner.RunAsync(python, args.ToString(), request.OutputFolder, cancellationToken);
                var after = Directory.GetFiles(packagesFolder).Where(f => !before.Contains(f)).ToList();
                if (run.ExitCode == 0)
                {
                    record.Status = after.Count == 0 ? "Skipped" : "Downloaded";
                    record.FileName = after.Count == 0 ? string.Empty : Path.Combine("packages", Path.GetFileName(after[0]));
                    record.Message = after.Count == 0 ? "No new file; pip may have reused existing files" : "pip download succeeded";
                }
                else
                {
                    record.Status = "Failed";
                    record.Message = run.Output.Trim();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                record.Status = "Failed";
                record.Message = ex.Message;
            }

            progress.Report(record);
        }

        WriteRequirementsLock(packagesFolder, result);
        WriteInstallScripts(result);
        NuGetProvider.WriteProviderLock(result, "python-lock.json");
        return result;
    }

    private static IEnumerable<string> RequestLines(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#'));
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void WriteRequirementsLock(string packagesFolder, ProviderRunResult result)
    {
        var path = Path.Combine(result.OutputFolder, "requirements.lock.txt");
        var lines = Directory.Exists(packagesFolder) ? Directory.GetFiles(packagesFolder, "*.whl").Select(Path.GetFileName).OrderBy(x => x).ToArray() : Array.Empty<string?>();
        File.WriteAllLines(path, lines!, Encoding.UTF8);
        CommonOutput.AddGenerated(result, path);
    }

    private static void WriteInstallScripts(ProviderRunResult result)
    {
        var ps1 = Path.Combine(result.OutputFolder, "install-offline.ps1");
        File.WriteAllText(ps1, "python -m pip install --no-index --find-links .\\packages -r .\\requirements.lock.txt\n", Encoding.UTF8);
        CommonOutput.AddGenerated(result, ps1);
        var sh = Path.Combine(result.OutputFolder, "install-offline.sh");
        File.WriteAllText(sh, "#!/usr/bin/env bash\npython3 -m pip install --no-index --find-links ./packages -r ./requirements.lock.txt\n", Encoding.UTF8);
        CommonOutput.AddGenerated(result, sh);
    }
}
