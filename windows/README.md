# VoiceFlow — porting Windows

Questa cartella contiene la soluzione .NET 8 per la versione Windows di
VoiceFlow. È una soluzione C#/.NET separata dal progetto Swift/macOS che
vive nella root del repository: non condivide file, target né dipendenze
con esso.

## Prerequisiti sul PC Windows

- **Git** — https://git-scm.com/download/win
- **.NET 8 SDK** — scaricabile da https://dotnet.microsoft.com/download/dotnet/8.0,
  oppure da terminale (PowerShell) con winget:

  ```powershell
  winget install Microsoft.DotNet.SDK.8
  ```

Verifica l'installazione con:

```powershell
dotnet --version
```

Deve riportare una versione `8.x`.

## Clonare il repository privato

Il repository è privato: serve un account GitHub con accesso al repo e
l'autenticazione configurata (token o SSH).

```powershell
git clone https://github.com/Pika899/voiceflow.git
cd voiceflow
git checkout feature/windows
```

## Build, test ed esecuzione

Tutti gli script si lanciano da PowerShell, dalla cartella `windows/scripts`
o con il percorso completo dalla root del repo:

```powershell
# Compila la soluzione (Core, App, Tests) in configurazione Release
.\windows\scripts\build.ps1

# Esegue la suite di test xUnit
.\windows\scripts\test.ps1

# Avvia l'app Windows Forms
.\windows\scripts\run.ps1
```

Il progetto `VoiceFlow.App` richiede Windows (target `net8.0-windows`,
Windows Forms) e non compila né si esegue su macOS o Linux.
