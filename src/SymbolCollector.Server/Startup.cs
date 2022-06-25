using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using JustEat.StatsD;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Sentry;
using Sentry.Extensibility;
using SymbolCollector.Core;
using SymbolCollector.Server.Properties;
using Sentry.AspNetCore;

namespace SymbolCollector.Server
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<SuffixGenerator>();
            services.AddSingleton<BundleIdGenerator>();

            // TODO: When replacing this to a real (external storage backed), fix lifetimes below (scoped)
            services.AddSingleton<ISymbolService, InMemorySymbolService>();

            services.AddSingleton<ObjectFileParser>();
            services.AddSingleton<FatBinaryReader>();
            services.AddSingleton<ClientMetrics>();
            services.AddSingleton<IBatchFinalizer, SymsorterBatchFinalizer>();
            services.AddSingleton<ISymbolGcsWriter, SymbolGcsWriter>();
            services.AddSingleton<IStorageClientFactory, StorageClientFactory>();

            services.AddSingleton<ISentryEventProcessor, SymbolServiceEventProcessor>();

            services.AddOptions<SymbolServiceOptions>();
            services.AddOptions<ObjectFileParserOptions>();

            services.AddOptions<JsonCredentialParameters>()
                .Configure<IConfiguration>((o, c) => c.Bind("GoogleCloud:JsonCredentialParameters", o));

            services.AddOptions<ObjectFileParserOptions>()
                .Configure<IConfiguration>((o, c) => c.Bind("ObjectFileParser", o));

            services.AddOptions<SymbolServiceOptions>()
                .Configure<IConfiguration>((o, c) =>
                {
                    o.BaseAddress = c.GetValue<string>("Kestrel:EndPoints:Http:Url");
                    c.Bind("SymbolService", o);
                })
                .Configure(o => o.SymsorterPath = GetSymsorterPath())
                .Validate(o => !string.IsNullOrWhiteSpace(o.SymsorterPath), "SymsorterPath is required.")
                .Validate(o => !string.IsNullOrWhiteSpace(o.BaseWorkingPath), "BaseWorkingPath is required.")
                .Validate(o => !Directory.Exists(o.SymsorterPath), $"SymsorterPath doesn't exist.");

            services.AddOptions<GoogleCloudStorageOptions>()
                .Configure<IConfiguration>((o, c) => c.Bind("GoogleCloud", o))
                .Configure<IOptions<JsonCredentialParameters>>((g, o) =>
                {
                    // Massive hack because the Google SDK config system doesn't play well with ASP.NET Core's
                    var jsonCredentials = o.Value;
                    if (jsonCredentials.PrivateKey == "smoke-test")
                    {
                        jsonCredentials.PrivateKey = SmokeTest.SamplePrivateKey;
                    }

                    var json = JsonConvert.SerializeObject(jsonCredentials, Formatting.Indented);
                    var credentials = GoogleCredential.FromJson(json);
                    g.Credential = credentials;
                })
                .Validate(o => !string.IsNullOrWhiteSpace(o.BucketName), "The GCS Bucket name is required.");

            services.AddSingleton(c => c.GetRequiredService<IOptions<GoogleCloudStorageOptions>>().Value);

            services.AddMvc()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

            services.AddSingleton<ISymbolServiceMetrics, MetricsPublisher>();
            services.AddSingleton<ISymbolControllerMetrics, MetricsPublisher>();
            services.AddSingleton<IMetricsPublisher, MetricsPublisher>();

            services.AddOptions<StatsDOptions>()
                .Configure<IConfiguration>((o, c) => c.Bind("StatsD", o))
                .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "StatD host is required.");

            services.AddStatsD(
                provider =>
                {
                    var options = provider.GetRequiredService<IOptions<StatsDOptions>>().Value;
                    var logger = provider.GetRequiredService<ILogger<StatsDConfiguration>>();

                    logger.LogInformation("Configuring statsd with {host}:{port} and prefix: {prefix}",
                        options.Host, options.Port, options.Prefix);

                    return new StatsDConfiguration()
                    {
                        Host = options.Host,
                        Port = options.Port,
                        Prefix = options.Prefix,
                        OnError = ex =>
                        {
                            // How spammy is this going to be?
                            logger.LogError(ex, "StatsD error.");
                            return true; // Don't rethrow
                        }
                    };
                });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Make sure this resolves.
            using (var s = app.ApplicationServices.CreateScope())
            {
                _ = s.ServiceProvider.GetRequiredService<ISymbolService>();
                var options = s.ServiceProvider.GetRequiredService<IOptions<SymbolServiceOptions>>().Value;
                var logger = s.ServiceProvider.GetRequiredService<ILogger<Core.Startup>>();
                if (options.DeleteBaseWorkingPathOnStartup)
                {
                    var paths = new[] { "symsorter_output", "done", "processing", "conflcit" }
                        .Select(p => Path.Combine(options.BaseWorkingPath, p));
                    foreach (var path in paths)
                    {
                        logger.LogDebug("Attempting to clean up {path}", path);
                        try
                        {
                            Directory.Delete(path, true);
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "Failed to clean up {path}", path);
                        }
                    }
                }
            }

            app.Use(async (context, func) =>
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.Add("TraceIdentifier", new[] {context.TraceIdentifier});
                    return Task.CompletedTask;
                });
                await func();
            });

            app.UseRouting();
            app.UseWhen(
                c => !c.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase),
                c => c.UseSentryTracing());

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                endpoints.Map("/smoke-test", context =>
                {
                    // TODO: Proper smoke-test (used to make sure DI is correct. Can't expect working config for GCS.
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    return Task.CompletedTask;
                });

                endpoints.Map("/health", context =>
                {
                    // TODO: Proper health check: Ensure config to GCS is proper (hit by load balancer)
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    return Task.CompletedTask;
                });
            });
        }

        private string GetSymsorterPath()
        {
            string fileName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                fileName = "symsorter-linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                fileName = "symsorter-mac";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fileName = "symsorter.exe";
            }
            else
            {
                throw new InvalidOperationException("No symsorter added for this platform.");
            }

            return "./" + fileName;
        }

        private class SymbolServiceEventProcessor : ISentryEventProcessor
        {
            private readonly IWebHostEnvironment _environment;
            private readonly IMetricsPublisher _metrics;
            private readonly SymbolServiceOptions _options;
            private readonly string _cores = Environment.ProcessorCount.ToString();
            public SymbolServiceEventProcessor(
                IWebHostEnvironment environment,
                IMetricsPublisher metrics,
                IOptions<SymbolServiceOptions> options)
            {
                _environment = environment;
                _metrics = metrics;
                _options = options.Value;
            }

            public SentryEvent? Process(SentryEvent @event)
            {
                _metrics.SentryEventProcessed();
                @event.SetTag("server-endpoint", _options.BaseAddress);
                @event.Contexts["SymbolServiceOptions"] = _options;

                @event.SetTag("cores", _cores);

                // In dev, ignore statsd errors
                if (_environment.IsDevelopment())
                {
                    if (@event.Exception is SocketException ex
                        && ex.ToString().Contains("StatsD"))
                    {
                        return null;
                    }
                }
                return @event;
            }
        }

        public class StatsDOptions
        {
            public string Host { get; set; } = null!;
            public int Port { get; set; }
            public string Prefix { get; set; } = "";
        }
    }
}
