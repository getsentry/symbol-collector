using System;
using System.Threading;
using System.Threading.Tasks;
using SymbolCollector.Core;
using static System.Console;

namespace SymbolCollector.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // TODO: Get the paths via parameter or confi file
            var paths = new[] {"/lib/", "/usr/lib/", "/usr/local/lib/"};

            // For local testing on macOS: https://docs.microsoft.com/en-US/aspnet/core/grpc/troubleshoot?view=aspnetcore-3.0#unable-to-start-aspnet-core-grpc-app-on-macos
            // 'HTTP/2 over TLS is not supported on macOS due to missing ALPN support.'.
            AppContext.SetSwitch(
                "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var cancellation = new CancellationTokenSource();
            CancelKeyPress += (s, ev) =>
            {
                WriteLine("Shutting down.");
                ev.Cancel = false;
                cancellation.Cancel();
            };

            WriteLine("Press any key to exit...");

            var logger = new LoggerAdapter<Client>();
            var client = new Client(new Uri("http://localhost:5000"), logger: logger);

            await client.UploadAllPathsAsync(paths, cancellation.Token);
        }
    }
}
