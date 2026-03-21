# 🔧 Developer Guide

This page covers advanced tools available in Developer Mode — including the serial sniffer for creating native controller profiles, extended diagnostic commands, and NVS internals.

> ⚠️ Developer Mode is intended for advanced users. Enabling it exposes commands that can modify firmware behaviour. Use with care.

- [Enabling Developer Mode](#enabling-developer-mode)
- [Developer-Only Commands](#developer-only-commands)
- [Creating a Native Controller Profile — Serial Sniffer](#creating-a-native-controller-profile--serial-sniffer)
- [NVS Architecture](#nvs-architecture)

---

## Enabling Developer Mode

Connect to the serial terminal (see [SERVICEMENUGUIDE.md](SERVICEMENUGUIDE.md) for wiring instructions), enter the service menu by typing `service`, then:

```
devmode on
```

The adapter will reboot. After rebooting, re-enter `service` — the full command set will now be available.

To disable:

```
devmode off
```

Developer mode state is saved to NVS and persists across reboots until explicitly disabled.

---

## Developer-Only Commands

| Command | Description |
|---|---|
| `new` | Launch the controller mapping wizard. Choose between hardware learning mode or the serial sniffer (option 2) to generate a native C++ profile. |
| `ledtest` | Cycle through all 10 palette color slots on the LED, 1.2 seconds each. Useful for verifying LED wiring and GRB/RGB byte order. |

All standard service menu commands remain available in Developer Mode. See [SERVICEMENUGUIDE.md](SERVICEMENUGUIDE.md) for the full list.

---

## Creating a Native Controller Profile — Serial Sniffer

The serial sniffer guides you through pressing each button and direction on your controller, isolates the relevant HID bytes, and generates a ready-to-paste C++ block for `JoystickProfiles.h`.

Use this when you want a controller to work with zero configuration on every boot, without relying on NVS mappings.

### Steps

1. Enable Developer Mode (see above) and re-enter the service menu.
2. Type `new` and press Enter.
3. Select option **2 — Serial Sniffer**.
4. Press each button and direction when prompted. The sniffer automatically filters background noise and jittery axes.
5. When complete, the adapter outputs a C++ struct block. Copy it.
6. Open `JoystickProfiles.h` in your Arduino project, paste the block, and recompile.
7. Flash the new firmware via the [esptool web flasher](https://espressif.github.io/esptool-js/) or esptool CLI.

> **Tip:** If the sniffer misidentifies an axis as noise, reduce controller movement to only the direction being mapped and press Enter to retry that step.

---

## NVS Architecture

Settings are stored in NVS (Non-Volatile Storage) in two tiers, applied in priority order:

| Tier | Source | Priority |
|---|---|---|
| **TIER 0** | NVS `jm_blob` — mapping saved via web configurator | Highest |
| **TIER 2** | `JoystickProfiles.h` — compiled native profiles | Lowest |

When a controller is connected, the firmware checks TIER 0 first. If a matching VID/PID blob exists in NVS, it is used and TIER 2 is ignored entirely.

### jm_blob format

```json
{
  "cmd": "save_jm",
  "vid": 1234,
  "pid": 5678,
  "dpad_idx": -1,
  "use_analog": 1,
  "analog_x": [0],
  "analog_y": [1],
  "rules": [
    { "i": 2, "v": 4, "op": 0, "f": 8 }
  ]
}
```

| Field | Description |
|---|---|
| `dpad_idx` | Byte index used for HAT/EQ direction rules. `-1` when directions are axis-based. |
| `use_analog` | `1` if directional input uses analogue axes |
| `analog_x` / `analog_y` | Array of HID axis indices mapped to X and Y |
| `rules[].op` | `0` = JM_EQ, `1` = JM_BITANY, `2` = JM_HAT |
| `rules[].f` | Output: `0`=UP, `1`=UP_RIGHT, `2`=RIGHT, `3`=RIGHT_DOWN, `4`=DOWN, `5`=DOWN_LEFT, `6`=LEFT, `7`=LEFT_UP, `8`=FIRE, `9`=FIRE2, `10`=FIRE3, `11`=AUTO_ON, `12`=AUTO_OFF, `13`=AUTO_HOLD |

To inspect or reset NVS content, use `info` and `factory` from the service menu, or the **Settings** page in the web UI ([`memory.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/memory.html)).

---

*Back to [README](README.md)*
