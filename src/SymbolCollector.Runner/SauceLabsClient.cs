using System.Net;
using OpenQA.Selenium.Remote;

namespace SymbolCollector.Runner;

public class SauceLabsClient : IDisposable
{
    private const string UploadFileUrl = "https://api.us-west-1.saucelabs.com/v1/storage/upload";
    private const string DriverUrl = "https://ondemand.us-west-1.saucelabs.com:443/wd/hub";
    private HttpClient? _client;
    private AndroidDriver? _driver;
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(2);
    private readonly string _username = Environment.GetEnvironmentVariable("SAUCE_USERNAME") ??
                                        throw new Exception("SAUCE_USERNAME is not set");
    private readonly string _accessKey = Environment.GetEnvironmentVariable("SAUCE_ACCESS_KEY") ??
                                         throw new Exception("SAUCE_ACCESS_KEY is not set");

    public AndroidDriver GetDriver(AppiumOptions options) =>
        _driver ??= new AndroidDriver(
            new SentryHttpCommandExecutor(
                HttpClient,
                new Uri(DriverUrl),
                _timeout,
                true),
            options);

    public HttpClient HttpClient => _client ??= CreateHttpClient(_username, _accessKey);

    private HttpClient CreateHttpClient(string username, string accessKey)
    {
        var handler = new HttpClientHandler();
        var sentryHandler = new SentryHttpMessageHandler(handler);

        var client = new HttpClient(sentryHandler);
        var byteArray = System.Text.Encoding.ASCII.GetBytes($"{username}:{accessKey}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, image/png");
        client.DefaultRequestHeaders.ExpectContinue = false;
        client.Timeout = _timeout;

        return client;
    }

    public async Task<string> UploadApkAsync(string apkPath, string appName)
    {
        using var form = new MultipartFormDataContent();

        var fileBytes = await File.ReadAllBytesAsync(apkPath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        form.Add(fileContent, "payload", appName);
        form.Add(new StringContent(appName), "name");
        form.Add(new StringContent("true"), "overwrite");

        Console.WriteLine("Uploading APK to device farm...");

        var response = await HttpClient.PostAsync(UploadFileUrl, form);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to upload APK to device farm: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var result = await response.Content.ReadFromJsonAsync<AppUploadResult>();
        var id = result!.Item.Id;
        Console.WriteLine("App uploaded successfully. Id: {0}", id);
        return id;
    }

    public void Dispose()
    {
        _driver?.Dispose();
        _client?.Dispose();
    }
}

public class SentryHttpCommandExecutor(
    HttpClient client,
    Uri addressOfRemoteServer,
    TimeSpan timeout,
    bool enableKeepAlive)
    : HttpCommandExecutor(addressOfRemoteServer, timeout, enableKeepAlive)
{
    protected override HttpClient CreateHttpClient() => client;
}

class AppUploadResult
{
    public ItemResult Item { get; set; } = null!;
    public class ItemResult
    {
        public string Id { get; set; } = null!;
    }
}

static class AndroidDriverExtensions
{
    public static void FailJob(this IWebDriver driver)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript("sauce:job-result=failed");
    }
}
