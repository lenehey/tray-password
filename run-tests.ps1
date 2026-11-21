param([string]$TestDirectory = (Join-Path $env:TEMP ('TrayPasswordTests-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $TestDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /out:"$TestDirectory\Tests.exe" /r:"$PSScriptRoot\TrayPassword.exe" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath "$PSScriptRoot\TrayPassword.exe" -Destination $TestDirectory
& "$TestDirectory\Tests.exe" $TestDirectory
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
