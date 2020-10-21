using System;
using System.IO;
using NUnit.Framework;
using Xamarin.UITest;
using Xamarin.UITest.Android;

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
            const string apkPath = "../../../../src/SymbolCollector.Android/bin/Release/io.sentry.symbol.collector-Signed.apk";
            if (File.Exists(apkPath))
            {
                setup = setup.ApkFile(apkPath);
                Console.WriteLine($"Using APK: {apkPath}");
            }

            // Quick feedback in debug, not running on a farm:
#if DEBUG
            else
            {
                var msg = $"APK path defined but no file exists at this path: {apkPath}";
                Console.WriteLine(msg);
                Assert.Fail(msg);
            }
#endif

            _app = setup
                .PreferIdeSettings()
                .StartApp();
        }

        [Test]
        public void CollectSymbols()
        {
            _app.Tap(q => q.Id("btnUpload"));
            _app.WaitForElement(query => query.Id("done_text"), timeout: TimeSpan.FromMinutes(30));
            _app.Screenshot("💯");
        }
    }
}
