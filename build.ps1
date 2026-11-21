$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon = Join-Path $PSScriptRoot 'TrayPassword.ico'
$iconArguments = @()
if (Test-Path -LiteralPath $icon) {
    $iconArguments = @("/win32icon:$icon", "/resource:$icon,TrayPassword.Icon")
}
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll @iconArguments /out:"$PSScriptRoot\TrayPassword.exe" "$PSScriptRoot\TrayPassword.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
