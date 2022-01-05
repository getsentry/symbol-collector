set -e

# Requirements: appcenter CLI. Env vars: SC_DEVICE_SET and SC_APP
# Optionally appcenter_batch_count to define how many batches to run. Default is 1.
# Builds the app and the UI tests in release mode and schedule it on appcenter

export appcenter_batch_count=${appcenter_batch_count:-1}
export apk_path=src/SymbolCollector.Android/bin/Release/net6.0-android/publish/io.sentry.symbolcollector.android-Signed.apk
rm $apk_path && echo deleted apk || echo apk not there

# Always a source of issues trying to find the Android SDK:
# \ Preparing tests... Object reference not set to an instance of an object
rm ~/.config/xbuild/monodroid-config.xml || echo monodroid config didnt exist

pushd src/SymbolCollector.Android/
dotnet publish -c Release
popd

pushd test/SymbolCollector.Android.UITests/
msbuild /restore /p:Configuration=Release /t:Build

pushd bin/Release/
for barch_number in $(seq 1 1 $appcenter_batch_count); do
	echo Running Batch \#$barch_number
    appcenter test run uitest --app $SC_APP \
        --devices $SC_DEVICE_SET \
        --app-path  ../../../../$apk_path \
        --test-series "master" \
        --locale "en_US" \
        --build-dir . \
        --async \
        --uitest-tools-dir ~/.nuget/packages/xamarin.uitest/3.2.2/tools/
done
