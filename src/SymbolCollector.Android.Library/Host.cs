using System.Collections.Generic;
using System.Net.Http;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Sentry;
using SymbolCollector.Core;
using OperationCanceledException = System.OperationCanceledException;

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
                // System.UnauthorizedAccessException: Access to the path '/proc/stat' is denied.
                o.DetectStartupTime = StartupTimeDetectionMode.Fast;
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

                    try
                    {
                        // TODO Add to Sentry.Xamarin
#pragma warning disable 618
                        @event.Contexts.Device.Architecture = Build.CpuAbi;
#pragma warning restore 618
                        // TODO: Same as Brand though?
                        @event.Contexts.Device.Manufacturer = Build.Manufacturer;

                        // Auto tag at least on error events:
                        // @event.SetTag("device", Build.Device ?? "?");
                    }
                    catch
                    {
                        // Capture the event without these values
                    }

                    return @event;
                };
                o.BeforeBreadcrumb = breadcrumb
                    // This logger adds 3 crumbs for each HTTP request and we already have a Sentry integration for HTTP
                    // Which shows the right category, status code and a link
                    => string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.LogicalHandler")
                       || string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.ClientHandler")
                        ? null
                        : breadcrumb;
            });

            var tran = SentrySdk.StartTransaction("AppStart", "activity.load");

            SentrySdk.ConfigureScope(s =>
            {
                s.Transaction = tran;

                // TODO: Remove once device data added to transactions on Sentry.Xamarin:
                s.User.Id = Build.Id;
#pragma warning disable 618
                s.Contexts.Device.Architecture = Build.CpuAbi;
#pragma warning restore 618
                s.Contexts.Device.Brand = Build.Brand;
                s.Contexts.Device.Manufacturer = Build.Manufacturer;
                s.Contexts.Device.Model = Build.Model;

                s.SetExtra("fingerprint", Build.Fingerprint ?? "?");
                s.SetExtra("host", Build.Host ?? "?");
                s.SetExtra("product", Build.Product ?? "?");

                s.SetTag("API", ((int) Build.VERSION.SdkInt).ToString());
#pragma warning disable 618
                s.SetTag("cpu-abi", Build.CpuAbi ?? "?");
                if (!string.IsNullOrEmpty(Build.CpuAbi2))
                {
                    s.SetTag("cpu-abi2", Build.CpuAbi2 ?? "?");
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
                c.AddSingleton<HttpMessageHandlerBuilder, AndroidClientHandlerBuilder>();
            });
            iocSpan.Finish();

            SentrySdk.ConfigureScope(s => s.SetTag("user-agent", userAgent));
            return host;
        }
    }

    public class AndroidClientHandlerBuilder : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }
        public override HttpMessageHandler? PrimaryHandler { get; set; }

        public override IList<DelegatingHandler> AdditionalHandlers => new List<DelegatingHandler>();

        public override HttpMessageHandler Build() => new Xamarin.Android.Net.AndroidClientHandler();
    }
}
