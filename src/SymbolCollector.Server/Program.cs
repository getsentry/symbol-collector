using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SymbolCollector.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            using var host = CreateHostBuilder(args).Build();

            if (!(args.Length == 1 && args[0] == "--smoke-test"))
            {
                host.Run();
            }
            else
            {
                await HealthCheck(host);
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseSentry();
                    webBuilder.UseStartup<Startup>();
                });

        private static async Task HealthCheck(IHost host)
        {
            var cancellationTokenSource = new CancellationTokenSource();

            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var url = configuration.GetValue<string>("Kestrel:EndPoints:Http:Url");

            // host.StartAsync and client.GetAsync combined will need to take less than:
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(3));

            await host.StartAsync(cancellationTokenSource.Token);

            using var client = new HttpClient();
            using var response = await client.GetAsync(url + "/health",
                cancellationTokenSource.Token);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Health check passed.");
                cancellationTokenSource.Cancel(); // Stops the host, graceful shutdown.
            }
            else
            {
                throw new Exception($"Health check failed with status code: {response.StatusCode}.");
            }
        }
    }
}
