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

# Avvia l'app Windows Forms (richiede l'SDK: usa `dotnet run`)
.\windows\scripts\run.ps1

# Pubblica un eseguibile pronto da distribuire (vedi sotto)
.\windows\scripts\publish.ps1
```

Il progetto `VoiceFlow.App` richiede Windows (target `net8.0-windows`,
Windows Forms) e non compila né si esegue su macOS o Linux.

## Pubblicare un eseguibile

`publish.ps1` produce una build *framework-dependent* (l'eseguibile non
include il runtime .NET, dev'essere già installato sul PC di destinazione)
in `windows/publish/`:

```powershell
.\windows\scripts\publish.ps1
```

Sul PC dove gira l'app serve il **.NET 8 Desktop Runtime** (non l'SDK
completo, a meno di voler anche compilare in locale):

```powershell
winget install Microsoft.DotNet.DesktopRuntime.8
```

`windows/publish/` è ignorata da git (`windows/.gitignore`): va rigenerata
a ogni release, non è un posto dove salvare stato.

## Prima esecuzione sul PC di destinazione — cosa aspettarsi

1. **Installa i prerequisiti**: Git e il .NET 8 Desktop Runtime.

   ```powershell
   winget install Git.Git
   winget install Microsoft.DotNet.DesktopRuntime.8
   ```

2. **Clona il repository** (privato: serve un account con accesso e
   autenticazione configurata):

   ```powershell
   git clone https://github.com/Pika899/voiceflow.git
   cd voiceflow
   git checkout feature/windows
   ```

3. **Compila e pubblica** (richiede l'SDK, non solo il runtime):

   ```powershell
   winget install Microsoft.DotNet.SDK.8
   .\windows\scripts\publish.ps1
   ```

4. **Lancia `windows\publish\VoiceFlow.App.exe`**. Windows mostra
   **"Windows protected your PC"** (SmartScreen): l'eseguibile non è firmato
   con un certificato riconosciuto da Microsoft, come previsto per una build
   locale non distribuita pubblicamente. Clic su **"More info"**, poi
   **"Run anyway"**. Questo passaggio va ripetuto a ogni nuova build, perché
   SmartScreen valuta il file (non un'identità stabile come sul Mac).

5. **Nessuna icona in finestra**: l'app vive solo nella tray (l'area delle
   icone accanto all'orologio, in basso a destra). Se non è visibile,
   controlla la freccia "mostra icone nascoste".

6. **Al primo avvio il modello non è ancora scaricato**: appena provi a
   dettare compare la finestra "Model not downloaded" (vedi sotto). Clic su
   **OK** per scaricarlo: appare una finestra con una barra di avanzamento
   determinata. A download completo (verificato con checksum SHA-256 contro
   i valori noti) il motore Whisper si carica e l'icona torna a riposo
   (grigia).

7. **Dettatura**: tieni premuto **Ctrl+Alt+Space** in un campo di testo,
   parla, rilascia. Il rilascio è rilevato appena una qualsiasi delle tre
   componenti della combinazione si solleva (non serve rilasciarle tutte
   insieme) — stesso comportamento del rilascio dell'hotkey sul Mac.
   L'icona nella tray passa grigia (a riposo) → rossa (in ascolto) → blu
   (in trascrizione) → grigia.

## Dove vivono i file

| Cosa | Percorso |
|---|---|
| Impostazioni (`settings.json`) | `%APPDATA%\VoiceFlow\settings.json` |
| Modelli Whisper scaricati | `%LOCALAPPDATA%\VoiceFlow\models\` |

I modelli non sono mai inclusi nell'eseguibile: si scaricano al primo utilizzo
da huggingface.co (URL e checksum in `VoiceFlow.Core/ModelCatalogue.cs`).

## Privacy

L'unica chiamata di rete che l'app fa è il download del modello Whisper
scelto, al primo utilizzo (o quando il file manca/risulta corrotto). Una
volta scaricato e verificato, la dettatura è interamente locale: audio e
testo non lasciano il PC. Per verificarlo: disattiva il Wi-Fi dopo che il
modello è già presente e prova a dettare — deve funzionare identico.

## Limiti noti

- **Finestre elevate (amministratore)**: `SendInput` non può scrivere in una
  finestra che gira con privilegi più alti del processo che invia l'input
  (UIPI — User Interface Privilege Isolation, una protezione di Windows).
  Dettare dentro un'app aperta "Esegui come amministratore" (es. Gestione
  attività) non inserisce testo, senza un errore visibile: limite noto della
  piattaforma, non un bug dell'app.
- **Nessun tasto fn**: a differenza del Mac, Windows non ha un tasto
  modificatore equivalente a fn/🌐; l'hotkey su questa piattaforma è fissa,
  **Ctrl+Alt+Space** (nessuna alternativa, nessun rebind libero in v1).
- **Antivirus**: alcuni antivirus segnalano o bloccano eseguibili che usano
  `SendInput` per digitare testo sinteticamente, perché la stessa API è usata
  anche da malware. Se il tuo antivirus lo segnala, aggiungi un'eccezione per
  `VoiceFlow.App.exe` dopo aver verificato che proviene dalla tua build.
- **Eseguibile non firmato**: senza certificato di firma codice, SmartScreen
  interviene a ogni nuova build (vedi sopra). Non è previsto un certificato
  di firma in v1 (fuori scope, come sul Mac).

## Come segnalare un problema

Se qualcosa non funziona, includi nella segnalazione:

- Versione di Windows (`winver`) e se è x64.
- Modello Whisper in uso (Base / Small / Medium) e lingua impostata.
- Il testo esatto della finestra di dialogo comparsa, se ce n'è una (vedi i
  testi elencati nella checklist di verifica manuale).
- Cosa è stato digitato dall'app rispetto a cosa ci si aspettava (copia/incolla
  entrambi se possibile).
- App di destinazione in cui stavi dettando (es. Blocco note, VS Code,
  Chrome) e se girava con privilegi di amministratore.
