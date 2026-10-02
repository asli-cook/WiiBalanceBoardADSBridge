$ErrorActionPreference = 'Stop'

$taskName = 'Balance Board ADS Bridge'
$installDirectory = Join-Path $env:ProgramFiles 'Balance Board ADS Bridge'
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
Add-Type -AssemblyName System.Windows.Forms

function Show-SetupMessage([string]$message, [string]$title, [System.Windows.Forms.MessageBoxIcon]$icon) {
    [System.Windows.Forms.MessageBox]::Show($message, $title, [System.Windows.Forms.MessageBoxButtons]::OK, $icon) | Out-Null
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principalCheck = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principalCheck.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        $elevated = Start-Process -FilePath $windowsPowerShell -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $elevated.ExitCode
    }
    catch {
        if ($_.Exception.NativeErrorCode -eq 1223) {
            exit 1223
        }
        throw
    }
}

try {
    $payload = @(
        'BalanceBoardADS.exe',
        'BalanceBoardADS.dll',
        'BalanceBoardADS.deps.json',
        'BalanceBoardADS.runtimeconfig.json'
    )

    foreach ($file in $payload) {
        $source = Join-Path $PSScriptRoot $file
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Installer payload is missing: $file"
        }
    }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    foreach ($file in $payload) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $installDirectory $file) -Force
    }

    $executable = Join-Path $installDirectory 'BalanceBoardADS.exe'
    $action = New-ScheduledTaskAction -Execute $executable -WorkingDirectory $installDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
    $taskSettings = New-ScheduledTaskSettingsSet `
        -MultipleInstances IgnoreNew `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 3 `
        -RestartInterval ([TimeSpan]::FromMinutes(1)) `
        -StartWhenAvailable `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries
    $task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $taskSettings `
        -Description 'Starts the Wii Balance Board ADS keyboard bridge at sign-in with highest privileges.'
    Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $taskRunner = Join-Path $env:WINDIR 'System32\schtasks.exe'
    $shortcutTargets = @(
        (Join-Path $shell.SpecialFolders.Item('Desktop') 'Balance Board ADS Bridge.lnk'),
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Balance Board ADS Bridge.lnk')
    )
    foreach ($shortcutPath in $shortcutTargets) {
        $shortcutDirectory = Split-Path -Parent $shortcutPath
        New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $taskRunner
        $shortcut.Arguments = "/Run /TN `"$taskName`""
        $shortcut.WorkingDirectory = $installDirectory
        $shortcut.IconLocation = "$executable,0"
        $shortcut.Description = 'Open the Balance Board bridge (runs elevated through Task Scheduler).'
        $shortcut.Save()
    }

    Start-ScheduledTask -TaskName $taskName
    Show-SetupMessage `
        "Installed successfully. The bridge will start with Windows and keep searching for the board. It runs elevated through Task Scheduler, so Windows will not ask for UAC each time. Calibration and preferences are preserved in your user profile." `
        'Balance Board ADS Bridge Setup' `
        ([System.Windows.Forms.MessageBoxIcon]::Information)
    exit 0
}
catch {
    $message = "Installation could not finish:`r`n$($_.Exception.Message)"
    try {
        Show-SetupMessage $message 'Balance Board ADS Bridge Setup' ([System.Windows.Forms.MessageBoxIcon]::Error)
    }
    catch {
        Write-Error $message
    }
    exit 1
}
