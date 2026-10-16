$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$statusPath=Join-Path $env:LOCALAPPDATA 'HaloLyrics\status.json'
function State { Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json }
function Wait-State([scriptblock]$condition){
    $until=[DateTime]::UtcNow.AddSeconds(7)
    while([DateTime]::UtcNow -lt $until){$s=State;if(&$condition $s){return $s};Start-Sleep -Milliseconds 150}
    throw '播放器状态验证超时。'
}
$process=Get-Process -Name AppleMusic|Select-Object -First 1
$roots=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
$root=$roots|Where-Object{$_.Current.ProcessId -eq $process.Id}|Select-Object -First 1
$nodes=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
$play=$nodes|Where-Object{$_.Current.AutomationId -eq 'TransportControl_PlayPauseStop'}|Select-Object -First 1
$scrubber=$nodes|Where-Object{$_.Current.AutomationId -eq 'LCDScrubber'}|Select-Object -First 1
if(-not $play -or -not $scrubber){throw '原生播放器控制接口暂不可读。'}
$invoker=$play.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$range=$scrubber.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
$initial=State
if(-not $initial.running -or -not $initial.playing){throw '请先播放歌曲并启动歌词同步。'}
$report=[ordered]@{time=[DateTimeOffset]::Now;source=$initial.source;pause=$false;resume=$false;seekForward=$false;seekBackward=$false}
try{
    $invoker.Invoke()
    $paused=Wait-State {param($s) $s.playing -eq $false}
    Start-Sleep -Seconds 2
    $held=State
    if([Math]::Abs($held.position-$paused.position) -gt 0.15){throw '暂停后时间仍在前进。'}
    $report.pause=$true
    $invoker.Invoke()
    $resumed=Wait-State {param($s) $s.playing -eq $true -and $s.position -gt $held.position+0.5}
    $report.resume=$true
    $position=[double]$range.Current.Value
    if($position+25 -lt $range.Current.Maximum){
        $target=$position+20
        $range.SetValue($target)
        $null=Wait-State {param($s) $s.position -ge $target-1 -and $s.position -le $target+3}
        $report.seekForward=$true
        $range.SetValue($position+2)
        $null=Wait-State {param($s) $s.position -ge $position+1 -and $s.position -le $position+5}
        $report.seekBackward=$true
    }else{throw '当前歌曲接近结尾，跳转测试尚未完成。'}
}
finally{
    if((State).playing -eq $false){$invoker.Invoke()}
    $report|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\playback-verification.json') -Encoding utf8
}
$report|ConvertTo-Json -Compress
