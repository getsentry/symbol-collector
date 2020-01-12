using NUnit.Framework;
using NUnit.Framework.Internal;
using Xamarin.UITest;
using Xamarin.UITest.Android;

namespace SymbolCollector.Android.UITests
{
    [TestFixture]
    public class Tests
    {
        private AndroidApp _app = default!;

        [SetUp]
        public void BeforeEachTest()
        {
            _app = ConfigureApp
                .Android
                .StartApp();
        }

        [Test]
        public void AppLaunches()
        {
            _app.Screenshot("First screen.");
        }
    }
}
