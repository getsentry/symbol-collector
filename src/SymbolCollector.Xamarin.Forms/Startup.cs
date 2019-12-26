using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xamarin.Essentials;

namespace SymbolCollector.Xamarin.Forms
{
    public class Startup
    {
        public static IServiceProvider ServiceProvider { get; set; } = null!;

        public static void Init(Action<IServiceCollection> configureServices)
        {
            var host = new HostBuilder()
                // .UseContentRoot(FileSystem.AppDataDirectory)
                .ConfigureHostConfiguration(c =>
                {
                    // Tell the host configuration where to find the file (this is required for Xamarin apps)
                    c.AddCommandLine(new[] {$"ContentRoot={FileSystem.AppDataDirectory}"});

                    c.AddJsonFile(GetAppSettingsFilePath());
                })
                .ConfigureServices((hostBuilderContext, services) =>
                {
                    configureServices?.Invoke(services);
                    ConfigureServices(hostBuilderContext, services);
                })
                .ConfigureLogging(l =>
                    l.AddConsole(o =>
                    {
                        //setup a console logger and disable colors since they don't have any colors in VS
                        o.DisableColors = true;
                    }))
                .Build();

            ServiceProvider = host.Services;
        }

        private static void ConfigureServices(HostBuilderContext ctx, IServiceCollection services)
        {
            var world = ctx.Configuration["Hello"];
            Console.WriteLine(world);
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

            throw new InvalidOperationException($"Configuration file 'appsettings.json' was in {fileName}");
        }
    }
}
