using Android.Content;
using Android.Runtime;
using AndroidAPI = global::Android;

namespace SymbolCollector.Android;

[Instrumentation(Name = "io.sentry.symbolcollector.android.UploadSymbols",
    Label = "Upload Symbols")]
public class InstrumentationUploadSymbols : Instrumentation
{
    public static InstrumentationUploadSymbols Instance { get; private set; } = null!;

    public InstrumentationUploadSymbols(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer)
    {
        Instance = this;
    }
    public override void OnCreate(Bundle? arguments)
    {
        AndroidAPI.Util.Log.Error("MyInstrumentation", "OnCreate Worked");
        base.OnCreate(arguments);
        Start();
    }

    public override void OnStart()
    {
        base.OnStart();

        var resultData = new Bundle();
        try
        {
            var intent = new Intent(Intent.ActionMain);

            intent.SetComponent(new ComponentName("io.sentry.symbolcollector.android", "io.sentry.symbolcollector.MainActivity"));
            intent.SetFlags(ActivityFlags.NewTask);

            var activity = StartActivitySync(intent);
            if (activity is null)
            {
                resultData.PutString("result", "StartActivitySync failed");
                Finish(Result.Canceled, resultData);
                return; // Either this, or ! on activity below since compiler doesn't know Finish() closes the app
            }

            WaitForIdleSync();

            PressButton(activity);

            resultData.PutString("result", "Instrumentation test completed successfully");
            Finish(Result.Ok, resultData);
        }
        catch (Exception ex)
        {
            AndroidAPI.Util.Log.Error("InstrumentationUploadSymbol", ex.ToString());
            resultData.PutString("result", ex.ToString());
            Finish(Result.Canceled, resultData);
        }
    }

    private void PressButton(Activity activity)
    {
        var resultData = new Bundle();
        var btnUpload = activity.FindViewById<Button>(Resource.Id.btnUpload);

        if (btnUpload is null)
        {
            resultData.PutString("result", "❌ Upload button not found");
            Finish(Result.Canceled, resultData);
            return;
        }

        // Run the click on the UI thread
        activity.RunOnUiThread(() => btnUpload.PerformClick());

        // TODO: Wait for 100!
        Thread.Sleep(2500);

        var totalWaitTimeSeconds = 40 * 60;
        var retryCounter = 200;
        var iterationTimeout = TimeSpan.FromSeconds(totalWaitTimeSeconds / retryCounter);
        while (true)
        {
            try
            {
                var doneText = activity.FindViewById<Button>(Resource.Id.done_text);
                if (doneText is not null)
                {
                    return;
                }

                // var alertTitle = activity.FindViewById<AlertDialog>(Resource.String.alert_title);
                // if (alertTitle is not null)
                {
                    // throw new Exception(alertTitle.)
                }

                Thread.Sleep(iterationTimeout);

                break;
            }
            catch (Exception e) when (e.InnerException is TimeoutException)
            {
                if (--retryCounter == 0)
                {
                    // _app.Screenshot("Timeout");
                    throw;
                }

                // Check if it failed
                // var result = _app.Query(p => p.Id("alertTitle"));
                // if (result?.Any() == true)
                // {
                //     // _app.Screenshot("Error");
                //     throw new Exception("Error modal found, app errored.");
                // }
            }
        }
    }
}
