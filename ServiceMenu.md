# 🛠️ Service Menu — Reference Guide

The Service Menu is an advanced serial console built into the firmware. It is intended for developers, power users, and troubleshooting. For everyday configuration, use the **Web UI** instead.

## Accessing the Service Menu

1. Connect a USB-to-TTL serial adapter to the ESP32:

   | Serial Adapter | ESP32 |
   |---|---|
   | TX | GPIO 44 (RX) |
   | RX | GPIO 43 (TX) |
   | GND | GND |
   | 5V | 5V *(only if not powered from DB9 or USB-C)* |

2. Open a serial terminal at **115200 baud, 8N1, newline line ending**.
3. Type `service` and press Enter.

> The service menu is available in all modes: normal play, standalone (USB-C only), and dev mode. Some commands are restricted depending on context — see each command below.

---

## Command Reference

---

### `new`
*Available: normal + dev mode*

Maps a new USB controller and generates a native C++ profile for `JoystickProfiles.h`.

**Auto-Import flow (recommended):**
1. First map your controller visually using `configurator.html` and save it.
2. Type `new` in the service menu.
3. The firmware detects the active HTML profile and asks: `HTML Profile detected! Do you want to Auto-Import it? (Y/N)`
4. Type `Y`, enter a name for the profile, press Enter.
5. Copy the generated C++ block into `JoystickProfiles.h` and recompile.

**Manual wizard:** If no HTML profile is active, the firmware walks you through mapping each button by pressing them one at a time.

---

### `raw`
*Available: normal + dev mode*

Displays the raw USB HID data stream coming from the connected controller as a continuous hex dump. Useful for identifying button byte positions when writing a new native profile manually.

Press `Enter` to stop and return to the service menu.

---

### `test`
*Available: normal + dev mode*

Prints logical button events to the terminal as you press buttons on the connected controller. Shows which actions (UP, DOWN, FIRE1, etc.) are being triggered by the current mapping.

Useful to verify a new profile before flashing it permanently.

Press `Enter` to stop.

---

### `lag`
*Available: normal + dev mode*

Runs a hardware latency benchmark. Measures the controller's USB polling interval and calculates the effective input lag in milliseconds.

Output example:
```
Polling rate : 125 Hz
Input lag    : ~8 ms
```

---

### `mousetest`
*Available: normal + dev mode*

Displays live mouse USB packets including X/Y delta, buttons, and computed speed. Useful for diagnosing mouse behavior and tuning speed settings.

Press `Enter` to stop.

---

### `gpio`
*Available: normal + dev mode*

Opens a real-time dashboard showing the electrical state of every DB9 output pin.

> ⚠️ **Note:** When the DB9 connector is not physically connected to a C64 or Amiga, all pins will read LOW (appear as PRESSED) because the pull-up resistors on the console side are absent. This is expected behavior — it is not a fault.

Press `exit` to return to the service menu.

---

### `coloradj`
*Available: always (including standalone mode)*

Interactive LED color calibration. Adjust the R/G/B values of each system color slot live — the LED updates in real time as you type.

**Color slots:**

| # | Slot | Default |
|---|---|---|
| 1 | Idle C64 | Orange |
| 2 | Idle Amiga | White |
| 3 | Directions glowing | Purple |
| 4 | Fire 1 | Green |
| 5 | Fire 2 | Red |
| 6 | Fire 3 | Cyan |
| 7 | Alt UP (Jump) | Blue |
| 8 | Autofire | Yellow |

**Commands inside coloradj:**

```
1–8         → select a color slot to edit
r+  r-      → increase / decrease Red by current step
g+  g-      → increase / decrease Green by current step
b+  b-      → increase / decrease Blue by current step
r++ r--     → increase / decrease Red by 5
g++ g--     → increase / decrease Green by 5
b++ b--     → increase / decrease Blue by 5
step N      → set step size (1–50, default: 1)
save        → write all colors to NVS
back        → return to color slot selector without saving
reset       → restore firmware defaults for all slots and save
exit        → exit coloradj and return to service menu
```

> Settings saved here are shared with `led.html` — both write to the same NVS storage blob (`col_blob`).

---

### `ledtest`
*Available: dev mode only*

Cycles through all 10 palette colors on the LED sequentially (1.2 seconds each), then restores the idle color. Useful for verifying LED hardware and RGB/GRB format.

---

### `devmode on` / `devmode off`
*Available: always*

Enables or disables developer mode. Requires a reboot to take effect.

In dev mode:
- `ledtest` becomes available
- Console mode (C64/Amiga) can be forced via `c64` / `amiga` commands, overriding the physical slide switch
- Additional diagnostic output is printed at boot

---

### `c64` / `amiga`
*Available: dev mode only (not in standalone)*

Forces the adapter into C64 or Amiga logic mode for bench testing without the physical console connected. Overrides the physical slide switch position until the next reboot.

> In normal mode, console selection is controlled exclusively by the physical slide switch on the board. Changing the switch position while powered triggers an automatic reboot.

---

### `info`
*Available: standalone mode*

Displays a summary of the current NVS memory state: which keys are stored, their sizes, and whether custom mappings or color settings are present.

---

### `factory`
*Available: standalone mode*

Erases all user settings from NVS (controller mapping, LED colors, LED format, mouse speed) and reboots. The adapter returns to factory defaults.

> This is equivalent to using the factory reset button in `memory.html`.

---

### `reboot`
*Available: always*

Performs a soft reboot of the ESP32. All NVS settings are preserved.

---

### `flash`
*Available: always*

Reboots the ESP32 directly into DFU/programming mode, ready to receive a new firmware via esptool. No need to press the physical BOOT button on the board.

---

### `exit`
*Available: always*

Closes the service menu and returns the adapter to normal zero-lag play mode. Any active controller input is unblocked immediately.

> ⚠️ If a controller is connected and was being used during service mode, `exit` will call `release_all_outputs()` to ensure no DB9 lines are left in a pressed state before returning to play mode.
