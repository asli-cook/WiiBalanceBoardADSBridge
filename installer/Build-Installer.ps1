$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot 'publish-release'
$outputDirectory = Join-Path $projectRoot 'dist'
$stageDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('BalanceBoardADS-' + [guid]::NewGuid().ToString('N'))
$sedPath = Join-Path $stageDirectory 'BalanceBoardADS.sed'
$installerPath = Join-Path $outputDirectory 'Wii Balance Board ADS Setup.exe'
$iexpressPath = Join-Path $env:WINDIR 'System32\iexpress.exe'
$dotnetEnvironmentNames = @('DOTNET_CLI_HOME', 'APPDATA', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_NOLOGO', 'NUGET_PACKAGES')
$originalDotnetEnvironment = @{}
foreach ($name in $dotnetEnvironmentNames) {
    $originalDotnetEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli-home'
$env:APPDATA = Join-Path $projectRoot '.local-appdata'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget-packages'

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

try {
    & dotnet publish (Join-Path $projectRoot 'BoardADSBridge.csproj') -c Release -o $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $payloadFiles = @(
        'BalanceBoardADS.exe',
        'BalanceBoardADS.dll',
        'BalanceBoardADS.deps.json',
        'BalanceBoardADS.runtimeconfig.json'
    )
    foreach ($file in $payloadFiles) {
        $sourcePath = Join-Path $publishDirectory $file
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Published application is missing: $file"
        }
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $stageDirectory $file)
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $stageDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.cmd') -Destination $stageDirectory

    $fileLines = @()
    for ($i = 0; $i -lt 6; $i++) {
        $fileLines += "%FILE$i%="
    }
    $stringLines = @(
        'FILE0=BalanceBoardADS.exe',
        'FILE1=BalanceBoardADS.dll',
        'FILE2=BalanceBoardADS.deps.json',
        'FILE3=BalanceBoardADS.runtimeconfig.json',
        'FILE4=Install.ps1',
        'FILE5=Install.cmd'
    )
    $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName="$installerPath"
FriendlyName=Balance Board ADS Bridge Setup
AppLaunched=Install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=Install.cmd
UserQuietInstCmd=Install.cmd
SourceFiles=SourceFiles
[SourceFiles]
SourceFiles0="$stageDirectory"
[SourceFiles0]
$($fileLines -join "`r`n")
[Strings]
$($stringLines -join "`r`n")
"@
    Set-Content -LiteralPath $sedPath -Value $sed -Encoding Ascii

    & $iexpressPath /N /Q $sedPath
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw 'IExpress could not create the installer.'
    }

    Write-Output "Installer created: $installerPath"
}
finally {
    foreach ($name in $dotnetEnvironmentNames) {
        [Environment]::SetEnvironmentVariable($name, $originalDotnetEnvironment[$name], 'Process')
    }
    if ($env:BBADS_KEEP_STAGE -ne '1' -and (Test-Path -LiteralPath $stageDirectory)) {
        Remove-Item -LiteralPath $stageDirectory -Recurse -Force
    }
}
