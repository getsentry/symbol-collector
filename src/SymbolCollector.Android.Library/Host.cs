using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Sentry;
using SymbolCollector.Core;
using OperationCanceledException = Android.OS.OperationCanceledException;

namespace SymbolCollector.Android.Library
{
    /// <summary>
    /// Symbol Collector client Host.
    /// </summary>
    public class Host
    {
        /// <summary>
        /// Initializes <see cref="IHost"/> with Sentry monitoring.
        /// </summary>
        public static IHost Init(string dsn)
        {
            SentryXamarin.Init(o =>
            {
                // Reset the Sentry Xamarin SDK detection in favor of keeping it consistent with Console/Server
                o.Release = null;

                o.TracesSampleRate = 1.0;
                o.MaxBreadcrumbs = 100;
                o.Debug = true;
#if DEBUG
                o.Environment = "development";
#else
                o.DiagnosticLevel = SentryLevel.Warning;
#endif
                o.AttachStacktrace = true;
                o.AttachScreenshots = true;
                o.Dsn = dsn;
                o.SendDefaultPii = true;

                // TODO: This needs to be built-in
                o.BeforeSend += @event =>
                {
                    const string traceIdKey = "TraceIdentifier";
                    switch (@event.Exception)
                    {
                        case OperationCanceledException _:
                            return null;
                        case var e when e?.Data.Contains(traceIdKey) == true:
                            @event.SetTag(traceIdKey, e.Data[traceIdKey]?.ToString() ?? "unknown");
                            break;
                    }

                    return @event;
                };
            });

            var tran = SentrySdk.StartTransaction("AppStart", "activity.load");

            // TODO: This should be part of a package: Sentry.Xamarin.Android
            SentrySdk.ConfigureScope(s =>
            {
                s.Transaction = tran;
                s.User.Id = Build.Id;
#pragma warning disable 618
                s.Contexts.Device.Architecture = Build.CpuAbi;
#pragma warning restore 618
                s.Contexts.Device.Brand = Build.Brand;
                s.Contexts.Device.Manufacturer = Build.Manufacturer;
                s.Contexts.Device.Model = Build.Model;

                s.SetTag("API", ((int) Build.VERSION.SdkInt).ToString());
                s.SetExtra("host", Build.Host ?? "?");
                s.SetTag("device", Build.Device ?? "?");
                s.SetTag("product", Build.Product ?? "?");
#pragma warning disable 618
                s.SetTag("cpu-abi", Build.CpuAbi ?? "?");
#pragma warning restore 618
                s.SetTag("fingerprint", Build.Fingerprint ?? "?");

#pragma warning disable 618
                if (!string.IsNullOrEmpty(Build.CpuAbi2))
#pragma warning restore 618
                {
#pragma warning disable 618
                    s.SetTag("cpu-abi2", Build.CpuAbi2 ?? "?");
#pragma warning restore 618
                }
#pragma warning restore 618
            });

            // TODO: Where is this span?
            var iocSpan = tran.StartChild("container.init", "Initializing the IoC container");
            var userAgent = "Android/" + typeof(Host).Assembly.GetName().Version;
            var host = Startup.Init(c =>
            {
                // Can be removed once addressed: https://github.com/getsentry/sentry-dotnet/issues/824
                c.AddSingleton<IHttpMessageHandlerBuilderFilter, SentryHttpMessageHandlerBuilderFilter>();

                c.AddSingleton<AndroidUploader>();
                c.AddOptions().Configure<SymbolClientOptions>(o =>
                {
                    o.UserAgent = userAgent;
                    o.BlackListedPaths.Add("/system/build.prop");
                    o.BlackListedPaths.Add("/system/vendor/bin/netstat");
                    o.BlackListedPaths.Add("/system/vendor/bin/swapoff");
                });
                c.AddOptions().Configure<ObjectFileParserOptions>(o =>
                {
                    o.IncludeHash = false;
                    o.UseFallbackObjectFileParser = false; // Android only, use only ELF parser.
                });
            });
            iocSpan.Finish();

            SentrySdk.ConfigureScope(s => s.SetTag("user-agent", userAgent));
            return host;
        }
    }
}
