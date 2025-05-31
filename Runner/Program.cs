using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.Enums;

Console.WriteLine("Starting runner...");
const string appName = "SymbolCollector.apk";
const string appPackage = "io.sentry.symbolcollector.android";

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

    var app = $"storage:filename={appName}";

    if (args.Length > 0 && bool.TryParse(args[0], out var uploadApp) && uploadApp)
    {
        var buildId = await UploadApkAsync(username, accessKey);
        // Run on this specific app
        app = $"storage:{buildId}";
    }
    UploadSymbolsOnSauceLabs(username, accessKey, app);

    transaction.Finish();
}
catch (Exception e)
{
    SentrySdk.CaptureException(e);
    transaction.Finish(e);
    throw;
}

return;

void UploadSymbolsOnSauceLabs(string username, string accessKey, string app)
{
    const string driverUrl = "https://ondemand.us-west-1.saucelabs.com:443/wd/hub";

    var options = new AppiumOptions
    {
        PlatformName = "Android",
        DeviceName = "Google.*", // TODO: Get devices
        PlatformVersion = "13",
        AutomationName = "UiAutomator2",
        App = app,
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
        var appTerminatedMessage = "App has been terminated";

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
        catch (Exception e)  when (appTerminatedMessage.Equals(e.Message))
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
    }
    finally
    {
        driver.Quit();
    }
}

async Task<string> UploadApkAsync(string username, string accessKey)
{
    const string sauceUrl = "https://api.us-west-1.saucelabs.com/v1/storage/upload";
    const string filePath = $"../src/SymbolCollector.Android/bin/Release/net9.0-android/{appPackage}-Signed.apk";

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

    if (!response.IsSuccessStatusCode)
    {
        throw new Exception($"Failed to upload APK to Sauce Labs: {(int)response.StatusCode} {response.ReasonPhrase}");
    }


    var result = await response.Content.ReadFromJsonAsync<AppUploadResult>();
    var id = result!.Item.Id;
    Console.WriteLine("App uploaded successfully. Id: {0}", id);
    return id;
}

class AppUploadResult
{
    // {"item": {"id": "9cfe4d59-a83c-40af-8cda-4505e6023f77", "owner": {"id": "a51fe61e81024cbe81e90e218d01e762", "org_id": "bd19f16814d9436ba0e0caa55ce401b4"}, "name": "SymbolCollector.apk", "upload_timestamp": 1748721660, "etag": "CPeRk+u/zo0DEAE="
    public ItemResult Item { get; set; } = null!;
    public class ItemResult
    {
        public string Id { get; set; } = null!;
    }
}

