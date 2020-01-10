using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Support.V7.App;
using Android.Systems;
using Android.Util;
using Java.Lang;
using SymbolCollector.Core;
using Exception = System.Exception;

namespace SymbolCollector.Android
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        private const string Tag = "MainActivity";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);
            _ = StartUpload();
        }

        private Task StartUpload()
        {
            var bundle = PackageManager.GetApplicationInfo(PackageName, global::Android.Content.PM.PackageInfoFlags.MetaData).MetaData;
            var url = bundle.GetString("io.sentry.symbol-collector");
            Log.Info(Tag, "Using Symbol Collector endpoint: " + url);

            return Task.Run(async () =>
            {
                var paths = new[] {
                    "/system/lib",
                    "/system/lib64",
                    "/system/"};

                var client = new Client(
                    new SymbolClient(new Uri(url), new LoggerAdapter<SymbolClient>(), assemblyName: GetType().Assembly.GetName()),
                    new ObjectFileParser(logger: new LoggerAdapter<ObjectFileParser>()),
                    logger: new LoggerAdapter<Client>());
                try
                {
                    // TODO: Create a friendly name based on this Android model/version
                    var friendlyName = "Android: " + Os.Uname();
                    await client.UploadAllPathsAsync(friendlyName, BatchType.Android, paths, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Log.Error(Tag, Throwable.FromException(e), "Failed uploading.");
                }
            });
        }
    }
}
