param([switch]$Test)
$ErrorActionPreference = 'Stop'
$appDir = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$compiler = Join-Path $framework 'csc.exe'
$refs = @('System.dll','System.Core.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Runtime.dll','System.Runtime.WindowsRuntime.dll')
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+',('/win32manifest:' + (Join-Path $appDir 'app.manifest')),('/out:' + (Join-Path $appDir '原彩显示.exe')),('/resource:' + (Join-Path $appDir 'MainWindow.xaml') + ',MainWindow.xaml'))
$arguments += '/win32icon:' + (Join-Path $appDir 'Assets\app-icon.ico')
$arguments += '/resource:' + (Join-Path $appDir 'Assets\app-icon.png') + ',AppIcon.png'
foreach ($ref in $refs) { $refPath = Join-Path $framework $ref; if (-not (Test-Path -LiteralPath $refPath)) { $refPath = Join-Path (Join-Path $framework 'WPF') $ref }; $arguments += '/r:' + $refPath }
$arguments += '/r:' + (Join-Path $metadata 'Windows.Devices.winmd')
$arguments += '/r:' + (Join-Path $metadata 'Windows.Foundation.winmd')
$arguments += Join-Path $appDir 'AmbientTone.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ($Test) {
 $process = Start-Process -FilePath (Join-Path $appDir '原彩显示.exe') -ArgumentList @('--self-test', ('"' + (Join-Path $appDir 'test-results.txt') + '"')) -WindowStyle Hidden -Wait -PassThru
 if ($process.ExitCode -ne 0) { throw 'Self tests failed' }
 Get-Content -LiteralPath (Join-Path $appDir 'test-results.txt')
}


