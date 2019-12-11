using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Support.V7.App;
using Android.Util;
using Java.Lang;
using LibObjectFile.Elf;
using SymbolCollector.Core;
using Exception = System.Exception;

namespace SymbolCollector.Android
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        private const string Tag = "MainActivity";
        int counter = 0;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);
            StartUpload().GetAwaiter().GetResult();
            Console.WriteLine("Total: " + counter);
        }

        private Task StartUpload()
        {
            foreach (var item in Directory.GetFiles("/system/"))
            {
                Console.WriteLine(typeof(LibObjectFile.DiagnosticBag));
                using var inStream = File.OpenRead(item);
                try
                {
                    if (ElfObjectFile.TryRead(inStream, out var elf, out var diagnosticBag))
                    {

                        foreach (var section in elf.Sections)
                        {
                            Console.WriteLine(section.Name);
                        }
                        // Print the content of the ELF as readelf output
                        elf.Print(Console.Out);
                        Interlocked.Increment(ref counter);
                    }
                    else
                    {
                        Console.WriteLine("diag" + diagnosticBag);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
            return Task.CompletedTask;
//#if DEBUG
//            // For local testing on macOS: https://docs.microsoft.com/en-US/aspnet/core/grpc/troubleshoot?view=aspnetcore-3.0#unable-to-start-aspnet-core-grpc-app-on-macos
//            // 'HTTP/2 over TLS is not supported on macOS due to missing ALPN support.'.
//            AppContext.SetSwitch(
//                "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
//#endif
//            var bundle = PackageManager.GetApplicationInfo(PackageName, global::Android.Content.PM.PackageInfoFlags.MetaData).MetaData;
//            var url = bundle.GetString("io.sentry.symbol-collector");
//            Log.Info(Tag, "Using Symbol Collector endpoint: " + url);

//            return Task.Run(async () =>
//            {
//                var paths = new[] {"/system/lib", "/system/lib64", "/system/"};
//                var client = new Client(new Uri(url), logger: new LoggerAdapter<Client>());
//                try
//                {
//                    await client.UploadAllPathsAsync(paths, CancellationToken.None);
//                }
//                catch (Exception e)
//                {
//                    Log.Error(Tag, Throwable.FromException(e), "Failed uploading.");
//                }
//            });
        }
    }
}
