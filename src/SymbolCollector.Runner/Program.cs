// Runner uploads the apk by default (on current directory or under the apps' bin).
// To skip, pass 'skipUpload:true' as the first argument
var skipUpload = args.Any(a => a.Equals("skipUpload:true", StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"Starting runner (skipUpload:{skipUpload}...");

const string appName = "SymbolCollector.apk";
const string appPackage = "io.sentry.symbolcollector.android";
const string fullApkName = $"{appPackage}-Signed.apk";
const string solutionBuildApkPath = $"src/SymbolCollector.Android/bin/Release/net9.0-android/{fullApkName}";

// If running on demand, no job name is passed via env var
var cronJobName = Environment.GetEnvironmentVariable("CRON_JOB_NAME");
if (cronJobName is not null)
{
    Console.WriteLine("Running cron job: {0}", cronJobName);
}

string? filePath = null;
// if there's a one in the current directory, use that.
// in CI, scripts/download-latest-android-apk.ps1 will download only if there's a new version
// otherwise skip uploading apk since saucelabs uses the latest build already
if (File.Exists(fullApkName))
{
    filePath = fullApkName;
}
else if (File.Exists(solutionBuildApkPath))
{
    filePath = solutionBuildApkPath;
}

SentrySdk.Init(options =>
{
    options.Dsn = "https://ea58a7607ff1b39433af3a6c10365925@o1.ingest.us.sentry.io/4509420348964864";
    options.Debug = false;
    options.AutoSessionTracking = true;
    options.TracesSampleRate = 1.0;
});

var transaction = SentrySdk.StartTransaction("appium.runner", "runner appium to upload apk to saucelabs and collect symbols real devices");
SentrySdk.ConfigureScope(s => s.Transaction = transaction);
if (cronJobName is not null)
{
    SentrySdk.CaptureCheckIn(cronJobName, CheckInStatus.InProgress);
}

try
{
    using var client = new SauceLabsClient();
    var getDevicesSpan = transaction.StartChild("appium.cache-results", "caching results");
    var devices = await client.GetDevices();
    getDevicesSpan.Finish();
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

    if (!skipUpload)
    {
        if (filePath is null)
        {
            Console.WriteLine("'filePath' is null, skipping apk upload.");
        }
        else
        {
            Console.WriteLine("Uploading apk: {0}", filePath);

            var span = transaction.StartChild("appium.upload-apk", "uploading apk to saucelabs");
            var buildId = await client.UploadApkAsync(filePath, appName);
            span.Finish();
            app = $"storage:{buildId}";
        }
    }
    else
    {
        Console.WriteLine("'skipUpload' is true, skipping apk upload.");
    }

    // Saucelabs reports devices it actually doesn't have available ever. So we'll store in the cache whatever the result of the run, and skip to the next.
    // "All devices busy: Your test could not be executed, because the device type you requested was in high demand, and after a 15-minute search in our US-West data center, we couldn't find an available device for you."
    // Otherwise we stay retrying the same device day after day and going nowhere.
    var cacheSpan = transaction.StartChild("appium.cache-results", "caching results");
    deviceToRun.LastSymbolUploadRanTime = DateTime.UtcNow;
    await client.SaveResults(devices);
    Console.WriteLine($"Marked {deviceToRun.Id} with LastSymbolUploadRanTime: {deviceToRun.LastSymbolUploadRanTime}");
    cacheSpan.Finish();

    UploadSymbolsOnSauceLabs(app, deviceToRun, transaction, client);

    transaction.Finish();
    if (cronJobName is not null)
    {
        SentrySdk.CaptureCheckIn(cronJobName, CheckInStatus.Ok);
    }
}
catch (Exception e)
{
    SentrySdk.CaptureException(e);
    if (cronJobName is not null)
    {
        SentrySdk.CaptureCheckIn(cronJobName, CheckInStatus.Error);
    }
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
