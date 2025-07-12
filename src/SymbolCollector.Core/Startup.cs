using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace SymbolCollector.Core;

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
                l.AddSentry(o =>
                {
#pragma warning disable SENTRY0001
                    o.Experimental.EnableLogs = true;
#pragma warning restore SENTRY0001
                    o.InitializeSdk = false;
                });
                l.AddSimpleConsole(o => o.ColorBehavior = LoggerColorBehavior.Disabled);
            })
            .Build();

        return host;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
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
