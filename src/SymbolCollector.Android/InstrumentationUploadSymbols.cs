using Android.Content;
using Android.Runtime;
using Android.Views;
using AndroidAPI = global::Android;

namespace SymbolCollector.Android;

[Instrumentation(Name = "io.sentry.symbolcollector.android.UploadSymbols",
    Label = "Upload Symbols")]
public class InstrumentationUploadSymbols : Instrumentation
{
    public static InstrumentationUploadSymbols Instance { get; private set; } = null!;
    private const string InstrumentationUploadSymbolTag = "InstrumentationUploadSymbol";

    public InstrumentationUploadSymbols(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer)
    {
        Instance = this;
    }
    public override void OnCreate(Bundle? arguments)
    {
        AndroidAPI.Util.Log.Error(InstrumentationUploadSymbolTag, "Starting");
        base.OnCreate(arguments);
        Start();
    }

    public override async void OnStart()
    {
        base.OnStart();

        var resultData = new Bundle();
        try
        {
            var intent = new Intent(Intent.ActionMain);

            intent.SetComponent(new ComponentName(
                "io.sentry.symbolcollector.android",
                Java.Lang.Class.FromType(typeof(MainActivity)).Name));
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);


            var activity = StartActivitySync(intent);
            if (activity is null)
            {
                resultData.PutString("result", "StartActivitySync failed");
                Finish(Result.Canceled, resultData);
                return; // Either this or ! on activity below since compiler doesn't know Finish() closes the app
            }

            WaitForIdleSync();

            await Task.Run(() => PressButton(activity));

            AndroidAPI.Util.Log.Info(InstrumentationUploadSymbolTag, "Instrumentation test completed successfully");
            resultData.PutString("result", "Instrumentation test completed successfully");
            Finish(Result.Ok, resultData);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            AndroidAPI.Util.Log.Error(InstrumentationUploadSymbolTag, ex.ToString());
            resultData.PutString("result", ex.ToString());
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
            Finish(Result.Canceled, resultData);
        }
    }

    private static void PressButton(Activity activity)
    {
        var btnUpload = activity.FindViewById<Button>(Resource.Id.btnUpload);

        var clickDone = new ManualResetEvent(false);
        activity.RunOnUiThread(() =>
        {
            btnUpload!.PerformClick();
            clickDone.Set();
        });
        clickDone.WaitOne();
        AndroidAPI.Util.Log.Info(InstrumentationUploadSymbolTag, "Clicked Upload. Waiting for batch completion");

        var totalWaitTimeSeconds = 40 * 60;
        var retryCounter = 200;
        var iterationTimeout = TimeSpan.FromSeconds(totalWaitTimeSeconds / retryCounter);
        do
        {
            // Did it complete?
            var doneText = activity.FindViewById<TextView>(Resource.Id.done_text);
            if (doneText is not null && doneText.Visibility == ViewStates.Visible)
            {
                return;
            }

            // Did it fail?
            var dialogView = activity.FindViewById<LinearLayout>(Resource.Id.dialog_error);
            if (dialogView is not null && dialogView.Visibility == ViewStates.Visible)
            {
                var dialogBody = activity.FindViewById<TextView>(Resource.Id.dialog_body);
                throw new Exception(dialogBody!.Text);
            }

            AndroidAPI.Util.Log.Debug(InstrumentationUploadSymbolTag, $"Not done nor errored. Waiting {iterationTimeout}...");
            Thread.Sleep(iterationTimeout);
        } while (--retryCounter != 0);

        throw new TimeoutException($"Waited {totalWaitTimeSeconds} seconds but didn't complete.");
    }
}
