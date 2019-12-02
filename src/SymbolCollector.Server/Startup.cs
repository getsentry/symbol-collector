using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace SymbolCollector.Server
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            var store = new ConcurrentDictionary<string, byte[]>();
            var log = app.ApplicationServices.GetService<ILoggerFactory>()
                .CreateLogger<Startup>();

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/image")
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
                                    await Add(log, debugId, context, store);
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
                        await context.Response.WriteAsync($@"Total images {store.Count}
Total size in bytes: {store.Values.Sum(s => s.Length)}");
                    }
                    await context.Response.CompleteAsync();
                }
            });
        }

        private static async Task Add(ILogger<Startup> log, StringValues debugId, HttpContext context,
            ConcurrentDictionary<string, byte[]> store)
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
                    store.TryAdd(debugId, mem.ToArray());
                }

                section = await reader.ReadNextSectionAsync();
            }
        }
    }
}
