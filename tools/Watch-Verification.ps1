param([int]$DurationSeconds=600,[int]$AppProcessId)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$statusPath=Join-Path $env:LOCALAPPDATA 'HaloLyrics\status.json'
$artifactRoot=Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force|Out-Null
$samplesPath=Join-Path $artifactRoot 'background-samples.jsonl'
$reportPath=Join-Path $artifactRoot 'background-verification.json'
$began=[DateTimeOffset]::Now
$samples=0;$stale=0;$stopped=0;$missingLyrics=0;$titles=[Collections.Generic.HashSet[string]]::new();$firstChanges=$null;$lastChanges=0
$lastTitle=''
while(([DateTimeOffset]::Now-$began).TotalSeconds -lt $DurationSeconds){
    if(-not (Get-Process -Id $AppProcessId -ErrorAction SilentlyContinue)){throw '验证进程已退出。'}
    try{$state=Get-Content -LiteralPath $statusPath -Raw|ConvertFrom-Json}catch{Start-Sleep -Milliseconds 250;continue}
    $samples++
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
$report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $reportPath -Encoding utf8
$report|ConvertTo-Json -Depth 4 -Compress|Write-Output
