# VoiceFlow v1 — checklist di verifica manuale

Questa è la parte del lavoro che solo una persona può fare: parlare nel
microfono, concedere permessi, guardare la barra dei menu. Nessun passo
qui sotto è stato eseguito da un agente, e nessun numero è stato inventato:
le caselle sono vuote finché non le compili tu con ciò che osservi.

Regola: **scrivi solo valori misurati**. Se un passo non lo fai, lascialo
vuoto — una casella vuota è un'informazione, un numero plausibile è una bugia.

## Prima di cominciare

```bash
cd "/Users/andreariganello/Desktop/AGENCY /VOICE APP"
./scripts/package-app.sh          # produce dist/VoiceFlow.app
open dist/VoiceFlow.app
```

Al primo avvio macOS chiede il permesso **Microfono**; l'app chiede subito
anche **Accessibilità** (serve per scrivere nelle altre app). Concedili
entrambi da Impostazioni di Sistema → Privacy e Sicurezza. Se l'app non
compare nella lista Accessibilità, trascina `dist/VoiceFlow.app` dentro la
lista.

Il modello `base` è già scaricato e verificato in
`~/Library/Application Support/VoiceFlow/models/ggml-base.bin`.

## A. Latenza (Task 5 del piano — la misura che decide il modello di default)

Lo spike stampa i tempi reali in console. Ripeti 2-3 volte per modello.

```bash
swift run LatencySpike /tmp/ggml-base.bin     # tieni premuto Ctrl+Opt+Spazio, parla 5-10 s, rilascia
swift run LatencySpike /tmp/ggml-small.bin
```

| Modello | Lingua | Durata audio (s) | "Total latency" stampata (s) | Testo corretto? |
|---|---|---|---|---|
| base  | it |  |  |  |
| base  | en |  |  |  |
| small | it |  |  |  |
| small | en |  |  |  |

Decisione: il default resta `base` (fallback previsto dallo spec) a meno che
`small` risulti abbastanza veloce da usare in push-to-talk. Se vuoi
cambiare il default: `Sources/VoiceFlowCore/SettingsStore.swift`, `?? .base`.

## B. Dettatura end-to-end (Task 10 / Task 13)

- [ ] TextEdit: clic in un documento, tieni premuto Ctrl+Opt+Spazio, parla, rilascia → il testo compare al cursore
- [ ] L'icona nella barra dei menu passa: microfono → microfono pieno (ascolto) → onda (trascrizione) → microfono
- [ ] Una seconda app (Note, o un editor): stesso risultato
- [ ] Nessuna icona nel Dock (`LSUIElement`)
- [ ] **Osservazione R19 (parcheggiata)**: in ogni app provata il testo è comparso *tutto*? Segna qui le app in cui mancano caratteri o non compare nulla senza errore: ______________________

## C. Offline (il claim centrale del prodotto)

- [ ] Disattiva completamente il Wi-Fi → la dettatura funziona identica
- [ ] Riattiva il Wi-Fi

## D. Risorse (Task 13)

Da Monitoraggio Attività, processo `VoiceFlow`:

| Stato | Memoria (MB) | CPU (%) |
|---|---|---|
| A riposo (nessuna dettatura) |  |  |
| Durante una trascrizione |  |  |

Per riferimento, valori osservati dagli agenti solo all'avvio (modello base
caricato, nessuna dettatura): 243 MB e 519 MB in due avvii diversi. Non sono
una garanzia — misura i tuoi.

## E. Impostazioni (Task 11)

- [ ] Clic sull'icona → si apre il popover con Modello, Lingua, Avvio al login
- [ ] Cambia modello a `small`, esci e rilancia → la scelta è rimasta
- [ ] Cambia lingua a English → una dettatura in inglese viene trascritta in inglese
- [ ] Avvio al login ON → compare in Impostazioni di Sistema → Generali → Elementi login. Se invece compare una scritta rossa sotto il toggle, è il limite atteso di un'app firmata ad-hoc fuori da /Applications: annotalo qui: ______________________

## F. Gestione errori (Task 12 — i cinque casi dello spec)

- [ ] Revoca il permesso Microfono → premi l'hotkey → alert con bottone "Open System Settings" funzionante
- [ ] Revoca il permesso Accessibilità → rilancia l'app → l'alert compare **all'avvio**, non dopo un fallimento
- [ ] Sposta via il modello (`mv ~/Library/Application\ Support/VoiceFlow/models/ggml-base.bin /tmp/`) → rilancia → alert "Model not downloaded" → Download → barra di avanzamento che si muove → completa → l'app torna a riposo. (Poi puoi cancellare `/tmp/ggml-base.bin` o rimetterlo.)
- [ ] Corrompi il modello (`truncate -s 1000 ~/Library/Application\ Support/VoiceFlow/models/ggml-base.bin`) → rilancia → stesso flusso di ri-download (checksum non corrisponde)
- [ ] Occupa Ctrl+Opt+Spazio con un'altra app → rilancia VoiceFlow → alert "Hotkey already in use" con il consiglio di liberare la combinazione

## G. Limiti noti di v1 (dichiarati, non da verificare)

- L'hotkey non è modificabile dall'interfaccia (ruling R25; v2).
- Il timeout di 15 s sulla trascrizione cambia ciò che l'interfaccia mostra, non interrompe whisper.cpp che continua in background.
- "Cancel" sul download del modello nasconde la barra ma il download continua in background fino alla fine.
- Firma ad-hoc: l'app gira solo su questo Mac; niente notarizzazione (fuori scope v1).

## Esito

Data: ________  Compilato da: ________

Tutto verde? [ ] sì [ ] no — cosa non va: ______________________
