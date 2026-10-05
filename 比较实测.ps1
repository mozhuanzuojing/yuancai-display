param([string]$Measurements)
$ErrorActionPreference='Stop'
$appDir=$PSScriptRoot
if(-not $Measurements){throw '请指定仪器实测文件。可从验证目录中的实测数据模板.csv开始。'}
$outFile=Join-Path $appDir '验证\实测比较结果.csv'
$p=Start-Process -FilePath (Join-Path $appDir '对齐验证.exe') -ArgumentList @('--measure',('"'+$Measurements+'"'),('"'+$outFile+'"')) -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath $outFile -Encoding UTF8
if($p.ExitCode -eq 2){Write-Warning '缺少配对实测数据；苹果对齐未验证。'}elseif($p.ExitCode -ne 0){throw '实测文件处理失败'}
