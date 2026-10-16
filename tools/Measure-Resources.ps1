param(
    [Parameter(Mandatory)][int]$AppProcessId,
    [Parameter(Mandatory)][string]$Label,
    [int]$DurationSeconds=60,
    [int]$WarmupSeconds=30
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$statusPath=Join-Path $env:LOCALAPPDATA 'HaloLyrics\status.json'
if($Label -notmatch '^[A-Za-z0-9.-]+$'){throw '测量标签只能使用字母、数字、点和连字符。'}
Write-Output ('Warmup: '+$WarmupSeconds+' seconds')
if($WarmupSeconds -gt 0){Start-Sleep -Seconds $WarmupSeconds}
$app=Get-Process -Id $AppProcessId
$began=[DateTimeOffset]::Now
$cpuBefore=$app.CPU
$samples=[Collections.Generic.List[object]]::new()
while(([DateTimeOffset]::Now-$began).TotalSeconds -lt $DurationSeconds){
    $app.Refresh()
    if($app.HasExited){throw '测量进程已退出。'}
    $state=Get-Content -LiteralPath $statusPath -Raw|ConvertFrom-Json
    $samples.Add([pscustomobject]@{time=[DateTimeOffset]::Now;workingSetBytes=$app.WorkingSet64;privateBytes=$app.PrivateMemorySize64;running=$state.running;playing=$state.playing;lyricLines=$state.lyricLines})
    Start-Sleep -Seconds 2
}
$app.Refresh()
$elapsed=([DateTimeOffset]::Now-$began).TotalSeconds
$report=[ordered]@{
    label=$Label;began=$began;completed=[DateTimeOffset]::Now;durationSeconds=[Math]::Round($elapsed,1)
    processId=$app.Id;executableBytes=(Get-Item -LiteralPath $app.Path).Length
    workingSetAverageBytes=($samples|Measure-Object workingSetBytes -Average).Average
    privateAverageBytes=($samples|Measure-Object privateBytes -Average).Average
    privateMaxBytes=($samples|Measure-Object privateBytes -Maximum).Maximum
    cpuPercentOneCore=[Math]::Round(($app.CPU-$cpuBefore)/$elapsed*100,3)
    playingSamples=@($samples|Where-Object playing).Count;runningSamples=@($samples|Where-Object running).Count
    samples=$samples.ToArray()
}
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'artifacts') -Force|Out-Null
$report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $projectRoot ('artifacts\resources-'+$Label+'.json')) -Encoding utf8
$summary=[ordered]@{};foreach($key in $report.Keys){if($key -ne 'samples'){$summary[$key]=$report[$key]}}
$summary|ConvertTo-Json -Compress
