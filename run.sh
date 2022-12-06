set -e

# Requirements: appcenter CLI. Env vars: SC_DEVICE_SET and SC_APP
# Optionally appcenter_batch_count to define how many batches to run. Default is 1.
# Builds the app and the UI tests in release mode and schedule it on appcenter

export appcenter_batch_count=${appcenter_batch_count:-1}
export apk_path=src/SymbolCollector.Android/uitest/io.sentry.symbolcollector.android-Signed.apk
rm $apk_path && echo deleted apk || echo apk not there

# Always a source of issues trying to find the Android SDK:
# \ Preparing tests... Object reference not set to an instance of an object
rm ~/.config/xbuild/monodroid-config.xml || echo monodroid config didnt exist

pushd src/SymbolCollector.Android/
dotnet publish -c Release -o uitest
popd

pushd test/SymbolCollector.Android.UITests/
dotnet build -c Release -o uitest

pushd uitest/
cp ../../../../$apk_path .
for barch_number in $(seq 1 1 $appcenter_batch_count); do
	echo Running Batch \#$barch_number
    appcenter test run uitest --app $SC_APP \
        --devices $SC_DEVICE_SET \
        --app-path  *.apk \
        --test-series "master" \
        --locale "en_US" \
        --build-dir . \
        --async \
        --uitest-tools-dir .
done
