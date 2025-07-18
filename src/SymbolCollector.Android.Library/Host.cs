using System.Net;
using Java.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.ApplicationModel;
using Polly;
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
    private static string[] RetryMessages =
    [
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
        "Failure in SSL library, usually a protocol error"
    ];

    /// <summary>
    /// Initializes <see cref="IHost"/> with Sentry monitoring.
    /// </summary>
    public static IHost Init(Context context, string dsn, string? sentryTrace = null)
    {
        SentrySdk.Init(o =>
        {

#pragma warning disable SENTRY0001
            o.Experimental.EnableLogs = true;
#pragma warning restore SENTRY0001

            o.CaptureFailedRequests = true;

            o.SetBeforeSend(@event =>
            {
                // Polly sets these without a value, and Sentry shows a processing error
                @event.UnsetTag("PipelineInstance");

                // Don't capture Debug events
                if (@event.Level == SentryLevel.Debug)
                {
                    return null;
                }

                return @event;
            });

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
            // Enable Session Replay - All sessions, No PII scrubbing (no PII on this app)
            o.Native.ExperimentalOptions.SessionReplay.MaskAllImages = false;
            o.Native.ExperimentalOptions.SessionReplay.MaskAllText = false;
            o.Native.ExperimentalOptions.SessionReplay.SessionSampleRate = 1.0;
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

            o.IsGlobalModeEnabled = true;

            o.AddExceptionFilterForType<OperationCanceledException>();
            o.AddInAppExclude("Interop.");
            o.SetBeforeBreadcrumb(breadcrumb
                // This logger adds 3 crumbs for each HTTP request and we already have a Sentry integration for HTTP
                // Which shows the right category, status code and a link
                =>
            {
                // One of this for each HEAD request, spamming the logs
                // info: Polly[3]
                //       Execution attempt. Source: 'ISymbolClient-standard//Retry', Operation Key: '', Result: '208', Handled: 'False', Attempt: '0', Execution Time: 56.1457ms
                if (breadcrumb.Message?.Contains("Handled: 'False', Attempt: '0'") == true)
                {
                    return null;
                }
                return string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.LogicalHandler")
                       || string.Equals(breadcrumb.Category, "System.Net.Http.HttpClient.ISymbolClient.ClientHandler")
                    ? null
                    : breadcrumb;
            });
        });

        var tran = sentryTrace is not null
                   && SentryTraceHeader.Parse(sentryTrace) is { } trace
                   && trace.TraceId != SentryId.Empty
            ? SentrySdk.StartTransaction("AppStart", "activity.load", trace)
            : SentrySdk.StartTransaction("AppStart", "activity.load");

        SentrySdk.ConfigureScope(s =>
        {
            s.Transaction = tran;
            s.AddAttachment(new ScreenshotAttachment());
        });

        var iocSpan = tran.StartChild("container.init", "Initializing the IoC container");
        var userAgent = Java.Lang.JavaSystem.GetProperty("http.agent") ?? "Android/" + typeof(Host).Assembly.GetName().Version;
        var host = Startup.Init(services =>
        {
            services.AddTransient<AndroidMessageHandler>();
            services.AddHttpClient<ISymbolClient, SymbolClient>()
                .ConfigurePrimaryHttpMessageHandler<AndroidMessageHandler>()
                .AddStandardResilienceHandler(configure =>
                {
                    var strategy = ResilienceHelpers.SentryRetryStrategy();
                    strategy.ShouldHandle = arg => arg.Outcome.Exception switch
                    {
                        IOException ioException when RetryMessages.Any(m => ioException.Message.Contains(m)) =>
                            PredicateResult.True(),
                        // On Android web get WebException instead of HttpResponseMessage
                        WebException webException when (RetryMessages.Any(m => webException.Message.Contains(m))) =>
                            PredicateResult.True(),
                        SocketTimeoutException => PredicateResult.True(),
                        _ => PredicateResult.False()
                    };
                    configure.Retry = strategy;
                });

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
