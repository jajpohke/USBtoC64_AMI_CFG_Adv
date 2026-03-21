# 🛠️ Service Menu Guide

The service menu provides access to diagnostics, live input monitoring, LED calibration, and system management via a serial terminal. It is intended for advanced users who want to debug controller behaviour, measure input lag, or manage NVS settings directly.

For developer-specific commands and the serial sniffer, see [DEVELOPER.md](DEVELOPER.md).

- [Connecting to the Serial Terminal](#connecting-to-the-serial-terminal)
- [Entering the Service Menu](#entering-the-service-menu)
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

---

## Entering the Service Menu

Type `service` and press Enter. The adapter will pause input processing and enter the service menu. Type `exit` when done to return to zero-lag play mode.

---

## Command Reference

| Command | Available in | Description |
|---|---|---|
| `raw` | Normal + Dev | Show live raw USB hex stream from the connected controller |
| `test` | Normal + Dev | Print logical button outputs (UP, DOWN, FIRE, etc.) |
| `lag` | Normal + Dev | Measure USB polling rate and input lag in real time |
| `mousetest` | Normal + Dev | Mouse packet analysis and speed benchmark |
| `gpio` | Normal + Dev | Real-time DB9 pin state dashboard |
| `coloradj` | Always | Interactive LED color calibration — adjust R/G/B live, save to NVS |
| `info` | Standalone | Show NVS memory usage and stored settings summary |
| `factory` | Standalone | Erase all saved settings and reboot |
| `amiga` / `c64` | Normal + Dev | Force console mode for bench testing without the slide switch |
| `reboot` | Always | Soft reboot |
| `flash` | Always | Reboot into DFU/programming mode — no BOOT button needed |
| `exit` | Always | Return to zero-lag play mode |

> Some commands are only available in **Developer Mode**. See [DEVELOPER.md](DEVELOPER.md) for details.

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

The LED updates in real time. Changes are shared with the web UI — both write to the same NVS blob. For a full description of what each color slot represents, see [LEDGUIDE.md](LEDGUIDE.md).

---

*Back to [README](README.md)*
