using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sentry;
using SymbolCollector.Core;
using static System.Console;

namespace SymbolCollector.Console
{
    internal class Program
    {
        private const string Dsn = "https://02619ad38bcb40d0be5167e1fb335954@sentry.io/1847454";

        private static readonly ClientMetrics _metrics = new ClientMetrics();


        static async Task Main(
            string? upload = null,
            string? check = null,
            string? package = null,
            string? symsorter = null,
            string? bundleId = null,
            string? batchType = null,
            Uri? serverEndpoint = null)
        {
            var userAgent = "Console/" + typeof(Program).Assembly.GetName().Version;
            using var _ = SentrySdk.Init(o =>
            {
                o.Debug = true;
                o.DiagnosticsLevel = Sentry.Protocol.SentryLevel.Warning;
                o.AttachStacktrace = true;
                o.SendDefaultPii = true;
                o.AddInAppExclude("Polly");
                o.Dsn = new Dsn(Dsn);
                // TODO: This needs to be built-in
                o.BeforeSend += @event =>
                {
                    const string traceIdKey = "TraceIdentifier";
                    switch (@event.Exception)
                    {
                        case var e when e is OperationCanceledException:
                            return null;
                        case var e when e?.Data.Contains(traceIdKey) == true:
                            @event.SetTag(traceIdKey, e.Data[traceIdKey]?.ToString() ?? "unknown");
                            break;
                    }

                    return @event;
                };
            });
            {
                SentrySdk.ConfigureScope(s =>
                {
                    s.SetTag("app", typeof(Program).Assembly.GetName().Name);
                    s.SetTag("user-agent", userAgent);
                    if (serverEndpoint is {})
                    {
                        s.SetTag("server-endpoint", serverEndpoint.AbsoluteUri);
                    }

                    s.SetExtra("parameters", new
                    {
                        upload,
                        check,
                        package,
                        symsorter,
                        bundleId,
                        batchType,
                        endpoint = serverEndpoint
                    });
                });
            }

            var cancellation = new CancellationTokenSource();
            CancelKeyPress += (s, ev) =>
            {
                _metrics.Write(Out);
                WriteLine("Shutting down.");
                ev.Cancel = false;
                cancellation.Cancel();
            };

            try
            {
                using var host = Startup.Init(s =>
                {
                    if (serverEndpoint != null)
                    {
                        s.AddOptions()
                            .PostConfigure<SymbolClientOptions>(o =>
                            {
                                o.UserAgent = userAgent;
                                o.BaseAddress = serverEndpoint;
                            });
                    }

                    s.AddSingleton(_metrics);
                    s.AddSingleton<ConsoleUploader>();
                });

                var logger = host.Services.GetRequiredService<ILogger<Program>>();

                switch (upload)
                {
                    case "device":
                        if (bundleId is null)
                        {
                            WriteLine("A 'bundleId' is required to upload symbols from this device.");
                            return;
                        }

                        SentrySdk.ConfigureScope(s =>
                        {
                            s.SetTag("friendly-name", bundleId);
                        });

                        logger.LogInformation("Uploading images from this device.");
                        var uploader = host.Services.GetRequiredService<ConsoleUploader>();
                        await uploader.StartUploadSymbols(bundleId, cancellation.Token);
                        return;
                    case "package":
                        if (package is null || batchType is null || bundleId is null)
                        {
                            WriteLine(@"Missing required parameters:
--bundle-id MacOS_15.11
--batch-type macos
--package path/to/package.dmg");
                            return;
                        }

                        logger.LogInformation("Uploading stuff from package: '{package}'.", package);
                        // TODO:
                        break;
                }

                var parser = host.Services.GetRequiredService<ObjectFileParser>();

                if (check is { } checkLib)
                {
                    if (!File.Exists(check))
                    {
                        WriteLine($"File to check '{checkLib}' doesn't exist.");
                        return;
                    }

                    logger.LogInformation("Checking '{checkLib}'.", checkLib);
                    if (parser.TryParse(checkLib, out var result) && result is {})
                    {
                        if (result is FatMachOFileResult fatMachOFileResult)
                        {
                            WriteLine($"Fat Mach-O File:");
                            Print(fatMachOFileResult);
                            foreach (var innerFile in fatMachOFileResult.InnerFiles)
                            {
                                WriteLine("Inner file:");
                                Print(innerFile);
                            }
                        }
                        else
                        {
                            Print(result);
                        }

                        static void Print(ObjectFileResult r)
                            => WriteLine($@"
Path: {r.Path}
CodeId: {r.CodeId}
DebugId: {r.DebugId}
BuildId: {r.UnifiedId}
BuildIdType: {r.BuildIdType}
File hash: {r.Hash}
File Format: {r.FileFormat}
Architecture: {r.Architecture}
ObjectKind: {r.ObjectKind}
");
                    }
                    else
                    {
                        WriteLine($"Failed to parse {checkLib}.");
                    }

                    return;
                }

                if (symsorter is { })
                {
                    if (string.IsNullOrWhiteSpace(bundleId))
                    {
                        WriteLine("Missing bundle Id");
                        return;
                    }

                    if (!Directory.Exists(symsorter))
                    {
                        WriteLine($"Directory '{symsorter}' doesn't exist.");
                        return;
                    }

                    foreach (var file in Directory.GetFiles(symsorter, "*", SearchOption.AllDirectories))
                    {
                        if (parser.TryParse(file, out var result) && result is {})
                        {
                            if (result is FatMachOFileResult fatMachOFileResult)
                            {
                                foreach (var innerFile in fatMachOFileResult.InnerFiles)
                                {
                                    WriteLine($"{innerFile.DebugId[..2]}/{innerFile.DebugId[2..].Replace("-", "")}");
                                }
                            }
                            else
                            {
                                if (result.FileFormat == FileFormat.Elf)
                                {
                                    WriteLine($"{result.CodeId[..2]}/{result.CodeId[2..].Replace("-", "")}");
                                }
                                else
                                {
                                    WriteLine($"{result.DebugId[..2]}/{result.DebugId[2..].Replace("-", "")}");
                                }
                            }
                        }
                    }

                    return;
                }

                WriteLine(@"Parameters:
--upload device --bundle-id id
--upload package --bundle-id id --batch-type type --package ~/location
    Valid Batch Types are: android, macos, ios, watchos, android
--check file-to-check");
            }
            catch (Exception e)
            {
                WriteLine(e);
                SentrySdk.CaptureException(e);
            }
            finally
            {
                await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
            }
        }
    }
}
