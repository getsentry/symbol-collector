export apk=src/SymbolCollector.Android/bin/Debug/net6.0-android/io.sentry.symbolcollector.android-Signed.apk
export package=io.sentry.symbolcollector.android
rm $apk
dotnet build src/SymbolCollector.Android
adb uninstall $package
adb install -r $apk
adb shell am start -n $package/io.sentry.symbolcollector.MainActivity
