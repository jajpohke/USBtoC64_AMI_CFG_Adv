# 🕹️ USB to Commodore 64 & Amiga — Advanced Edition v1.2

An ESP32-S3 firmware to connect modern USB controllers and mice to classic **Commodore 64** and **Amiga** computers via the DB9 joystick port.

This is a fork of the [original USBtoC64 project](https://github.com/emanuelelaface/USBtoC64) by Emanuele Laface.

This fork was born with a few specific goals: improving compatibility with stubborn controllers that don't work out of the box, adding Fire 2 and Fire 3 support on both C64 and Amiga, and offering a more user-friendly experience — without needing the Arduino IDE or reflashing the board every time something needs to change. Most configuration is handled directly from the browser via the [built-in web configurator](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/index.html).

For a full setup walkthrough, see the [Getting Started guide](GETSTARTED.md).

### ⚠️ Features not implemented in this fork

The following features from the original project are **not** available in this fork:

- Joystick-to-mouse emulation
- Mouse-to-joystick emulation
- Live reconfiguration via the BOOT button
- Atari Mouse support

**On mouse support:** C64 SID 1351 proportional mouse support is particularly complex. We have tried to replicate the behaviour from Emanuele's original firmware as closely as possible, adding only the ability to adjust pointer speed via the web configurator. No issues have been found during testing, but we do not have the equipment to guarantee full correctness across all hardware revisions.

If you need any of the above features, or run into mouse-related issues, we recommend flashing the original firmware — it only takes a few seconds using the [esptool web flasher](https://espressif.github.io/esptool-js/).

---

> ⚠️ **THIS PROJECT IS EXPERIMENTAL AND NOT COMMERCIAL. USE AT YOUR OWN RISK.**

> 🆘 **Something not working?** See the [Troubleshooting & FAQ](TROUBLESHOOTING.md) page.

---

## 📖 Documentation

- [🚀 Getting Started](GETSTARTED.md) — flash the firmware, verify the connection, configure your first controller
- [🎮 Controller Support](JOYSTICKGUIDE.md) — supported pads, plug & play profiles, mapping guide
- [🚥 LED Guide](LEDGUIDE.md) — status colors, behaviour, customisation
- [🛠️ Service Menu](SERVICEMENUGUIDE.md) — serial console, advanced commands, coloradj
- [✨ What's New](#whats-new-in-v12) — v1.2 changelog
- [🤝 Credits](#credits)

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

---

## ⚠️ Known Hardware Quirks

**HORI Mini Fighting Stick (PS3/PS4):** May not be recognized on cold boot. Fix: press RESET on the ESP32 after the controller is connected, or unplug/replug the USB cable.

**SNES wireless dongle clones:** Plug the dongle only *after* the adapter has fully booted (LED shows orange or white). Plugging it in during boot may hang the USB host.

**Nintendo Switch licensed controllers (e.g. Cuphead edition):** Some Switch-branded controllers are protocol-compatible and are recognized by the adapter, but several units tested caused a continuous reboot loop on the ESP32. If your Switch controller triggers this behaviour, it is currently unsupported.

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
