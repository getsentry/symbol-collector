// To skip uploading the package, pass 'false' as the first argument

using SymbolCollector.Runner;

Console.WriteLine("Starting runner...");

const string appName = "SymbolCollector.apk";
const string appPackage = "io.sentry.symbolcollector.android";
const string filePath = $"src/SymbolCollector.Android/bin/Release/net9.0-android/{appPackage}-Signed.apk";

SentrySdk.Init(options =>
{
    options.Dsn = "https://ea58a7607ff1b39433af3a6c10365925@o1.ingest.us.sentry.io/4509420348964864";
    options.Dsn = "";
    options.Debug = false;
    options.AutoSessionTracking = true;
    options.TracesSampleRate = 1.0;
});

var transaction = SentrySdk.StartTransaction("appium.runner", "runner appium to upload apk to saucelabs and collect symbols real devices");
SentrySdk.ConfigureScope(s => s.Transaction = transaction);

try
{
    using var client = new SauceLabsClient();
    var devices = await client.GetDevices();
    // Prioritize devices that don't have a timestamp saved in the cache yet
    if (devices.FirstOrDefault(p => p.LastSymbolUploadRanTime is null) is not { } deviceToRun)
    {
        Console.WriteLine("No new devices, running on the one we ran last.");
        // TODO: Skip if ran last than 30 days ago
        deviceToRun = devices.OrderBy(d => d.LastSymbolUploadRanTime).First();
        Console.WriteLine("Running on device that ran last {0}: {1}", deviceToRun.LastSymbolUploadRanTime, deviceToRun);
    }
    else
    {
        Console.WriteLine("Brand new device detected: {0}", deviceToRun);
    }

    var app = $"storage:filename={appName}";

    if (args.Length == 0 || bool.TryParse(args[0], out var skipUploadApp) && !skipUploadApp)
    {
        var span = transaction.StartChild("appium.upload-apk", "uploading apk to saucelabs");
        // var buildId = await client.UploadApkAsync(filePath, appName);
        span.Finish();
        // app = $"storage:{buildId}";
        app = $"storage:c4be83c3-a0f3-4bbd-9fb0-58b0f47f0cbf";
    }
    else
    {
        Console.WriteLine("Skipping apk upload");
    }

    UploadSymbolsOnSauceLabs(app, deviceToRun, transaction, client);

    var cacheSpan = transaction.StartChild("appium.cache-results", "caching results");
    deviceToRun.LastSymbolUploadRanTime = DateTime.UtcNow;
    await client.SaveResults(devices);
    cacheSpan.Finish();

    transaction.Finish();
}
catch (Exception e)
{
    SentrySdk.CaptureException(e);
    transaction.Finish(e);
    throw;
}
finally
{
    await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
}

return;

void UploadSymbolsOnSauceLabs(string app, SauceLabsDevice deviceToRun, ISpan span, SauceLabsClient client)
{
    var uploadSymbolsSpan = span.StartChild("appium.symbol.upload", "instructing app to start uploading symbols");
    span.SetData("device", deviceToRun.Id);

    var options = new AppiumOptions
    {
        PlatformName = "Android",
        DeviceName = deviceToRun.Id,
        AutomationName = "UiAutomator2",
        App = app,
    };

    options.AddAdditionalAppiumOption("appiumVersion", "stable");
    options.AddAdditionalAppiumOption("intentAction", "android.intent.action.MAIN");
    options.AddAdditionalAppiumOption("intentCategory", "android.intent.category.LAUNCHER");
    if (span.GetTraceHeader() is { } trace && !trace.TraceId.Equals(SentryId.Empty))
    {
        options.AddAdditionalAppiumOption("optionalIntentArguments", $"--es sentryTrace {trace}");
    }

    var sauceOptions = new Dictionary<string, object>
    {
        { "name", "CollectSymbolInstrumentation" },
        // appiumVersion is mandatory to use Appium 2
        { "appiumVersion", "stable" },
    };

    options.AddAdditionalAppiumOption("sauce:options", sauceOptions);
    options.AddAdditionalAppiumOption("appWaitActivity", "*");

    var driverSpan = uploadSymbolsSpan.StartChild("appium.start-driver", "Starting the Appium driver");
    var driver = client.GetDriver(options);
    driverSpan.Finish();

    try
    {
        Console.WriteLine("Starting symbol upload...");

        var totalWaitTimeSeconds = 40 * 60;
        var retryCounter = 200;
        var iterationTimeout = TimeSpan.FromSeconds(totalWaitTimeSeconds / retryCounter);

        var wait = new OpenQA.Selenium.Support.UI.WebDriverWait(driver, iterationTimeout);
        var uploadButton = wait.Until(d => d.FindElement(By.Id("io.sentry.symbolcollector.android:id/btnUpload")));
        if (uploadButton is null)
        {
            throw new Exception("Didn't find Collect symbols button");
        }

        uploadButton.Click();
        const string appTerminatedMessage = "App has been terminated";

        try
        {
            do
            {
                try
                {
                    _ = wait.Until(d => d.FindElement(
                        By.Id($"{appPackage}:id/done_text")));
                    Console.WriteLine("💯!");
                    return;
                }
                catch (WebDriverTimeoutException)
                {
                    var state = driver.GetAppState(appPackage);

                    if (state is AppState.NotRunning)
                    {
                        throw new Exception(appTerminatedMessage);
                    }

                    try
                    {
                        _ = driver.FindElement(By.Id($"{appPackage}:id/dialog_error"));

                        var dialogView = driver.FindElement(
                            By.Id($"{appPackage}:id/dialog_error"));
                        if (dialogView is not null)
                        {
                            Console.WriteLine("Failed collecting symbols:");
                            var dialogBody = driver.FindElement(
                                By.Id($"{appPackage}:id/dialog_body"));
                            throw new Exception(dialogBody.Text);
                        }
                    }
                    catch (NoSuchElementException)
                    {
                    }

                    Console.WriteLine($"Not done nor errored. Waiting {iterationTimeout}...");
                }
            } while (--retryCounter != 0);

            throw new TimeoutException($"Waited {totalWaitTimeSeconds} seconds but didn't complete.");
        }
        catch (WebDriverException e)
        {
            Console.WriteLine("WebDriver error, terminating the app: {0}", e);
            try
            {
                driver.TerminateApp(appPackage);
                Thread.Sleep(1000);
            }
            catch
            {
                // ignored - Might be dead already, or not.
            }

            RestartAppAndCrashRunner(e);
        }
        catch (Exception e) when (appTerminatedMessage.Equals(e.Message))
        {
            Console.WriteLine("App was not running: {0}", e);
            RestartAppAndCrashRunner(e);
        }

        void RestartAppAndCrashRunner(Exception e)
        {
            Console.WriteLine("Restarting the app");

            // Relaunch so we can capture any crashes stored on disk on the previous run
            driver.ActivateApp(appPackage);
            Thread.Sleep(3000);
            throw new Exception("Symbol collection failed.", e);
        }

        uploadSymbolsSpan.Finish();
    }
    catch (Exception e)
    {
        uploadSymbolsSpan.Finish(e);
        driver.FailJob();
        throw;
    }
    finally
    {
        driver.Quit();
    }
}
