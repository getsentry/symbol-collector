using System;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using SymbolCollector.Core;
using Exception = System.Exception;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]

namespace SymbolCollector.Xamarin.Forms
{
    public partial class App
    {
        private readonly string _serverEndpoint;

        public App(string serverEndpoint)
        {
            _serverEndpoint = serverEndpoint ?? throw new ArgumentNullException(nameof(serverEndpoint));
            InitializeComponent();

            MainPage = new MainPage();
        }

        protected override void OnStart()
        {
            _ = StartUpload(_serverEndpoint);
        }

        protected override void OnSleep()
        {
            // Handle when your app sleeps
        }

        protected override void OnResume()
        {
            // Handle when your app resumes
        }

        private Task StartUpload(string url)
        {

            return Task.Run(async () =>
            {
                var paths = new[] {
                    "/system/lib",
                    "/system/lib64",
                    "/system/"};

                var client = new Client(
                    new Uri(url),
                    new ObjectFileParser(
                        // TODO: logging
                    ),
                    assemblyName: GetType().Assembly.GetName());
                    // logger: new LoggerAdapter<Client>());
                try
                {
                    await client.UploadAllPathsAsync(paths, CancellationToken.None);
                }
                catch (Exception e)
                {
                    // TODO logging
                    Console.WriteLine(e);
                    // Log.Error(Tag, Throwable.FromException(e), "Failed uploading.");
                }
            });
        }
    }
}
