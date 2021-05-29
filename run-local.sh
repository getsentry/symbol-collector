export apk=src/SymbolCollector.Android/bin/Debug/net6.0-android/io.sentry.symbolcollector.android-Signed.apk
rm $apk
dotnet build src/SymbolCollector.Android
adb install -r $apk
