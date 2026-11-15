# USBtoC64 Config Tool

Strumento Windows per preparare i profili gamepad dell'adattatore USBtoC64.
Progetto e motivazioni: [DESIGN.md](DESIGN.md).

**Stato: fase 1** — classificazione automatica dei controlli, proposta della mappatura,
editor delle azioni, controlli di validità, anteprima dal vivo, import dei vecchi `.h`/`.json`,
export del JSON `save_jm`. Nessuna scrittura sull'ESP32 (arriva con la fase 2).

Due programmi:

- **`USBtoC64Config.exe`** — interfaccia grafica.
- **`usbtoc64.exe`** — stessa logica da riga di comando.

## Interfaccia grafica

1. Collega il pad al PC e avvia `USBtoC64Config.exe`: il pad viene scelto e analizzato in automatico.
2. La tabella elenca i controlli trovati (pulsanti, D-pad/hat, assi) con byte e bit che userà l'adattatore.
   Premendo un tasto la riga si evidenzia in verde.
3. Nella colonna **Azione sul C64** si sceglie cosa fa ogni controllo. **Proposta automatica** ripristina i valori iniziali.
4. A destra si vede dal vivo cosa riceverebbe il C64 (direzioni, F1–F3, autofire).
5. In basso: **Avvisi** (problemi del profilo), **Regole per il firmware**, **JSON save_jm**.
6. **Salva JSON…** / **Copia JSON** esportano il profilo. **Importa profilo…** apre un `JoystickMapping.h`
   o un `.json` e lo applica ai controlli del pad collegato (senza pad: sola lettura con i controlli).

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
```

- `analyze` legge il descrittore HID del pad, classifica in automatico direzioni (hat, D-pad,
  assi X/Y) e pulsanti, propone le azioni e mostra le regole che finirebbero nel firmware.
- `monitor` mostra dal vivo cosa vedrebbe il C64 (direzioni, fire, autofire), usando una replica
  del motore del firmware.
- `check` / `convert` leggono i `JoystickMapping.h` dei configuratori web (anche il vecchio
  `config.html`) e segnalano i problemi noti.
- `-o` produce la riga JSON `save_jm`: si può inviare all'adattatore con un terminale seriale
  (115200, fine riga LF) finché la fase 2 non è pronta.

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
| `src/UsbToC64.Cli` | comandi da terminale (`usbtoc64.exe`) |
| `src/UsbToC64.Gui` | interfaccia grafica WinForms (`USBtoC64Config.exe`) |
| `tests/UsbToC64.Core.Tests` | test xUnit, con profili reali in `Fixtures/` |
