set -xe

# Requirements: appcenter CLI. Env vars: SC_DEVICE_SET and SC_APP
# Builds the app and the UI tests in release mode and schedule it on appcenter

export apk_path=src/SymbolCollector.Android/bin/Release/io.sentry.symbol.collector-Signed.apk
rm $apk_path && echo deleted apk || echo apk not there

pushd src/SymbolCollector.Android/
msbuild /restore /p:Configuration=Release \
	/p:AndroidBuildApplicationPackage=true \
	/t:Clean\;Build\;SignAndroidPackage
popd

pushd test/SymbolCollector.Android.UITests/
msbuild /restore /p:Configuration=Release /t:Build

pushd bin/Release/
appcenter test run uitest --app $SC_APP \
    --devices $SC_DEVICE_SET \
    --app-path  ../../../../$apk_path \
    --test-series "master" \
    --locale "en_US" \
    --build-dir . \
    --uitest-tools-dir ~/.nuget/packages/xamarin.uitest/3.0.17/tools/
