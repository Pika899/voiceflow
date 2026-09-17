$ErrorActionPreference = "Stop"

dotnet publish "$PSScriptRoot/../VoiceFlow.App" -c Release -r win-x64 --self-contained false -o "$PSScriptRoot/../publish"
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Pubblicato in: $PSScriptRoot/../publish"
Write-Host "L'eseguibile e' framework-dependent: sul PC di destinazione deve essere"
Write-Host "installato il .NET 8 Desktop Runtime (non basta l'SDK di sviluppo)."
Write-Host "  winget install Microsoft.DotNet.DesktopRuntime.8"
