using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfflinePackageDownloader;

public static class SmokeRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetOption(args, "--output") ?? Path.Combine(Path.GetTempPath(), "OfflinePackageDownloader-Smoke", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var mode = GetOption(args, "--mode") ?? "preview";
        var providerFilter = GetOption(args, "--provider");
        Directory.CreateDirectory(root);

        var registry = new ProviderRegistry();
        var providers = registry.Providers.Where(p => providerFilter == null || string.Equals(p.Definition.Id, providerFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var failures = new List<string>();

        foreach (var provider in providers)
        {
            if (provider.Definition.Id == "vscode-extension")
            {
                var searchResults = await MarketplaceSearchClient.SearchAsync("git", 5, CancellationToken.None);
                Console.WriteLine($"SMOKE SEARCH vscode-extension results={searchResults.Count}");
                if (searchResults.Count == 0 || searchResults.All(result => string.IsNullOrWhiteSpace(result.ExtensionId)))
                {
                    failures.Add("vscode-extension:SearchFailed");
                    continue;
                }
            }

            var providerOutput = CommonOutput.ProviderOutputFolder(root, provider.Definition.Id);
            var request = new ProviderRunRequest(
                provider.Definition.Id,
                SmokeRequests(provider.Definition.Id),
                SmokeTarget(provider.Definition.Id),
                providerOutput,
                false,
                mode.Equals("preview", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"SMOKE START {provider.Definition.Id} mode={mode} output={providerOutput}");
            var progress = new Progress<DownloadRecord>(r => Console.WriteLine($"{r.ProviderId},{r.Kind},{r.Name},{r.Version},{r.Status},{CommonOutput.Redact(r.Message)}"));
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(provider.Definition.Id == "ubuntu" ? 8 : 4));
                var result = await provider.RunAsync(request, progress, cts.Token);
                CommonOutput.WriteCommonFiles(request, result);
                Console.WriteLine($"SMOKE RESULT {provider.Definition.Id} {result.OverallStatus} records={result.Records.Count}");
                Console.WriteLine($"SMOKE OUTPUT {result.OutputFolder}");
                if (result.OverallStatus is "Failed" or "Canceled")
                {
                    failures.Add($"{provider.Definition.Id}:{result.OverallStatus}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SMOKE ERROR {provider.Definition.Id} {ex}");
                failures.Add($"{provider.Definition.Id}:{ex.GetType().Name}");
            }
        }

        if (failures.Count > 0)
        {
            Console.WriteLine("SMOKE FAIL " + string.Join(";", failures));
            return 1;
        }

        Console.WriteLine($"SMOKE PASS output={root}");
        return 0;
    }

    private static string SmokeRequests(string providerId) => providerId switch
    {
        "nuget" => "Microsoft.Extensions.Configuration.Json 8.0.0",
        "python" => "requests==2.32.3",
        "vscode-extension" => "njpwerner.autodocstring",
        "ubuntu" => "hello",
        _ => string.Empty
    };

    private static string SmokeTarget(string providerId) => providerId switch
    {
        "nuget" => "source=https://api.nuget.org/v3/index.json\ntargetFramework=net8.0\nmaxParallelism=5",
        "python" => "python=python\nindexUrl=https://pypi.org/simple",
        "vscode-extension" => "vscodeVersion=1.91.0\ntargetPlatform=win32-x64",
        "ubuntu" => "version=noble\narchitecture=amd64\ncomponents=main\npockets=release\nbaseUrl=http://archive.ubuntu.com/ubuntu\nmaxPackages=20",
        _ => string.Empty
    };

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
