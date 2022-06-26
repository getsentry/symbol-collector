using System.Collections.Generic;
using System.Net.Http;
using Android.Content;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Sentry;
using SymbolCollector.Core;
using Xamarin.Android.Net;
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
        public static IHost Init(Context context, string dsn)
        {
            SentrySdk.Init(context, o =>
            {
                // TODO: Should this be in the Sentry package for net6-android?
                // System.UnauthorizedAccessException: Access to the path '/proc/stat' is denied.
                // o.DetectStartupTime = StartupTimeDetectionMode.Fast;
                o.TracesSampleRate = 1.0;
                o.Debug = true;
#if DEBUG
                o.Environment = "development";
#else
                o.DiagnosticLevel = SentryLevel.Warning;
#endif
                o.AttachStacktrace = true;
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
                o.BeforeBreadcrumb = breadcrumb
                    // This logger adds 3 crumbs for each HTTP request and we already have a Sentry integration for HTTP
                    // Which shows the right category, status code and a link
                    => string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.LogicalHandler")
                       || string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.ClientHandler")
                        ? null
                        : breadcrumb;
            });

            var tran = SentrySdk.StartTransaction("AppStart", "activity.load");

            SentrySdk.ConfigureScope(s => s.Transaction = tran);

            // TODO: Where is this span?
            var iocSpan = tran.StartChild("container.init", "Initializing the IoC container");
            var userAgent = Java.Lang.JavaSystem.GetProperty("http.agent") ?? "Android/" + typeof(Host).Assembly.GetName().Version;
            var host = Startup.Init(c =>
            {
                c.AddSingleton<AndroidUploader>();
                c.AddOptions().Configure<SymbolClientOptions>(o =>
                {
                    o.UserAgent = userAgent;
                    o.BlockListedPaths.Add("/system/etc/.booking.data.aid");
                    o.BlockListedPaths.Add("/system/build.prop");
                    o.BlockListedPaths.Add("/system/vendor/bin/netstat");
                    o.BlockListedPaths.Add("/system/vendor/bin/swapoff");
                });
                c.AddOptions().Configure<ObjectFileParserOptions>(o =>
                {
                    o.IncludeHash = false;
                    o.UseFallbackObjectFileParser = false; // Android only, use only ELF parser.
                });
                c.AddSingleton<AndroidMessageHandlerBuilder, AndroidMessageHandlerBuilder>();
            });
            iocSpan.Finish();

            SentrySdk.ConfigureScope(s => s.SetTag("user-agent", userAgent));
            return host;
        }
    }

    public class AndroidMessageHandlerBuilder : HttpMessageHandlerBuilder
    {
        public override string Name { get; set; } = "AndroidMessageHandlerBuilder";
        public override HttpMessageHandler PrimaryHandler { get; set; } = null!;

        public override IList<DelegatingHandler> AdditionalHandlers => new List<DelegatingHandler>();

        public override HttpMessageHandler Build() => new AndroidMessageHandler();
    }
}
