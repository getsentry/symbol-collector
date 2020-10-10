using Microsoft.Extensions.DependencyInjection;
using Sentry;
using SymbolCollector.Core;
using Xamarin.Forms;

namespace SymbolCollector.Tizen.Wearable
{
    class Program : global::Xamarin.Forms.Platform.Tizen.FormsApplication
    {
        protected override void OnCreate()
        {
            base.OnCreate();

            LoadApplication(new App());
        }

        static void Main(string[] args)
        {
            SentryTizen.Init();

            // TODO: doesn't the AppDomain hook is invoked in all cases?
            var userAgent = "Tizen/" + typeof(Program).Assembly.GetName().Version;

            SentrySdk.ConfigureScope(s => s.SetTag("user-agent", userAgent));

            var host = Startup.Init(c =>
            {
                //c.AddSingleton<AndroidUploader>();
                c.AddOptions().Configure<SymbolClientOptions>(o =>
                {
                    o.UserAgent = userAgent;
                    o.BlackListedPaths.Add("/system/build.prop");
                    o.BlackListedPaths.Add("/system/vendor/bin/netstat");
                    o.BlackListedPaths.Add("/system/vendor/bin/swapoff");
                });
            });

            var app = new Program();
            Forms.Init(app);
            global::Tizen.Wearable.CircularUI.Forms.Renderer.FormsCircularUI.Init();
            app.Run(args);
        }
    }
}
