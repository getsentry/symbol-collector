using System.IO;
using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Sentry;
using Sentry.Extensibility;
using Sentry.Protocol;
using SymbolCollector.Core;
using Xamarin.Essentials;

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
        public static IHost Init()
        {
            SentryXamarin.Init(o =>
            {
                o.TracesSampleRate = 1.0;
                o.MaxBreadcrumbs = 200;
                o.Debug = true;
                o.DiagnosticLevel = SentryLevel.Debug;
                o.AttachStacktrace = true;
                o.Dsn = "https://2262a4fa0a6d409c848908ec90c3c5b4@sentry.io/1886021";
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
                s.SetTag("app", "SymbolCollector.Android");
                s.SetTag("host", Build.Host ?? "?");
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

            // Don't let logging scopes drop records TODO: review this API
            HubAdapter.Instance.LockScope();

            // TODO: doesn't the AppDomain hook is invoked in all cases?
            AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
            {
                e.Exception.Data[Mechanism.HandledKey] = e.Handled;
                e.Exception.Data[Mechanism.MechanismKey] = "UnhandledExceptionRaiser";
                SentrySdk.CaptureException(e.Exception);
                if (!e.Handled)
                {
                    SentrySdk.Close();
                }
            };

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
                    o.IncludeHash = true; // Backing store sorted format does not support hash distinction yet.
                    o.UseFallbackObjectFileParser = false; // Android only, use only ELF parser.
                });
            });
            iocSpan.Finish();

            SentrySdk.ConfigureScope(s =>
            {
                s.SetTag("user-agent", userAgent);
                s.AddAttachment(new ScreenshotAttachment());
            });
            return host;
        }

        private class ScreenshotAttachment : Attachment
        {
            public ScreenshotAttachment()
                : this(
                    AttachmentType.Default,
                    new ScreenshotAttachmentContent(),
                    "screenshot",
                    "image/png")
            {
            }

            private ScreenshotAttachment(
                AttachmentType type,
                IAttachmentContent content,
                string fileName,
                string? contentType)
                : base(type, content, fileName, contentType)
            {
            }

            private class ScreenshotAttachmentContent : IAttachmentContent
            {
                public Stream GetStream()
                {
                    var screenshot = Screenshot.CaptureAsync().GetAwaiter().GetResult();
                    return screenshot.OpenReadAsync().GetAwaiter().GetResult();
                }
            }
        }
    }
}
