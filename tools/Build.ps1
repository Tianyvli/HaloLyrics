param([switch]$TestOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetExe = if(Test-Path -LiteralPath $localSdk){$localSdk}else{'dotnet'}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
if(Test-Path -LiteralPath $localSdk){
    $env:DOTNET_CLI_HOME=Join-Path $projectRoot '.tools\cli'
    $env:NUGET_PACKAGES=Join-Path $projectRoot '.tools\packages'
}
& $dotnetExe run --project (Join-Path $projectRoot 'tests\HaloLyrics.Tests\HaloLyrics.Tests.csproj') -c Release
if($LASTEXITCODE -ne 0){throw '自动检查失败，停止打包。'}
if($TestOnly){exit 0}
& $dotnetExe publish (Join-Path $projectRoot 'src\HaloLyrics\HaloLyrics.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o (Join-Path $projectRoot 'dist\HaloLyrics') --nologo
if($LASTEXITCODE -ne 0){throw '打包失败。'}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $projectRoot 'dist\HaloLyrics\使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $projectRoot 'dist\HaloLyrics\THIRD_PARTY_NOTICES.md') -Force
if(Test-Path -LiteralPath (Join-Path $projectRoot 'VALIDATION.md')){
    Copy-Item -LiteralPath (Join-Path $projectRoot 'VALIDATION.md') -Destination (Join-Path $projectRoot 'dist\HaloLyrics\验证记录.md') -Force
}
$packageRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget\packages'}
$licenseRoot=Join-Path $projectRoot 'dist\HaloLyrics\licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $packageRoot 'hidsharp\2.6.4\LICENSE.txt') -Destination (Join-Path $licenseRoot 'HidSharp-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $packageRoot 'microsoft.web.webview2\1.0.4258.31\LICENSE.txt') -Destination (Join-Path $licenseRoot 'WebView2-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $packageRoot 'microsoft.web.webview2\1.0.4258.31\NOTICE.txt') -Destination (Join-Path $licenseRoot 'WebView2-NOTICE.txt') -Force
Get-ChildItem -LiteralPath (Join-Path $packageRoot 'microsoft.netcore.app.runtime.win-x64') -Recurse -File -Filter '*txt' | Where-Object {$_.Name -match 'LICENSE|THIRD-PARTY-NOTICES'} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseRoot ('dotnet-'+$_.Name)) -Force}
Get-ChildItem -LiteralPath (Join-Path $packageRoot 'microsoft.windowsdesktop.app.runtime.win-x64') -Recurse -File | Where-Object {$_.Name -match '^LICENSE(\.txt)?$|^THIRD-PARTY-NOTICES(\.txt)?$'} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseRoot ('desktop-'+$_.Name)) -Force}
Write-Output ('程序已生成：'+(Join-Path $projectRoot 'dist\HaloLyrics\HaloLyrics.exe'))
