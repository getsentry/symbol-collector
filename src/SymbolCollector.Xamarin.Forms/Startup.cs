using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SymbolCollector.Core;
using Xamarin.Essentials;

namespace SymbolCollector.Xamarin.Forms
{
    public class Startup
    {
        public static IServiceProvider Init(Action<IServiceCollection> configureServices)
        {
            var host = new HostBuilder()
                .UseContentRoot(FileSystem.AppDataDirectory)
                .ConfigureHostConfiguration(c => c.AddJsonFile(GetAppSettingsFilePath()))
                .ConfigureServices((hostBuilderContext, services) =>
                {
                    ConfigureServices(hostBuilderContext, services);
                    configureServices?.Invoke(services);
                })
                .ConfigureLogging(l => l.AddConsole(o => o.DisableColors = true))
                .Build();

            return host.Services;
        }

        private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.Configure<SymbolCollectorOptions>(context.Configuration.GetSection("SymbolCollector"));

            services.AddSingleton<App>();
            services.AddSingleton<ObjectFileParser>();
            services.AddSingleton<ClientMetrics>();
            services.AddSingleton<FatBinaryReader>();
            services.AddSingleton(r =>
            {
                var options = r.GetRequiredService<IOptions<SymbolCollectorOptions>>().Value;

                if (options.ServerEndpoint is null)
                {
                    throw new InvalidOperationException("No Server endpoint was configured.");
                }

                return new Client(
                    options.ServerEndpoint,
                    r.GetRequiredService<ObjectFileParser>(),
                    options.ClientName ?? "SymbolCollector/0.0.0",
                    metrics: r.GetRequiredService<ClientMetrics>(),
                    blackListedPaths: options.BlackListedPaths,
                    parallelTasks: options.ParallelTasks,
                    logger: r.GetRequiredService<ILogger<Client>>());
            });
        }

        private static string GetAppSettingsFilePath()
        {
            var asm = Assembly.GetExecutingAssembly();
            var fileName = asm.GetName().Name + ".appsettings.json";
            using var fileStream = asm.GetManifestResourceStream(fileName);

            if (fileStream != null)
            {
                var fullPath = Path.Combine(FileSystem.AppDataDirectory, fileName);
                using var stream = File.Create(fullPath);
                fileStream.CopyTo(stream);
                return fullPath;
            }

            throw new InvalidOperationException($"Configuration file 'appsettings.json' was not found at {fileName}.");
        }
    }
}
