# Changelog

## Unreleased

### Fixes

- NotSupportedException when serializing Args([#125](https://github.com/getsentry/symbol-collector/pull/125))

## 1.5.2

### Various fixes & improvements

- fix: Android theme appcompat (#122) by @bruno-garcia
- disable AndroidUseAssemblyStore to work with appcenter (f300b966) by @bruno-garcia

## 1.5.1

### Various fixes & improvements

- artifact apk only (0671588e) by @bruno-garcia
- aar from app (273c6132) by @bruno-garcia
- fix artifact path (5bc19a1d) by @bruno-garcia
- debug gh artifact (941ba29f) by @bruno-garcia
- changelog 1.5.0 (47cd1456) by @bruno-garcia
- simplify craft yml (0ff5fc3d) by @bruno-garcia
- craft changelog auto (41593155) by @bruno-garcia

## 1.5.0

* Target .NET 6 (#111)

## 1.4.2

* Use android http client (#121)

## 1.4.1

* Remove dupe batchId with different casing 1167d16
* Remove dupe tag ec17a2c

## 1.4.0 

* Sentry release and performance (#119)

## 1.3.2

* Fix Mac OSX publish issue (#118)
* Fix CLI should respect batchType (#117)

## 1.3.1

* Run script UITest lib version 649d8d8
* Don't upload already reported (#112)

## 1.3.0

* Return 208 on conflict (#110)
* Sentry SDK for .NET 3.3.4 (#109)
* Update dependencies (#108)
* Xamarin Android library (#107)

## 1.2.3

* Fixed concurrency issue on symbol check #106

## 1.2.2

* Capture transactions (#102)
* Stop test if error modal is visible (+.NET 5 bump) (#100)
* Bump sentry sdk and other deps (#99)

## 1.2.1

* Embed pdb (#97)
* Initial performance usage (#96)
* Sentry dotnet 3.0.1 (#93)

## 1.2.0

No documented changes

## 1.1.4

* Clean workdir in prod (#86)
* Sentry sdk 3.0.0-alpha (#85)
* Hashing images is optional (#84)
* Fix warn on sufix (#82)

## 1.1.3

* ref: polly retry as breadcrumb (#81)
* feat: Event for finished batch (#80)
* ref: Use full rust backtrace (#79)

## 1.1.2

* fix: symsorter output msg ecdb6e7

## 1.1.1

* fix: trigger scope evaluation d168b98
* ref: Server log error 8627c21
* fix: Match symsorter id requirement (#78)

## 1.1.0

* dep: bump Sentry, ignore OperationCancelledException (#77)
* dep: bump dependencies including symsorter (#76)
* ref: Ignore symsorter errors to finish batch (#75)

## 1.0.5

* Support Android API 16 #70
* Gzip symbols #71

## 1.0.4

* fix: close in backgroud (#68)
* sort per file (#67)

## 1.0.3

* push to GCS in the background (#66)
* Includes fix: googleapis/google-cloud-dotnet#3254
* listen to stderr (#65)
* fix: Don't cancel batch over disconnect (#64)
* upload from single dir (#61)

## 1.0.2

* The server ingests symbols in batches. When a batch is closed, symsorter runs and the result is copied over to google cloud storage. 
* Working directories are removed upon success.
* The server requires .NET Core 3.1 to run. The Dockerfile in the repo builds a working image.
* The Android application has an text box where the URL to the server can be defined. Upon clicking a button, symbols are uploaded to the server while displaying metrics on the screen. In this release, the app forces the screen to stay on so the job doesn't get pushed to the background.
* The console app is able to collect symbols from the Linux and macOS device it runs on.
* All components have Sentry integrated and batchId/requestId are added to events to help with correlation.
* Logs in prod are JSON dumps to stdout (no files to disk) which are captured by Stackdriver.
