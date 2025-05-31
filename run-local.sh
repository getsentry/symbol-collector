export apk=src/SymbolCollector.Android/bin/Release/net9.0-android/io.sentry.symbolcollector.android-Signed.apk
export package=io.sentry.symbolcollector.android
rm $apk
dotnet build src/SymbolCollector.Android -c Release
export PATH="$HOME/Library/Android/sdk/platform-tools:$PATH"

adb uninstall $package
adb install -r $apk
adb shell am instrument -r -w -e debug false io.sentry.symbolcollector.android/io.sentry.symbolcollector.android.UploadSymbols
