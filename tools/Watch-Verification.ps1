param([int]$DurationSeconds=600,[int]$AppProcessId,[string]$Label='')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$statusPath=Join-Path $env:LOCALAPPDATA 'HaloLyrics\status.json'
$artifactRoot=Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force|Out-Null
if($Label -and $Label -notmatch '^[A-Za-z0-9.-]+$'){throw '验证标签只能使用字母、数字、点和连字符。'}
$prefix='background'+$(if($Label){'-'+$Label})
$samplesPath=Join-Path $artifactRoot ($prefix+'-samples.jsonl')
$reportPath=Join-Path $artifactRoot ($prefix+'-verification.json')
[IO.File]::WriteAllText($samplesPath,'')
$began=[DateTimeOffset]::Now
$app=Get-Process -Id $AppProcessId
$cpuBefore=$app.CPU
$resources=[Collections.Generic.List[object]]::new()
$playingSamples=0;$rhythmSamples=0;$firstMetadata=$null
$samples=0;$stale=0;$stopped=0;$missingLyrics=0;$titles=[Collections.Generic.HashSet[string]]::new();$firstChanges=$null;$lastChanges=0
$lastTitle=''
while(([DateTimeOffset]::Now-$began).TotalSeconds -lt $DurationSeconds){
    $app.Refresh();if($app.HasExited){throw '验证进程已退出。'}
    try{$state=Get-Content -LiteralPath $statusPath -Raw|ConvertFrom-Json}catch{Start-Sleep -Milliseconds 250;continue}
    $samples++
    $resources.Add([pscustomobject]@{workingSet=$app.WorkingSet64;privateBytes=$app.PrivateMemorySize64})
    if($state.playing){$playingSamples++}
    if($state.screenMode -eq 'rhythm'){$rhythmSamples++}
    if($null -eq $firstMetadata){$firstMetadata=$state.metadataReads}
    if(([DateTimeOffset]::Now-[DateTimeOffset]$state.observedAt).TotalSeconds -gt 6){$stale++}
    if(-not $state.running){$stopped++}
    if($state.playing -and $state.lyricLines -eq 0){$missingLyrics++}
    if($null -eq $firstChanges){$firstChanges=$state.lyricChanges}
    $lastChanges=$state.lyricChanges
    if($state.title){$titles.Add($state.title)|Out-Null}
    if($state.title -ne $lastTitle){Write-Output ('Song: '+$state.title);$lastTitle=$state.title}
    $state|ConvertTo-Json -Compress|Add-Content -LiteralPath $samplesPath -Encoding utf8
    Start-Sleep -Seconds 2
}
$report=[ordered]@{began=$began;completed=[DateTimeOffset]::Now;durationSeconds=[Math]::Round(([DateTimeOffset]::Now-$began).TotalSeconds,1);samples=$samples;staleSamples=$stale;stoppedSamples=$stopped;missingLyricSamples=$missingLyrics;songs=@($titles);lyricUpdates=$lastChanges-$firstChanges;offsetMilliseconds=$state.offsetMilliseconds;source=$state.source}
$report.playingSamples=$playingSamples;$report.rhythmSamples=$rhythmSamples
$report.workingSetAverageBytes=($resources|Measure-Object workingSet -Average).Average
$report.privateAverageBytes=($resources|Measure-Object privateBytes -Average).Average
$report.privateMaxBytes=($resources|Measure-Object privateBytes -Maximum).Maximum
$report.cpuPercentOneCore=[Math]::Round(($app.CPU-$cpuBefore)/([DateTimeOffset]::Now-$began).TotalSeconds*100,3)
$report.metadataReads=$state.metadataReads-$firstMetadata
$report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $reportPath -Encoding utf8
$report|ConvertTo-Json -Depth 4 -Compress|Write-Output
