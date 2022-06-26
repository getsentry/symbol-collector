using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.ApplicationModel;
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
        public static IHost Init(Context context, string dsn)
        {
            SentrySdk.Init(context, o =>
            {
                // TODO: ShouldCan be deleted once this PR is released: https://github.com/getsentry/sentry-dotnet/pull/1750/files#diff-c55d438dd1d5f3731c0d04d0f1213af4873645b1daa44c4c6e1b24192110d8f8R166-R167
                // System.UnauthorizedAccessException: Access to the path '/proc/stat' is denied.
                // o.DetectStartupTime = StartupTimeDetectionMode.Fast;
#if ANDROID
                // TODO: Should be added OOTB
                o.Release = $"{AppInfo.PackageName}@{AppInfo.VersionString}+{AppInfo.BuildString}";

                o.Android.AttachScreenshot = true;
                o.Android.ProfilingEnabled = true;
                o.Android.EnableAndroidSdkTracing = true; // Will double report transactions but to get profiler data
#endif
                o.TracesSampleRate = 1.0;
                o.Debug = true;
#if DEBUG
                o.Environment = "development";
#else
                o.DiagnosticLevel = SentryLevel.Info;
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
                // TODO: https://github.com/getsentry/sentry-dotnet/issues/1751
                // o.BeforeBreadcrumb = breadcrumb
                //     // This logger adds 3 crumbs for each HTTP request and we already have a Sentry integration for HTTP
                //     // Which shows the right category, status code and a link
                //     => string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.LogicalHandler")
                //        || string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.ClientHandler")
                //         ? null
                //         : breadcrumb;
            });

            var tran = SentrySdk.StartTransaction("AppStart", "activity.load");

            SentrySdk.ConfigureScope(s =>
            {
                s.Transaction = tran;
                s.AddAttachment(new ScreenshotAttachment());
            });

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
            });
            iocSpan.Finish();

            SentrySdk.ConfigureScope(s => s.SetTag("user-agent", userAgent));
            return host;
        }
    }
}
