using OpenQA.Selenium.Remote;

public class SauceLabsClient : IDisposable
{
    private const string SauceLabsBaseDomainWithRegion = "us-west-1.saucelabs.com";
    private const string SauceLabsApiBaseAddress = "https://api." + SauceLabsBaseDomainWithRegion;
    private const string DriverUrl = "https://ondemand." + SauceLabsBaseDomainWithRegion + ":443/wd/hub";
    private const string UploadFileUrl = SauceLabsApiBaseAddress + "/v1/storage/upload";
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

    public async Task<List<SauceLabsDevice>> GetDevices()
    {
        return await GetAndroidRealDevices();
    }

    private async Task<List<SauceLabsDevice>> GetAndroidRealDevices()
    {
        var response = await HttpClient.GetAsync($"{SauceLabsApiBaseAddress}/v1/rdc/devices");
        response.EnsureSuccessStatusCode();

        var devices = await response.Content.ReadFromJsonAsync<List<SauceLabsDevice>>();
        if (devices is null)
        {
            throw new Exception("Failed to parse response while getting devices");
        }

        Console.WriteLine("{0} total real devices found. Filtering by Android and sorting by API level..", devices.Count);
        var androidRealDevices = devices
            .Where(d => string.Equals(d.Os, "android", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.ApiLevel)
            .ToList();
        Console.WriteLine("Got a list of {0} Android devices", androidRealDevices.Count);
        return androidRealDevices;
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

public class SauceLabsDevice
{
    // Defined by the API
    public string? AbiType { get; set; }
    public int ApiLevel { get; set; }
    public int CpuCores { get; set; }
    public int CpuFrequency { get; set; }
    public string? DefaultOrientation { get; set; }
    public int Dpi { get; set; }
    public bool HasOnScreenButtons { get; set; }
    public string? Id { get; set; }
    public string? InternalOrientation { get; set; }
    public int InternalStorageSize { get; set; }
    public bool IsArm { get; set; }
    public bool IsKeyGuardDisabled { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsRooted { get; set; }
    public bool IsTablet { get; set; }
    public List<string>? Manufacturer { get; set; }
    public string? ModelNumber { get; set; }
    public string? Name { get; set; }
    public string? Os { get; set; }
    public string? OsVersion { get; set; }
    public double PixelsPerPoint { get; set; }
    public int RamSize { get; set; }
    public int ResolutionHeight { get; set; }
    public int ResolutionWidth { get; set; }
    public double ScreenSize { get; set; }
    public int SdCardSize { get; set; }
    public bool SupportsAppiumWebAppTesting { get; set; }
    public bool SupportsGlobalProxy { get; set; }
    public bool SupportsMinicapSocketConnection { get; set; }
    public bool SupportsMockLocations { get; set; }
    public string? CpuType { get; set; }
    public string? DeviceFamily { get; set; }
    public string? DpiName { get; set; }
    public bool IsAlternativeIoEnabled { get; set; }
    public bool SupportsManualWebTesting { get; set; }
    public bool SupportsMultiTouch { get; set; }
    public bool SupportsXcuiTest { get; set; }

    public override string ToString() => $"id:{Id} - name:{Name} - API: {ApiLevel}";
}
