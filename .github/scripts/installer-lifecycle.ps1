<#
.SYNOPSIS
    The install lifecycle checks (evaluation matrix §8) for a freshly packed Setup.exe.

.DESCRIPTION
    Real installs and uninstalls against the current user, so run it only on a throwaway machine
    (the release workflow's runner). Never on a PC someone uses: it deletes %LocalAppData%\VoiceCtrl.

    1. Fresh install: per user, Start Menu shortcut, then a first start and an uninstall that ticks
       "Also delete your data".
    2. v3 -> v4 upgrade: a seeded v3 folder, Run value and shortcut; the key carries over, Setup is
       skipped, the shortcut and Run value move to v4.
    3. Auto-update, against a local feed holding the next patch: a tampered package is refused and
       deleted; the genuine one downloads, installs at the next start, and the Updated to card is
       recorded. Then an uninstall left to time out, which keeps the data.

    Windows PowerShell 5.1, for UI Automation. Exits 1 if any check fails.
#>
param(
    [Parameter(Mandatory)] [string] $Setup,
    # A Velopack feed folder with this release and a signed, newer one (release.yml's UpdateFeed).
    [Parameter(Mandatory)] [string] $UpdateFeed,
    [Parameter(Mandatory)] [string] $UpdateVersion
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TaskDialogButtons {
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    // TDM_CLICK_BUTTON. TaskDialog buttons have no UIA Invoke pattern; 100 is the first custom id.
    public static void Click(IntPtr dialog, int id) { SendMessage(dialog, 0x0466, (IntPtr)id, IntPtr.Zero); }
}
'@

$installDir = Join-Path $env:LOCALAPPDATA 'VoiceCtrlApp'
$exe = Join-Path $installDir 'current\VoiceCtrl.exe'
$dataRoot = Join-Path $env:LOCALAPPDATA 'VoiceCtrl'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$userUninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VoiceCtrlApp'
$machineUninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VoiceCtrlApp'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'VoiceCtrl.lnk'
$script:failures = 0

function Check([bool] $ok, [string] $what) {
    if ($ok) { Write-Output "PASS  $what" } else { Write-Output "FAIL  $what"; $script:failures++ }
}

function Get-RunValue { (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).VoiceCtrl }

function Get-ShortcutTarget {
    if (Test-Path $shortcut) { (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath }
}

function Wait-Until([scriptblock] $condition, [int] $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return [bool](& $condition)
}

function Install-V4 {
    $started = Get-Date
    $setupProcess = Start-Process $Setup -ArgumentList '--silent' -PassThru -Wait
    Write-Output ("      Setup took {0:N0} s" -f ((Get-Date) - $started).TotalSeconds)
    Check ($setupProcess.ExitCode -eq 0) 'Setup exits 0'
    Check (Test-Path $exe) "VoiceCtrl.exe is in $installDir"
    Check (Test-Path $userUninstallKey) 'Apps & features entry is per user (HKCU)'
    Check (-not (Test-Path $machineUninstallKey)) 'no machine-wide entry (HKLM)'
    Check ((Get-ShortcutTarget) -like "$installDir\*") 'Start Menu VoiceCtrl.lnk points into the install folder'
}

# Started the way the Run key starts it, so it stays in the tray. Returns once settings exist.
function Start-V4 {
    Start-Process $exe -ArgumentList '--autostart' | Out-Null
    Check (Wait-Until { Test-Path (Join-Path $dataRoot 'settings.json') } 60) 'first start writes settings.json'
    Check (Wait-Until { (Get-RunValue) -eq "`"$exe`" --autostart" } 20) 'Run value points at the installed exe'
}

function Read-Settings { Get-Content (Join-Path $dataRoot 'settings.json') -Raw | ConvertFrom-Json }

function Read-Log { Get-Content (Join-Path $dataRoot 'log.txt') -Raw }

# The way a newer build takes over (SingleInstance): the running copy quits normally.
function Stop-V4 {
    $quit = [Threading.EventWaitHandle]::OpenExisting('VoiceCtrl.QuitRequestedEvent')
    [void]$quit.Set()
    $quit.Dispose()
    Check (Wait-Until { -not (Get-Process VoiceCtrl -ErrorAction SilentlyContinue) } 30) 'VoiceCtrl quits when asked'
}

# Null while Velopack swaps the current folder during an install.
function Get-InstalledVersion {
    try { (Get-Item $exe -ErrorAction Stop).VersionInfo.ProductVersion.Split('+')[0] } catch { $null }
}

# A copy of the feed whose newer package is changed after signing, with Velopack's own hashes
# updated to match: what someone who could edit the GitHub release could do. Only the signed
# manifest can catch it.
function New-TamperedFeed([string] $into) {
    Copy-Item $UpdateFeed $into -Recurse
    $package = Join-Path $into "VoiceCtrlApp-$UpdateVersion-full.nupkg"
    $stream = [IO.File]::Open($package, 'Append')
    $stream.Write([byte[]](1, 2, 3, 4), 0, 4)
    $stream.Dispose()
    $feedFile = Join-Path $into 'releases.win.json'
    $feed = Get-Content $feedFile -Raw | ConvertFrom-Json
    foreach ($asset in $feed.Assets | Where-Object { $_.FileName -eq (Split-Path $package -Leaf) }) {
        $asset.SHA1 = (Get-FileHash $package -Algorithm SHA1).Hash
        $asset.SHA256 = (Get-FileHash $package -Algorithm SHA256).Hash
        $asset.Size = (Get-Item $package).Length
    }
    [IO.File]::WriteAllText($feedFile, ($feed | ConvertTo-Json -Depth 5))
}

function Find-UninstallDialog([int] $seconds) {
    $automation = [System.Windows.Automation.AutomationElement]
    $isDialog = New-Object System.Windows.Automation.AndCondition @(
        (New-Object System.Windows.Automation.PropertyCondition ($automation::NameProperty), 'VoiceCtrl'),
        (New-Object System.Windows.Automation.PropertyCondition ($automation::ClassNameProperty), '#32770'))
    $deadline = (Get-Date).AddSeconds($seconds)
    # Looks at least once, so a zero wait asks "is it up right now?".
    while ($true) {
        $dialog = $automation::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $isDialog)
        if ($dialog) { return $dialog }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 250
    }
}

# The app is running: Velopack stops it before the hook, which is part of what this checks.
function Uninstall-V4([ValidateSet('delete', 'no answer')] [string] $answer) {
    $updater = Start-Process (Join-Path $installDir 'Update.exe') -ArgumentList 'uninstall', '--silent' -PassThru
    $dialog = Find-UninstallDialog 30
    Check ($null -ne $dialog) 'uninstall asks "Also delete your data?"'
    if ($dialog) {
        $nameIs = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), 'Also delete your data'
        $box = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameIs)
        $toggle = $box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        Check ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off) 'the box starts unticked'

        if ($answer -eq 'delete') {
            $toggle.Toggle()
            [TaskDialogButtons]::Click([IntPtr]$dialog.Current.NativeWindowHandle, 100)
        } else {
            $shown = Get-Date
            $closed = Wait-Until { $null -eq (Find-UninstallDialog 0) } 35
            $openFor = ((Get-Date) - $shown).TotalSeconds
            Write-Output ("      closed after {0:N1} s" -f $openFor)
            # The dialog was found a moment after it opened, so this reads a little under 25 s.
            Check ($closed -and $openFor -ge 20) 'an unanswered dialog closes by itself after about 25 s'
        }
    }

    Check ($updater.WaitForExit(90000)) 'uninstall finishes'
    # Update.exe deletes itself, and with it the folder, a few seconds after it exits; a reinstall
    # before then loses its files to that delete.
    Check (Wait-Until { -not (Test-Path $installDir) } 30) 'the install folder is removed'
    Check (-not (Test-Path $userUninstallKey)) 'the Apps & features entry is removed'
    Check (-not (Test-Path $shortcut)) 'the Start Menu shortcut is removed'
    Check ($null -eq (Get-RunValue)) 'the Run value is removed'
}

Write-Output '== Preconditions'
Check (-not (Test-Path $dataRoot)) 'no VoiceCtrl data on this machine (the test step writes none)'
if (Test-Path $dataRoot) { Get-ChildItem $dataRoot -Recurse | ForEach-Object { Write-Output "      $($_.FullName)" } }
Check (-not (Test-Path $installDir)) 'no VoiceCtrl install on this machine'
Check ($null -eq (Get-RunValue)) 'no VoiceCtrl Run value'
$setupBytes = [IO.File]::ReadAllBytes((Resolve-Path $Setup))
Check ([Text.Encoding]::ASCII.GetString($setupBytes).Contains('level="asInvoker"')) 'Setup.exe asks for no elevation (asInvoker)'

Write-Output '== Fresh install'
Install-V4
Check (-not (Test-Path $dataRoot)) 'installing alone writes no user data'
Start-V4
Check (-not (Read-Settings).app.setupComplete) 'a fresh install still has Setup to do'
Write-Output '== Uninstall, deleting data'
Uninstall-V4 'delete'
Check (Wait-Until { -not (Test-Path $dataRoot) } 10) "ticked: $dataRoot is deleted"
if (Test-Path $dataRoot) {
    Get-ChildItem $dataRoot -Recurse -File | Select-Object -First 20 | ForEach-Object { Write-Output "      left: $($_.FullName)" }
    Get-Process msedgewebview2 -ErrorAction SilentlyContinue | ForEach-Object { Write-Output "      running: msedgewebview2 $($_.Id), started $($_.StartTime)" }
    Remove-Item $dataRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# v2.1.0 (the oldest public build with a .env) is a zip with the .env next to the exe, a Run value
# without --autostart, and prefs.json in the same data root v4 uses. It has no single-instance
# mutex, so v4 can't close a running copy: the trial and the README tell people to quit it first
# (VED-49). This seeds exactly what v2.1.0 leaves behind.
Write-Output '== v2.1.0 -> v4 upgrade'
$v2Folder = Join-Path $env:TEMP 'VoiceCtrl v2.1.0'
$v2Exe = Join-Path $v2Folder 'VoiceCtrl.exe'
New-Item -ItemType Directory -Force $v2Folder | Out-Null
Set-Content $v2Exe 'stands in for the v2.1.0 exe; only its path matters'
$v2EnvText = "# VoiceCtrl configuration`r`nGEMINI_API_KEY=AIzaSyLifecycleV2Check000000000000000000`r`nTRANSCRIPTION_MODE=Offline`r`nCLEANUP_LEVEL=light`r`n"
Set-Content (Join-Path $v2Folder '.env') $v2EnvText -NoNewline
New-Item -ItemType Directory -Force $dataRoot | Out-Null
Set-Content (Join-Path $dataRoot 'prefs.json') '{"TranscriptionMode":"Online"}'
Set-ItemProperty $runKey -Name VoiceCtrl -Value "`"$v2Exe`""
$v2Shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$v2Shortcut.TargetPath = $v2Exe
$v2Shortcut.Save()

Install-V4
Start-V4
$settings = Read-Settings
Check ([bool]$settings.app.setupComplete) 'Setup is skipped'
Check ($settings.transcription.mode -eq 'Online') 'the Mode from prefs.json carries over'
Check ($settings.transcription.defaultCleanup -eq 'light') 'the cleanup level from .env carries over'
Check (Test-Path (Join-Path $dataRoot 'secrets.bin')) 'the key is saved to secrets.bin'
$log = Read-Log
Check ($log -match [regex]::Escape("v3 migration: .env found at $v2Folder") -and $log -match 'key carried over') 'the log shows the v2 .env found and the key carried over'
Check ((Get-Content (Join-Path $v2Folder '.env') -Raw) -eq $v2EnvText) 'the v2 .env is left exactly as it was'
Check ((Get-ShortcutTarget) -like "$installDir\*") 'the Start Menu shortcut moved to v4'
Write-Output '== Uninstall, deleting data'
Uninstall-V4 'delete'
Check (Wait-Until { -not (Test-Path $dataRoot) } 10) "ticked: $dataRoot is deleted"
if (Test-Path $dataRoot) { Remove-Item $dataRoot -Recurse -Force -ErrorAction SilentlyContinue }
Remove-Item $v2Folder -Recurse -Force -ErrorAction SilentlyContinue

Write-Output '== v3 -> v4 upgrade'
$v3Folder = Join-Path $env:TEMP 'VoiceCtrl v3'
$v3Exe = Join-Path $v3Folder 'VoiceCtrl.exe'
New-Item -ItemType Directory -Force $v3Folder | Out-Null
Set-Content $v3Exe 'stands in for the v3 exe; only its path matters'
Set-Content (Join-Path $v3Folder '.env') "GEMINI_API_KEY=AIzaSyLifecycleCheck00000000000000000000`r`nTRANSCRIPTION_MODE=Offline"
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty $runKey -Name VoiceCtrl -Value "`"$v3Exe`""
$v3Shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$v3Shortcut.TargetPath = $v3Exe
$v3Shortcut.Save()

Install-V4
Start-V4
$settings = Read-Settings
Check ([bool]$settings.app.setupComplete) 'Setup is skipped'
Check (Test-Path (Join-Path $dataRoot 'secrets.bin')) 'the key is saved to secrets.bin'
$log = Get-Content (Join-Path $dataRoot 'log.txt') -Raw
Check ($log -match [regex]::Escape("v3 migration: .env found at $v3Folder") -and $log -match 'key carried over') 'the log shows the v3 .env found and the key carried over'
Write-Output "== Auto-update to $UpdateVersion"
$packages = Join-Path $installDir 'packages'
$newPackage = Join-Path $packages "VoiceCtrlApp-$UpdateVersion-full.nupkg"
$installedBefore = Get-InstalledVersion
Stop-V4

Write-Output '   a tampered package'
$tampered = Join-Path $env:TEMP 'VoiceCtrl tampered feed'
New-TamperedFeed $tampered
$env:VOICECTRL_UPDATE_FEED = $tampered
Start-V4
# The first check is 30 s after the start.
Check (Wait-Until { (Read-Log) -match "Update $([regex]::Escape($UpdateVersion)) download\] UpdateVerificationException: Update refused: the package's SHA-256 doesn't match" } 150) 'the check finds it, and the signed manifest refuses it'
Check (-not (Test-Path $newPackage)) 'the refused package is deleted'
Stop-V4
Start-V4
Start-Sleep -Seconds 5
Check ((Get-InstalledVersion) -eq $installedBefore) "the next start stays on $installedBefore"
Stop-V4

Write-Output '   the genuine package'
$env:VOICECTRL_UPDATE_FEED = (Resolve-Path $UpdateFeed).Path
Start-V4
Check (Wait-Until { (Read-Log) -match "Updates: $([regex]::Escape($UpdateVersion)) downloaded and verified" } 150) 'it downloads in the background and passes the signature check'
Check (Test-Path (Join-Path $packages "VoiceCtrlApp-$UpdateVersion-manifest.json")) 'the signed manifest is kept beside it'
Check ((Get-InstalledVersion) -eq $installedBefore) 'nothing is installed while VoiceCtrl runs'
Stop-V4
Start-Process $exe -ArgumentList '--autostart' | Out-Null
Check (Wait-Until { (Get-InstalledVersion) -eq $UpdateVersion } 120) "the next start installs $UpdateVersion"
Check (Wait-Until { (Read-Settings).app.lastRunVersion -eq $UpdateVersion } 60) "$UpdateVersion starts"
Check ((Read-Settings).app.updateCard.version -eq $UpdateVersion) "the Updated to $UpdateVersion card is recorded"
Check (Wait-Until { -not (Test-Path (Join-Path $dataRoot 'starting.json')) } 60) 'that start finishes (no failed-update marker left)'
Check ($null -ne (Get-Process VoiceCtrl -ErrorAction SilentlyContinue)) 'VoiceCtrl is running again'
Remove-Item Env:\VOICECTRL_UPDATE_FEED

Write-Output '== Uninstall, no answer'
Uninstall-V4 'no answer'
Check ((Test-Path (Join-Path $dataRoot 'settings.json')) -and (Test-Path (Join-Path $dataRoot 'secrets.bin'))) 'unticked: settings and key are kept'

Write-Output ''
if ($script:failures -gt 0) {
    Write-Output "$($script:failures) check(s) failed"
    exit 1
}
Write-Output 'All install lifecycle checks passed'
