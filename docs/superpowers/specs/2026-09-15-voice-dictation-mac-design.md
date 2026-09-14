# Design: VoiceFlow — dettatura vocale locale per Mac (v1)

## Contesto e obiettivo

Clone locale/offline di Wispr Flow: trascrive la voce dell'utente e digita
il testo in qualsiasi applicazione macOS attiva. La differenza rispetto a
Wispr Flow (che è cloud-based, con privacy garantita da certificazioni
SOC 2/ISO/HIPAA) è strutturale: **l'audio non lascia mai il Mac**. Dopo il
download iniziale del modello Whisper, l'app funziona con la rete
completamente disattivata.

Uso previsto: personale in prima battuta, con possibilità di diventare un
prodotto vendibile in futuro (fuori scope per v1).

## Scope v1

Incluso:
- App menu bar macOS nativa, sempre in background.
- Dettatura push-to-talk con hotkey globale configurabile.
- Trascrizione locale via whisper.cpp, italiano + inglese.
- Iniezione del testo trascritto nel cursore attivo, in qualsiasi app.
- Gestione permessi (microfono, accessibilità) e download del modello al
  primo avvio.
- Impostazioni minime: modello, lingua, hotkey, avvio al login.

Esplicitamente escluso da v1 (vedi `CLAUDE.md` per l'elenco completo):
riscrittura/cleanup del testo via LLM, dizionario personale, snippet,
supporto multilingue ampio, porting cross-platform, distribuzione pubblica,
billing.

## Architettura

**Tipo di processo**: menu bar app, `LSUIElement = true` nell'`Info.plist`
(nessuna finestra principale, nessuna icona nel Dock). Un `NSStatusItem`
nella menu bar mostra lo stato (inattivo / in ascolto / in trascrizione /
errore) e apre un piccolo popover per le impostazioni.

**Componenti**:

1. **HotkeyManager** — registra la combinazione globale (default
   `Control+Option+Space`) via Carbon Hot Key API (o un wrapper Swift come
   `HotKey`), espone eventi "press" / "release".
2. **AudioCapture** — usa `AVAudioEngine` per catturare il microfono dal
   press al release dell'hotkey, accumula i sample in un buffer in memoria
   (nessun file temporaneo su disco, per ridurre la superficie di dati
   residui). Converte al sample rate richiesto da whisper.cpp (16kHz mono).
3. **TranscriptionEngine** — wrapper Swift attorno a whisper.cpp (linkato
   come libreria C/C++, non come processo esterno). Carica il modello
   `ggml` selezionato una volta all'avvio dell'app e lo tiene in memoria;
   esegue l'inferenza su un thread in background per non bloccare la UI.
4. **TextInjector** — inserisce il testo trascritto nel punto del cursore
   dell'app attiva. Due strategie possibili, da validare nello spike:
   - `AXUIElement` (Accessibility API) per scrivere direttamente nel campo
     di testo focalizzato, quando l'app lo supporta bene;
   - fallback con eventi tastiera sintetici (`CGEvent` keyboard events) per
     app che non espongono bene l'albero di accessibilità.
   Richiede il permesso "Accessibilità" in Impostazioni di Sistema →
   Privacy e Sicurezza.
5. **ModelManager** — al primo avvio verifica se il modello scelto esiste
   in `~/Library/Application Support/VoiceFlow/models/`; se manca, lo
   scarica (con progress bar visibile, mai in silenzio) da una fonte
   ufficiale dei pesi ggml di Whisper. Da quel momento in poi, zero
   richieste di rete per dettare.
6. **SettingsStore** — preferenze utente (modello, lingua, hotkey, avvio al
   login), persistite localmente (`UserDefaults` è sufficiente per v1, non
   serve un database).

**Flusso dati** (happy path):

```
hotkey press
  → AudioCapture inizia a registrare
hotkey release
  → AudioCapture chiude il buffer, lo passa a TranscriptionEngine
  → TranscriptionEngine esegue whisper.cpp su thread background
  → risultato testo
  → TextInjector scrive il testo nel cursore attivo
  → NSStatusItem torna a stato "inattivo"
```

Nessuno di questi passaggi tocca la rete.

## Gestione errori

- **Permesso microfono negato**: alert che spiega perché serve e link
  diretto alle Impostazioni di Sistema; l'hotkey resta registrato ma
  mostra lo stato "permesso mancante" invece di tentare la cattura.
- **Permesso Accessibilità negato**: stesso pattern — l'app deve
  rilevarlo proattivamente (non aspettare un fallimento silenzioso
  dell'iniezione testo) e guidare l'utente.
- **Modello mancante o corrotto**: al mancato match di un checksum atteso,
  l'app propone il ri-download invece di crashare o produrre trascrizioni
  vuote/garbage.
- **Timeout o fallimento della trascrizione**: se whisper.cpp non
  restituisce risultato entro una soglia ragionevole, lo stato nella menu
  bar mostra un errore visibile (non un buco silenzioso — l'utente non
  deve chiedersi se ha parlato al vento).
- **Hotkey già in uso da un'altra app**: rilevarlo alla registrazione e
  offrire di sceglierne un'altra nelle impostazioni, invece di fallire
  silenziosamente in background.

## Rischio tecnico principale

Latenza di whisper.cpp **su CPU**, specialmente su hardware non Apple
Silicon o datato, con il modello `small` (più accurato ma più lento del
`base`). Se la latenza percepita è troppo alta per un uso fluido
push-to-talk, l'alternativa è default su `base` con `small` opzionale per
chi ha CPU più potenti.

**Primo milestone di implementazione**: uno spike minimo, isolato da tutto
il resto — hotkey → cattura audio → whisper.cpp → stampa il testo in
console (nessuna UI, nessuna iniezione testo) — per misurare la latenza
reale prima di costruire il resto dell'app attorno a un'assunzione non
verificata.

## Piano di verifica (manuale, v1)

- Dettare in TextEdit e in almeno un'altra app (es. Note, un editor di
  codice) e confermare che il testo appare correttamente nel punto giusto.
- Disattivare completamente il Wi-Fi dopo il primo download del modello e
  confermare che la dettatura continua a funzionare identica.
- Controllare in Activity Monitor l'uso di memoria e CPU sia a riposo
  (app in background, nessuna dettatura in corso) sia durante una
  trascrizione, per verificare che restino contenuti su hardware non
  potente.
- Revocare il permesso Accessibilità dalle Impostazioni di Sistema e
  confermare che l'app lo rileva e guida l'utente, invece di fallire in
  silenzio.
- Cancellare la cartella dei modelli e confermare che al riavvio l'app
  propone il download invece di crashare.

## Fuori scope — roadmap v2 (solo per contesto, non implementare ora)

Riscrittura/cleanup del testo via un piccolo LLM locale (es. Qwen/Llama
quantizzato 1-3B) per rimuovere filler, correggere autocorrezioni a metà
frase e formattare come fa Wispr Flow; dizionario personale; snippet;
stile per-app; supporto multilingue ampio; eventuale porting
cross-platform (progetto separato, stack da ridiscutere); firma e
notarizzazione per distribuzione pubblica; billing/licensing se diventa
un prodotto vendibile.
