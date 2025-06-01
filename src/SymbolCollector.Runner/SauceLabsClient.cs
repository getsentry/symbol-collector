using System.Net;
using OpenQA.Selenium.Remote;

namespace SymbolCollector.Runner;

public class SauceLabsClient
{
    private const string DriverUrl = "https://ondemand.us-west-1.saucelabs.com:443/wd/hub";
    private HttpClient? _client;
    private AndroidDriver? _driver;
    private readonly string _username;
    private readonly string _accessKey;

    public AndroidDriver CreateDriver(AppiumOptions options)
    {
        if (_driver is not null)
        {
            return _driver;
        }
        return _driver = new AndroidDriver(
            new SentryHttpCommandExecutor(
                HttpClient,
                new Uri(DriverUrl),
                TimeSpan.FromSeconds(2),
                // TimeSpan.FromSeconds(5),
                true),
            options);
        // return _driver = new AndroidDriver(new Uri(DriverUrl), options, TimeSpan.FromMinutes(10));
    }

    public HttpClient HttpClient => _client ??= CreateHttpClient(_username, _accessKey);

    public SauceLabsClient() : base()
    {
        _username = Environment.GetEnvironmentVariable("SAUCE_USERNAME") ??
                       throw new Exception("SAUCE_USERNAME is not set");
        _accessKey = Environment.GetEnvironmentVariable("SAUCE_ACCESS_KEY") ??
                        throw new Exception("SAUCE_ACCESS_KEY is not set");

    }

    private static HttpClient CreateHttpClient(string username, string accessKey)
    {
        var handler = new HttpClientHandler();
        // handler.Credentials = new NetworkCredential(username, accessKey);
        // handler.PreAuthenticate = true;

        var sentryHandler = new SentryHttpMessageHandler(handler);

        var client = new HttpClient(sentryHandler);
        // var byteArray = System.Text.Encoding.ASCII.GetBytes($"{username}:{accessKey}");
        // client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        // client.DefaultRequestHeaders.UserAgent.ParseAdd("selenium/{0} (.net Appium)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, image/png");
        client.DefaultRequestHeaders.ExpectContinue = false;
        // if (!this.IsKeepAliveEnabled)
            client.DefaultRequestHeaders.Connection.ParseAdd("close");
        // client.Timeout = this.serverResponseTimeout;

        return client;
    }

    // public void Dispose() => _client.Dispose();
}

public class SentryHttpCommandExecutor : HttpCommandExecutor
{
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public SentryHttpCommandExecutor(HttpClient client, Uri addressOfRemoteServer, TimeSpan timeout)
        : base(addressOfRemoteServer, timeout)
    {
        _timeout = timeout;
        _client = client;
    }

    public SentryHttpCommandExecutor(HttpClient client, Uri addressOfRemoteServer, TimeSpan timeout, bool enableKeepAlive)
        : base(addressOfRemoteServer, timeout, enableKeepAlive)
    {
        _client = client;
        _timeout = timeout;
    }

    protected override HttpClient CreateHttpClient()
    {
        return _client;
        var clientHandler = CreateHttpClientHandler();
        // var handler = new SentryHttpMessageHandler(clientHandler);
        // var httpClient = new HttpClient(handler);
        // httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(this.UserAgent);
        // httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json, image/png");
        // httpClient.DefaultRequestHeaders.ExpectContinue = false;
        // if (!IsKeepAliveEnabled)
        // {
        //     httpClient.DefaultRequestHeaders.Connection.ParseAdd("close");
        // }
        // httpClient.Timeout = _timeout;
        // return httpClient;
    }
}
