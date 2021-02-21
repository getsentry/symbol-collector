using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Systems;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Sentry;
using Sentry.Extensibility;
using Sentry.Protocol;
using SymbolCollector.Core;
using AlertDialog = Android.App.AlertDialog;
using OperationCanceledException = System.OperationCanceledException;
using Xamarin.Essentials;

namespace SymbolCollector.Android
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true,
        ScreenOrientation = ScreenOrientation.Portrait)]
    public class MainActivity : AppCompatActivity
    {
        private string _friendlyName;
        private readonly IHost _host;
        private readonly IServiceProvider _serviceProvider;
        private readonly ITransaction _startupTransaction;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            var span = _startupTransaction.StartChild("OnCreate");
            try
            {
                base.OnCreate(savedInstanceState);
                Platform.Init(this, savedInstanceState);

                SetContentView(Resource.Layout.activity_main);

                var footerText = (TextView)base.FindViewById(Resource.Id.footer)!;
                var versionName = Application.Context.ApplicationContext?.PackageManager?
                    .GetPackageInfo(Application.Context.ApplicationContext?.PackageName ?? "", 0)?.VersionName;
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

                async void OnUploadButtonOnClick(object sender, EventArgs args)
                {
                    SentrySdk.AddBreadcrumb("OnUploadButtonOnClick", category: "ui.event");
                    var options = _serviceProvider.GetRequiredService<SymbolClientOptions>();
                    options.BaseAddress = new Uri(url.Text); // TODO validate

                    SentrySdk.ConfigureScope(s => s.SetTag("server-endpoint", options.BaseAddress.AbsoluteUri));

                    Unfocus();

                    uploadButton.Enabled = false;
                    source = new CancellationTokenSource();

                    var uploadTask = uploader.StartUpload(_friendlyName, source.Token);
                    var updateUiTask = StartUiUpdater(source.Token, metrics);

                    await UploadAsync(uploadTask, updateUiTask, metrics, cancelButton, uploadButton, source);
                }

                void OnCancelButtonOnClick(object sender, EventArgs args)
                {
                    SentrySdk.AddBreadcrumb("OnCancelButtonOnClick", category: "ui.event");
                    Unfocus();
                    source.Cancel();
                }

                span.Finish();
                _startupTransaction.Finish();
            }
            catch
            {
                // TODO: How do I pass the exception so it can connect span to error event later?
                span.Finish(SpanStatus.InternalError);
                _startupTransaction.Finish(SpanStatus.InternalError);
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
                }
                else if (uploadTask.IsFaulted)
                {
                    ShowError(uploadTask.Exception);
                }
                else
                {
                    cancelButton.Enabled = false;
                    uploadButton.Enabled = true;
                }
            }
            catch (Exception e)
            {
                ShowError(e);
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

        private void ShowError(Exception e)
        {
            if (e is null)
            {
                SentrySdk.CaptureMessage("ShowError called but no Exception instance provided.", SentryLevel.Error);
            }

            if (e is AggregateException ae && ae.InnerExceptions.Count == 1)
            {
                e = ae.InnerExceptions[0];
            }

            var uploadButton = (Button)base.FindViewById(Resource.Id.btnUpload)!;
            var cancelButton = (Button)base.FindViewById(Resource.Id.btnCancel)!;

            var lastEvent = SentrySdk.LastEventId;
            // TODO: SentryId.Empty should operator overload ==
            var message = SentryId.Empty.ToString() == lastEvent.ToString()
                ? e?.ToString() ?? "Something didn't quite work."
                : $"Sentry id {lastEvent}:\n{e}";

            var builder = new AlertDialog.Builder(this)
                .SetTitle("Error")
                ?.SetMessage(message)
                ?.SetNeutralButton("Ironic eh?", (o, eventArgs) =>
                {
                    uploadButton.Enabled = true;
                    cancelButton.Enabled = false;
                });
            if (builder is null)
            {
                throw new InvalidOperationException("Couldn't get a dialog built.");
            }

            builder.Show();
        }

        public MainActivity()
        {
#pragma warning disable 618
            _friendlyName = $"Android:{Build.Manufacturer}-{Build.CpuAbi}-{Build.Model}";
#pragma warning restore 618
            StructUtsname? uname = null;

            SentryXamarin.Init(o =>
            {
                o.TracesSampleRate = 1.0;
                o.MaxBreadcrumbs = 200;
                o.Debug = true;
                o.DiagnosticLevel = SentryLevel.Debug;
                o.AttachStacktrace = true;
#if DEBUG
                // It's 'production' by default otherwise
                o.Environment = "development";
#endif
                o.Dsn = "https://2262a4fa0a6d409c848908ec90c3c5b4@sentry.io/1886021";
                o.SendDefaultPii = true;

                // TODO: This needs to be built-in
                o.BeforeSend += @event =>
                {
                    const string traceIdKey = "TraceIdentifier";
                    switch (@event.Exception)
                    {
                        case var e when e is OperationCanceledException:
                            return null;
                        case var e when e?.Data.Contains(traceIdKey) == true:
                            @event.SetTag(traceIdKey, e.Data[traceIdKey]?.ToString() ?? "unknown");
                            break;
                    }

                    return @event;
                };
            });

            var tran = SentrySdk.StartTransaction("AppStart", "activity.load");
            _startupTransaction = tran;

            // TODO: This should be part of a package: Sentry.Xamarin.Android
            SentrySdk.ConfigureScope(s =>
            {
                s.Transaction = tran;
                s.User.Id = Build.Id;
#pragma warning disable 618
                s.Contexts.Device.Architecture = Build.CpuAbi;
#pragma warning restore 618
                s.Contexts.Device.Brand = Build.Brand;
                s.Contexts.Device.Manufacturer = Build.Manufacturer;
                s.Contexts.Device.Model = Build.Model;

                s.Contexts.OperatingSystem.KernelVersion = uname?.Release;

                s.SetTag("API", ((int)Build.VERSION.SdkInt).ToString());
                s.SetTag("app", "SymbolCollector.Android");
                s.SetTag("host", Build.Host ?? "?");
                s.SetTag("device", Build.Device ?? "?");
                s.SetTag("product", Build.Product ?? "?");
#pragma warning disable 618
                s.SetTag("cpu-abi", Build.CpuAbi ?? "?");
#pragma warning restore 618
                s.SetTag("fingerprint", Build.Fingerprint ?? "?");

#pragma warning disable 618
                if (!string.IsNullOrEmpty(Build.CpuAbi2))
#pragma warning restore 618
                {
#pragma warning disable 618
                    s.SetTag("cpu-abi2", Build.CpuAbi2 ?? "?");
#pragma warning restore 618
                }
#pragma warning restore 618

#if DEBUG
                s.SetTag("build-type", "debug");
#elif RELEASE
                s.SetTag("build-type", "release");
#else
                s.SetTag("build-type", "other");
#endif
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
                }
            });

            // Don't let logging scopes drop records TODO: review this API
            HubAdapter.Instance.LockScope();

            // TODO: doesn't the AppDomain hook is invoked in all cases?
            AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
            {
                e.Exception.Data[Mechanism.HandledKey] = e.Handled;
                e.Exception.Data[Mechanism.MechanismKey] = "UnhandledExceptionRaiser";
                SentrySdk.CaptureException(e.Exception);
                if (!e.Handled)
                {
                    SentrySdk.Close();
                }
            };

            var iocSpan = tran.StartChild("container.init", "Initializing the IoC container");
            var userAgent = "Android/" + GetType().Assembly.GetName().Version;
            _host = Startup.Init(c =>
            {
                // Can be removed once addressed: https://github.com/getsentry/sentry-dotnet/issues/824
                c.AddSingleton<IHttpMessageHandlerBuilderFilter, SentryHttpMessageHandlerBuilderFilter>();

                c.AddSingleton<AndroidUploader>();
                c.AddOptions().Configure<SymbolClientOptions>(o =>
                {
                    o.UserAgent = userAgent;
                    o.BlackListedPaths.Add("/system/build.prop");
                    o.BlackListedPaths.Add("/system/vendor/bin/netstat");
                    o.BlackListedPaths.Add("/system/vendor/bin/swapoff");
                });
                c.AddOptions().Configure<ObjectFileParserOptions>(o =>
                {
                    o.IncludeHash = true; // Backing store sorted format does not support hash distinction yet.
                    o.UseFallbackObjectFileParser = false; // Android only, use only ELF parser.
                });
            });
            iocSpan.Finish();
            _serviceProvider = _host.Services;

            SentrySdk.ConfigureScope(s =>
            {
                s.SetTag("user-agent", userAgent);
                s.SetTag("friendly-name", _friendlyName);
                s.AddAttachment(new ScreenshotAttachment());
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _host.Dispose();
        }

        private class ScreenshotAttachment : Attachment
        {
            public ScreenshotAttachment()
                : this(
                AttachmentType.Default,
                new ScreenshotAttachmentContent(),
                "screenshot",
                 "image/png")
            {
            }

            private ScreenshotAttachment(
                AttachmentType type,
                IAttachmentContent content,
                string fileName,
                string? contentType)
                : base(type, content, fileName, contentType)
            {
            }

            private class ScreenshotAttachmentContent : IAttachmentContent
            {
                public Stream GetStream()
                {
                    var screenshot = Screenshot.CaptureAsync().GetAwaiter().GetResult();
                    return screenshot.OpenReadAsync().GetAwaiter().GetResult();
                }
            }
        }
    }
}
