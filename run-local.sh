export apk=src/SymbolCollector.Android/bin/Debug/net6.0-android/io.sentry.symbolcollector.android-Signed.apk
rm $apk
dotnet build src/SymbolCollector.Android
adb uninstall io.sentry.symbolcollector.android
adb install -r $apk
adb shell am start -n io.sentry.symbolcollector.android/io.sentry.symbolcollector.MainActivity
