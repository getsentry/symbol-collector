export apk=src/SymbolCollector.Android/bin/Release/net9.0-android/io.sentry.symbolcollector.android-Signed.apk
export package=io.sentry.symbolcollector.android
rm $apk
dotnet build src/SymbolCollector.Android -c Release
adb uninstall $package
adb install -r $apk
adb shell am start -n $package/io.sentry.symbolcollector.MainActivity
