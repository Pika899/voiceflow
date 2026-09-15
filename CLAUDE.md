# CLAUDE.md

Questo file guida Claude Code (claude.ai/code) su questo progetto.

## Cos'è questo progetto

**VoiceFlow** (nome di lavoro, rinominabile) — un'app menu bar nativa per macOS
che fa dettatura vocale **interamente locale**: trascrive con Whisper (via
whisper.cpp) e digita il testo in qualsiasi applicazione attiva, senza inviare
l'audio a nessun server.

È un clone locale/offline di Wispr Flow. La differenza che conta è la
privacy: dopo il download iniziale del modello, **l'app non richiede alcuna
connessione di rete per funzionare** e non invia mai l'audio o il testo
dettato a terzi. Nessun dato viene usato per addestrare modelli di nessuna
azienda.

**Stato: lo stack è deciso, il codice non è ancora stato creato.** Finché non
c'è lo scaffold Xcode, non esistono target, file `.swift` o pacchetti: non
cercarli e non dedurne l'esistenza da questo file. Le regole qui sotto
valgono dal primo file scritto in poi.

Lo spec tecnico completo è in
`docs/superpowers/specs/2026-09-15-voice-dictation-mac-design.md` — leggilo
prima di scrivere codice.

## Scope

**v1 (fase attuale, solo Mac):**
- App menu bar (`LSUIElement`, nessuna icona nel Dock), Swift/SwiftUI + AppKit.
- Hotkey globale push-to-talk → cattura audio (`AVAudioEngine`) → trascrizione
  locale via whisper.cpp (modello `ggml`, multilingue, italiano + inglese) →
  iniezione del testo nell'app attiva via Accessibility API / `CGEvent`.
- Solo trascrizione pulita (Whisper + punteggiatura automatica). **Niente**
  riscrittura/cleanup del testo via LLM in v1 — troppo pesante per CPU non
  potenti, è la feature di punta di v2.
- Modello Whisper scaricato al primo avvio in
  `~/Library/Application Support/VoiceFlow/models/`, mai incluso nel binario.

**Esplicitamente fuori scope v1** (non implementare finché non viene chiesto
esplicitamente): riscrittura LLM del testo, dizionario personale, snippet,
stile per-app, sync multi-device, supporto multilingue ampio (100+ lingue),
porting Windows/Linux, firma/notarizzazione per distribuzione pubblica,
billing/licensing, **UI per cambiare l'hotkey** (v1 usa Control+Option+Space
fisso; il conflitto viene rilevato e segnalato, ma la scelta di una
combinazione diversa è rimandata a v2 — vedi ruling R25 nel piano).

**Nessun limite di utilizzo**: niente contatore di parole, quote settimanali
o tempo massimo di dettatura. Non c'è un server e non c'è billing, quindi
non c'è nulla da misurare — differenza voluta rispetto al piano gratuito di
Wispr Flow.

## Privacy — non negoziabile

- **Zero chiamate di rete nel flusso di dettatura**, a parte il download
  del modello al primo avvio (esplicito, con progress bar, mai silenzioso).
- **Nessuna telemetria, nessun analytics, nessun crash reporter di terzi**
  che invii dati fuori dal Mac. Se serve logging, resta locale su disco.
- Verifica manuale da fare prima di ogni consegna: disattivare il Wi-Fi e
  confermare che la dettatura continua a funzionare normalmente.
- Se in futuro (v2+) si aggiunge qualsiasi funzione che tocca la rete
  (update checker, licensing online, ecc.), va dichiarata esplicitamente
  all'utente e mai attivata di default in silenzio.

## Comandi

**Questa macchina ha solo gli Xcode Command Line Tools, non Xcode.app.**
Quindi niente `.xcodeproj` e niente `xcodebuild`: tutto passa da Swift
Package Manager. L'app viene assemblata in un vero `.app` da uno script.

```bash
swift build                      # build di tutto
./scripts/test.sh                # esegue la suite di test (usare questo, non `swift test`)
./scripts/test.sh --filter SettingsStoreTests   # un singolo gruppo di test
./scripts/build-whisper.sh       # vendorizza e compila whisper.cpp (una tantum)
./scripts/package-app.sh         # release + assembla dist/VoiceFlow.app
open dist/VoiceFlow.app          # lancia l'app impacchettata
swift run LatencySpike <modello> # spike di latenza, richiede un .bin ggml
```

**Test runner: Swift Testing** (`import Testing`, `@Test`, `#expect`), non
XCTest. XCTest è distribuito solo dentro Xcode.app e qui non esiste;
`Testing.framework` invece arriva con i Command Line Tools e funziona.

**Lanciare i test sempre con `./scripts/test.sh`, mai con `swift test`
diretto**: questo toolchain CLT tiene il plugin delle macro di Testing in
una sottocartella che il compilatore scansiona solo a volte, e `swift test`
liscio fallisce a intermittenza con "plugin for module 'TestingMacros' not
found". Il wrapper passa il percorso del plugin esplicitamente e rende ogni
esecuzione deterministica. Verificato: stesso comando liscio fallito e poi
passato di seguito; con il wrapper passa sempre.

Vale la divisione decisa all'inizio: test automatici per la logica isolabile
(settings, checksum, resampling, post-processing del testo), verifica
manuale per ciò che tocca sistema e hardware (hotkey globale, microfono,
permessi, iniezione testo).

**Non testare l'app con `swift run VoiceFlowApp`**: `LSUIElement` e
`NSMicrophoneUsageDescription` stanno nell'`Info.plist`, che ha effetto solo
dentro il bundle `.app`. Senza, il processo crasha appena chiede il
microfono.

## Convenzioni

- **Lingua**: conversazione e documentazione in italiano; codice, commenti,
  nomi di variabili e messaggi di commit in inglese.
- **Percorso con spazi**: la cartella si chiama `VOICE APP` dentro `AGENCY `
  (con spazio finale). Va sempre tra virgolette nei comandi shell.
- **Niente dati inventati**: nessuna metrica di accuratezza, benchmark di
  latenza o percentuale di risparmio tempo inventata, nemmeno come
  placeholder. Se serve un numero non ancora misurato, va dichiarato come
  stima o lasciato esplicitamente da misurare.
- **Fedeltà al brief**: non aggiungere feature, schermate o impostazioni
  non richieste — vedi "Esplicitamente fuori scope v1" sopra.
- **Accessibilità come correttezza**: contrasto sufficiente e focus
  visibile nella UI SwiftUI, anche per un'app menu bar minimale.

## Rischio tecnico da validare presto

Latenza di whisper.cpp su CPU non Apple Silicon / hardware datato con il
modello `small`. Il primo milestone di implementazione è uno spike minimo
(hotkey → cattura audio → whisper.cpp → stampa testo in console) per
misurare questa latenza prima di costruire il resto dell'app attorno.

## Contesto

Questa cartella è sorella di `../DEV APP` nel contenitore `AGENCY `, ma non
condivide nulla con quel progetto (Next.js/Supabase): sono due prodotti
diversi. Non copiare le convenzioni tecniche di `DEV APP` qui — build step,
Tailwind, Supabase non c'entrano con questo progetto Swift nativo.

## Da compilare quando il codice esiste

- **Struttura del progetto Xcode** — target, gruppi di file, dipendenze
  (whisper.cpp, eventuale wrapper hotkey).
- **Come si testa manualmente** — checklist di verifica end-to-end.
- **Distribuzione** — se e quando si arriva a firma/notarizzazione per
  condividere l'app fuori dal proprio Mac.
