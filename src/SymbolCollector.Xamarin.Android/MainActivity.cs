using Android.Runtime;
using Android.App;
using Android.Content.PM;
using Android.OS;
using SymbolCollector.Xamarin.Forms;

namespace SymbolCollector.Xamarin.Android
{
    [Activity(Label = "Symbol Collector", Theme = "@style/MainTheme", MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        private const string Tag = "MainActivity";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            TabLayoutResource = Resource.Layout.Tabbar;
            ToolbarResource = Resource.Layout.Toolbar;

            base.OnCreate(savedInstanceState);

            global::Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);

            var bundle = PackageManager.GetApplicationInfo(PackageName, PackageInfoFlags.MetaData).MetaData;
            var url = bundle.GetString("io.sentry.symbol-collector");

            LoadApplication(new App(url));
        }

        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            [GeneratedEnum] Permission[] grantResults)
        {
            global::Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}
