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
            services.AddSingleton<ObjectFileParser>();
            services.AddSingleton<FatBinaryReader>();
            services.AddTransient<ClientMetrics>();

            services.AddSingleton<ISymbolService, InMemorySymbolService>();
            services.AddSingleton<ISymbolGcsWriter, SymbolGcsWriter>();
            services.AddSingleton<IStorageClientFactory, StorageClientFactory>();

            services.Configure<SymbolServiceOptions>(_configuration.GetSection("SymbolService"));
            services.Configure<SymbolServiceOptions>(o => o.SymsorterPath = GetSymsorterPath());

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

            services.AddMvc()
                .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                endpoints.Map("/health", context =>
                {
                    // TODO: Proper health check
                    context.Response.StatusCode = (int) HttpStatusCode.OK;
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
            else
            {
                throw new InvalidOperationException("No symsorter added for this platform.");
            }

            return "./" + fileName;
            // var asm = GetType().Assembly;
            // using var fileStream = asm.GetManifestResourceStream($"{asm.GetName().Name}.{fileName}");
            //
            // if (fileStream != null)
            // {
            //     var fullPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            //     using (var stream = File.Create(fullPath))
            //     {
            //         fileStream.CopyTo(stream);
            //     }
            //
            //     return fullPath;
            // }
            //
            // throw new InvalidOperationException($"Symsorter {fileName} not embedded in assembly.");
        }
    }

    public class SymbolServiceOptions
    {
        public string? SymsorterPath { get; set; }
        public string? BaseWorkingPath { get; set; }
    }
}
