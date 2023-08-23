set -e

export version=1.11.1
export apk=io.sentry.symbolcollector.android-Signed.apk
export localApk=apk/$version/$apk

# Requirements: appcenter CLI. Env vars: SC_DEVICE_SET and SC_APP
# Optionally appcenter_batch_count to define how many batches to run. Default is 1.
# Builds the app and the UI tests in release mode and schedule it on appcenter

export appcenter_batch_count=${appcenter_batch_count:-1}

# Always a source of issues trying to find the Android SDK:
# \ Preparing tests... Object reference not set to an instance of an object
rm ~/.config/xbuild/monodroid-config.xml || echo monodroid config didnt exist

pushd test/SymbolCollector.Android.UITests/
dotnet build -c Release -o uitest

pushd uitest/

if [ ! -f "$localApk" ]; then
  curl -L0 https://github.com/getsentry/symbol-collector/releases/download/$version/$apk --create-dirs -o $localApk
fi

for barch_number in $(seq 1 1 $appcenter_batch_count); do
	echo Running Batch \#$barch_number
    appcenter test run uitest --app $SC_APP \
        --devices $SC_DEVICE_SET \
        --app-path  $localApk \
        --test-series "master" \
        --locale "en_US" \
        --build-dir . \
        --async \
        --uitest-tools-dir .
done
