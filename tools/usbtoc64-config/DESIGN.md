# USBtoC64 Config Tool (Windows) — Fattibilità e design

> Stato: **bozza di progetto**. Nessun codice ancora scritto.
> Obiettivo: un programma Windows (GUI + CLI) che prepara la mappatura di un
> gamepad in modo più automatico del configuratore web, permette di
> personalizzare le azioni, la salva sull'adattatore e verifica il salvataggio.

---

## 1. Contesto e problemi osservati

Il configuratore web attuale (`configurator/configurator.html`) funziona da
**ponte**: il pad è collegato al PC (WebHID), l'ESP32 è collegato via USB-C
come porta seriale (standalone, dev mode OFF). Il programma manterrà lo stesso
schema:

```
Pad ──USB──> PC (tool legge il pad) ──USB seriale CDC──> ESP32 (riceve JSON, salva in NVS)
```

Problemi riscontrati analizzando profili reali e il codice:

| # | Problema | Dove |
|---|---|---|
| P1 | "CONFIGURATION SAVED" appare sempre: la risposta `{"status":"ok"}` viene letta ma mai controllata | `configurator.html` → `sendSerialData()` |
| P2 | `navigator.serial.getPorts()[0]` sceglie in silenzio la prima porta autorizzata, anche se non è l'ESP32 | `sendSerialData()` |
| P3 | Il firmware non risponde nulla se il JSON non è valido → impossibile distinguere errore da porta sbagliata | `USBtoC64_Adv.ino`, rami `save` / `save_jm` |
| P4 | SAVE genera pulsanti con `op 0` (`==`), l'export `.h` con `JM_BITANY`: due pulsanti sullo stesso byte premuti insieme non scattano | `buildPayload()` vs `generateJoystickMappingHeader()` |
| P5 | Con "Force Axis" il D-pad viene registrato come asse (`[IDX:n]` → valore 255) | file 24 / 25 |
| P6 | Stesso bit catturato per due pulsanti (B e X entrambi `10:4`) senza avviso | file 24 |
| P7 | L'export `.h` sceglie `JM_DPAD_INDEX` dalla prima regola di direzione, anche se è un pulsante → tutti i pulsanti su quel byte vengono ignorati | file 24 |
| P8 | **Pad XInput (X360 & co.)**: Windows/WebHID vedono un report HID *sintetico*, l'ESP32 riceve il report XInput *nativo*: gli indici dei byte non coincidono | vedi §4 |
| P9 | Profili esportati vuoti (`WEB_META` vuoto, VID/PID 0) senza avviso | file 1_1, 2_1 |

---

## 2. Il contratto con il firmware (verificato sul codice)

Il tool deve produrre esattamente ciò che il firmware si aspetta. Questa
sezione è la specifica di riferimento; va aggiornata a ogni cambio firmware.

### 2.1 Canale seriale

- Solo in **standalone** (`!C64_Amiga_Connected`) e **dev mode OFF**: comandi su USB-C (`Serial`, CDC, 115200).
- Una riga che inizia con `{` va al motore JSON; qualsiasi altra riga va al service menu testuale (`info`, `dumppad`, …). JSON e console condividono la porta.
- Lettura con `readStringUntil('\n')` (timeout di default 1000 ms): il JSON deve essere **una sola riga terminata da `\n`**.
- Al boot c'è `delay(2000)` prima che la porta sia pronta.
- VID USB dell'ESP32-S3 (CDC nativo): `0x303A` (Espressif) → serve per trovare la porta automaticamente.

### 2.2 Comando `save_jm` (TIER 0)

```json
{"cmd":"save_jm","vid":1118,"pid":654,"dpad_idx":-1,"use_analog":0,
 "analog_x":[],"analog_y":[],
 "rules":[{"i":10,"v":1,"op":1,"f":8}]}
```

| Campo | Significato | Limiti firmware |
|---|---|---|
| `vid`, `pid` | Devono coincidere con il pad collegato all'adattatore, altrimenti il profilo non si attiva | uint16 |
| `dpad_idx` | Salvato nel blob ma **non usato** dal motore dinamico (solo informativo) | int8 |
| `use_analog`, `analog_x`, `analog_y` | Byte usati come assi | max **4** per asse |
| `rules[].i` | Indice del byte nel report grezzo | uint8 |
| `rules[].v` | Valore / maschera | uint8 |
| `rules[].op` | `0` EQ (`==`), `1` BITANY (`& v != 0`), `2` HAT (`(b & 0x0F) == v`) | |
| `rules[].f` | Funzione, vedi tabella sotto | 0–15 |
| numero regole | | max **32** |

Funzioni (`JM_Func`):

| f | Funzione | f | Funzione |
|---|---|---|---|
| 0 | UP | 8 | FIRE 1 |
| 1 | UP_RIGHT | 9 | FIRE 2 |
| 2 | RIGHT | 10 | FIRE 3 |
| 3 | RIGHT_DOWN | 11 | AUTOFIRE ON (latch) |
| 4 | DOWN | 12 | AUTOFIRE OFF (latch) |
| 5 | DOWN_LEFT | 13 | AUTOFIRE HOLD |
| 6 | LEFT | 14 | AUTO L/R ON (non gestito dal motore dinamico) |
| 7 | LEFT_UP | 15 | AUTO L/R OFF (non gestito dal motore dinamico) |

Risposta: `{"status":"ok"}\n`, poi `delay(200)` e **riavvio**. Nessuna risposta in caso di errore (P3).

Rischio dimensione: il documento JSON è `StaticJsonDocument<3072>`. Con 32 regole
la stima di occupazione (~2,8 KB) è vicina al limite → da verificare sul
dispositivo. Il tool, in via prudenziale, avvisa sopra ~28 regole.

### 2.3 Semantica del motore dinamico (`InputEngine.h`, ramo `use_html_configurator`)

È il motore che esegue i profili salvati con SAVE. **È diverso dall'export `.h`**:

- Tutte le regole sono valutate su tutti i byte; **nessun filtro per D-pad** (a differenza del `.h`, P7).
- Le direzioni diagonali impostano le due direzioni corrispondenti.
- Autofire: `ON`/`OFF` agiscono su un latch, `HOLD` vale solo finché è premuto; `auto = latch || hold`.
- Assi analogici (anche in modalità joystick): byte **unsigned** `< 64` → sinistra/su, `> 190` → destra/giù.
- Attivazione: al collegamento del pad, se `connected_vid/pid == blob.vid/pid` → "TIER 0 MATCH".

### 2.4 Altri comandi utili

| Comando | Uso nel tool |
|---|---|
| `{"cmd":"mem_status"}` | → `{"mouse":..,"pad":..,"led":..,"dev":..,"inv":..}` presenza profili |
| `{"cmd":"clear_mem","target":"pad"}` | cancella il profilo pad |
| `{"cmd":"get_info"}` | modalità C64/Amiga, dev mode |
| `dumppad` (testo) | unica rilettura del blob oggi; formato testuale con emoji, fragile da analizzare |

---

## 3. Report grezzo: Windows vs ESP32

Le regole indicizzano il **report USB grezzo** letto dall'ESP32 sull'endpoint
interrupt IN. Per i pad **HID standard** il report visto da Windows è lo stesso, con una
sola differenza:

- Windows (HidD / hidapi) restituisce **sempre** il report ID come primo byte (`0` se il dispositivo non usa report ID).
- L'ESP32 riceve il report ID **solo se il dispositivo lo usa**.
- Regola: se il report ID è `0` → togliere il primo byte; altrimenti lasciarlo (è anche ciò che fa il configuratore web, `handleInputReport`).

Dal **report descriptor** (via preparsed data / `HidP_*`) il tool ricava per ogni
controllo: usage, report ID, bit offset, dimensione, logical min/max. Quindi:

- **Pulsante** → byte = offset/8, valore = `1 << (offset % 8)`, op BITANY.
- **Hat switch** → supportato dal firmware solo se occupa il **nibble basso** di un byte (op HAT). Valori 0..7 = N, NE, E, SE, S, SW, W, NW; neutro 8 o 15. Altrimenti avviso.
- **Asse 8 bit unsigned** → usabile direttamente come asse analogico.
- **Asse 16 bit unsigned** → usare il byte alto (indice +1).
- **Asse signed** (logical min < 0) → **non compatibile** con le soglie 64/190 del firmware → avviso, non mappabile come asse.

---

## 3bis. Classificazione automatica dei controlli (principio della fase 1)

Nel configuratore web l'utente doveva capire da solo se leva e D-pad erano
"assi" o "pulsanti" (opzione "Force Axis"). Errori di questa scelta sono
all'origine di P5, P6 e P7.

Il tool **non chiede più questa scelta**: ogni pad HID dichiara nel descriptor
che cosa è ogni controllo (usage standard), e Windows lo espone. Il tool lo
legge e assegna le funzioni in automatico.

| Usage nel descriptor | Cos'è | Assegnazione automatica |
|---|---|---|
| Generic Desktop `X` / `Y` (0x30 / 0x31) | stick sinistro, o D-pad dei pad economici | assi analogici → direzioni (soglie firmware 64/190) |
| Generic Desktop `Rx` / `Ry` / `Z` / `Rz` (0x33 / 0x34 / 0x32 / 0x35) | secondo stick o grilletti | non assegnati di default (attivabili come assi) |
| Generic Desktop `Hat switch` (0x39) | D-pad a croce | regole HAT → 8 direzioni |
| Generic Desktop `D-pad Up/Down/Right/Left` (0x90–0x93) | D-pad come 4 tasti | regole BITANY → direzioni |
| Button page (0x09) `Button 1…N` | pulsanti | azioni FIRE / autofire con proposta predefinita |
| Pad XInput | layout fisso (§4) | direzioni, pulsanti e grilletti noti, nomi A/B/X/Y |

Casi particolari, gestiti senza domande all'utente:

- **D-pad dichiarato come asse X/Y** (pad economici, valori 0/127/255): resta un asse; le soglie 64/190 del firmware lo leggono correttamente come digitale.
- **Stick e D-pad insieme**: hat → direzioni, X/Y → assi; il firmware combina entrambi.
- **Asse 16 bit unsigned**: si usa il byte alto. **Asse signed**: avviso, non compatibile con il firmware attuale.

Cosa Windows **non** sa: quale pulsante deve fare FIRE 1. I pulsanti HID sono
solo numerati. Il tool propone una mappatura predefinita
(Button 1 → FIRE 1, Button 2 → UP, Button 3 → FIRE 2, …) e l'utente la
cambia nell'editor. Premere un tasto lo evidenzia nella lista: serve solo a
riconoscerlo fisicamente, non a classificarlo.

**Fallback a procedura guidata** ("premi il tasto", cattura del bit che cambia)
solo quando il descriptor non basta: usage vendor-defined, hat non allineato al
nibble basso, report non decodificabile.

---

## 4. Pad XInput (X360, compatibili)

Windows non espone il report nativo: crea un HID sintetico per DirectInput
(5 assi da 16 bit nei byte 0–9, pulsanti al byte 10, hat al byte 11). È quello
che compare nei profili 24/25. L'ESP32 invece riceve il report XInput nativo:

| Byte | Contenuto (X360 wired, 20 byte) |
|---|---|
| 0 | tipo messaggio `0x00` |
| 1 | lunghezza `0x14` |
| 2 | bit0 Up, bit1 Down, bit2 Left, bit3 Right, bit4 Start, bit5 Back, bit6 LS click, bit7 RS click |
| 3 | bit0 LB, bit1 RB, bit2 Guide, bit4 A, bit5 B, bit6 X, bit7 Y |
| 4 / 5 | LT / RT (0–255) |
| 6–7, 8–9, 10–11, 12–13 | LX, LY, RX, RY (int16 little endian, signed) |

Conseguenze:

- I profili X360 creati via WebHID **probabilmente non funzionano** sull'adattatore. Da verificare con `raw` in dev mode (il report deve iniziare con `00 14`).
- Riconoscimento nel tool: su Windows i dispositivi XInput hanno `IG_` nel device path HID.
- Per i pad XInput il tool **non sniffa i byte**: usa la tabella sopra. La pressione dei tasti (via XInput API) serve solo a dire quale tasto fisico si vuole configurare.
- D-pad e pulsanti: regole BITANY sui byte 2/3 → **supportati**.
- Grilletti: BITANY `0x80` su byte 4/5 = premuto oltre metà corsa → **supportati come pulsanti**.
- Stick: valori signed → **non supportati** con il firmware attuale (le soglie unsigned 64/190 non funzionano). Richiede un'estensione firmware (fuori da questa fase).
- Xbox One/Series (GIP): supportati dal firmware **1.2.2** (vedi sotto).

### 4.1 Firmware 1.2.2: pad Xbox

- **Interfaccia giusta**: prima il firmware rivendicava l'*ultima* interfaccia del dispositivo e leggeva il
  *primo* endpoint di input; sui pad Xbox (più interfacce, input sulla 0) non funzionava. Ora rivendica
  l'interfaccia che possiede l'endpoint (solo alternate setting 0). Pad HID a interfaccia singola: invariato.
- **Riconoscimento** dalla classe dell'interfaccia: 0xFF/0x5D/0x01 = Xbox 360, 0xFF/0x47/0xD0 = GIP.
- **Avvio GIP**: sull'endpoint OUT della stessa interfaccia, in catena (ogni pacchetto parte al completamento
  del precedente): power-on `05 20 seq 01 00`; Microsoft anche `05 20 seq 0F 06` e LED `0A 20 seq 03 00 01 14`;
  PDP LED + `06 20 seq 02 01 00` (stessi pacchetti del driver Linux xpad). Xbox 360: LED giocatore 1 `01 03 06`.
- **Filtro**: solo i report di input arrivano al motore delle regole (GIP: byte 0 = 0x20; 360: byte 0 = 0x00).
  Guide, keep-alive e messaggi di stato non possono più far scattare regole.
- Log: la riga `CONNECTED` riporta interfaccia, endpoint e protocollo; errori di claim/submit/init sono stampati.

Layout GIP (report che riceve l'ESP32): byte 4 Menu 0x04, View 0x08, A 0x10, B 0x20, X 0x40, Y 0x80;
byte 5 Up 0x01, Down 0x02, Left 0x04, Right 0x08, LB 0x10, RB 0x20, LS 0x40, RS 0x80; byte 6-7 / 8-9 LT / RT
(10 bit LE, "oltre metà" = bit 0x02 del byte alto); byte 10-17 stick int16 (non utilizzabili, signed).

Tool: Windows mostra anche i pad GIP come XInput; il tipo (360 o GIP) si ricava dal driver associato al
dispositivo USB nel registro (`xboxgip` / `xusb2x`), poi dalla lista VID/PID nota, altrimenti Xbox 360.

---

## 5. Architettura

**Stack:** C# / .NET 8, pubblicazione single-file self-contained (`win-x64`).

```
tools/usbtoc64-config/
├── DESIGN.md                      (questo documento)
├── src/
│   ├── UsbToC64.Core/             logica pura, multipiattaforma, testabile
│   │   ├── Model/                 Profile, Rule, ControlMap, enum Op/Func
│   │   ├── Hid/                   parser descriptor → ControlMap, normalizzazione report ID
│   │   ├── XInput/                tabella layout nativo X360
│   │   ├── Rules/                 generatore regole, validatore (conflitti, limiti)
│   │   ├── Emulation/             replica del motore dinamico firmware (anteprima)
│   │   └── Formats/               JSON save_jm, .h (con WEB_META), import .h/.json
│   ├── UsbToC64.Input/            lettura pad: HidSharp (HID, descrittore) e XInput
│   ├── UsbToC64.Device/           seriale: ricerca porta (VID Espressif), apertura; la logica save/verify è in Core/Device
│   ├── UsbToC64.App/              WPF + WPF-UI (Fluent): Gamepad, Firmware, Terminale, Registro (Mouse/LED/Impostazioni in arrivo)
│   └── UsbToC64.Cli/              comandi da terminale
└── tests/UsbToC64.Core.Tests/     xUnit, incluse catture reali (file 24/25)
```

Librerie: **HidSharp** (lettura report e descriptor), XInput via P/Invoke
(`xinput1_4.dll`), `System.IO.Ports` (fase 2).

`Core` non dipende da Windows: generazione regole, validazione ed emulazione
si sviluppano e testano anche fuori da Windows.

---

## 5bis. Build e distribuzione

- **Nessuna build sul PC dell'utente**: l'exe viene compilato da **GitHub Actions** su runner `windows-latest`.
- Workflow (`.github/workflows/usbtoc64-config.yml`), attivato da push sui file in `tools/usbtoc64-config/**` e manualmente (`workflow_dispatch`):
  1. test di `UsbToC64.Core` (xUnit);
  2. `dotnet publish` GUI e CLI, `win-x64`, single-file, self-contained (non serve installare .NET);
  3. upload dell'exe come **artifact** scaricabile dalla pagina della run.
- Release: sui tag `config-tool-v*` l'exe viene allegato a una GitHub Release.
- Senza firma del codice Windows SmartScreen mostra un avviso al primo avvio ("Esegui comunque").

---

## 6. Piano a fasi

### Fase 1 — Preparazione della configurazione (prima fase)

Nessuna scrittura sull'ESP32. Output: profilo validato, esportabile.

1. **Rilevamento pad**: elenco dispositivi HID gamepad/joystick + XInput, con VID/PID e nome; scelta del pad.
2. **Classificazione automatica** (§3bis): dal descriptor (HID) o dalla tabella (XInput) il tool sa quali controlli sono direzionali e quali pulsanti, con byte/bit/tipo. Nessuna scelta "assi o pulsanti", niente "Force Axis".
3. **Proposta automatica** della mappatura: hat/D-pad/assi X-Y → direzioni, pulsanti → FIRE 1, UP, FIRE 2, … secondo l'ordine predefinito.
4. **Identificazione dal vivo**: premendo un tasto o muovendo la leva, il controllo corrispondente si evidenzia. Procedura guidata "premi il tasto" solo come fallback (§3bis).
5. **Editor azioni**: per ogni controllo scelta della funzione (tabella §2.2), colori opzionali.
6. **Validazione**, prima di consentire l'export:
   - conflitti (stesso byte+maschera su due controlli, P6);
   - profilo vuoto, VID/PID 0 (P9);
   - limiti firmware (32 regole, 4 assi, dimensione JSON);
   - assi signed / hat non supportati;
   - pad XInput: avviso su stick e su Xbox One.
7. **Anteprima dal vivo**: i report reali del pad passano nell'emulatore del motore dinamico (§2.3) → indicatori su/giù/sinistra/destra, fire 1-3, autofire, come li vedrebbe il C64.
8. **Export/import**: `save_jm` JSON (nel formato del §2.2) e `.h` compatibile con il configuratore web (incluso `WEB_META`), import di entrambi.

Criteri di accettazione fase 1:
- I file 24 e 25 importati producono avvisi che spiegano P5/P6/P7/P8.
- Un pad HID generico riceve direzioni e pulsanti assegnati automaticamente dal descriptor, senza scelte manuali "assi/pulsanti" né di byte.
- Il JSON generato è valido per `save_jm` (verifica manuale con terminale, come fatto nella prova B).
- I test di `Core` girano senza Windows.

### Fase 2 — Scrittura e verifica (implementata)

- Porta trovata per VID `0x303A`; uso esclusivo della porta.
- Invio `save_jm`, attesa `{"status":"ok"}` con timeout → errore esplicito.
- Gestione del riavvio: la porta scompare/ricompare, attesa del boot.
- Rilettura e confronto (vedi richieste firmware §7).

### Fase 3 — CLI e monitor

```
usbtoc64 ports
usbtoc64 analyze                     # elenca i controlli del pad collegato
usbtoc64 convert file.h --to json
usbtoc64 save profilo.json --verify
usbtoc64 dump --json
usbtoc64 clear pad
```

Monitor: console seriale integrata, log su file di tutto lo scambio, anteprima regole.

---

## 7. Modifiche al firmware (protocollo 1) e compatibilità

Implementate in `USBtoC64/USBtoC64_Adv.ino`, solo sul branch di sviluppo. Sono **solo aggiunte**:

1. `save` e `save_jm`: se il JSON non è valido (o manca `rules`) il firmware ora risponde
   `{"status":"error","reason":"json|no_rules","detail":"..."}`. Prima non rispondeva nulla.
2. Nuovo comando `{"cmd":"get_jm"}`: restituisce il blob TIER 0 nello stesso formato di `save_jm`
   (senza `cmd`), più `status`, `proto` e `has_custom`:
   `{"status":"ok","proto":1,"has_custom":true,"vid":..,"pid":..,"dpad_idx":..,"use_analog":..,"analog_x":[..],"analog_y":[..],"rules":[..]}`.
   Senza profilo: `{"status":"empty","proto":1}`.

3. `Serial.setRxBufferSize(4096)` prima di `Serial.begin` (standalone). Il buffer predefinito di 256 byte è
   più piccolo di un `save_jm` completo (~700 byte): il JSON arrivava troncato e veniva scartato senza risposta.
   Era la causa dei SAVE del configuratore web che mostravano "CONFIGURATION SAVED" senza salvare nulla.
   Il tool comunque invia a blocchi da 32 byte con pause di 15 ms, così funziona anche con il firmware originale.

**Compatibilità con il configuratore web attuale**

- Le risposte esistenti (`get_info`, `mem_status`, `mouse_get`, `led_get`, gli `ok` di salvataggio…)
  sono invariate, carattere per carattere. Le pagine web leggono fino alla prima `}` e fanno `JSON.parse`:
  nessun campo nuovo è stato aggiunto alle risposte che usano.
- `get_jm` non è usato dalle pagine web.
- La risposta d'errore arriva solo dove prima c'era silenzio: `configurator.html` la legge e la ignora,
  quindi si comporta come prima.
- Il tool funziona anche con il firmware **non aggiornato**: se `get_jm` non risponde, rilegge il profilo
  con il comando testuale `dumppad` (stesso confronto, formato meno robusto).

**Verifica dopo la scrittura (tool, fase 2)**

`save_jm` → attesa di `{"status":"ok"}` (timeout 3 s, errore esplicito altrimenti) → la porta scompare e
ricompare per il riavvio (max 20 s) → attesa del boot (3 s) → `get_jm` (o `dumppad`) → confronto campo per campo
con quanto inviato (VID/PID, `dpad_idx`, regole nell'ordine, assi; tenendo conto del limite di 32 regole).

### 7.1 Firmware 1.2.3: operatori con segno per gli stick

Nuovi op nel motore dinamico: `3` = `S<` (`(int8)byte < (int8)valore`), `4` = `S>`. Applicati al byte alto
di un asse con segno (stick Xbox int16, assi HID con logical min < 0). Il tool genera due regole per asse
con soglia ±51 (~40 % della corsa): X → LEFT/RIGHT; Y → UP/DOWN (XInput: positivo = su; HID: positivo = giù).
Il firmware precedente tratterebbe op 3/4 come `==`: il tool **rifiuta di scrivere** questi profili se
`get_version` non riporta almeno 1.2.3. `dumppad` stampa `S<`/`S>`; il formato del blob non cambia.

---

## 8. Rischi

| Rischio | Mitigazione |
|---|---|
| Emulatore non allineato al firmware | Specifica §2.3 versionata; test con casi presi dal codice firmware |
| Descriptor esotici (hat non allineato, report ID multipli) | Avviso e fallback a cattura manuale del bit |
| Layout XInput diverso su cloni | Verifica con `raw`; tabella sovrascrivibile da file |
| SmartScreen / antivirus | Firma del codice (certificato a pagamento) o distribuzione con istruzioni |
| JSON vicino al limite 3072 byte | Avviso sopra ~28 regole; test sul dispositivo |

## 9. Stima

| Fase | Stima (1 persona) |
|---|---|
| 1 — Preparazione configurazione | 2–3 settimane |
| 2 — Scrittura e verifica | 3–5 giorni (+1–2 giorni firmware) |
| 3 — CLI e monitor | 1–2 settimane |
