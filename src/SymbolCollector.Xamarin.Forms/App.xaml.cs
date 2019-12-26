using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xamarin.Forms.Xaml;
using SymbolCollector.Core;
using Exception = System.Exception;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]

namespace SymbolCollector.Xamarin.Forms
{
    public partial class App
    {
        private readonly Client _client;
        private readonly ILogger<App> _logger;

        public App(Client client, ILogger<App> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeComponent();
            MainPage = new MainPage();
        }

        protected override void OnStart() => _ = StartUpload(CancellationToken.None);

        private Task StartUpload(CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                // TODO: from the config system
                var paths = new[] {
                    "/system/lib",
                    "/system/lib64",
                    "/system/"};

                try
                {
                    await _client.UploadAllPathsAsync(paths, cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed uploading files.");
                }
            }, cancellationToken);
    }
}
