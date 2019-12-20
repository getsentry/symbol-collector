using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using SymbolCollector.Server.Properties;

namespace SymbolCollector.Server
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration) => _configuration = configuration;

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ISymbolGcsWriter, SymbolGcsWriter>();
            services.AddSingleton<IStorageClientFactory, StorageClientFactory>();

            services.Configure<JsonCredentialParameters>(_configuration.GetSection("GoogleCloud:JsonCredentialParameters"));
            services.AddSingleton(c =>
            {
                // Massive hack because the Google SDK config system doesn't play well with ASP.NET Core's
                var jsonCredentials = c.GetRequiredService<IOptions<JsonCredentialParameters>>().Value;
                if (jsonCredentials.PrivateKey == "smoke-test")
                {
                    jsonCredentials.PrivateKey = SmokeTest.SamplePrivateKey;
                }
                var json = JsonConvert.SerializeObject(jsonCredentials, Formatting.Indented);
                var credentials = GoogleCredential.FromJson(json);
                return new GoogleCloudStorageOptions(credentials);
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            var store = new ConcurrentDictionary<string, byte>();
            var log = app.ApplicationServices.GetService<ILoggerFactory>()
                .CreateLogger<Startup>();

            var writer = app.ApplicationServices.GetRequiredService<ISymbolGcsWriter>();
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/health")
                {
                    // TODO: Proper health check
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                }
                else if (context.Request.Path == "/image")
                {
                    if (context.Request.Headers.TryGetValue("debug-id", out var debugId))
                    {
                        log.LogInformation("Incoming image with debug Id:{debugId}", debugId);

                        if (store.ContainsKey(debugId))
                        {
                            log.LogDebug($"Debug Id:{debugId} already exists.");
                            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                        }
                        else
                        {
                            switch (context.Request.Method)
                            {
                                case "HEAD":
                                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                                    break;
                                case "POST":
                                {
                                    await Add(log, debugId, context, writer, store);
                                }
                                    context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                                    break;
                                default:
                                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                                    break;
                            }
                        }
                    }
                    else if (context.Request.Method == "GET")
                    {
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync($@"Total images {store.Count}");
                    }
                    await context.Response.CompleteAsync();
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            });
        }

        private static async Task Add(ILogger<Startup> log, StringValues debugId, HttpContext context,
            ISymbolGcsWriter writer, ConcurrentDictionary<string, byte> store)
        {
            using var _ = log.BeginScope(new {DebugId = debugId});

            await using var mem = new MemoryStream();
            var boundary = HeaderUtilities.RemoveQuotes(
                MediaTypeHeaderValue.Parse(context.Request.ContentType).Boundary);
            if (boundary.Length < 10)
            {
                return;
            }

            var reader = new MultipartReader(boundary.ToString(), context.Request.Body);

            var section = await reader.ReadNextSectionAsync();
            while (section != null)
            {
                var hasContentDispositionHeader =
                    ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition);
                if (hasContentDispositionHeader)
                {
                    await section.Body.CopyToAsync(mem);
                    log.LogInformation("Size: " + mem.Length);
                    mem.Position = 0;
                    await writer.WriteAsync(debugId, mem, CancellationToken.None);
                    store.TryAdd(debugId, 1);
                }

                section = await reader.ReadNextSectionAsync();
            }
        }
    }
}
