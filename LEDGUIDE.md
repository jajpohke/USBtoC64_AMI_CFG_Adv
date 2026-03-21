# 🚥 LED Guide

The USB2C64 uses a single WS2812B RGB LED to communicate the adapter's current state at a glance — mode, input activity, and autofire. All colors are fully customisable.

- [Status Colors](#status-colors)
- [Customising Colors — Web UI](#customising-colors--web-ui)
- [Customising Colors — Serial Terminal](#customising-colors--serial-terminal)
- [Wrong Colors? GRB vs RGB](#wrong-colors-grb-vs-rgb)

---

## Status Colors

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

> These are the default colors. All of them can be changed via the web UI or serial terminal and are saved to NVS.

---

## Customising Colors — Web UI

1. Connect the adapter via USB-C to your PC.
2. Open [`led.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/led.html) in Chrome.
3. Select a color slot, adjust the RGB values using the sliders, and click **SAVE**. The LED updates immediately.

Changes are saved to NVS and survive reboots.

---

## Customising Colors — Serial Terminal

Connect a USB-to-TTL serial adapter to **GPIO 44 (RX)** and **GPIO 43 (TX)** at **115200 baud, 8N1, newline line ending**. Open a serial terminal and type `service`, then `coloradj`.

Select a color slot (1–8), then use the following commands:

```
r+  r-  g+  g-  b+  b-     → adjust by current step (default: 1)
r++ r-- g++ g-- b++ b--     → adjust by 5
step N                       → change step size (1–50)
save                         → write to NVS
back                         → return without saving
reset                        → restore all firmware defaults
```

The LED updates in real time as you type. The web UI and serial terminal share the same NVS color blob — changes made in one are reflected in the other.

---

## Wrong Colors? GRB vs RGB

Some WS2812B variants use GRB byte order instead of RGB. If your LED shows the wrong colors (e.g. green instead of red on Fire 1), fix it in [`led.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/led.html) → **LED Format** selector, or via `service → coloradj` in the serial terminal.

After changing the LED format, the web page will prompt you to refresh — this is expected.

---

*Back to [README](README.md)*
