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

6. **Al primo avvio il modello non è ancora scaricato**: subito dopo la
   comparsa dell'icona nella tray, senza bisogno di premere l'hotkey o di
   aprire nulla, compare **da sola** la finestra "Model not downloaded" —
   l'app controlla il modello configurato all'avvio, prima ancora di
   registrare l'hotkey. Clic su **OK** per scaricarlo: appare una finestra
   con una barra di avanzamento determinata. A download completo (verificato
   con checksum SHA-256 contro i valori noti) il motore Whisper si carica e
   l'icona torna a riposo (grigia). Lo stesso succede, sempre senza premere
   l'hotkey, se cambi modello dalle Impostazioni verso uno non ancora
   scaricato: la finestra compare non appena selezioni la nuova voce.

7. **Dettatura**: tieni premuto **Ctrl** (tasto da solo, senza combinarlo con
   altro) in un campo di testo, parla, rilascia. È l'opzione di default.
   Dalle Impostazioni puoi passare a **Ctrl+Alt+Space** come alternativa
   (utile su tastiere esterne o se preferisci una combinazione più
   "esplicita"); il cambio è immediato, senza bisogno di rilanciare l'app.
   L'icona nella tray passa grigia (a riposo) → rossa (in ascolto) → blu
   (in trascrizione) → grigia.

   Con Ctrl tenuto, premere un **altro tasto** mentre lo tieni premuto
   annulla la dettatura senza trascrivere niente (nessun testo digitato,
   nessun suono di fine) — è così che le scorciatoie che usano Ctrl
   (Ctrl+C, Ctrl+V, Ctrl+Z, ecc.) continuano a funzionare normalmente:
   VoiceFlow non le blocca né le intercetta, si limita ad accorgersi che
   quella pressione di Ctrl non era una dettatura. Costo onesto di questo
   comportamento: con i suoni attivi, ogni scorciatoia Ctrl+… fa comunque
   sentire il suono di **inizio** dettatura (parte prima di sapere se sarà
   una scorciatoia) e l'icona in tray lampeggia rossa per un istante prima
   di tornare grigia. E **Ctrl+clic del mouse** avvia comunque una
   dettatura: nessun tasto la annulla in quel caso, quindi tenendo Ctrl
   mentre fai clic per aprire un link, ad esempio, VoiceFlow entra in
   ascolto — rilascia semplicemente Ctrl per chiuderla senza dettare nulla.
   Se preferisci evitarlo del tutto, passa a Ctrl+Alt+Space dalle
   Impostazioni.

   Se scegli Ctrl+Alt+Space: il rilascio è rilevato appena una qualsiasi
   delle tre componenti della combinazione si solleva (non serve
   rilasciarle tutte insieme) — stesso comportamento del rilascio
   dell'hotkey sul Mac.

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

## Log diagnostico

VoiceFlow tiene un log locale, solo testo, in
`%LOCALAPPDATA%\VoiceFlow\voiceflow.log`. Contiene:

- i codici di errore, con la catena completa dell'eccezione (messaggio e
  stack trace) quando ce n'è una in mano;
- un evento per ogni fase di una dettatura (avvio ascolto, fine ascolto,
  trascrizione, iniezione, annullamento, timeout, risultato tardivo
  scartato), con durate e conteggi — non contenuto;
- la durata di caricamento del modello Whisper a ogni avvio dell'app.

**Non contiene mai** il testo dettato, l'audio catturato o il risultato
della trascrizione: solo lunghezze e durate. Resta sempre e solo sul PC.

Il file è limitato a 1 MB: superata la soglia viene cancellato e ricreato da
zero, invece di crescere indefinitamente. Viene rimosso da
`uninstall.ps1` insieme al resto di `%LOCALAPPDATA%\VoiceFlow` (a meno di
`-KeepModels`, che lascia l'intera cartella — modelli e log — al suo posto).

## Disinstallazione

VoiceFlow non ha un installer: gira semplicemente dalla cartella `publish`
(o da un clone del repository tramite `run.ps1`). Disinstallarlo vuol dire
rimuovere le tracce che lascia sul PC, che sono tre:

1. **Avvio automatico** — una voce nel registro (solo se avevi attivato
   "Launch at login" nelle Impostazioni).
2. **Impostazioni** — `%APPDATA%\VoiceFlow\settings.json`, un file piccolo.
3. **Modelli Whisper scaricati** — `%LOCALAPPDATA%\VoiceFlow\models\`. Sono
   la parte grande: se ne hai scaricati più di uno (Base, Small, Medium)
   possono occupare diverso spazio su disco.

Lo script `windows\scripts\uninstall.ps1` automatizza i primi due punti e,
per default, anche il terzo:

```powershell
.\windows\scripts\uninstall.ps1
```

Chiede conferma prima di procedere (`Continuare? [s/N]`, rispondi `s` per
proseguire). Opzioni:

```powershell
# Non cancellare i modelli scaricati (utile se pensi di reinstallare)
.\windows\scripts\uninstall.ps1 -KeepModels

# Salta la conferma (per uso non presidiato/scriptato)
.\windows\scripts\uninstall.ps1 -Force
```

Lo script arresta il processo `VoiceFlow.App` se è in esecuzione, rimuove la
voce di avvio automatico, e cancella le cartelle di impostazioni e modelli
secondo le opzioni scelte.

**Ultimo passaggio, a mano**: lo script vive dentro la cartella dell'app
(`windows\publish\`, o l'intero clone del repository se hai usato
`run.ps1`) e non può cancellare la cartella in cui si trova mentre è ancora
in esecuzione. Alla fine stampa il percorso esatto: chiudi la finestra di
PowerShell e cancella quella cartella a mano per completare la
disinstallazione.

## Limiti noti

- **CPU senza AVX2**: la libreria whisper.cpp standard di Whisper.net richiede
  AVX2; su processori che non ce l'hanno (es. Intel Core di 2ª/3ª generazione
  come l'i7-3770, o molti Pentium/Celeron) l'app include anche il runtime
  `Whisper.net.Runtime.NoAvx`, che Whisper.net sceglie da solo. Senza di esso
  il caricamento del modello fallisce con la finestra "Model couldn't be
  loaded" anche a file integro (osservato su un i7-3770 il 2026-09-17).
- **Finestre elevate (amministratore)**: `SendInput` non può scrivere in una
  finestra che gira con privilegi più alti del processo che invia l'input
  (UIPI — User Interface Privilege Isolation, una protezione di Windows).
  Dettare dentro un'app aperta "Esegui come amministratore" (es. Gestione
  attività) non inserisce testo, senza un errore visibile: limite noto della
  piattaforma, non un bug dell'app.
- **Due opzioni fisse, nessun rebind libero**: come sul Mac (fn/🌐 e
  Control+Option+Space), v1 offre solo due combinazioni per il
  push-to-talk — **Ctrl** (default) e **Ctrl+Alt+Space** — selezionabili
  dalle Impostazioni; non c'è un key-recorder per assegnarne una a piacere.
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
- Le ultime righe di `%LOCALAPPDATA%\VoiceFlow\voiceflow.log` (vedi "Log
  diagnostico" sopra — non contiene mai il testo dettato).
