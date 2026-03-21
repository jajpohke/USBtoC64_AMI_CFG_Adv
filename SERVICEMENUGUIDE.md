# 🛠️ Service Menu Guide

The service menu provides access to diagnostics, live input monitoring, LED calibration, and system management via a serial terminal.

- [Connecting to the Serial Terminal](#connecting-to-the-serial-terminal)
- [Operating Modes](#operating-modes)
- [Command Reference](#command-reference)
- [coloradj — Interactive LED Calibration](#coloradj--interactive-led-calibration)

---

## Connecting to the Serial Terminal

You need a USB-to-TTL serial adapter (3.3V logic level). Connect it as follows:

| Serial Adapter | ESP32-S3 GPIO |
|---|---|
| TX | GPIO 44 (RX) |
| RX | GPIO 43 (TX) |
| GND | GND |

> Do **not** connect the 5V pin if the board is already powered via USB-C or DB9.

Open your preferred serial terminal (PuTTY, minicom, Arduino Serial Monitor, etc.) at **115200 baud, 8N1, newline line ending**.

Type `service` and press Enter to open the menu. Type `exit` when done.

---

## Operating Modes

The service menu adapts depending on how the adapter is connected. The available commands change accordingly.

### Standalone Mode
The adapter is connected to a PC via USB-C only — no C64/Amiga on the DB9 port, and Developer Mode is off. This is the most limited context, intended for NVS management and basic diagnostics.

### Normal Mode
The adapter is connected to a C64 or Amiga via the DB9 port. The full diagnostic command set becomes available.

### Developer Mode
Enabled via `devmode on` (requires reboot). Unlocks additional commands and allows running diagnostics over USB-C even without the C64/Amiga connected. See [DEVELOPER.md](DEVELOPER.md) for details.

---

## Command Reference

| Command | Standalone | Normal | Developer | Description |
|---|:---:|:---:|:---:|---|
| `info` | ✅ | ❌ | ❌ | Show NVS memory status: saved profiles, LED palette, mouse config, switch invert |
| `factory` | ✅ | ❌ | ❌ | Erase all saved settings and reboot |
| `gpio` | ✅ | ✅ | ✅ | Real-time DB9 pin state dashboard. Use `gp<N>` (e.g. `gp10`) to probe a specific GPIO |
| `coloradj` | ✅ | ✅ | ✅ | Interactive LED color calibration — adjust R/G/B live, save to NVS |
| `devmode on/off` | ✅ | ✅ | ✅ | Enable or disable Developer Mode (requires reboot) |
| `reboot` | ✅ | ✅ | ✅ | Soft reboot |
| `flash` | ✅ | ✅ | ✅ | Reboot into DFU/programming mode — no BOOT button needed |
| `exit` | ✅ | ✅ | ✅ | Exit the service menu |
| `new` | ❌ | ✅ | ✅ | Launch the controller mapping wizard (Hardware Learning or Serial Sniffer) |
| `raw` | ❌ | ✅ | ✅ | Show live raw USB hex stream from the connected controller |
| `test` | ❌ | ✅ | ✅ | Print logical button outputs (UP, DOWN, FIRE, etc.) |
| `lag` | ❌ | ✅ | ✅ | Measure USB polling rate and input lag in real time |
| `mousetest` | ❌ | ✅ | ✅ | Mouse packet analysis and speed benchmark |
| `amiga` / `c64` | ❌ | ✅ | ✅ | Force console mode for bench testing without moving the slide switch |
| `ledtest` | ❌ | ❌ | ✅ | Cycle through all LED palette color slots, 1.2s each |

> `new` and the Serial Sniffer are documented in detail in [DEVELOPER.md](DEVELOPER.md).

---

## coloradj — Interactive LED Calibration

Type `coloradj` from the service menu to enter the interactive color editor. Select a color slot (1–8) and use the following commands:

```
r+  r-  g+  g-  b+  b-     → adjust by current step (default: 1)
r++ r-- g++ g-- b++ b--     → adjust by 5
step N                       → change step size (1–50)
save                         → write to NVS
back                         → return without saving
reset                        → restore all firmware defaults
```

The LED updates in real time. Changes are shared with the web UI — both write to the same NVS blob. For a description of what each color slot represents, see [LEDGUIDE.md](LEDGUIDE.md).

---

*Back to [README](README.md)*
