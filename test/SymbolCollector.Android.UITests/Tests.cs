using System;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Internal;
using Xamarin.UITest;
using Xamarin.UITest.Android;
using Xamarin.UITest.Queries;

namespace SymbolCollector.Android.UITests
{
    [TestFixture]
    public class Tests
    {
        private AndroidApp _app = default!;

        [SetUp]
        public void BeforeEachTest()
        {
            var setup = ConfigureApp.Android;
            var apkPath = Environment.GetEnvironmentVariable("SYMBOL_COLLECTOR_APK");

            if (apkPath is { })
            {
                if (File.Exists(apkPath))
                {
                    setup = setup.ApkFile(apkPath);
                    Console.WriteLine($"Using APK: {apkPath}");
                }
                else
                {
                    var msg = $"APK path defined but no file exists at this path: {apkPath}";
                    Console.WriteLine(msg);
                    Assert.Fail(msg);
                }
            }

            _app = setup.StartApp();
        }

        [Test]
        public void AppLaunches()
        {
            var serverUrl = Environment.GetEnvironmentVariable("SYMBOL_COLLECTOR_SERVER_URL");
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                serverUrl = "https://symbol-collector.test";
            }

            static AppQuery serverUrlTextBox(AppQuery query) => query.Id("server_url");
            _app.ClearText(serverUrlTextBox);
            _app.EnterText(serverUrlTextBox, serverUrl);
            _app.Tap(query => query.Id("btnUpload").Button());
            _app.WaitForElement(query => query.Id("done_text"));
            _app.Screenshot("Done");
        }
    }
}
