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
           return Task.Run(async () =>
            {
                // For local testing on macOS: https://docs.microsoft.com/en-US/aspnet/core/grpc/troubleshoot?view=aspnetcore-3.0#unable-to-start-aspnet-core-grpc-app-on-macos
                // 'HTTP/2 over TLS is not supported on macOS due to missing ALPN support.'.
                AppContext.SetSwitch(
                    "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

                var paths = new[] {"/system/lib", "/system/lib64", "/system/"};
                var client = new Client(new Uri("http://localhost:5000"), new LoggerAdapter<Client>());
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
