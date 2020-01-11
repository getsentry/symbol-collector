using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Systems;
using Microsoft.Extensions.Logging;
using Sentry;
using Sentry.Extensibility;
using SymbolCollector.Core;
using Exception = System.Exception;

namespace SymbolCollector.Android
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        private readonly ILogger<MainActivity> _logger;
        private readonly IDisposable _sentry;
        private readonly string _friendlyName;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            _ = StartUpload();
        }

        private Task StartUpload()
        {
            var bundle = PackageManager
                .GetApplicationInfo(PackageName, global::Android.Content.PM.PackageInfoFlags.MetaData).MetaData;
            var url = bundle.GetString("io.sentry.symbol-collector");
            _logger.LogInformation("Using Symbol Collector endpoint: {url}", url);

            return Task.Run(async () =>
            {
                var paths = new[] {"/system/lib", "/system/lib64", "/system/", "/vendor/lib"};

                var clientOptions = new SymbolClientOptions
                {
                    BaseAddress = new Uri(url),
                    // TODO: get version
                    UserAgent = "Android/0.0.0",
                    BlackListedPaths = new[]
                    {
                        "/system/build.prop", "/system/vendor/bin/netstat", "/system/vendor/bin/swapoff"
                    }.ToHashSet()
                };

                var client = new Client(
                    new SymbolClient(clientOptions, new LoggerAdapter<SymbolClient>()),
                    new ObjectFileParser(logger: new LoggerAdapter<ObjectFileParser>()),
                    clientOptions,
                    new ClientMetrics(),
                    logger: new LoggerAdapter<Client>());

                _logger.LogInformation("Using friendly name: {friendlyName}", _friendlyName);

                try
                {
                    await SentrySdk.FlushAsync(TimeSpan.FromSeconds(10));
                    await client.UploadAllPathsAsync(_friendlyName, BatchType.Android, paths, CancellationToken.None);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed uploading {friendlyName} paths: {paths}",
                        _friendlyName, paths);
                }
            });
        }

        public MainActivity()
        {
            _friendlyName = $"Android:{Build.Manufacturer}-{Build.CpuAbi}-{Build.Model}";
            StructUtsname? uname = null;
            try
            {
                uname = Os.Uname();
                _friendlyName += $"-kernel-{uname.Release}";
            }
            catch
            {
                // android.runtime.JavaProxyThrowable: System.NotSupportedException: Could not activate JNI Handle 0x7ed00025 (key_handle 0x4192edf8) of Java type 'md5eb7159ad9d3514ee216d1abd14b6d16a/MainActivity' as managed type 'SymbolCollector.Android.MainActivity'. --->
                // Java.Lang.NoClassDefFoundError: android/system/Os ---> Java.Lang.ClassNotFoundException: Didn't find class "android.system.Os" on path: DexPathList[[zip file "/data/app/SymbolCollector.Android.SymbolCollector.Android-1.apk"],nativeLibraryDirectories=[/data/app-lib/SymbolCollector.Android.SymbolCollector.Android-1, /vendor/lib, /system/lib]]
            }

            _logger = new LoggerAdapter<MainActivity>();
            _sentry = SentrySdk.Init(o =>
            {
                o.Debug = true;
                o.DiagnosticsLevel = Sentry.Protocol.SentryLevel.Info;
                o.AttachStacktrace = true;
                o.Dsn = new Dsn("https://02619ad38bcb40d0be5167e1fb335954@sentry.io/1847454");
            });

            // TODO: This should be part of a package: Sentry.Xamarin.Android
            SentrySdk.ConfigureScope(s =>
            {
                s.User.Id = Build.Id;
                s.Contexts.Device.Architecture = Build.CpuAbi;
                s.Contexts.Device.Brand = Build.Brand;
                s.Contexts.Device.Manufacturer = Build.Manufacturer;
                s.Contexts.Device.Model = Build.Model;

                s.Contexts.OperatingSystem.Name = "Android";
                s.Contexts.OperatingSystem.KernelVersion = uname?.Release;
                s.Contexts.OperatingSystem.Version = Build.VERSION.SdkInt.ToString();

                s.SetTag("API", ((int)Build.VERSION.SdkInt).ToString());
                s.SetTag("app", "SymbolCollector.Android");
                s.SetTag("host", Build.Host);
                s.SetTag("device", Build.Device);
                s.SetTag("product", Build.Product);
                s.SetTag("cpu-abi", Build.CpuAbi);
                s.SetTag("fingerprint", Build.Fingerprint);

                if (!string.IsNullOrEmpty(Build.CpuAbi2))
                {
                    s.SetTag("cpu-abi2", Build.CpuAbi2);
                }
#if DEBUG
                s.SetTag("build-type", "debug");
#elif RELEASE
                s.SetTag("build-type", "release");
#else
                s.SetTag("build-type", "other");
#endif
                if (uname is {})
                {
                    s.Contexts["uname"] = new
                    {
                        uname.Machine,
                        uname.Nodename,
                        uname.Release,
                        uname.Sysname,
                        uname.Version
                    };
                }
            });

            // Don't let logging scopes drop records TODO: review this API
            HubAdapter.Instance.LockScope();

            // TODO: doesn't the AppDomain hook is invoked in all cases?
            AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
            {
                var evt = new SentryEvent(e.Exception);
                evt.SetTag("Handler", "UnhandledExceptionRaiser");
                evt.SetTag("Handled", e.Handled.ToString());
                SentrySdk.CaptureEvent(evt);
                if (!e.Handled)
                {
                    SentrySdk.Close();
                }
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _sentry.Dispose();
        }
    }
}
