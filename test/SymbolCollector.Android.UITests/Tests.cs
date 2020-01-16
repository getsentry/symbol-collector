using System;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Internal;
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
            _app.Screenshot("First screen.");
        }
    }
}
