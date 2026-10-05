$ErrorActionPreference='Stop'
$appDir=$PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$arguments=@('/nologo','/target:winexe','/optimize+',('/out:'+(Join-Path $appDir '对齐验证.exe')),('/win32manifest:'+(Join-Path $appDir 'app.manifest')),('/r:'+(Join-Path $appDir '原彩显示.exe')))
foreach($ref in @('System.dll','System.Drawing.dll','System.Core.dll','System.Xaml.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')){$path=Join-Path $framework $ref;if(-not(Test-Path -LiteralPath $path)){$path=Join-Path (Join-Path $framework 'WPF') $ref};$arguments+='/r:'+$path}
$arguments+=Join-Path $appDir 'ValidationLab.cs'
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Validation tool build failed'}

