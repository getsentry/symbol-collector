using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sentry;
using Sentry.Extensibility;
using Sentry.Protocol;
using Serilog;
using SystemEnvironment = System.Environment;

namespace SymbolCollector.Server
{
    public class Program
    {
        private static readonly string Environment
            = SystemEnvironment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        public static IConfiguration Configuration { get; private set; } = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment}.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        public static async Task<int> Main(string[] args)
        {
            if (Environment != "Production")
            {
                Serilog.Debugging.SelfLog.Enable(Console.Error);
            }

            Console.WriteLine($"Environment: {Environment}");

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration)
                .CreateLogger();

            try
            {
                Log.Information("Starting with {environment}", Environment);

                using var host = CreateHostBuilder(args).Build();

                if (args.Length == 1 && args[0] == "--smoke-test")
                {
                    await SmokeTest(host);
                }
                else
                {
                    host.Run();
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.Configure<KestrelServerOptions>(
                        context.Configuration.GetSection("Kestrel"));
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseSentry(o =>
                    {
                        o.AddInAppExclude("Serilog");
                        o.AddInAppExclude("Google");
                        o.AddExceptionFilterForType<OperationCanceledException>();
                        o.BeforeSend = @event =>
                        {
                            // Stop raising warning that endpoint was overriden
                            if (@event.Message?.Formatted?.Contains(@"Binding to endpoints defined in") == true
                                && @event.Level == SentryLevel.Warning)
                            {
                                return null!;
                            }

                            // Don't capture Debug events
                            if (@event.Level == SentryLevel.Debug)
                            {
                                return null!;
                            }

                            return @event;
                        };
                    });
                    webBuilder.UseSerilog();
                    webBuilder.UseStartup<Startup>();
                });


        private static async Task SmokeTest(IHost host)
        {
            var cancellationTokenSource = new CancellationTokenSource();

            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var url = configuration.GetValue<string>("Kestrel:EndPoints:Http:Url");

            // host.StartAsync and client.GetAsync combined will need to take less than:
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(3));

            await host.StartAsync(cancellationTokenSource.Token);

            using var client = new HttpClient();
            using var response = await client.GetAsync(new Uri(new Uri(url, UriKind.Absolute), "/smoke-test"),
                cancellationTokenSource.Token);

            if (response.IsSuccessStatusCode)
            {
                Log.Information("Health check passed.");
                cancellationTokenSource.Cancel(); // Stops the host, graceful shutdown.
            }
            else
            {
                throw new Exception($"Health check failed with status code: {response.StatusCode}.");
            }
        }
    }
}
