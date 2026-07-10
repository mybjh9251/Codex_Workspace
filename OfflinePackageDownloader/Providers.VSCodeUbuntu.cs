using System;
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

public sealed class VSCodeExtensionProvider : IOfflinePackageProvider
{
    private static readonly HttpClient Client = new();

    public ProviderDefinition Definition { get; } = new(
        "vscode-extension",
        "VS Code Extension",
        "Download Visual Studio Marketplace VSIX files for offline VS Code installation.",
        "ms-python.python",
        "vscodeVersion=1.91.0\ntargetPlatform=win32-x64");

    public async Task<ProviderRunResult> RunAsync(ProviderRunRequest request, IProgress<DownloadRecord> progress, CancellationToken cancellationToken)
    {
        var settings = CommonOutput.ParseKeyValueText(request.TargetText);
        var targetPlatform = settings.GetValueOrDefault("targetPlatform", "win32-x64");
        var vscodeVersion = settings.GetValueOrDefault("vscodeVersion", "1.91.0");
        var output = Path.Combine(request.OutputFolder, "extensions");
        Directory.CreateDirectory(output);
        var result = new ProviderRunResult { ProviderId = Definition.Id, OutputFolder = request.OutputFolder };
        var queue = new Queue<(string Id, string Kind)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in RequestLines(request.RequestText))
        {
            queue.Enqueue((id, "Requested"));
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (extensionId, kind) = queue.Dequeue();
            if (!seen.Add(extensionId)) continue;
            var record = new DownloadRecord { ProviderId = Definition.Id, Name = extensionId, Kind = kind, Source = "https://marketplace.visualstudio.com", Status = "Resolving" };
            result.Records.Add(record);
            progress.Report(record);

            try
            {
                var metadata = await QueryExtensionAsync(extensionId, cancellationToken);
                if (metadata == null)
                {
                    record.Status = "Failed";
                    record.Message = "Extension metadata not found";
                    progress.Report(record);
                    continue;
                }

                record.Version = metadata.Version;
                foreach (var dependency in metadata.Dependencies)
                {
                    if (!seen.Contains(dependency)) queue.Enqueue((dependency, "Dependency"));
                }

                foreach (var packItem in metadata.ExtensionPack)
                {
                    if (!seen.Contains(packItem)) queue.Enqueue((packItem, "ExtensionPack"));
                }

                if (request.PreviewOnly)
                {
                    record.Status = "Resolved";
                    record.Message = $"VS Code {vscodeVersion}, target {targetPlatform}";
                    progress.Report(record);
                    continue;
                }

                var safeId = extensionId.Replace('.', '-');
                var fileName = $"{safeId}-{metadata.Version}-{targetPlatform}.vsix";
                var path = Path.Combine(output, fileName);
                if (File.Exists(path) && !request.OverwriteExisting)
                {
                    record.Status = "Skipped";
                    record.FileName = Path.Combine("extensions", fileName);
                    record.Message = "Already exists";
                    progress.Report(record);
                    continue;
                }

                record.Status = "Downloading";
                progress.Report(record);
                var bytes = await Client.GetByteArrayAsync(metadata.VsixUrl, cancellationToken);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                record.Status = "Downloaded";
                record.FileName = Path.Combine("extensions", fileName);
                record.Message = "VSIX downloaded";
                progress.Report(record);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                record.Status = "Failed";
                record.Message = ex.Message;
                progress.Report(record);
            }
        }

        WriteInstallScripts(result);
        NuGetProvider.WriteProviderLock(result, "vscode-extensions.lock.json");
        return result;
    }

    private static IEnumerable<string> RequestLines(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#'));

    private static async Task<VsixMetadata?> QueryExtensionAsync(string extensionId, CancellationToken cancellationToken)
    {
        var parts = extensionId.Split('.', 2);
        if (parts.Length != 2) return null;
        var body = new
        {
            filters = new[]
            {
                new
                {
                    criteria = new[]
                    {
                        new { filterType = 7, value = extensionId }
                    },
                    pageNumber = 1,
                    pageSize = 1,
                    sortBy = 0,
                    sortOrder = 0
                }
            },
            flags = 914
        };
        using var response = await Client.PostAsJsonAsync("https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery?api-version=7.2-preview.1", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var extension = document.RootElement.GetProperty("results")[0].GetProperty("extensions").EnumerateArray().FirstOrDefault();
        if (extension.ValueKind == JsonValueKind.Undefined) return null;
        var version = extension.GetProperty("versions")[0];
        var versionText = version.GetProperty("version").GetString() ?? "latest";
        var files = version.GetProperty("files").EnumerateArray();
        var vsixUrl = files.FirstOrDefault(f => f.TryGetProperty("assetType", out var a) && string.Equals(a.GetString(), "Microsoft.VisualStudio.Services.VSIXPackage", StringComparison.OrdinalIgnoreCase)).GetProperty("source").GetString();
        var properties = version.TryGetProperty("properties", out var props) ? props.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        var dependencies = SplitProperty(properties, "Microsoft.VisualStudio.Code.ExtensionDependencies");
        var extensionPack = SplitProperty(properties, "Microsoft.VisualStudio.Code.ExtensionPack");
        return vsixUrl == null ? null : new VsixMetadata(versionText, vsixUrl, dependencies, extensionPack);
    }

    private static IReadOnlyList<string> SplitProperty(IEnumerable<JsonElement> properties, string key)
    {
        var value = properties.FirstOrDefault(p => p.TryGetProperty("key", out var k) && k.GetString() == key);
        if (value.ValueKind == JsonValueKind.Undefined || !value.TryGetProperty("value", out var raw)) return Array.Empty<string>();
        return (raw.GetString() ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Contains('.')).ToArray();
    }

    private static void WriteInstallScripts(ProviderRunResult result)
    {
        var records = result.Records.Where(r => r.Status is "Downloaded" or "Skipped" && r.FileName.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase)).ToArray();
        var ps1 = Path.Combine(result.OutputFolder, "install-offline.ps1");
        File.WriteAllLines(ps1, records.Select(r => $"code --install-extension .\\{r.FileName}"), Encoding.UTF8);
        CommonOutput.AddGenerated(result, ps1);
        var sh = Path.Combine(result.OutputFolder, "install-offline.sh");
        File.WriteAllLines(sh, new[] { "#!/usr/bin/env bash" }.Concat(records.Select(r => $"code --install-extension ./{r.FileName.Replace('\\', '/')}")), Encoding.UTF8);
        CommonOutput.AddGenerated(result, sh);
    }

    private sealed record VsixMetadata(string Version, string VsixUrl, IReadOnlyList<string> Dependencies, IReadOnlyList<string> ExtensionPack);
}

public sealed class UbuntuProvider : IOfflinePackageProvider
{
    private static readonly HttpClient Client = new();

    public ProviderDefinition Definition { get; } = new(
        "ubuntu",
        "Ubuntu",
        "Download Ubuntu .deb packages and dependency closure from Packages.gz metadata.",
        "hello",
        "version=noble\narchitecture=amd64\ncomponents=main universe\npockets=release updates security\nbaseUrl=http://archive.ubuntu.com/ubuntu\nmaxPackages=80");

    public async Task<ProviderRunResult> RunAsync(ProviderRunRequest request, IProgress<DownloadRecord> progress, CancellationToken cancellationToken)
    {
        var settings = CommonOutput.ParseKeyValueText(request.TargetText);
        var version = settings.GetValueOrDefault("version", "noble");
        var arch = settings.GetValueOrDefault("architecture", "amd64");
        var components = settings.GetValueOrDefault("components", "main universe").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pockets = settings.GetValueOrDefault("pockets", "release updates security").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var baseUrl = settings.GetValueOrDefault("baseUrl", "http://archive.ubuntu.com/ubuntu").TrimEnd('/');
        var maxPackages = int.TryParse(settings.GetValueOrDefault("maxPackages", "80"), out var parsed) ? parsed : 80;
        var result = new ProviderRunResult { ProviderId = Definition.Id, OutputFolder = request.OutputFolder };
        Directory.CreateDirectory(request.OutputFolder);

        var recordsByName = await LoadPackagesAsync(version, arch, components, pockets, baseUrl, cancellationToken);
        var queue = new Queue<(string Name, string Kind)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in RequestLines(request.RequestText)) queue.Enqueue((item, "Requested"));

        while (queue.Count > 0 && result.Records.Count < maxPackages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (name, kind) = queue.Dequeue();
            if (!seen.Add(name)) continue;
            if (!recordsByName.TryGetValue(name, out var package))
            {
                var missing = new DownloadRecord { ProviderId = Definition.Id, Name = name, Kind = kind, Status = "Failed", Message = "Package metadata not found", Source = baseUrl };
                result.Records.Add(missing);
                progress.Report(missing);
                continue;
            }

            var record = new DownloadRecord { ProviderId = Definition.Id, Name = package.Name, Version = package.Version, Kind = kind, Source = package.Url(baseUrl), Status = request.PreviewOnly ? "Resolved" : "Pending" };
            result.Records.Add(record);
            progress.Report(record);

            foreach (var dep in ParseDependencies(package.DependsRaw).Concat(ParseDependencies(package.PreDependsRaw)))
            {
                if (!seen.Contains(dep)) queue.Enqueue((dep, "Dependency"));
            }
        }

        if (queue.Count > 0)
        {
            result.Warnings.Add($"Dependency closure stopped at maxPackages={maxPackages}.");
        }

        if (!request.PreviewOnly)
        {
            await NuGetProvider.ParallelForEachAsync(result.Records.Where(r => r.Status != "Failed"), 5, async record =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(new Uri(record.Source).LocalPath);
                var path = Path.Combine(request.OutputFolder, fileName);
                if (File.Exists(path) && !request.OverwriteExisting)
                {
                    record.Status = "Skipped";
                    record.FileName = fileName;
                    record.Message = "Already exists";
                    progress.Report(record);
                    return;
                }

                record.Status = "Downloading";
                progress.Report(record);
                var bytes = await Client.GetByteArrayAsync(record.Source, cancellationToken);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                record.Status = "Downloaded";
                record.FileName = fileName;
                record.Message = ".deb downloaded";
                progress.Report(record);
            });
        }

        WriteChecksums(result);
        NuGetProvider.WriteProviderLock(result, "ubuntu-lock.json");
        return result;
    }

    private static IEnumerable<string> RequestLines(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#'));

    private static async Task<Dictionary<string, UbuntuPackage>> LoadPackagesAsync(string version, string arch, IReadOnlyList<string> components, IReadOnlyList<string> pockets, string baseUrl, CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, UbuntuPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var pocket in pockets)
        {
            var suite = pocket.Equals("release", StringComparison.OrdinalIgnoreCase) ? version : $"{version}-{pocket}";
            foreach (var component in components)
            {
                var url = $"{baseUrl}/dists/{suite}/{component}/binary-{arch}/Packages.gz";
                try
                {
                    await using var stream = await Client.GetStreamAsync(url, cancellationToken);
                    using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzip, Encoding.UTF8);
                    var content = await reader.ReadToEndAsync(cancellationToken);
                    foreach (var package in ParsePackages(content, baseUrl))
                    {
                        if (!dict.ContainsKey(package.Name)) dict[package.Name] = package;
                    }
                }
                catch
                {
                    // Some component/pocket combinations are legitimately absent on mirrors.
                }
            }
        }

        return dict;
    }

    private static IEnumerable<UbuntuPackage> ParsePackages(string content, string baseUrl)
    {
        foreach (var stanza in Regex.Split(content, @"\n\s*\n"))
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? current = null;
            foreach (var raw in stanza.Split('\n'))
            {
                if (raw.StartsWith(' ') && current != null)
                {
                    fields[current] += " " + raw.Trim();
                    continue;
                }

                var index = raw.IndexOf(':');
                if (index <= 0) continue;
                current = raw[..index];
                fields[current] = raw[(index + 1)..].Trim();
            }

            if (fields.TryGetValue("Package", out var name) && fields.TryGetValue("Version", out var version) && fields.TryGetValue("Filename", out var filename))
            {
                yield return new UbuntuPackage(name, version, filename, fields.GetValueOrDefault("Depends", string.Empty), fields.GetValueOrDefault("Pre-Depends", string.Empty));
            }
        }
    }

    private static IEnumerable<string> ParseDependencies(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var group in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var first = group.Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(first)) continue;
            first = Regex.Replace(first, @"\s*\([^)]*\)", string.Empty);
            first = Regex.Replace(first, @":\w+", string.Empty);
            first = first.Trim();
            if (Regex.IsMatch(first, "^[a-z0-9][a-z0-9+.-]+$", RegexOptions.IgnoreCase)) yield return first;
        }
    }

    private static void WriteChecksums(ProviderRunResult result)
    {
        var path = Path.Combine(result.OutputFolder, "checksums.sha256");
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var record in result.Records.Where(r => r.Status is "Downloaded" or "Skipped" && r.FileName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)))
        {
            var fullPath = Path.Combine(result.OutputFolder, record.FileName);
            if (!File.Exists(fullPath)) continue;
            var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fullPath));
            writer.WriteLine($"{Convert.ToHexString(hash).ToLowerInvariant()}  {record.FileName}");
        }
        CommonOutput.AddGenerated(result, path);
    }

    private sealed record UbuntuPackage(string Name, string Version, string Filename, string DependsRaw, string PreDependsRaw)
    {
        public string Url(string baseUrl) => $"{baseUrl.TrimEnd('/')}/{Filename}";
    }
}
