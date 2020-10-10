using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sentry;
using Sentry.Extensibility;
using Sentry.Protocol;
using SymbolCollector.Core;

namespace SymbolCollector.Tizen.Wearable
{
    class SentryTizen
    {
        public static void Init()
        {
            SentrySdk.Init(o =>
            {
                o.Debug = true;
                o.DiagnosticsLevel = SentryLevel.Info;
                o.AttachStacktrace = true;
#if DEBUG
                o.Dsn = new Dsn("https://02619ad38bcb40d0be5167e1fb335954@sentry.io/1847454");
#else
                o.Dsn = new Dsn("https://2262a4fa0a6d409c848908ec90c3c5b4@sentry.io/1886021");
#endif
                o.SendDefaultPii = true;
                o.AddInAppExclude("Polly");
                o.AddInAppExclude("Mono");

                o.AddExceptionFilterForType<OperationCanceledException>();

                // TODO: This needs to be built-in
                o.BeforeSend += @event =>
                {
                    const string traceIdKey = "TraceIdentifier";
                    switch (@event.Exception)
                    {
                        case var e when e is OperationCanceledException:
                            return null;
                        case var e when e?.Data.Contains(traceIdKey) == true:
                            @event.SetTag(traceIdKey, e.Data[traceIdKey]?.ToString() ?? "unknown");
                            break;
                    }

                    return @event;
                };
            });

            // TODO: This should be part of a package: Sentry.Xamarin.Android
            SentrySdk.ConfigureScope(s =>
            {
                //s.User.Id = 
#pragma warning disable 618
                //s.Contexts.Device.Architecture = Build.CpuAbi;
#pragma warning restore 618
                //s.Contexts.Device.Brand = Build.Brand;
                //s.Contexts.Device.Manufacturer = Build.Manufacturer;
                //s.Contexts.Device.Model = Build.Model;

                s.Contexts.OperatingSystem.Name = "Tizen";
                //s.Contexts.OperatingSystem.KernelVersion = uname?.Release;
                //s.Contexts.OperatingSystem.Version = Build.VERSION.SdkInt.ToString();

                //s.SetTag("API", ((int)Build.VERSION.SdkInt).ToString());
                s.SetTag("app", "SymbolCollector.Tizen.Wearable");
//                s.SetTag("host", Build.Host);
//                s.SetTag("device", Build.Device);
//                s.SetTag("product", Build.Product);
//#pragma warning disable 618
//                s.SetTag("cpu-abi", Build.CpuAbi);
//#pragma warning restore 618
//                s.SetTag("fingerprint", Build.Fingerprint);

//#pragma warning disable 618
//                if (!string.IsNullOrEmpty(Build.CpuAbi2))
//#pragma warning restore 618
//                {
//#pragma warning disable 618
//                    s.SetTag("cpu-abi2", Build.CpuAbi2);
//#pragma warning restore 618
//                }
//#pragma warning restore 618

#if DEBUG
                s.SetTag("build-type", "debug");
#elif RELEASE
                s.SetTag("build-type", "release");
#else
                s.SetTag("build-type", "other");
#endif
                //if (uname is { })
                //{
                //    s.Contexts["uname"] = new
                //    {
                //        uname.Machine,
                //        uname.Nodename,
                //        uname.Release,
                //        uname.Sysname,
                //        uname.Version
                //    };
                //}
            });

            // Don't let logging scopes drop records TODO: review this API
            HubAdapter.Instance.LockScope();
        }
    }
}
