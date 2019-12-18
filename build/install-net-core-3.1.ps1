# Install .NET Core SDK 3.1 onto the appveyor build server.
# Workaround until they add it to the official image.
# src: https://github.com/appveyor/ci/issues/2746#issuecomment-444292816

Write-Host "Installing .NET Core 3.1 SDK..." -ForegroundColor Cyan
Write-Host "Downloading..."
$exePath = "$env:TEMP\dotnet-sdk-3.1.100-win-x64.exe"
(New-Object Net.WebClient).DownloadFile('https://download.visualstudio.microsoft.com/download/pr/639f7cfa-84f8-48e8-b6c9-82634314e28f/8eb04e1b5f34df0c840c1bffa363c101/dotnet-sdk-3.1.100-win-x64.exe', $exePath)
Write-Host "Installing..."
cmd /c start /wait "$exePath" /quiet /norestart
del $exePath
Write-Host "Installed" -ForegroundColor Green
