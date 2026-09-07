# Builds AudioBridge as a single self-contained .exe. Run on Windows.
#
# Requires the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
#
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#
# Pass -Drive to also copy the result into the shared Google Drive test folder.
param(
    [string]$Drive = "$env:USERPROFILE\Google Drive\My Drive\AudioBridge"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is not installed. Get .NET 10 from https://dotnet.microsoft.com/download/dotnet/10.0"
}

Write-Host "==> Testing"
dotnet test $root --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "==> Publishing"
$publish = Join-Path $root 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\AudioBridge.App') -c Release -r win-x64 --self-contained true -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $publish 'AudioBridge.exe'

Write-Host "==> Checking it starts"
# No console on a WPF app, so a startup crash is otherwise silent.
$process = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 10
if ($process.HasExited) {
    $log = "$env:LOCALAPPDATA\AudioBridge\log.txt"
    if (Test-Path $log) { Get-Content $log }
    throw "AudioBridge exited on startup with code $($process.ExitCode). See $log"
}
Stop-Process -Id $process.Id -Force
Write-Host "    OK"

if (Test-Path $Drive) {
    Copy-Item $exe (Join-Path $Drive 'AudioBridge.exe') -Force
    Write-Host "==> Copied to $Drive"
}

Get-Item $exe | Select-Object Name, @{n='Size';e={"{0:N0} MB" -f ($_.Length / 1MB)}}
