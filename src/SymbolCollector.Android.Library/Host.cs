using System.Net;
using Java.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.ApplicationModel;
using Polly.Extensions.Http;
using SymbolCollector.Core;
using Xamarin.Android.Net;
using Context = Android.Content.Context;
using OperationCanceledException = System.OperationCanceledException;

namespace SymbolCollector.Android.Library;

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
        SentrySdk.Init(o =>
        {
            // TODO: ShouldCan be deleted once this PR is released: https://github.com/getsentry/sentry-dotnet/pull/1750/files#diff-c55d438dd1d5f3731c0d04d0f1213af4873645b1daa44c4c6e1b24192110d8f8R166-R167
            // System.UnauthorizedAccessException: Access to the path '/proc/stat' is denied.
            o.DetectStartupTime = StartupTimeDetectionMode.Fast;
            o.CaptureFailedRequests = true;

            // TODO: Should be added OOTB
            o.Release = $"{AppInfo.PackageName}@{AppInfo.VersionString}+{AppInfo.BuildString}";

            o.TracesSampleRate = 1.0;
            o.Debug = true;

#if ANDROID
            o.Android.LogCatIntegration = Sentry.Android.LogCatIntegrationType.All;
            // Bindings to the native SDK
            o.Native.EnableNetworkEventBreadcrumbs = true;
            o.Native.AttachScreenshot = true;
            o.Native.EnableTracing = true; // Will double report transactions but to get profiler data
            o.Native.ProfilesSampleRate = 0.4;
#endif

#if DEBUG
            o.Environment = "development";
#else
            o.DiagnosticLevel = SentryLevel.Info;
#endif
            o.MaxBreadcrumbs = 350;
            o.InitCacheFlushTimeout = TimeSpan.FromSeconds(5);
            o.AttachStacktrace = true;
            o.Dsn = dsn;
            o.SendDefaultPii = true;

            o.AddExceptionFilterForType<OperationCanceledException>();
            o.AddInAppExclude("Interop.");
            o.SetBeforeBreadcrumb(breadcrumb
                // This logger adds 3 crumbs for each HTTP request and we already have a Sentry integration for HTTP
                // Which shows the right category, status code and a link
                => string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.LogicalHandler")
                   || string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.ClientHandler")
                    ? null
                    : breadcrumb);
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
        var host = Startup.Init(services =>
        {
            var messages = new []
            {
                // Unable to resolve host "symbol-collector.services.sentry.io": No address associated with hostname
                "No address associated with hostname",
                // Read error: ssl=0x79ea0d6988: SSL_ERROR_WANT_READ occurred. You should never see this.
                "You should never see this",
                // handshake error: ssl=0x78f5b01b48: I/O error during system call, Try again
                "Try again",
                // failed to connect to symbol-collector.services.sentry.io/35.188.18.176 (port 443) from /10.22.91.71 (port 43860) after 86400000ms: isConnected failed: ETIMEDOUT (Connection timed out)
                "Connection timed out",
                // Read error: ssl=0x77f787e308: Failure in SSL library, usually a protocol error
                // error:100003fc:SSL routines:OPENSSL_internal:SSLV3_ALERT_BAD_RECORD_MAC (external/boringssl/src/ssl/tls_record.cc:592 0x77f854d8c8:0x00000001)
                "Failure in SSL library, usually a protocol error",
            };
            services.AddTransient<AndroidMessageHandler>();
            services.AddHttpClient<ISymbolClient, SymbolClient>()
                .ConfigurePrimaryHttpMessageHandler<AndroidMessageHandler>()
                .AddPolicyHandler((s, r) =>
                    HttpPolicyExtensions.HandleTransientHttpError()
                        // Could be deleted if merged: https://github.com/App-vNext/Polly.Extensions.Http/pull/33
                        // On Android web get WebException instead of HttpResponseMessage which HandleTransientHttpError covers
                        .Or<IOException>(e => messages.Any(m => e.Message.Contains(m)))
                        .Or<WebException>(e => messages.Any(m => e.Message.Contains(m)))
                        .Or<SocketTimeoutException>()
                        .SentryPolicy(s));

            services.AddSingleton<AndroidUploader>();
            services.AddOptions().Configure<SymbolClientOptions>(o =>
            {
                o.UserAgent = userAgent;
                o.BlockListedPaths.Add("/system/etc/.booking.data.aid");
                o.BlockListedPaths.Add("/system/build.prop");
                o.BlockListedPaths.Add("/system/vendor/bin/netstat");
                o.BlockListedPaths.Add("/system/vendor/bin/swapoff");
                o.BlockListedPaths.Add("/system/etc/.booking.data.aid");
            });
            services.AddOptions().Configure<ObjectFileParserOptions>(o =>
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
