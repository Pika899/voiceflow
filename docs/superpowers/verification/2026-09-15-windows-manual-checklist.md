# VoiceFlow Windows — checklist di verifica manuale

Questa è la parte del lavoro che solo una persona su un PC Windows può fare:
parlare nel microfono, concedere il via libera a SmartScreen, guardare la
tray. Nessun passo qui sotto è stato eseguito da un agente (nessuna macchina
Windows è disponibile in questo ambiente di sviluppo), e nessun numero è
stato inventato: le caselle restano vuote finché non le compili tu con ciò
che osservi.

Regola: **scrivi solo valori misurati**. Se un passo non lo fai, lascialo
vuoto — una casella vuota è un'informazione, un numero plausibile è una
bugia.

## A. Prerequisiti e installazione

- [ ] `winget install Git.Git`
- [ ] `winget install Microsoft.DotNet.DesktopRuntime.8` (basta il runtime se
      usi un eseguibile già pubblicato; se compili in locale serve anche
      `winget install Microsoft.DotNet.SDK.8`)
- [ ] Clona il repository privato e passa al branch:
      `git clone https://github.com/Pika899/voiceflow.git`,
      `git checkout feature/windows`
- [ ] `.\windows\scripts\publish.ps1` produce `windows\publish\VoiceFlow.App.exe`
      senza errori

## B. Primo avvio

- [ ] Lanciando `VoiceFlow.App.exe` compare **"Windows protected your PC"**
      (SmartScreen) — atteso, l'eseguibile non è firmato: clic su **"More
      info"**, poi **"Run anyway"**
- [ ] L'app non apre nessuna finestra: compare solo un'icona nella tray
      (area accanto all'orologio; controlla "mostra icone nascoste" se non
      la vedi)
- [ ] Al primo avvio, subito dopo la comparsa dell'icona nella tray, compare
      **da sola** (nessun hotkey da premere, nessuna finestra da aprire) la
      finestra **"Model not downloaded"** con il testo: *"The speech model
      hasn't been downloaded yet, or failed a corruption check. Click OK to
      download it now, or Cancel."* — l'app controlla il modello configurato
      appena parte, prima di registrare l'hotkey
- [ ] Clic su **OK** → appare la finestra di download con etichetta
      "Downloading <model> model..." e una barra di avanzamento che si
      muove
- [ ] A download completo la finestra si chiude da sola e l'icona in tray
      torna grigia (a riposo) — significa che il motore Whisper si è
      caricato
- [ ] **Annulla durante il download**: rilancia il download (es. cambia
      modello) e clic su **Annulla** nella finestra di progresso → la
      finestra si chiude subito, nessuna finestra di errore compare, e
      `%LOCALAPPDATA%\VoiceFlow\models\` non contiene alcun file `.part`
      (il `CancellationToken` interrompe lo stream HTTP e `ModelManager`
      cancella il `.part` prima di rilanciare l'eccezione) — al lancio
      successivo dell'app compare di nuovo la finestra "Model not
      downloaded"

## C. Dettatura end-to-end

- [ ] Blocco note: clic in un documento, tieni premuto **Ctrl** (default),
      parla, rilascia → il testo compare al cursore
- [ ] Due dettature di seguito (rilascia, ri-premi, parla) → tra le due c'è
      uno spazio, non "siamo!Adesso"
- [ ] Dettando subito dopo una virgola, o dopo "l'", non compare uno spazio
      in più
- [ ] VS Code (app Electron): il testo compare, **una sola volta**
- [ ] Un campo di testo in un browser (es. la barra degli indirizzi o un
      form): il testo compare, **una sola volta**
- [ ] L'icona nella tray passa: grigia (a riposo) → rossa (in ascolto) →
      blu (in trascrizione) → grigia
- [ ] **Ctrl+C annulla, non detta**: in Blocco note, seleziona del testo e
      premi **Ctrl+C** (tenendo Ctrl, premi C) → il testo viene copiato
      normalmente (verificalo incollandolo altrove con Ctrl+V), **nessuna
      trascrizione parte** (nessun testo VoiceFlow viene digitato) e
      l'icona in tray torna grigia — il suono di inizio dettatura può
      comunque sentirsi (parte prima che VoiceFlow sappia che sarà una
      scorciatoia), ma non quello di fine
- [ ] **Cambio tasto dalle Impostazioni, senza rilanciare**: apri
      Impostazioni, cambia "Push-to-talk" da Ctrl a Ctrl+Alt+Space (o
      viceversa), chiudi la finestra → senza uscire e riaprire l'app, prova
      subito a dettare con il **nuovo** tasto scelto → funziona; il tasto
      precedente non avvia più una dettatura
- [ ] **AltGr su tastiera italiana**: con Ctrl impostato come push-to-talk,
      premi **AltGr+ò** (produce `@` su layout italiano) in un campo di
      testo → compare `@`, **nessuna dettatura parte** (AltGr viene
      consegnato a Windows come Ctrl sinistro + Alt destro, quindi lo stesso
      meccanismo di annullamento di Ctrl+C si applica)

## D. Impostazioni

- [ ] Clic sull'icona in tray → si apre la finestra Impostazioni con
      Model, Language, Play sounds, Launch at login, Push-to-talk (con le
      due voci "Ctrl (hold)" e "Ctrl+Alt+Space", "Ctrl (hold)" selezionata
      di default su un'installazione pulita)
- [ ] Toggle "Play sounds" ON → si sentono due suoni di sistema (uno a
      inizio, uno a fine dettatura); OFF → nessun suono
- [ ] Cambia modello verso uno non ancora scaricato → la finestra "Model not
      downloaded" compare **subito**, appena selezioni la nuova voce nel menu
      a tendina — non serve premere l'hotkey
- [ ] Cambia modello, esci (Quit dalla tray o dal pulsante nella finestra
      Impostazioni) e rilancia l'app → la scelta del modello è rimasta
- [ ] Cambia lingua, esci e rilancia → la scelta della lingua è rimasta
- [ ] "Launch at login" ON → riavvia il PC (o controlla subito) e verifica
      che VoiceFlow compaia in **Gestione attività → App di avvio**
      (Task Manager → Startup apps)
- [ ] "Launch at login" OFF → VoiceFlow non compare più in App di avvio

## E. Offline (il claim centrale del prodotto)

- [ ] Con il modello già scaricato, disattiva completamente il Wi-Fi → la
      dettatura funziona identica
- [ ] Riattiva il Wi-Fi

## F. Gestione errori

- [ ] **Conflitto hotkey**: con il push-to-talk impostato su Ctrl+Alt+Space
      (cambialo dalle Impostazioni se è ancora sul default Ctrl), registra
      Ctrl+Alt+Space in un altro programma (es. AutoHotkey, o le scorciatoie
      di un'altra app) → rilancia VoiceFlow → compare la finestra **"Hotkey
      already in use"** con il testo: *"Ctrl+Alt+Space is already in use by
      another application, or couldn't be registered. Pick a different
      push-to-talk key in Settings, or free the shortcut in the other app,
      then try again."* Verifica che il nome della combinazione nel testo
      corrisponda a quella davvero configurata (se il test lo ripeti con
      Ctrl come default, il testo deve dire "Ctrl", non "Ctrl+Alt+Space").
      Libera la combinazione dopo il test.
- [ ] **Modello corrotto**: cancella il file del modello (PowerShell:
      `Remove-Item "$env:LOCALAPPDATA\VoiceFlow\models\ggml-base.bin"`) →
      rilancia → compare **"Model not downloaded"** (stesso testo del primo
      avvio: il file manca, quindi il controllo del checksum fallisce nello
      stesso modo) → Download → il modello si ri-scarica e l'engine si
      ricarica
- [ ] **Finestra elevata**: apri Gestione attività **come amministratore**
      (tasto destro → "Esegui come amministratore"), prova a dettare in un
      suo campo di testo (se presente) → nessun testo compare, nessun
      errore visibile — limite noto (UIPI), non un bug
- [ ] **Permesso microfono negato**: Impostazioni → Privacy e sicurezza →
      Microfono → disattiva l'accesso per le app desktop → premi
      **Ctrl+Alt+Space** → compare la finestra **"Microphone access
      needed"** con il testo: *"VoiceFlow needs microphone access to
      transcribe your speech. Click OK to open Windows Settings > Privacy
      & security > Microphone, or Cancel."* — clic su **OK** apre la
      pagina delle Impostazioni Windows. Riattiva l'accesso al microfono
      al termine del test.
- [ ] **Nessun microfono disponibile**: disabilita il dispositivo audio in
      Gestione dispositivi (o scollega un microfono USB) → prova a
      dettare → compare la finestra **"Couldn't start recording"** con il
      testo: *"VoiceFlow couldn't open the microphone. Check that one is
      connected and not in use by another app, then try again."*
      Riabilita/ricollega il microfono al termine del test.
- [ ] **Timeout trascrizione**: fai una dettatura abbastanza lunga da far
      superare i 60 s di trascrizione (dipende dal PC e dal modello usato
      — prova con il modello medium e una frase lunga) → compare la
      finestra **"Transcription failed"** con il testo: *"Something went
      wrong during transcription. Try again."*, e quando il risultato
      tardivo arriva in background non viene digitato alcun testo. Se il
      timeout non scatta mai su questo PC, annota "non riproducibile su
      questo PC" invece di un numero inventato.
- [ ] **Singola istanza**: con VoiceFlow già in esecuzione, lancia di
      nuovo `VoiceFlow.App.exe` → non succede nulla (nessuna seconda
      finestra, nessun errore) e resta una sola icona nella tray.

## G. Latenza — release-to-text per modello

Ripeti 2-3 volte per modello, in Blocco note, e scrivi il tempo osservato
dal rilascio dell'hotkey alla comparsa del testo. Nessun numero precompilato:
misuralo tu con un cronometro o a occhio.

- base: misurato ___ s
- small: misurato ___ s
- medium: misurato ___ s

## H. Antivirus

`SendInput` (l'API usata per digitare il testo) è talvolta segnalata dagli
antivirus perché la stessa API è usata anche da malware. Annota qui se il
tuo antivirus ha segnalato o bloccato `VoiceFlow.App.exe`, e quale prodotto:

Antivirus in uso: ___  Ha segnalato l'exe? [ ] sì [ ] no — dettagli: ______

## I. Uscita

- [ ] Clic destro sull'icona in tray → **Quit** → l'icona sparisce
      immediatamente e il processo `VoiceFlow.App.exe` non compare più in
      Gestione attività

## J. Limiti noti (dichiarati, non da verificare)

- Due opzioni fisse per il push-to-talk, **Ctrl** (default) e
  **Ctrl+Alt+Space**, nessun rebind libero in v1 (come sul Mac con fn/🌐 e
  Control+Option+Space).
- Con Ctrl come push-to-talk, Ctrl+clic del mouse avvia comunque una
  dettatura (nessun tasto la annulla in quel caso) e ogni scorciatoia
  Ctrl+… fa comunque sentire il suono di inizio dettatura, anche se annulla
  senza trascrivere — vedi sezione C.
- Finestre elevate (amministratore) non ricevono testo: limite della
  piattaforma (UIPI), non dell'app — vedi sezione F.
- Eseguibile non firmato: SmartScreen interviene a ogni nuova build.
- Il timeout di 60 s sulla trascrizione (15 s sul Mac; alzato dopo la misura
  di 21 s per caricamento + 1 s di silenzio su un i7-3770 senza AVX2) cambia solo ciò che l'interfaccia
  mostra: la trascrizione in corso continua in background e il suo
  risultato tardivo viene scartato.

## K. Disinstallazione

- [ ] Con VoiceFlow in esecuzione, lancia `.\windows\scripts\uninstall.ps1`,
      conferma con `s` → il processo `VoiceFlow.App` sparisce da Gestione
      attività
- [ ] Dopo lo script, la voce di avvio automatico non c'è più: controlla in
      **Gestione attività → App di avvio** (Task Manager → Startup apps),
      oppure da PowerShell:
      `Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"`
      non deve più elencare `VoiceFlow`
- [ ] `%APPDATA%\VoiceFlow` e `%LOCALAPPDATA%\VoiceFlow` non esistono più
- [ ] Rilancia `.\windows\scripts\uninstall.ps1` una seconda volta → per
      ogni passo riporta che non c'è niente da fare (nessun processo,
      nessuna voce di registro, nessuna cartella)
- [ ] Ripeti con `-KeepModels` (dopo aver rifatto un download di prova) →
      `%LOCALAPPDATA%\VoiceFlow\models\` resta al suo posto, il resto viene
      comunque rimosso
- [ ] Chiudi la finestra di PowerShell e cancella a mano la cartella
      indicata dallo script (`windows\publish\`, o l'intero clone se
      lanciato con `run.ps1`) → la disinstallazione è completa
- [ ] Rilanciando `run.ps1` dopo la cancellazione, l'app si comporta come al
      primo avvio: compare di nuovo la finestra "Model not downloaded"

## Esito

Data: ________  Compilato da: ________  Versione Windows (`winver`): ________

Tutto verde? [ ] sì [ ] no — cosa non va: ______________________
