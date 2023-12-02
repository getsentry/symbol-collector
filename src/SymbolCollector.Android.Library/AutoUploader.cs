using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Systems;
using Microsoft.Extensions.DependencyInjection;
using Sentry;
using SymbolCollector.Core;

namespace SymbolCollector.Android.Library;

/// <summary>
/// Automatically collects symbols with sentry.io's collector server.
/// </summary>
[Register("io/sentry/symbolcollector/AutoUploader")]
public class AutoUploader : Java.Lang.Object
{
    /// <summary>
    /// Run symbol collection.
    /// </summary>
    public void Run(Context context)
    {
        var host = Host.Init(context, "https://656e2e78d37d4511a4ea2cb3602e7a65@sentry.io/5953206");

        var tran = SentrySdk.StartTransaction("SymbolUpload", "symbol.upload");

        SentrySdk.ConfigureScope(s => s.Transaction = tran);
        // 'BatchUpload' transaction could connect FE+BE on batch start/end HTTP calls

        var options = host.Services.GetRequiredService<SymbolClientOptions>();
        options.BaseAddress = new Uri("https://symbol-collector.services.sentry.io");

        SentrySdk.ConfigureScope(s => s.SetTag("server-endpoint", options.BaseAddress.AbsoluteUri));

        var source = new CancellationTokenSource();

        var uploader = host.Services.GetRequiredService<AndroidUploader>();

#pragma warning disable 618
        var friendlyName = $"Android:{Build.Manufacturer}-{Build.CpuAbi}-{Build.Model}";
#pragma warning restore 618

        StructUtsname? uname = null;
        try
        {
            uname = Os.Uname();
            friendlyName += $"-kernel-{uname?.Release ?? "??"}";
        }
        catch (Exception e)
        {
            SentrySdk.AddBreadcrumb("Couldn't run uname", category: "exec",
                data: new Dictionary<string, string> {{"exception", e.Message}}, level: BreadcrumbLevel.Error);
            // android.runtime.JavaProxyThrowable: System.NotSupportedException: Could not activate JNI Handle 0x7ed00025 (key_handle 0x4192edf8) of Java type 'md5eb7159ad9d3514ee216d1abd14b6d16a/MainActivity' as managed type 'SymbolCollector.Android.MainActivity'. --->
            // Java.Lang.NoClassDefFoundError: android/system/Os ---> Java.Lang.ClassNotFoundException: Didn't find class "android.system.Os" on path: DexPathList[[zip file "/data/app/SymbolCollector.Android.SymbolCollector.Android-1.apk"],nativeLibraryDirectories=[/data/app-lib/SymbolCollector.Android.SymbolCollector.Android-1, /vendor/lib, /system/lib]]
        }

        SentrySdk.ConfigureScope(s =>
        {
            if (uname is { })
            {
                s.Contexts["uname"] = new
                {
                    uname.Machine,
                    uname.Nodename,
                    uname.Release,
                    uname.Sysname,
                    uname.Version
                };
                s.Contexts.OperatingSystem.KernelVersion = uname.Release;
            }
        });
        var uploadTask = uploader.StartUpload(friendlyName, source.Token);
        uploadTask.ContinueWith(t =>
        {
            tran.Finish(t.IsCompletedSuccessfully ? SpanStatus.Ok : SpanStatus.UnknownError);
            // ReSharper disable once MethodSupportsCancellation - Don't want to cancel
            SentrySdk.FlushAsync(TimeSpan.FromSeconds(5)).Wait();
        });
    }
}