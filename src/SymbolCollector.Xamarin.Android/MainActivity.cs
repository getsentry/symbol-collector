using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Support.V7.App;
using Android.Util;
using Java.Lang;
using SymbolCollector.Core;
using Exception = System.Exception;

namespace SymbolCollector.Xamarin.Android
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
                    new Uri(url),
                    new ObjectFileParser(logger: new LoggerAdapter<ObjectFileParser>()),
                    assemblyName: GetType().Assembly.GetName(),
                    logger: new LoggerAdapter<Client>());
                try
                {
                    await client.UploadAllPathsAsync(paths, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Log.Error(Tag, Throwable.FromException(e), "Failed uploading.");
                }
            });
        }
    }
}
