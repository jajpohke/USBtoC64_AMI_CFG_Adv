# 🕹️ USB to Commodore 64 & Amiga — Advanced Edition v1.2

An ESP32-S3 firmware to connect modern USB controllers and mice to classic **Commodore 64** and **Amiga** computers via the DB9 joystick port.

This is a fork of the [original USBtoC64 project](https://github.com/emanuelelaface/USBtoC64) by Emanuele Laface.

---

> ⚠️ **THIS PROJECT IS EXPERIMENTAL AND NOT COMMERCIAL. USE AT YOUR OWN RISK.**

> 🆘 **Something not working?** See the [Troubleshooting & FAQ](TROUBLESHOOTING.md) page.

---

## ⚠️ Safety Warnings — Read Before You Start

- **NEVER power the Commodore 64/Amiga and the USB port simultaneously.** Power flowing through the DB9 port can reach the SID chip and destroy it.
- Some wireless/Bluetooth controllers use the USB port to charge their internal battery. This can draw more than 100 mA from the C64, potentially shutting it down or damaging it. **Remove the battery or disable charging before use.**
- If testing the board standalone on a desk, connect all 4 serial adapter pins (including 5V). If the ESP32 is already powered by the DB9 port or its own USB-C connector, **connect only TX, RX and GND** — never 5V.

---

## ✨ What's New in v1.2

- **Integrated Web UI** — full configuration, LED color management and system settings directly from the browser via the built-in web server. No Arduino IDE or serial terminal required for everyday use.
- **Standalone Mode** — the adapter can be configured over USB-C from a PC without the C64/Amiga connected. Useful for setting up before a session.
- **USB Mouse support** — automatic detection in both C64 (SID 1351 proportional) and Amiga mode.
- **NVS persistent storage** — all settings (controller mapping, LED colors, LED format, mouse speed) survive power cycles without recompiling.
- **Interactive LED Color Calibration** (`coloradj`) — adjust every LED color live from the serial terminal, save to NVS.

---

## 🔌 Hardware

### Components

| Component | Notes |
|---|---|
| **ESP32-S3 Zero** | [Waveshare version](https://www.waveshare.com/esp32-s3-zero.htm) recommended. Other S3 boards may have a different pinout. |
| **2N3904 NPN transistor** | |
| **PCB Slide Switch, 3-pin** | C64 / Amiga selection |
| **2× 1 kΩ resistors, 1%** | |
| **2× 150 Ω resistors, 1%** | In series with BAT43 on POT X/Y lines |
| **1× 5.1 kΩ resistor, 1%** | |
| **2× BAT43 Schottky diodes** | Protect the C64 SID POT lines |
| **DB9 female connector** | Remove the metal shell — it can short the C64's +5V when inserted |
| **WS2812B RGB LED** | Status and action feedback |

> The PCB is available in two versions: THT (v3.2) and SMD (v4.1). Functionality is identical. Fully assembled boards can be purchased from [Emanuele's Tindie shop](https://www.tindie.com/products/burglar_ot/usbtoc64/).

### Schematic

[![Schematic](https://github.com/emanuelelaface/USBtoC64/raw/main/images/schematic.jpeg)](https://github.com/emanuelelaface/USBtoC64/blob/main/images/schematic.jpeg)

### GPIO Pin Mapping

| GPIO | Name | Circuit | Function |
|---|---|---|---|
| GP3 | `GP_FIRE3` | Direct → DB9 | Fire 3 / POT Y signal in joystick mode |
| GP4 | `GP_POTX` | 150Ω + BAT43 → DB9 | POT X driver for SID mouse |
| GP5 | `GP_FIRE2` | Direct → DB9 | Fire 2 |
| GP6 | `GP_POTY` | 150Ω + BAT43 → DB9 | POT Y driver for SID mouse |

---

## 🚀 Installation

### Option A — Flash the precompiled binary (recommended)

1. Disconnect the adapter from the C64/Amiga.
2. Hold the **BOOT** button, connect the board via USB-C, wait one second, then release.
3. Open [esptool web flasher](https://espressif.github.io/esptool-js/) in Chrome, click **Connect**, set Flash Address to `0x0000`, and upload the `.bin` file.

Or via Python:
```bash
pip install esptool
esptool.py -b 921600 -c esp32s3 -p <PORT> write_flash --flash_freq 80m 0x00000 USBtoC64_Adv.bin
```

### Option B — Compile from source (Arduino IDE)

1. Install the **ESP32S3 Dev Module** board in Arduino IDE.
2. Install the [ESP32_USB_Host_HID](https://github.com/esp32beans/ESP32_USB_Host_HID) library (`Sketch → Include Library → Add .ZIP Library`).
3. Open `USBtoC64_Adv.ino` and upload.

---

## 💻 Web UI — Browser Configuration (v1.2)

Once the firmware is running, connect the adapter via USB-C to your PC. Open any of the web pages in the [`configurator/` folder](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/index.html) in Chrome (WebSerial and WebHID are required — Firefox is not supported).

| Page | Purpose |
|---|---|
| [`index.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/index.html) | Main hub — access all pages |
| [`configurator.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/configurator.html) | Map USB controller buttons visually, save mapping to ESP32 |
| [`led.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/led.html) | Customize all LED colors, select GRB/RGB format |
| [`memory.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/memory.html) | System settings, NVS memory status, factory reset |
| [`mouse.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/mouse.html) | Mouse speed and mode settings |

All settings are saved to NVS and survive reboots. When you change the **LED format (GRB/RGB)**, the page will prompt you to refresh after saving — this is expected.

---

## 🚥 LED Status

### Boot / Idle

| Color | Meaning |
|---|---|
| 🟠 Orange | C64 mode — waiting for controller |
| ⚪ White | Amiga mode — waiting for controller |

### Controller Active

| Color | Meaning |
|---|---|
| 🟣 Purple | Direction pressed |
| 🟢 Green | Fire 1 |
| 🔴 Red | Fire 2 |
| 🩵 Cyan | Fire 3 |
| 🔵 Blue | Alt UP (Jump) |
| 🟡 Yellow (blinking) | Autofire active |

> All colors are customizable via [`led.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/led.html) or via `coloradj` in the service menu.

> **Wrong colors?** Your board may have RGB/GRB swapped. Fix it in [`led.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/led.html) → LED Format selector, or via `service → coloradj`.

---

## 🛠️ Service Menu (Serial Console)

For advanced diagnostics. Connect a USB-to-TTL serial adapter to **GPIO 44 (RX)** and **GPIO 43 (TX)** at **115200 baud, 8N1, newline line ending**. Then open a serial terminal and type `service`.

| Command | Available | Description |
|---|---|---|
| `new` | Normal + Dev | Map a new controller via wizard (hardware learning or serial sniffer) |
| `raw` | Normal + Dev | Show live raw USB hex stream |
| `test` | Normal + Dev | Print logical button outputs |
| `lag` | Normal + Dev | Measure USB polling rate and input lag |
| `mousetest` | Normal + Dev | Mouse packet analysis and speed benchmark |
| `gpio` | Normal + Dev | Real-time DB9 pin state dashboard |
| `coloradj` | **Always** | Interactive LED color calibration — adjust R/G/B live, save to NVS |
| `ledtest` | Dev only | Cycle all 10 palette colors on the LED (1.2s each) |
| `devmode on/off` | Always | Enable/disable developer mode (requires reboot) |
| `info` | Standalone | Show NVS memory status |
| `factory` | Standalone | Erase all saved settings and reboot |
| `amiga` / `c64` | Normal + Dev | Force console mode for bench testing |
| `reboot` | Always | Soft reboot |
| `flash` | Always | Reboot into DFU/programming mode (no BOOT button needed) |
| `exit` | Always | Return to zero-lag play mode |

### `coloradj` — Color Calibration

Type `coloradj` to enter the interactive menu. Select a color slot (1–8), then use:

```
r+  r-  g+  g-  b+  b-     → adjust by current step (default: 1)
r++ r-- g++ g-- b++ b--     → adjust by 5
step N                       → change step size (1–50)
save                         → write to NVS
back                         → return without saving
reset                        → restore all firmware defaults
```

The LED updates in real time as you type. Changes are shared with [`led.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/led.html) — both write to the same NVS blob.

---

## 🎮 Controller Support

### Plug & Play (pre-configured native profiles)

| Controller | Notes |
|---|---|
| PlayStation 4 (original) | Full support |
| HORI Mini 4 | Full support |
| Buffalo Classic USB | Full support |
| HoriPad GameCube (Pokémon / Peach) | Full support |
| SNES Wi-Fi clone (D-input) | Full support |
| NES2USB RetroBit | Full support |
| USB2SNES Mayflash | Full support |
| China Arcade PS3/PC | Full support |
| Zero Lag China clone | Full support |
| Xbox One | ❌ Not supported |

> For a complete and updated list, see [SUPPORTED_PADS.md](SUPPORTED_PADS.md).

### Mapping a New Controller

**Quick way — Web UI (no recompile):**
1. Connect the controller and open [`configurator.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/configurator.html).
2. Map all buttons visually and click **💾 SAVE**.
3. Done — the mapping is saved to NVS and active immediately on every boot.

**Permanent way — native C++ profile (zero latency, built-in):**
1. Map the controller via [`configurator.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/configurator.html) first to understand the button layout.
2. Open a serial terminal, type `service` → `new` → choose option **2 (Serial Sniffer)**.
3. Press each button when prompted. The adapter outputs a ready-to-paste C++ block.
4. Copy it into `JoystickProfiles.h` and recompile.

---

## ⚠️ Known Hardware Quirks

**HORI Mini Fighting Stick (PS3/PS4):** May not be recognized on cold boot. Fix: press RESET on the ESP32 after the controller is connected, or unplug/replug the USB cable.

**SNES wireless dongle clones:** Plug the dongle only *after* the adapter has fully booted (LED shows orange or white). Plugging it in during boot may hang the USB host.

**C64/Amiga slide switch:** The physical slide switch on the board selects the console mode. Changing its position while the adapter is powered triggers an automatic reboot, after which the new mode is applied. There is no software override for normal use — always set the switch before connecting the adapter.

---

## 🤝 Credits

- Original hardware design and SID signal research: **[Emanuele Laface](https://github.com/emanuelelaface/USBtoC64)**
- Advanced firmware fork: **Jahpohke** and **Thelowest**
- v1.1 AI assistance: **Google Gemini Pro**
- v1.2 AI assistance: **Anthropic Claude**
- Thanks to:
  - **Kenobisboch Productions** feat. Andrea Babich (Commodore 64 Advent Show 2025) — the spark that started this
  - **Davide Bottino** — for pushing Fire 2/3 support with Bubble Bobble Remastered and Lost Cave

---

*MIT License — see [LICENSE](LICENSE) for details.*
