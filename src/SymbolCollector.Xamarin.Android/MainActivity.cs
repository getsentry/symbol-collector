using System;
using Android.Runtime;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using SymbolCollector.Xamarin.Forms;

namespace SymbolCollector.Xamarin.Android
{
    [Activity(Label = "Symbol Collector", Theme = "@style/MainTheme", MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        private IServiceProvider _provider = null!;
        protected override void OnCreate(Bundle savedInstanceState)
        {
            TabLayoutResource = Resource.Layout.Tabbar;
            ToolbarResource = Resource.Layout.Toolbar;

            base.OnCreate(savedInstanceState);

            global::Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);

            _provider = Startup.Init(c =>
                c.PostConfigure<SymbolCollectorOptions>(o =>
                {
                    var packageInfo = PackageManager.GetPackageInfo(PackageName, PackageInfoFlags.MetaData);
                    o.ClientName = $"{packageInfo.PackageName}/{packageInfo.VersionName}";
                }));

            var app = _provider.GetRequiredService<App>();

            LoadApplication(app);
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
