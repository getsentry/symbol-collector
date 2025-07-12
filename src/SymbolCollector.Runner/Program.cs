// Runner uploads the apk by default (on current directory or under the apps' bin).
// To skip, pass 'skipUpload:true' as the first argument
#pragma warning disable SENTRY0001
var skipUpload = args.Any(a => a.Equals("skipUpload:true", StringComparison.OrdinalIgnoreCase));

IntrLog.Info($"Starting runner (skipUpload:{skipUpload})...");

const string appName = "SymbolCollector.apk";
const string appPackage = "io.sentry.symbolcollector.android";
const string fullApkName = $"{appPackage}-Signed.apk";
const string solutionBuildApkPath = $"src/SymbolCollector.Android/bin/Release/net9.0-android/{fullApkName}";

// If running on demand, no job name is passed via env var
var cronJobName = Environment.GetEnvironmentVariable("CRON_JOB_NAME");
if (cronJobName is not null)
{
    IntrLog.Info("Running cron job: {0}", cronJobName);
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

    options.Experimental.EnableLogs = true;
});

var transaction = SentrySdk.StartTransaction("appium.runner", "runner appium to upload apk to saucelabs and collect symbols real devices");
SentrySdk.ConfigureScope(s => s.Transaction = transaction);
var jobId = SentryId.Empty;
if (cronJobName is not null)
{
    jobId = SentrySdk.CaptureCheckIn(cronJobName, CheckInStatus.InProgress);
}

try
{
    using var client = new SauceLabsClient();
    var getDevicesSpan = transaction.StartChild("appium.get-devices", "getting android devices");
    var devices = await client.GetDevices();
    getDevicesSpan.Finish();

    // Simply pick a random device
    var deviceToRun = devices[Random.Shared.Next(devices.Count)];
    IntrLog.Info("Randomly selected device: {0}", deviceToRun);

    var app = $"storage:filename={appName}";

    if (!skipUpload)
    {
        if (filePath is null)
        {
            IntrLog.Info("'filePath' is null, skipping apk upload.");
        }
        else
        {
            IntrLog.Info("Uploading apk: {0}", filePath);

            var span = transaction.StartChild("appium.upload-apk", "uploading apk to saucelabs");
            var buildId = await client.UploadApkAsync(filePath, appName);
            span.Finish();
            app = $"storage:{buildId}";
        }
    }
    else
    {
        IntrLog.Info("'skipUpload' is true, skipping apk upload.");
    }

    await UploadSymbolsOnSauceLabs(app, deviceToRun, transaction, client);

    transaction.Finish();
    if (cronJobName is not null)
    {
        SentrySdk.CaptureCheckIn(cronJobName, CheckInStatus.Ok, jobId);
    }
}
catch (Exception e)
{
    SentrySdk.CaptureException(e);
    if (cronJobName is not null)
    {
        SentrySdk.CaptureCheckIn(cronJobName, CheckInStatus.Error, jobId);
    }
    transaction.Finish(e);
    throw;
}
finally
{
    await SentrySdk.FlushAsync(TimeSpan.FromSeconds(5));
}

return;

async Task UploadSymbolsOnSauceLabs(string app, SauceLabsDevice deviceToRun, ISpan span, SauceLabsClient client)
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
        IntrLog.Info("Starting symbol upload...");

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
                    IntrLog.Info("💯!");
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
                            IntrLog.Error("Failed collecting symbols:");
                            var dialogBody = driver.FindElement(
                                By.Id($"{appPackage}:id/dialog_body"));
                            throw new Exception(dialogBody.Text);
                        }
                    }
                    catch (NoSuchElementException)
                    {
                    }

                    IntrLog.Info($"Not done nor errored. Waiting {iterationTimeout}...");
                }
            } while (--retryCounter != 0);

            throw new TimeoutException($"Waited {totalWaitTimeSeconds} seconds but didn't complete.");
        }
        catch (WebDriverException e)
        {
            IntrLog.Error("WebDriver error, terminating the app: {0}", e);
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
            IntrLog.Warning("App was not running: {0}", e);
            RestartAppAndCrashRunner(e);
        }

        void RestartAppAndCrashRunner(Exception e)
        {
            IntrLog.Info("Restarting the app");

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
        // quitting the appium driver kills the app immediately
        // before killing the app, give it 5 seconds to flush out the last frame of session replay
        await Task.Delay(TimeSpan.FromSeconds(5));
        await SentrySdk.FlushAsync();
        driver.Quit();
    }
}

static class IntrLog
{
    public static void Info(string message, params object[] args)
    {
        Console.WriteLine(message, args);
        SentrySdk.Experimental.Logger.LogInfo(message, args);
    }

    public static void Warning(string message, params object[] args)
    {
        Console.WriteLine(message, args);
        SentrySdk.Experimental.Logger.LogWarning(message, args);
    }

    public static void Error(string message, Exception? exception = null, params object[] args)
    {
        var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
        Console.WriteLine(formattedMessage);
        if (exception != null)
        {
            Console.WriteLine(exception);
            SentrySdk.Experimental.Logger.LogError($"{message} - {exception.Message}", args);
        }
        else
        {
            SentrySdk.Experimental.Logger.LogError(message, args);
        }
    }
}
