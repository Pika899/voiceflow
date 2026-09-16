<#
.SYNOPSIS
    Removes the traces VoiceFlow leaves on this PC: the running process, the
    "launch at login" registry entry, saved settings, and (unless
    -KeepModels) downloaded Whisper models. It cannot delete the app's own
    folder while running from inside it — see the manual step it prints at
    the end.

.PARAMETER KeepModels
    Do not delete %LOCALAPPDATA%\VoiceFlow (the downloaded Whisper models).
    Everything else is still removed.

.PARAMETER Force
    Skip the confirmation prompt.
#>

param(
    [switch]$KeepModels,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "VoiceFlow"
$appDataDir = Join-Path $env:APPDATA "VoiceFlow"
$localAppDataDir = Join-Path $env:LOCALAPPDATA "VoiceFlow"

# Purely lexical path normalization (works even if the target does not
# exist yet, unlike Resolve-Path).
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\publish"))
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$isInsideClone = Test-Path (Join-Path $repoRoot ".git")

# --- Snapshot current state (used for the preview and the prompt) ---------

$runningProcesses = Get-Process -Name VoiceFlow.App -ErrorAction SilentlyContinue
$runValuePresent = $null -ne (Get-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue)
$appDataExists = Test-Path $appDataDir
$localAppDataExists = Test-Path $localAppDataDir

# --- Preview + confirmation ------------------------------------------------

Write-Host "Disinstallazione di VoiceFlow"
Write-Host "=============================="
Write-Host ""

if (-not $Force) {
    Write-Host "Verranno rimossi:"
    if ($runningProcesses) {
        Write-Host "  - Il processo VoiceFlow.App in esecuzione ($($runningProcesses.Count))"
    } else {
        Write-Host "  - (nessun processo VoiceFlow.App in esecuzione)"
    }
    if ($runValuePresent) {
        Write-Host "  - La voce di avvio automatico nel registro ($runKeyPath\$runValueName)"
    } else {
        Write-Host "  - (nessuna voce di avvio automatico presente)"
    }
    if ($appDataExists) {
        Write-Host "  - Le impostazioni: $appDataDir"
    } else {
        Write-Host "  - (nessuna cartella di impostazioni presente: $appDataDir)"
    }
    if ($KeepModels) {
        Write-Host "  - I modelli NON verranno toccati (-KeepModels): $localAppDataDir resta al suo posto"
    } elseif ($localAppDataExists) {
        Write-Host "  - I modelli Whisper scaricati (grandi): $localAppDataDir"
    } else {
        Write-Host "  - (nessuna cartella di modelli presente: $localAppDataDir)"
    }
    Write-Host ""
    Write-Host "Questo script NON puo' eliminare la cartella dell'app stessa"
    Write-Host "(vive dentro di essa): quel passaggio va fatto a mano, vedi sotto."
    Write-Host ""

    $response = Read-Host "Continuare? [s/N]"
    if ($response -notmatch '^[sS]$') {
        Write-Host "Operazione annullata."
        exit 0
    }
    Write-Host ""
}

# --- (a) Stop the running process ------------------------------------------

Write-Host "1. Arresto del processo VoiceFlow.App..."
$runningProcesses = Get-Process -Name VoiceFlow.App -ErrorAction SilentlyContinue
if ($runningProcesses) {
    $runningProcesses | Stop-Process -Force
    foreach ($proc in $runningProcesses) {
        Wait-Process -Id $proc.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
    Write-Host "   Arrestato."
} else {
    Write-Host "   Nessun processo in esecuzione, niente da fare."
}

# --- (b) Remove the "launch at login" registry value ------------------------

Write-Host "2. Rimozione della voce di avvio automatico dal registro..."
$existingValue = Get-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
if ($existingValue) {
    Remove-ItemProperty -Path $runKeyPath -Name $runValueName -Force
    Write-Host "   Rimossa."
} else {
    Write-Host "   Nessuna voce presente, niente da fare."
}

# --- (c) Remove settings -----------------------------------------------------

Write-Host "3. Rimozione delle impostazioni ($appDataDir)..."
if (Test-Path $appDataDir) {
    Remove-Item -Path $appDataDir -Recurse -Force
    Write-Host "   Rimossa."
} else {
    Write-Host "   Non presente, niente da fare."
}

# --- (d) Remove downloaded models, unless -KeepModels ------------------------

if ($KeepModels) {
    Write-Host "4. Modelli mantenuti (-KeepModels): $localAppDataDir non viene toccato."
} else {
    Write-Host "4. Rimozione dei modelli scaricati ($localAppDataDir)..."
    if (Test-Path $localAppDataDir) {
        Remove-Item -Path $localAppDataDir -Recurse -Force
        Write-Host "   Rimossa."
    } else {
        Write-Host "   Non presente, niente da fare."
    }
}

# --- (e) Manual step: the app folder itself ----------------------------------

Write-Host ""
Write-Host "Ultimo passaggio, da fare a mano"
Write-Host "================================="
Write-Host "Questo script vive dentro la cartella dell'app e non puo' cancellarla"
Write-Host "mentre e' ancora in esecuzione. Chiudi questa finestra di PowerShell,"
Write-Host "poi elimina a mano:"
Write-Host ""
if (Test-Path $publishDir) {
    Write-Host "  $publishDir"
} else {
    Write-Host "  $publishDir  (non trovata)"
}
if ($isInsideClone) {
    Write-Host ""
    Write-Host "Questa e' una copia clonata del repository: se non ti serve piu',"
    Write-Host "puoi eliminare l'intera cartella del clone invece della sola publish\:"
    Write-Host ""
    Write-Host "  $repoRoot"
}
Write-Host ""
Write-Host "Fatto: processo, avvio automatico, impostazioni$(if (-not $KeepModels) { ' e modelli' }) rimossi."
