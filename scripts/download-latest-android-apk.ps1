$ErrorActionPreference = "Stop"

Install-Module -Name Sentry -RequiredVersion 0.3.0 -Scope CurrentUser -Force -AllowClobber
Import-Module Sentry

$commitSha = $env:GITHUB_SHA
if (-not $commitSha) {
    $commitSha = "local-dev"
}
Start-Sentry -Debug {
    $_.Dsn = "https://ea58a7607ff1b39433af3a6c10365925@o1.ingest.us.sentry.io/4509420348964864"
    $_.TracesSampleRate = 1.0
    $_.Environment = "github-actions"
    $_.Release = $commitSha
}
$transaction = Start-SentryTransaction -Name "download-latest-release" -Operation "release.download"

try {
    $versionFile = ".cache/.release_version"
    $timestampFile = ".cache/.last_upload_timestamp"

    # Sauce Labs Mobile App Storage expires APKs after 60 days of inactivity.
    # To ensure the APK is always available, we re-upload if the last upload was more than 30 days ago.
    # This provides a safe buffer before expiration.
    $maxAgeDays = 30

    $previousVersion = if (Test-Path $versionFile) { Get-Content $versionFile -TotalCount 1 } else { "" }
    $lastUploadTimestamp = if (Test-Path $timestampFile) { Get-Content $timestampFile -TotalCount 1 } else { "" }

    $headers = @{ Authorization = "token $env:GH_TOKEN" }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/getsentry/symbol-collector/releases/latest" -Headers $headers
    $currentVersion = $release.tag_name

    $needsUpload = $false
    $reason = ""

    if ($currentVersion -ne $previousVersion) {
        $needsUpload = $true
        $reason = "New version available: $previousVersion -> $currentVersion"
    } elseif (-not $lastUploadTimestamp) {
        $needsUpload = $true
        $reason = "No previous upload timestamp found"
    } else {
        $lastUploadDate = [DateTime]::Parse($lastUploadTimestamp)
        $daysSinceUpload = ((Get-Date) - $lastUploadDate).Days
        if ($daysSinceUpload -ge $maxAgeDays) {
            $needsUpload = $true
            $reason = "Last upload was $daysSinceUpload days ago (threshold: $maxAgeDays days)"
        }
    }

    if (-not $needsUpload) {
        $daysSinceUpload = ((Get-Date) - [DateTime]::Parse($lastUploadTimestamp)).Days
        Write-Host "Skipping download. Version ($currentVersion) unchanged and last upload was $daysSinceUpload days ago (threshold: $maxAgeDays days)."
        return 0
    }

    Write-Host "Downloading APK. Reason: $reason"

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

    # Update cache with version and timestamp
    $dir = Split-Path $versionFile
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $currentVersion | Set-Content $versionFile
    (Get-Date).ToString("o") | Set-Content $timestampFile

    Write-Host "Updated cache: version=$currentVersion, timestamp=$(Get-Date)"

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

