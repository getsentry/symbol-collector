namespace SymbolCollector.Runner;

public class LocalClient : IRunnerClient
{
    private AndroidDriver? _driver;
    public Task<string> UploadApkAsync(string apkPath, string appName)
    {
        // adv install
    }

    public AndroidDriver GetDriver(AppiumOptions options)
    {
        return _driver ??= new AndroidDriver(options);
    }

    public void Dispose()
    {
        _driver?.Dispose();
    }
}
