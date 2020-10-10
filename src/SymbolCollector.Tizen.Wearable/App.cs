using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Xamarin.Forms;
using Tizen.Wearable.CircularUI.Forms;
using Sentry;

namespace SymbolCollector.Tizen.Wearable
{
    public class App : Application
    {
        public App()
        {
            // The root page of your application
            MainPage = new CirclePage
            {
                Content = new StackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children = {
                        new Label {
                            HorizontalTextAlignment = TextAlignment.Center,
                            Text = "Welcome to Xamarin Forms!"
                        }
                    }
                }
            };

#pragma warning disable CS8597 // Thrown value may be null.
            //throw null;
#pragma warning restore CS8597 // Thrown value may be null.
        }

        protected override void OnStart()
        {
            // Handle when your app starts
            SentrySdk.AddBreadcrumb("OnStart", "app.lifecycle");
        }

        protected override void OnSleep()
        {
            SentrySdk.AddBreadcrumb("OnSleep", "app.lifecycle");
        }

        protected override void OnResume()
        {
            SentrySdk.AddBreadcrumb("OnResume", "app.lifecycle");
        }
    }
}
