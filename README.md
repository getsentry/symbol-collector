<p align="center">
  <a href="https://sentry.io" target="_blank" align="left">
    <img src="https://raw.githubusercontent.com/getsentry/sentry-unity/main/.github/sentry-wordmark-dark-400x119.svg?utm_source=github&utm_medium=logo" width="280">
  </a>
  <br />
</p>
<p align="center">

# Symbol Collector
[![build](https://github.com/getsentry/symbol-collector/workflows/build/badge.svg?branch=main)](https://github.com/getsentry/symbol-collector/actions?query=branch%3Amain)
[![codecov](https://codecov.io/gh/getsentry/symbol-collector/branch/main/graph/badge.svg)](https://codecov.io/gh/getsentry/symbol-collector)
[![Discord Chat](https://img.shields.io/discord/621778831602221064.svg)](https://discord.gg/Ww9hbqr)

Collect system symbols from different devices like Android, macOS, Linux, etc.
It involves a server that writes the symbols to Google cloud storage and a set of clients.

![Symbol Collector on a device farm](.github/SymbolCollector.png?raw=true "Devices")

## Uploading symbols

### Client applications

Current clients are:

* Android
* macOS
* Linux

The client applications will parse files and make sure they are valid ELF, Mach-O, Fat Binary, etc.
Besides that, before uploading it to the server, it will make a `HEAD` request with the image _build id_ to make sure
this file is still missing, to avoid wasting time and bandwidth uploading redundant files.

Looking for system images in the filesystem and the HTTP requests happen in parallel, so to go through GBs and thousands of files takes only a few seconds.
Finally, the client apps will upload its internal metrics to help reconcile the batch results and troubleshoot any issues.


### cURL
Although using the client programs is strongly recommended, it's possible to upload files via HTTP.

For example, uploading a batch of Android symbols:

1. Create a batch:

```sh
export batchId=$(uuidgen)
export batchFriendlyName="Android 4.4.4 - Sony Xperia"
export batchType="Android"
export body='{"BatchFriendlyName":"'$batchFriendlyName'","BatchType":"'$batchType'"}'
export server=http://localhost:5000

curl -sD - --header "Content-Type: application/json" --request POST \
  --data "$body" \
  $server/symbol/batch/$batchId/start
```
2. Upload files:

```sh
curl -i \
  -F "libxamarin-app-arm64-v8a.so=@test/TestFiles/libxamarin-app-arm64-v8a.so" \
  -F "libxamarin-app.so=@test/TestFiles/libxamarin-app.so" \
  $server/symbol/batch/$batchId/upload
```
3. Close batch (without providing metrics): 

```sh
curl -sD - --header "Content-Type: application/json" --request POST \
  --data "{}" \
  $server/symbol/batch/$batchId/close
```

## Why are you doing this?

In order to stack unwind from a memory dump, every loaded image involved in the call stack needs to be available.
Unwind information is not in the debug files but in the libraries instead.
This project allows collecting these libraries so that native crash processing can be done on the backend as opposed to stackwalking on the client.

## Releases

The [Releases page in this repository](https://github.com/getsentry/symbol-collector/releases) include builds for Android (apk), native CLIs for macOS and Linux (self contained), and the server.

## Building from source

### Dependencies

This project includes an Android app, an ASP.NET Core server and a Console apps as CLI clients for macOS and Linux.
The build script `build.sh` is focused on building **all** the components which means you'd need all the dependencies below.

#### Server and Console app

To build the Server, Libraries and the Console app (aka: everything except the Android app) you'll need:
* [.NET 8 SDK](https://dot.net)

#### Android app

To build the Android project you need:
* JDK 11
* .NET Android workload: `dotnet workload install android`

# Resources

* [Droidcon talk that foreshadows this project](https://player.vimeo.com/video/380844400)
* [Blog post about native crash reporting support](https://blog.sentry.io/2019/09/26/fixing-native-apps-with-sentry).
* [Support Android native (NDK) crashes](https://blog.sentry.io/2019/11/25/adding-native-support-to-our-android-sdk/)
* Sentry's [Symbolicator project](https://github.com/getsentry/symbolicator).
