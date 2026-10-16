$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot '..\.tools\hidsharp\lib\netstandard2.0\HidSharp.dll')
$device = [HidSharp.DeviceList]::Local.GetHidDevices(0x2D99, 0xA160) |
    Where-Object { $_.DevicePath -match 'mi_04&col02' -and $_.GetMaxOutputReportLength() -eq 64 } |
    Select-Object -First 1
if (-not $device) { throw 'A160 screen interface missing' }
$stream = $device.Open()
$stream.ReadTimeout = 1000
$stream.WriteTimeout = 1000
function Send-Packet([byte]$command, [byte[]]$payload) {
    [byte[]]$packet = New-Object byte[] 64
    $packet[0] = 0x2e; $packet[1] = 0xaa; $packet[2] = 0xec; $packet[3] = $command
    $packet[4] = [byte]($payload.Length -shr 8); $packet[5] = [byte]$payload.Length
    [Array]::Copy($payload, 0, $packet, 6, $payload.Length)
    $sum = 0
    for ($i=1; $i -lt 6+$payload.Length; $i++) { $sum += $packet[$i] }
    $packet[6+$payload.Length] = [byte]($sum % 256)
    $stream.Write($packet)
}
function Read-Packet([byte]$command, [byte[]]$payload) {
    Send-Packet $command $payload
    for ($i=0; $i -lt 10; $i++) {
        [byte[]]$reply = New-Object byte[] 64
        $count = $stream.Read($reply, 0, 64)
        if ($count -ge 7 -and $reply[0] -eq 0x2f -and $reply[3] -eq $command) {
            $length = ([int]$reply[4] -shl 8) + $reply[5]
            if ($length -gt $count-7) { throw 'Bad reply length' }
            $sum=0; for($j=1;$j -lt 6+$length;$j++){$sum+=$reply[$j]}
            if (($sum % 256) -ne $reply[6+$length]) { throw 'Bad reply checksum' }
            return ,([byte[]]$reply[6..(5+$length)])
        }
    }
    throw 'Query failed'
}
$originalLayout = $null
$originalText = $null
try {
    $originalLayout = Read-Packet 0xee @()
    $originalText = Read-Packet 0xe7 @(0)
    if($originalText.Length -lt 4 -or $originalText[3] -gt $originalText.Length-4){throw 'Unverified text state'}
    [byte[]]$originalBytes = if($originalText[3]){$originalText[4..(3+$originalText[3])]}else{@()}
    if ($originalBytes.Length -gt 48) { throw 'Saved text exceeds verified capacity; no writes performed' }
    $layout = [byte[]]$originalLayout[0..8].Clone()
    $layout[0]=1; $layout[4]=0; $layout[5]=2; $layout[6]=0; $layout[7]=1; $layout[8]=255
    Send-Packet 0xef $layout
    [byte[]]$textBytes = [Text.Encoding]::UTF8.GetBytes('苹果歌词同步测试')
    Send-Packet 0xe8 ([byte[]](@(0,$textBytes.Length)+$textBytes))
    Start-Sleep -Milliseconds 300
    $readback = Read-Packet 0xe7 @(0)
    $displayed = [Text.Encoding]::UTF8.GetString($readback,4,$readback[3])
    Write-Output ('Readback: '+$displayed)
    if ($displayed -ne '苹果歌词同步测试') { throw 'Text readback mismatch' }
    Start-Sleep -Seconds 15
}
finally {
    try {
        if ($originalLayout -and $null -ne $originalText) {
            Send-Packet 0xe8 ([byte[]](@(0,$originalBytes.Length)+$originalBytes))
            $restoreLayout=[byte[]]$originalLayout[0..8].Clone(); $restoreLayout[0]=if($restoreLayout[5] -eq 1){2}else{1}
            Send-Packet 0xef $restoreLayout
            Start-Sleep -Milliseconds 600
            $after=Read-Packet 0xee @()
            $same=([Convert]::ToHexString($after) -eq [Convert]::ToHexString($originalLayout))
            Write-Output ('Screen state restored: '+$same)
            if (-not $same) { throw 'Restoration readback mismatch' }
        }
    }
    finally { $stream.Dispose() }
}
