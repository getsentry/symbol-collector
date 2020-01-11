using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
                    ConfigureServices(hostBuilderContext, services);
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

        private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddSingleton<Client>();
            services.AddSingleton<ObjectFileParser>();
            services.AddSingleton<ClientMetrics>();
            services.AddSingleton<FatBinaryReader>();
            services.AddSingleton<ISymbolClient, SymbolClient>();
            services.AddSingleton<ClientMetrics>();
            services.AddOptions<SymbolClientOptions>()
                .Configure<IConfiguration>((o, f) => f.Bind("SymbolClient", o))
                .Validate(o => o.BaseAddress is {}, "BaseAddress is required.");

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
