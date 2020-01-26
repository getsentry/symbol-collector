using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Sentry;
using Sentry.Protocol;

namespace SymbolCollector.Core
{
    public class Startup
    {
        public static IHost Init(Action<IServiceCollection> configureServices)
        {
            var host = new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureHostConfiguration(c => c.AddJsonFile(GetAppSettingsFilePath()))
                .ConfigureServices((hostBuilderContext, services) =>
                {
                    ConfigureServices(services);
                    configureServices?.Invoke(services);
                })
                .ConfigureLogging(l =>
                {
                    l.AddSentry(o => o.InitializeSdk = false);
                    l.AddConsole(o => o.DisableColors = true);
                })
                .Build();

            return host;
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient<ISymbolClient, SymbolClient>()
                .AddPolicyHandler((s, r) => HttpPolicyExtensions.HandleTransientHttpError()
                    .WaitAndRetryAsync(new[]
                        {
                            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
#if RELEASE
                            // TODO: Until a proper re-entrancy is built in the clients, add a last hope retry
                            TimeSpan.FromSeconds(15)
#endif
                        },
                        onRetry: async (result, span, retryAttempt, context) =>
                        {
                            var sentry = s.GetService<ISentryClient>();
                            var evt = new SentryEvent(result.Exception)
                            {
                                Level = SentryLevel.Warning,
                                LogEntry = new LogEntry
                                {
                                    Formatted =
                                        $"Waiting {span} following attempt {retryAttempt} failed HTTP request.",
                                    Message =
                                        "Waiting {span} following attempt {retryAttempt} failed HTTP request.",
                                }
                            };
                            evt.SetTag("Tag", "Polly");
                            if (result.Result is { } request)
                            {
                                const string traceIdKey = "TraceIdentifier";
                                if (request.Headers.TryGetValues(traceIdKey, out var traceIds))
                                {
                                    evt.SetTag(traceIdKey, traceIds.FirstOrDefault() ?? "unknown");
                                }

                                evt.SetTag("StatusCode", request.StatusCode.ToString());
                                var responseBody = await request.Content.ReadAsStringAsync();
                                if (!string.IsNullOrWhiteSpace(responseBody))
                                {
                                    evt.SetExtra("body", responseBody);
                                }
                            }
                            sentry.CaptureEvent(evt);
                        }
                    ));

            services.AddSingleton<Client>();
            services.AddSingleton<ObjectFileParser>();
            services.AddSingleton<ClientMetrics>();
            services.AddSingleton<FatBinaryReader>();
            services.AddSingleton<ClientMetrics>();
            services.AddSingleton<Symsorter>();

            services.AddOptions<SymbolClientOptions>()
                .Configure<IConfiguration>((o, f) => f.Bind("SymbolClient", o))
                .Validate(o => o.BaseAddress is {}, "BaseAddress is required.");

            services.AddOptions<SymsorterOptions>()
                .Configure<IConfiguration>((o, f) => f.Bind("Symsorter", o));

            services.AddSingleton<SymbolClientOptions>(c =>
                c.GetRequiredService<IOptions<SymbolClientOptions>>().Value);
        }

        private static string GetAppSettingsFilePath()
        {
            var asm = Assembly.GetExecutingAssembly();
            var fileName = asm.GetName().Name + ".appsettings.json";
            using var fileStream = asm.GetManifestResourceStream(fileName);

            if (fileStream != null)
            {
                var fullPath = Path.Combine(Path.GetTempPath(), fileName);
                using var stream = File.Create(fullPath);
                fileStream.CopyTo(stream);
                return fullPath;
            }

            throw new InvalidOperationException($"Configuration file 'appsettings.json' was not found at {fileName}.");
        }
    }
}
