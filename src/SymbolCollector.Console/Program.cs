using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sentry;
using SymbolCollector.Core;
using static System.Console;

namespace SymbolCollector.Console
{
    internal class Program
    {
        private const string Dsn = "https://02619ad38bcb40d0be5167e1fb335954@sentry.io/1847454";
        private const string SymbolCollectorServiceUrl = "http://localhost:5000";

        private static Client? _client;

        private static async Task UploadSymbols()
        {
            // TODO: Get the paths via parameter or config file/env var?
            var paths = new List<string> {"/usr/lib/", "/usr/local/lib/"};
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // TODO: Add per OS paths
                paths.Add("/System/Library/Frameworks/");
            }
            else
            {
                paths.Add("/lib/");
            }

            var blackListedPaths = new HashSet<string> {"/usr/lib/cron/tabs"};

            var cancellation = new CancellationTokenSource();
            CancelKeyPress += (s, ev) =>
            {
                _client?.CurrentMetrics.Write(Out);
                WriteLine("Shutting down.");
                ev.Cancel = false;
                cancellation.Cancel();
            };

            WriteLine("Press Ctrl+C to exit...");

            // TODO: M.E.DependencyInjection/Configuration
            var logLevel = LogLevel.Warning;
            var loggerClient = new LoggerAdapter<Client>(logLevel);
            var loggerFatBinaryReader = new LoggerAdapter<FatBinaryReader>(logLevel);
            _client = new Client(
                new Uri(SymbolCollectorServiceUrl),
                new FatBinaryReader(loggerFatBinaryReader),
                blackListedPaths: blackListedPaths,
                logger: loggerClient);

            await _client.UploadAllPathsAsync(paths, cancellation.Token);
        }

        private static async Task Main(string[] args)
        {
            SentrySdk.Init(o =>
            {
                o.Debug = true;
#if !DEBUG
                o.DiagnosticsLevel = Sentry.Protocol.SentryLevel.Info;
#endif
                o.AttachStacktrace = true;
                o.Dsn = new Dsn(Dsn);
            });
            SentrySdk.ConfigureScope(s =>
            {
                s.SetTag("app", typeof(Program).Assembly.GetName().Name);
                s.SetTag("server-endpoint", SymbolCollectorServiceUrl);
                s.AddEventProcessor(@event =>
                {
                    var uploadMetrics = new Dictionary<string, object>();
                    @event.Contexts["metrics"] = uploadMetrics;
                    _client?.CurrentMetrics.Write(uploadMetrics);
                    return @event;
                });
            });
            try
            {
                await UploadSymbols();
            }
            catch (Exception e)
            {
                WriteLine(e);
                SentrySdk.CaptureException(e);
            }

            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));

            _client?.CurrentMetrics.Write(Out);
        }
    }
}
