using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Support.V7.App;
using Android.Util;
using Java.Lang;
using Microsoft.Extensions.Logging;
using SymbolCollector.Core;
using SymbolCollector.Xamarin.Forms;
using Exception = System.Exception;

namespace SymbolCollector.Xamarin.Android
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        private const string Tag = "MainActivity";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);
            var bundle = PackageManager.GetApplicationInfo(PackageName, global::Android.Content.PM.PackageInfoFlags.MetaData).MetaData;
            var url = bundle.GetString("io.sentry.symbol-collector");

            Log.Info(Tag, "Using Symbol Collector endpoint: " + url);

            LoadApplication(new App(url));

            // // Set our view from the "main" layout resource
            // SetContentView(Resource.Layout.activity_main);
        }
    }
}
