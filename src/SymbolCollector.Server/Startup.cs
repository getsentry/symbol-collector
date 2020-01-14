using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Sentry;
using Sentry.Extensibility;
using SymbolCollector.Core;
using SymbolCollector.Server.Properties;

namespace SymbolCollector.Server
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration) => _configuration = configuration;

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<SuffixGenerator>();

            // TODO: When replacing this to a real (external storage backed), fix lifetimes below (scoped)
            services.AddSingleton<ISymbolService, InMemorySymbolService>();

            services.AddSingleton<ObjectFileParser>();
            services.AddSingleton<FatBinaryReader>();
            services.AddSingleton<ClientMetrics>();
            services.AddSingleton<IBatchFinalizer, SymsorterBatchFinalizer>();
            services.AddSingleton<ISymbolGcsWriter, NoOpSymbolGcsWriter>();
            services.AddSingleton<IStorageClientFactory, StorageClientFactory>();

            services.AddSingleton<ISentryEventProcessor, SymbolServiceEventProcessor>();

            services.AddOptions<JsonCredentialParameters>()
                .Configure<IConfiguration>((o, c) => c.Bind("GoogleCloud:JsonCredentialParameters", o));

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
            }

            app.Use(async (context, func) =>
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.Add("TraceIdentifier", new[] { context.TraceIdentifier });
                    return Task.CompletedTask;
                });
                await func();
            });

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                endpoints.Map("/health", context =>
                {
                    // TODO: Proper health check
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
            private readonly SymbolServiceOptions _options;
            public SymbolServiceEventProcessor(IOptions<SymbolServiceOptions> options) => _options = options.Value;

            public SentryEvent Process(SentryEvent @event)
            {
                @event.SetTag("server-endpoint", _options.BaseAddress);
                @event.Contexts["SymbolServiceOptions"] = _options;
                return @event;
            }
        }
    }
}
