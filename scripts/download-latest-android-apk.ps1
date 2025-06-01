$ErrorActionPreference = "Stop"

Install-Module -Name Sentry -RequiredVersion 0.3.0 -Scope CurrentUser -Force -AllowClobber
Import-Module Sentry

Start-Sentry -Debug {
    $_.Dsn = "https://ea58a7607ff1b39433af3a6c10365925@o1.ingest.us.sentry.io/4509420348964864"
    $_.TracesSampleRate = 1.0
    $_.Environment = "github-actions"
    $_.Release = $env:GITHUB_SHA
}
$transaction = Start-SentryTransaction -Name "download-latest-release" -Operation "release.download"

try {
    $versionFile = ".cache/.release_version"
    $previousVersion = if (Test-Path $versionFile) { Get-Content $versionFile -TotalCount 1 } else { "" }

    $headers = @{ Authorization = "token $env:GH_TOKEN" }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/getsentry/symbol-collector/releases/latest" -Headers $headers
    $currentVersion = $release.tag_name

    if ($currentVersion -eq $previousVersion) {
        Write-Host "Latest version ($currentVersion) already downloaded. Skipping."
        return 0
    } else {
        Write-Host "Previous version ($previousVersion) different then ($currentVersion), downloading."
    }

    $dir = Split-Path $versionFile
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $currentVersion | Set-Content $versionFile

    $assetName = "io.sentry.symbolcollector.android-Signed.apk"
    $apiUrl = "https://api.github.com/repos/getsentry/symbol-collector/releases/latest"
    $releaseData = Invoke-RestMethod -Uri $apiUrl -Headers $headers

    $asset = $releaseData.assets | Where-Object { $_.name -eq $assetName }
    if (-not $asset) {
        throw "Asset '$assetName' not found in latest release"
    }

    $assetUrl = $asset.url
    Write-Host "Downloading $assetName from $assetUrl"

    Invoke-RestMethod -Uri $assetUrl `
                      -Headers @{
                          Authorization = "token $env:GH_TOKEN"
                          Accept        = "application/octet-stream"
                      } `
                      -OutFile $assetName

    Write-Host "Downloaded to $assetName"

    $transaction.Finish()
    exit 0
}
catch
{
    # Mark the transaction as finished (note: this needs to be done prior to calling Out-Sentry)
    $transaction.Finish($_.Exception)

    $_ | Out-Sentry
    "⚠️ Error on line $($_.InvocationInfo.ScriptLineNumber): $($Error[0])"
    exit 1
}

