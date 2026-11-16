# USBtoC64 Config Tool

Strumento Windows per preparare i profili gamepad dell'adattatore USBtoC64.
Progetto e motivazioni: [DESIGN.md](DESIGN.md).

**Stato: fase 1** — classificazione automatica dei controlli, proposta della mappatura,
editor delle azioni, controlli di validità, anteprima dal vivo, import dei vecchi `.h`/`.json`,
export del JSON `save_jm`, **scrittura sull'adattatore con verifica** (fase 2).

Due programmi:

- **`USBtoC64Config.exe`** — interfaccia grafica.
- **`usbtoc64.exe`** — stessa logica da riga di comando.

## Interfaccia grafica

Stile Windows 11 (tema chiaro/scuro come quello di Windows), menu laterale:

- **Gamepad**: il pad viene analizzato in automatico; tabella dei controlli (byte, bit, azione) con la riga
  del tasto premuto evidenziata; a destra cosa riceve il **C64/Amiga su porta DB9** e i controlli del profilo;
  in basso le viste *Anteprima / Regole per il firmware / JSON*, **Leggi dall'adattatore** e
  **Scrivi sull'adattatore e verifica**. Importa/Esporta profili `.h`/`.json`.
- **Firmware**: versione sull'adattatore e versione inclusa, aggiornamento (cancella tutte le impostazioni).
- **Terminale**: menu di servizio del firmware (`service`, `info`, `dumppad`…) e comandi JSON, con comandi
  rapidi, cronologia, conferma per i comandi che cambiano lo stato, riconnessione automatica dopo un riavvio.
- **Registro**: tutto lo scambio con l'adattatore della sessione, salvabile su file.
- **Mouse, LED, Impostazioni**: in arrivo (per ora dalle pagine del configuratore web).

Il riquadro in basso a sinistra mostra porta, firmware, modalità (C64 o Amiga, come rilevata) e profilo salvato.
Durante scrittura, lettura e aggiornamento un pannello mostra lo scambio seriale in corso.

## Scaricare l'exe

L'exe viene compilato da GitHub Actions (workflow *USBtoC64 Config Tool*):
aprire la run più recente → sezione **Artifacts** → `usbtoc64-config-win-x64`
(zip con i due exe). Sono file singoli, non serve installare .NET. Al primo avvio Windows SmartScreen
può mostrare un avviso (exe non firmato): *Ulteriori informazioni → Esegui comunque*.

## Riga di comando

```
usbtoc64 devices
usbtoc64 analyze 0
usbtoc64 analyze 0 --set btn3=Fire2 --set btn4=None -o profilo.json
usbtoc64 monitor 0
usbtoc64 monitor 0 --profile profilo.json
usbtoc64 check JoystickMapping.h
usbtoc64 convert JoystickMapping.h -o profilo.json
usbtoc64 descriptor descrittore.txt
usbtoc64 ports
usbtoc64 ping
usbtoc64 write profilo.json
usbtoc64 read
```

- `analyze` legge il descrittore HID del pad, classifica in automatico direzioni (hat, D-pad,
  assi X/Y) e pulsanti, propone le azioni e mostra le regole che finirebbero nel firmware.
- `monitor` mostra dal vivo cosa riceve il C64/Amiga su porta DB9 (direzioni, fire, autofire), usando una replica
  del motore del firmware.
- `check` / `convert` leggono i `JoystickMapping.h` dei configuratori web (anche il vecchio
  `config.html`) e segnalano i problemi noti.
- `write` / `read` scrivono (con verifica) e leggono il profilo sull'adattatore.

Pad XInput (Xbox 360 cablato): il tool usa il layout nativo che riceve l'ESP32, non il report
HID mostrato da Windows. Gli stick XInput non sono utilizzabili con il firmware attuale.

## Sviluppo

```
dotnet test tests/UsbToC64.Core.Tests
dotnet run --project src/UsbToC64.Cli -- check tests/UsbToC64.Core.Tests/Fixtures/JoystickMapping_25.h
```

| Progetto | Contenuto |
|---|---|
| `src/UsbToC64.Core` | modello firmware, parser descrittore HID, classificazione, proposta, validazione, emulatore, formati |
| `src/UsbToC64.Input` | lettura pad: HidSharp (HID) e XInput |
| `src/UsbToC64.Device` | porta seriale dell'adattatore (ricerca per VID Espressif, apertura) |
| `src/UsbToC64.Cli` | comandi da terminale (`usbtoc64.exe`) |
| `src/UsbToC64.App` | interfaccia grafica WPF + WPF-UI (`USBtoC64Config.exe`) |
| `tests/UsbToC64.Core.Tests` | test xUnit, con profili reali in `Fixtures/` |
