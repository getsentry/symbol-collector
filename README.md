<p align="center">
  <a href="https://sentry.io" target="_blank" align="center">
    <img src="https://sentry-brand.storage.googleapis.com/sentry-logo-black.png" width="280">
  </a>
  <br />
</p>

# Symbol Collector 
[![Travis](https://travis-ci.org/getsentry/sentry-dotnet.svg?branch=master)](https://travis-ci.org/getsentry/sentry-dotnet)
[![AppVeyor](https://ci.appveyor.com/api/projects/status/gldfulfd5kk2stst/branch/master?svg=true)](https://ci.appveyor.com/project/sentry/symbol-collector/branch/master)
[![Tests](https://img.shields.io/appveyor/tests/sentry/symbol-collector/master?compact_message)](https://ci.appveyor.com/project/sentry/symbol-collector/branch/master/tests)
[![Discord Chat](https://img.shields.io/discord/621778831602221064.svg)](https://discord.gg/Ww9hbqr)  

This is a work in progress to collect system symbols from different devices like Android, macOS, Linux, etc.
It involves a server that writes the symbols to Google cloud storage and a set of clients.

Current clients are:

* Android
* macOS
* Linux

## Why are you doing this?

In order to stack unwind from a memory dump, every loaded image involved in the call stack needs to be available. 
Unwind information is not in the debug files but in the libraries instead. 
This project allows collecting these libraries so that native crash processing can be done on the backend as opposed to stackwalking on the client.

## Dependencies

This project includes an Android app (Xamarin), as well as a ASP.NET Core and a Console application.
The build script `build.sh` is focused on building **all** the components which means you'd need all the dependencies below.

Travis-CI build installs all dependencies and runs the `build.sh` script and is a good source of information if needed.

### Server and Console app
To build the Server, Libraries and the Console app (aka: everything except the Android app) you'll need:
* [.NET Core 3.1 SDK](https://dot.net)

### Android app
To build the Android project you need:
* JDK 1.8 (OpenJDK is OK)
* Android SDK 29
* Xamarin 10 (installed with the IDEs, or via `boots`, both approaches described below)

Plus either:

Install one of the following IDEs (recommended)
* [JetBrains Rider (macOS, Linux and Windows)](https://www.jetbrains.com/rider/)
* [Visual Studio for Mac (macOS)](https://docs.microsoft.com/en-us/xamarin/get-started/installation)
* [Visual Studio 2019 (Windows)](https://docs.microsoft.com/en-us/xamarin/get-started/installation)

Or if you don't want to install any IDE and simply want to build the Xamarin application (like in CI), you can install Xamarin via the command line:

`dotnet tool install --global boots`

macOS:

```
boots https://download.mono-project.com/archive/6.6.0/macos-10-universal/MonoFramework-MDK-6.6.0.161.macos10.xamarin.universal.pkg
boots https://aka.ms/xamarin-android-commercial-d16-4-macos
```

Windows:

```
boots https://aka.ms/xamarin-android-commercial-d16-4-windows
```

#### Updating dependencies

To update the version of .NET Core/Mono on this repository a few files have to be changed:

- .NET Core: `global.json`, build `yml` files, `Dockerfile` base images and this document.
- Mono: Xamarin depends on Mono on macOS and Linux: `.travis.yml` build file and example commands in this document.

# Resources

* [Blog post about native crash reporting support](https://blog.sentry.io/2019/09/26/fixing-native-apps-with-sentry).
* [Support Android native (NDK) crashes](https://blog.sentry.io/2019/11/25/adding-native-support-to-our-android-sdk/)
* Sentry's [Symbolicator project](https://github.com/getsentry/symbolicator).
