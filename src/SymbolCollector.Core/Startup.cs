using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Sentry;

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
                    // Adds services such as SentryHttpMessageHandler
                    // https://github.com/getsentry/sentry-dotnet/blob/a9304a0a4b4702d0e62e2703d55c66483d27a0e5/src/Sentry.Extensions.Logging/Extensions/DependencyInjection/ServiceCollectionExtensions.cs#L46
                    // TODO: This should  be built in for console apps, added via IHostBuilder extension

                    // Results in a span for each HTTP request which currently means a HEAD request to check if symbol is needed
                    // and a post to upload it. In high latency scenarios this is anyway suboptimal and these HEAD requests should be batched
                    // Until then we're better off with a single span for the whole upload process
                    // services.AddSentry<SentryLoggingOptions>();

                    ConfigureServices(services);
                    configureServices?.Invoke(services);
                })
                .ConfigureLogging(l =>
                {
                    // TODO: Should also be added via IHostBuilder extension
                    l.AddSentry(o => o.InitializeSdk = false);
                    l.AddSimpleConsole(o => o.ColorBehavior = LoggerColorBehavior.Disabled);
                })
                .Build();

            return host;
        }

        private static void ConfigureServices(IServiceCollection services)
        {
#if ANDROID
            services.AddSingleton<Xamarin.Android.Net.AndroidMessageHandler>();
#endif
            var messages = new []
            {
                // Unable to resolve host "symbol-collector.services.sentry.io": No address associated with hostname
                "No address associated with hostname",
                // Read error: ssl=0x79ea0d6988: SSL_ERROR_WANT_READ occurred. You should never see this.
                "You should never see this",
                // handshake error: ssl=0x78f5b01b48: I/O error during system call, Try again
                "Try again",
                // failed to connect to symbol-collector.services.sentry.io/35.188.18.176 (port 443) from /10.22.91.71 (port 43860) after 86400000ms: isConnected failed: ETIMEDOUT (Connection timed out)
                "Connection timed out",
                // Read error: ssl=0x77f787e308: Failure in SSL library, usually a protocol error
                // error:100003fc:SSL routines:OPENSSL_internal:SSLV3_ALERT_BAD_RECORD_MAC (external/boringssl/src/ssl/tls_record.cc:592 0x77f854d8c8:0x00000001)
                "Failure in SSL library, usually a protocol error",
            };
            services.AddHttpClient<ISymbolClient, SymbolClient>()
#if ANDROID
                .ConfigurePrimaryHttpMessageHandler<Xamarin.Android.Net.AndroidMessageHandler>()
#endif

                .AddPolicyHandler((s, r) =>
                    HttpPolicyExtensions.HandleTransientHttpError()
                        // Could be deleted if merged: https://github.com/App-vNext/Polly.Extensions.Http/pull/33
                        // On Android web get WebException instead of HttpResponseMessage which HandleTransientHttpError covers
                        .Or<IOException>(e => messages.Any(m => e.Message.Contains(m)))
                        .Or<WebException>(e => messages.Any(m => e.Message.Contains(m)))
#if ANDROID
                        .Or<Java.Net.SocketTimeoutException>()
#endif
                    .WaitAndRetryAsync(new[]
                        {
                            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6),
#if RELEASE
                            // TODO: Until a proper re-entrancy is built in the clients, add a last hope retry
                            TimeSpan.FromSeconds(12)
#endif
                        },
                        (result, span, retryAttempt, context) =>
                        {
                            var sentry = s.GetRequiredService<IHub>();

                            var data = new Dictionary<string, string> {{"PollyRetryCount", retryAttempt.ToString()}};
                            if (result.Exception is Exception e)
                            {
                                data.Add("exception", e.ToString());
                            }

                            sentry.AddBreadcrumb(
                                $"Waiting {span} following attempt {retryAttempt} failed HTTP request.",
                                data: data);
                        }
                    ));

            services.AddSingleton<Client>();
            services.AddSingleton<ObjectFileParser>();
            services.AddSingleton<ClientMetrics>();
            services.AddSingleton<FatBinaryReader>();
            services.AddSingleton<ClientMetrics>();

            services.AddOptions<SymbolClientOptions>()
                .Configure<IConfiguration>((o, f) => f.Bind("SymbolClient", o))
                .Validate(o => o.BaseAddress is {}, "BaseAddress is required.");

            services.AddOptions<ObjectFileParserOptions>();

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
