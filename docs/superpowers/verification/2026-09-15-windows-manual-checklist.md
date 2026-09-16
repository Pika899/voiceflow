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
- [ ] Tenendo premuto l'hotkey (Ctrl+Alt+Space) la prima volta, o aprendo le
      Impostazioni, compare la finestra **"Model not downloaded"** con il
      testo: *"The speech model hasn't been downloaded yet, or failed a
      corruption check. Click OK to download it now, or Cancel."*
- [ ] Clic su **OK** → appare la finestra di download con etichetta
      "Downloading <model> model..." e una barra di avanzamento che si
      muove
- [ ] A download completo la finestra si chiude da sola e l'icona in tray
      torna grigia (a riposo) — significa che il motore Whisper si è
      caricato

## C. Dettatura end-to-end

- [ ] Blocco note: clic in un documento, tieni premuto **Ctrl+Alt+Space**,
      parla, rilascia → il testo compare al cursore
- [ ] Rilasciando **solo** il tasto Space (tenendo Ctrl e Alt premuti) la
      dettatura termina comunque — il rilascio è rilevato appena una
      qualsiasi delle tre componenti della combinazione si solleva, non
      serve rilasciarle tutte insieme
- [ ] Due dettature di seguito (rilascia, ri-premi, parla) → tra le due c'è
      uno spazio, non "siamo!Adesso"
- [ ] Dettando subito dopo una virgola, o dopo "l'", non compare uno spazio
      in più
- [ ] VS Code (app Electron): il testo compare, **una sola volta**
- [ ] Un campo di testo in un browser (es. la barra degli indirizzi o un
      form): il testo compare, **una sola volta**
- [ ] L'icona nella tray passa: grigia (a riposo) → rossa (in ascolto) →
      blu (in trascrizione) → grigia

## D. Impostazioni

- [ ] Clic sull'icona in tray → si apre la finestra Impostazioni con
      Model, Language, Play sounds, Launch at login
- [ ] Toggle "Play sounds" ON → si sentono due suoni di sistema (uno a
      inizio, uno a fine dettatura); OFF → nessun suono
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

- [ ] **Conflitto hotkey**: registra Ctrl+Alt+Space in un altro programma
      (es. AutoHotkey, o le scorciatoie di un'altra app) → rilancia
      VoiceFlow → compare la finestra **"Hotkey already in use"** con il
      testo: *"Ctrl+Alt+Space is already registered by another application.
      Quit that app or free the shortcut there, then relaunch VoiceFlow."*
      Libera la combinazione dopo il test.
- [ ] **Modello corrotto**: tronca il file del modello (PowerShell:
      `[System.IO.File]::WriteAllBytes("$env:LOCALAPPDATA\VoiceFlow\models\ggml-base.bin", (Get-Content "$env:LOCALAPPDATA\VoiceFlow\models\ggml-base.bin" -Encoding Byte -TotalCount 1000))`
      o semplicemente cancella il file) → rilancia → compare **"Model not
      downloaded"** (checksum non corrisponde più, stesso testo del primo
      avvio) → Download → il modello si ri-scarica e l'engine si ricarica
- [ ] **Finestra elevata**: apri Gestione attività **come amministratore**
      (tasto destro → "Esegui come amministratore"), prova a dettare in un
      suo campo di testo (se presente) → nessun testo compare, nessun
      errore visibile — limite noto (UIPI), non un bug

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

- Hotkey fissa **Ctrl+Alt+Space**, nessuna alternativa e nessun rebind
  libero in v1 (Windows non ha un tasto equivalente a fn/🌐 del Mac).
- Finestre elevate (amministratore) non ricevono testo: limite della
  piattaforma (UIPI), non dell'app — vedi sezione F.
- Eseguibile non firmato: SmartScreen interviene a ogni nuova build.
- Il timeout di 15 s sulla trascrizione cambia solo ciò che l'interfaccia
  mostra: la trascrizione in corso continua in background e il suo
  risultato tardivo viene scartato.
- "Cancel" sul download del modello chiude la finestra ma il download
  continua in background fino alla fine.

## Esito

Data: ________  Compilato da: ________  Versione Windows (`winver`): ________

Tutto verde? [ ] sì [ ] no — cosa non va: ______________________
