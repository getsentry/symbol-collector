using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Sentry;
using SymbolCollector.Core;
using static System.Console;

namespace SymbolCollector.Console
{
    internal class Program
    {
        private const string Dsn = "https://02619ad38bcb40d0be5167e1fb335954@sentry.io/1847454";
        private const string SymbolCollectorServiceUrl = "http://34.70.114.11";

        private static async Task UploadSymbols()
        {
            // TODO: Get the paths via parameter or confi file/env var?
            var paths = new List<string> {"/lib/", "/usr/lib/", "/usr/local/lib/"};
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // TODO: Add per OS paths
//                paths.Add("");
            }

            var cancellation = new CancellationTokenSource();
            CancelKeyPress += (s, ev) =>
            {
                WriteLine("Shutting down.");
                ev.Cancel = false;
                cancellation.Cancel();
            };

            WriteLine("Press any key to exit...");

            // TODO: M.E.DependencyInjection/Configuration
            var loggerClient = new LoggerAdapter<Client>();
            var loggerFatBinaryReader = new LoggerAdapter<FatBinaryReader>();
            var client = new Client(new Uri(SymbolCollectorServiceUrl), new FatBinaryReader(loggerFatBinaryReader), logger: loggerClient);
            await client.UploadAllPathsAsync(paths, cancellation.Token);
        }

        private static async Task Main(string[] args)
        {
            SentrySdk.Init(o =>
            {
#if DEBUG
                o.Debug = true;
#endif
                o.AttachStacktrace = true;
                o.Dsn = new Dsn(Dsn);
            });
            SentrySdk.ConfigureScope(s =>
            {
                s.SetTag("app", typeof(Program).Assembly.GetName().Name);
                s.SetTag("server-endpoint", SymbolCollectorServiceUrl);
            });
            try
            {
                await UploadSymbols();
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
            }

            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }
}
