using System.Diagnostics;

namespace SymbolCollector.Runner;

public class LocalClient : IRunnerClient
{
    private AndroidDriver? _driver;
    public async Task<string> UploadApkAsync(string apkPath, string appName)
    {
        // adv install
        var process = Process.Start(new ProcessStartInfo("adb", $"install -r {apkPath}"));
        if (process is null) throw new Exception("Failed to start adb");

        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);
        return "ok";
    }

    public AndroidDriver GetDriver(AppiumOptions options) =>
        _driver ??= new AndroidDriver(
            new SentryHttpCommandExecutor(
                HttpClient,
                new Uri(DriverUrl),
                _timeout,
                true),
            options);

    public void Dispose()
    {
        _driver?.Dispose();
    }
}
