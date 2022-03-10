using System;
using System.IO;
using System.Linq;
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
            const string apkPath = "../../../../src/SymbolCollector.Android/bin/Release/net6.0-android/io.sentry.symbolcollector.android-Signed.apk";
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
            // This can fail with:
            // System.err: junit.framework.AssertionFailedError: Click can not be completed!
            // System.err: 	at junit.framework.Assert.fail(Assert.java:50)
            // System.err: 	at junit.framework.Assert.assertTrue(Assert.java:20)
            // System.err: 	at com.jayway.android.robotium.solo.Clicker.clickOnScreen(Clicker.java:99)
            // System.err: 	at com.jayway.android.robotium.solo.Solo.clickOnScreen(Solo.java:769)
            // System.err: 	at sh.calaba.instrumentationbackend.actions.gestures.TouchCoordinates.execute(TouchCoordinates.java:17)
            // System.err: 	at sh.calaba.instrumentationbackend.Command.execute(Command.java:47)
            // System.err: 	at sh.calaba.instrumentationbackend.actions.HttpServer.runCommand(HttpServer.java:787)
            // System.err: 	at sh.calaba.instrumentationbackend.actions.HttpServer.serve(HttpServer.java:767)
            // System.err: 	at sh.calaba.instrumentationbackend.actions.NanoHTTPD$HTTPSession.run(NanoHTTPD.java:487)
            // TODO: check if the button is not 'disabled' and if not, retry a couple of times.
            var totalWaitTimeSeconds = 40 * 60;
            var retryCounter = 200;
            var iterationTimeout = TimeSpan.FromSeconds(totalWaitTimeSeconds / retryCounter);
            while (true)
            {
                try
                {
                    _app.WaitForElement(query => query.Id("done_text"), timeout: iterationTimeout);
                    _app.Screenshot("💯");
                    break;
                }
                catch (Exception e) when (e.InnerException is TimeoutException)
                {
                    if (--retryCounter == 0)
                    {
                        _app.Screenshot("Timeout");
                        throw;
                    }

                    // Check if it failed
                    var result = _app.Query(p => p.Id("alertTitle"));
                    if (result?.Any() == true)
                    {
                        _app.Screenshot("Error");
                        throw new Exception("Error modal found, app errored.");
                    }
                }
            }
        }
    }
}
