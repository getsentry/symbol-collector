using System.Net.Http.Headers;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

Console.WriteLine("Starting runner...");
const string appName = "SymbolCollector.apk";

SentrySdk.Init(options =>
{
    options.Dsn = "https://5e1a1d6f4244911acb405fc94c6e6b37@o117736.ingest.us.sentry.io/4509419352424448";
    // options.Debug = true;
    options.AutoSessionTracking = true;
});

var transaction = SentrySdk.StartTransaction("Runner", "runner");
SentrySdk.ConfigureScope(s => s.Transaction = transaction);

try
{
    var username = Environment.GetEnvironmentVariable("SAUCE_USERNAME") ?? throw new Exception("SAUCE_USERNAME is not set");
    var accessKey = Environment.GetEnvironmentVariable("SAUCE_ACCESS_KEY") ?? throw new Exception("SAUCE_ACCESS_KEY is not set");

    await UploadApkAsync(username, accessKey);
    await UploadSymbolsOnSauceLabs(username, accessKey);;

    transaction.Finish();
}
catch (Exception e)
{
    SentrySdk.CaptureException(e);
    transaction.Finish(e);
    throw;
}

return;

async Task UploadSymbolsOnSauceLabs(string username, string accessKey)
{
    const string driverUrl = "https://ondemand.us-west-1.saucelabs.com:443/wd/hub";

    var options = new AppiumOptions
    {
        PlatformName = "Android",
        DeviceName = "Google.*", // TODO: Get devices
        PlatformVersion = "13",
        AutomationName = "UiAutomator2",
        App = $"storage:filename={appName}",
    };

    var sauceOptions = new Dictionary<string, object>
    {
        { "username", username },
        { "accessKey", accessKey },
        { "name", "CollectSymbolInstrumentation" },
    };

    options.AddAdditionalAppiumOption("sauce:options", sauceOptions);
    options.AddAdditionalAppiumOption("appWaitActivity", "*");

    var driver = new AndroidDriver(new Uri(driverUrl), options, TimeSpan.FromMinutes(10));

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

        try
        {
            do
            {
                try
                {
                    _ = wait.Until(d => d.FindElement(
                        By.Id("io.sentry.symbolcollector.android:id/done_text")));
                    Console.WriteLine("💯!");
                    return;
                }
                catch (WebDriverTimeoutException)
                {
                    wait.Timeout = TimeSpan.FromMicroseconds(500);
                    try
                    {
                        var dialogView = wait.Until(d => d.FindElement(
                            By.Id("io.sentry.symbolcollector.android:id/dialog_error")));
                        if (dialogView is not null && dialogView.Displayed)
                        {
                            Console.WriteLine("Failed collecting symbols:");
                            var dialogBody = wait.Until(d => d.FindElement(
                                By.Id("io.sentry.symbolcollector.android:id/dialog_body")));
                            Console.WriteLine(dialogBody!.Text);
                            throw new Exception(dialogBody!.Text);
                        }
                    }
                    catch (WebDriverTimeoutException)
                    {
                    }
                    finally
                    {
                        wait.Timeout = iterationTimeout;
                    }

                    Console.WriteLine($"Not done nor errored. Waiting {iterationTimeout}...");
                }
            } while (--retryCounter != 0);

            throw new TimeoutException($"Waited {totalWaitTimeSeconds} seconds but didn't complete.");
        }
        catch (WebDriverException e)
        {
            Console.WriteLine("App might have crashed: {0}", e);
            Console.WriteLine("Restarting the app");
            try
            {
                driver.TerminateApp("io.sentry.symbolcollector.android");
            }
            catch
            {
                // ignored - Might be dead already, or not.
            }

            Thread.Sleep(1000);

            // Relaunch so we can capture any crashes stored on disk on the previous run
            driver.ActivateApp("io.sentry.symbolcollector.android");
            Thread.Sleep(3000);
            throw new Exception("Symbol collection failed.", e);
        }
    }
    finally
    {
        driver.Quit();
    }
}

async Task UploadApkAsync(string username, string accessKey)
{
    const string sauceUrl = "https://api.us-west-1.saucelabs.com/v1/storage/upload";
    const string filePath = "../src/SymbolCollector.Android/bin/Release/net9.0-android/io.sentry.symbolcollector.android-Signed.apk";

    using var client = new HttpClient();
    var byteArray = System.Text.Encoding.ASCII.GetBytes($"{username}:{accessKey}");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

    using var form = new MultipartFormDataContent();

    var fileBytes = await File.ReadAllBytesAsync(filePath);
    var fileContent = new ByteArrayContent(fileBytes);
    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
    form.Add(fileContent, "payload", appName);
    form.Add(new StringContent(appName), "name");
    form.Add(new StringContent("true"), "overwrite");

    Console.WriteLine("Uploading APK to Sauce Labs...");

    var response = await client.PostAsync(sauceUrl, form);
    var result = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"Response: {(int)response.StatusCode} {response.ReasonPhrase}");
    Console.WriteLine(result);

    if (!response.IsSuccessStatusCode)
    {
        throw new Exception($"Failed to upload APK to Sauce Labs: {result}");
    }
}


