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

# Resources

* [Blog post about native crash reporting support](https://blog.sentry.io/2019/09/26/fixing-native-apps-with-sentry).
* [Support Android native (NDK) crashes](https://blog.sentry.io/2019/11/25/adding-native-support-to-our-android-sdk/)
* Sentry's [Symbolicator project](https://github.com/getsentry/symbolicator).
