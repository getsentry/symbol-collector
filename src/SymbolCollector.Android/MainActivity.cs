using Android.Content.PM;
using Android.OS;
using Android.Systems;
using Android.Views;
using Android.Views.InputMethods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SymbolCollector.Core;
using SymbolCollector.Android.Library;
using OperationCanceledException = System.OperationCanceledException;
using Host = SymbolCollector.Android.Library.Host;

namespace SymbolCollector.Android;

[Activity(
    Name = "io.sentry.symbolcollector.MainActivity",
    Label = "@string/app_name", MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Portrait)]
public class MainActivity : Activity
{
    private string _friendlyName = null!; // set on OnCreate
    private IHost _host = null!; // set on OnCreate
    private IServiceProvider _serviceProvider = null!; // set on OnCreate
    private ITransactionTracer _startupTransaction  = null!; // set on OnCreate

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Can't take a screenshot otherwise: System.NullReferenceException: The current Activity can not be detected. Ensure that you have called Init in your Activity or Application class.
        Microsoft.Maui.ApplicationModel.ActivityStateManager.Default.Init(this, savedInstanceState);

#pragma warning disable 618
        _friendlyName = $"Android:{Build.Manufacturer}-{Build.CpuAbi}-{Build.Model}";
#pragma warning restore 618
        _host = Host.Init(this, "https://656e2e78d37d4511a4ea2cb3602e7a65@sentry.io/5953206");
        _serviceProvider = _host.Services;

        var tran = SentrySdk.StartTransaction("AppStart", "activity.load");
        _startupTransaction = tran;
        AddSentryContext();

        var span = _startupTransaction.StartChild("OnCreate");
        try
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.activity_main);

            var footerText = (TextView)base.FindViewById(Resource.Id.footer)!;
#pragma warning disable CS0618 // GetPackageInfo is deprecated
            var versionName = Application.Context.ApplicationContext?.PackageManager?
                .GetPackageInfo(Application.Context.ApplicationContext?.PackageName ?? "", 0)?.VersionName;
#pragma warning restore CS0618
            footerText.Text = $"Version: {versionName}\n" + footerText.Text;

            var uploader = _serviceProvider.GetRequiredService<AndroidUploader>();
            var metrics = _serviceProvider.GetRequiredService<ClientMetrics>();
            var uploadButton = (Button)base.FindViewById(Resource.Id.btnUpload)!;
            var cancelButton = (Button)base.FindViewById(Resource.Id.btnCancel)!;
            var url = (EditText)base.FindViewById(Resource.Id.server_url)!;
            var source = new CancellationTokenSource();

            url.FocusChange += (sender, args) =>
            {
                if (!args.HasFocus)
                {
                    SentrySdk.AddBreadcrumb("Unfocus", category: "ui.event");
                    Unfocus();
                }
            };

            uploadButton.Click += OnUploadButtonOnClick;
            cancelButton.Click += OnCancelButtonOnClick;

            async void OnUploadButtonOnClick(object? sender, EventArgs args)
            {
                var uploadTransaction = SentrySdk.StartTransaction("BatchUpload", "batch.upload");
                try
                {
                    SentrySdk.AddBreadcrumb("OnUploadButtonOnClick", category: "ui.event");
                    var options = _serviceProvider.GetRequiredService<SymbolClientOptions>();
                    options.BaseAddress = new Uri(url.Text!); // TODO validate

                    SentrySdk.ConfigureScope(s => s.SetTag("server-endpoint", options.BaseAddress.AbsoluteUri));

                    Unfocus();

                    uploadButton.Enabled = false;
                    source = new CancellationTokenSource();

                    // var uploadTask = uploader.StartUpload(_friendlyName, source.Token);
                    var uploadTask = Task.Run(async () =>
                    {
                        await Task.Delay(10000);
                        throw new Exception("test failed upload");
                    });
                    var updateUiTask = StartUiUpdater(source.Token, metrics);

                    await UploadAsync(uploadTask, updateUiTask, metrics, cancelButton, uploadButton, uploadTransaction, source);
                }
                catch (Exception e)
                {
                    uploadTransaction.Finish(e);
                    throw;
                }
            }

            void OnCancelButtonOnClick(object? sender, EventArgs args)
            {
                SentrySdk.AddBreadcrumb("OnCancelButtonOnClick", category: "ui.event");
                Unfocus();
                source.Cancel();
            }

            span.Finish(SpanStatus.Ok);
            _startupTransaction.Finish(SpanStatus.Ok);
        }
        catch (Exception e)
        {
            span.Finish(e);
            _startupTransaction.Finish(e);
            throw;
        }
    }

    private void Unfocus()
    {
        if (CurrentFocus?.WindowToken is { } windowToken)
        {
            (GetSystemService(InputMethodService) as InputMethodManager)
                ?.HideSoftInputFromWindow(windowToken, 0);
        }
    }

    private async Task UploadAsync(
        Task uploadTask,
        Task updateUiTask,
        ClientMetrics metrics,
        View cancelButton,
        View uploadButton,
        ISpan span,
        CancellationTokenSource source)
    {
        var container = base.FindViewById(Resource.Id.metrics_container)!;
        container.Visibility = ViewStates.Visible;

        var doneText = (TextView)base.FindViewById(Resource.Id.done_text)!;
        var ranForLabel = (TextView)base.FindViewById(Resource.Id.ran_for_label)!;
        var ranForContainer = base.FindViewById(Resource.Id.ran_for_container)!;
        var ranForView = base.FindViewById(Resource.Id.ran_for_view)!;

        try
        {
            cancelButton.Enabled = true;
            await Task.WhenAny(uploadTask, updateUiTask);
            if (uploadTask.IsCompletedSuccessfully)
            {
                cancelButton.Enabled = false;
                uploadButton.Enabled = false;

                doneText.Visibility = ViewStates.Visible;
                ranForView.Visibility = ViewStates.Visible;
                ranForContainer.Visibility = ViewStates.Visible;

                ranForLabel.Text = metrics.RanFor.ToString();
                span.Finish(SpanStatus.Ok);
            }
            else if (uploadTask.IsFaulted)
            {
                ShowError(uploadTask.Exception);
                span.Finish(SpanStatus.InternalError);
            }
            else
            {
                cancelButton.Enabled = false;
                uploadButton.Enabled = true;
                span.Finish(SpanStatus.Cancelled);
            }
        }
        catch (Exception e)
        {
            ShowError(e);
            span.Finish(e);
        }
        finally
        {
            source.Cancel();
        }
    }

    private Task StartUiUpdater(CancellationToken token, ClientMetrics metrics) =>
        Task.Run(async () =>
        {
            var uploadedCount = (TextView)base.FindViewById(Resource.Id.uploaded_count)!;
            var startedTime = (TextView)base.FindViewById(Resource.Id.started_time)!;
            var alreadyExisted = (TextView)base.FindViewById(Resource.Id.already_existed)!;
            var filesProcessed = (TextView)base.FindViewById(Resource.Id.files_processed)!;
            var successfullyUpload = (TextView)base.FindViewById(Resource.Id.successfully_upload)!;
            var elfFiles = (TextView)base.FindViewById(Resource.Id.elf_files)!;
            var failedParsing = (TextView)base.FindViewById(Resource.Id.failed_parsing)!;
            var failedUploading = (TextView)base.FindViewById(Resource.Id.failed_uploading)!;
            var jobsInFlight = (TextView)base.FindViewById(Resource.Id.jobs_in_flight)!;
            var directoryNotFound = (TextView)base.FindViewById(Resource.Id.directory_not_found)!;
            var fileNotFound = (TextView)base.FindViewById(Resource.Id.file_not_found)!;
            var unauthorizedAccess = (TextView)base.FindViewById(Resource.Id.unauthorized_access)!;

            while (!token.IsCancellationRequested)
            {
                RunOnUiThread(() =>
                {
                    uploadedCount.Text = metrics.UploadedBytesCountHumanReadable();
                    startedTime.Text = metrics.StartedTime.ToString();
                    alreadyExisted.Text = metrics.AlreadyExistedCount.ToString();
                    filesProcessed.Text = metrics.FilesProcessedCount.ToString();
                    successfullyUpload.Text = metrics.SuccessfullyUploadCount.ToString();
                    elfFiles.Text = metrics.ElfFileFoundCount.ToString();
                    failedParsing.Text = metrics.FailedToParseCount.ToString();
                    failedUploading.Text = metrics.FailedToUploadCount.ToString();
                    jobsInFlight.Text = metrics.JobsInFlightCount.ToString();
                    directoryNotFound.Text = metrics.DirectoryDoesNotExistCount.ToString();
                    fileNotFound.Text = metrics.FileDoesNotExistCount.ToString();
                    unauthorizedAccess.Text = metrics.FileOrDirectoryUnauthorizedAccessCount.ToString();
                });
                try
                {
                    await Task.Delay(250, token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }, token);

    private async Task ShowError(Exception e)
    {
        if (e is AggregateException { InnerExceptions.Count: 1 } ae)
        {
            e = ae.InnerExceptions[0];
        }

        var uploadButton = (Button)base.FindViewById(Resource.Id.btnUpload)!;
        var cancelButton = (Button)base.FindViewById(Resource.Id.btnCancel)!;

        var sentryEvent = new SentryEvent(e);
        var message = $"Sentry id: \n{sentryEvent.EventId}\n\n{e}";

        var dialogView = FindViewById<LinearLayout>(Resource.Id.dialog_error);
        var dialogBody = FindViewById<TextView>(Resource.Id.dialog_body);
        dialogBody!.Text = message;
        var dismissBtn = FindViewById<Button>(Resource.Id.dialog_dismiss);

        dialogView!.Visibility = ViewStates.Visible;

        dismissBtn!.Click += (s, e) =>
        {
            uploadButton.Enabled = true;
            cancelButton.Enabled = false;
            dialogView.Visibility = ViewStates.Gone;
        };

        // Let the UI thread run for the dialog to show up
        await Task.Yield();

        SentrySdk.CaptureEvent(sentryEvent);
    }

    private void AddSentryContext()
    {
        StructUtsname? uname = null;
        try
        {
            uname = Os.Uname();
            _friendlyName += $"-kernel-{uname?.Release ?? "??"}";
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
            s.Transaction = _startupTransaction;

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
#if DEBUG
            s.SetTag("build-type", "debug");
            // It's 'production' by default otherwise
            s.Environment = "development";
#elif RELEASE
                s.SetTag("build-type", "release");
#else
                s.SetTag("build-type", "other");
#endif
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _host.Dispose();
    }
}
