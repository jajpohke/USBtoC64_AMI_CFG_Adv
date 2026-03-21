# 🎮 Controller Support Guide

This page covers all controllers tested with the USB2C64 Advanced Edition — what works out of the box, what needs manual mapping, and what is currently unsupported.

- [Plug & Play — Native Profiles](#plug--play--native-profiles)
- [Mapping a New Controller](#mapping-a-new-controller)
- [Unsupported Controllers](#unsupported-controllers)
- [Known Quirks](#known-quirks)

---

## Plug & Play — Native Profiles

The following controllers are pre-configured in the firmware (`JoystickProfiles.h`) and work immediately on plug-in with no setup required.

| Controller | VID | PID | D-Pad Type | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **HORI Mini 4** | `0x0f0d` | `0x00ed` | BITMASK | Tested and working |
| **Buffalo Classic** | `0x0583` | `0x2060` | AXIS | Pure SNES profile |
| **Sony PS3 Clone** | `2064` | `1` | AXIS | Integrated anti-noise support |
| **HoriPad GameCube Pokémon** | `3853` | `220` | HYBRID | |
| **HoriPad GameCube Peach** | `3695` | `389` | EXACT_VALUE | |
| **SNES Wi-Fi Clone (D-input)** | `10093` | `291` | AXIS | |
| **Original PS4 (DualShock 4)** | `1356` | `1476` | HAT_SWITCH | See note below |
| **China Arcade PS3/PC** | `2064` | `3` | AXIS | Works well for arcade cabinets |
| **Zero Lag China Clone** | `121` | `6` | AXIS | |
| **NES2USB RetroBit** | `4754` | `17987` | BITMASK | Optimized profile — fixes the silent diagonals bug |
| **USB2SNES Mayflash** | `3727` | `12307` | AXIS | Smart Multiport: supports two SNES pads simultaneously (Co-Pilot Mode), filters false contacts from internal multiplexer |

> **Note on Original PS4 (DualShock 4):** Due to the complexity of its protocol and multiple onboard sensors, the native profile may not interpret all signals correctly in every situation. If you experience issues, map it manually via the [web configurator](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/configurator.html). A manually saved NVS mapping always takes priority over the built-in profile.

---

## Mapping a New Controller

If your controller is not in the list above, you can map it manually using the web configurator — no recompile needed, no serial terminal required.

This also applies to controllers that already have a native profile: a mapping saved via the web configurator always takes priority over the built-in one. This means you can fine-tune or completely override any pre-configured pad without touching the firmware.

1. Connect the controller and open [`configurator.html`](https://raw.githack.com/jajpohke/USBtoC64_AMI_CFG_Adv/main/configurator/configurator.html) in Chrome.
2. Follow the on-screen connection flow (press any button → click the yellow banner → go green).
3. Click each zone on the visual layout and press the corresponding button or direction on your controller.
4. Click **💾 SAVE** when done. The mapping is written to NVS and active on every subsequent boot.

For a detailed walkthrough of the configurator interface, see [GETSTARTED.md — Section 4](GETSTARTED.md#4-configuring-a-controller).

### Adding a permanent native profile (advanced)

If you want your mapping compiled directly into the firmware as a zero-latency native profile, use the serial sniffer. See [DEVELOPER.md](DEVELOPER.md) for instructions.

---

## Unsupported Controllers

| Controller | Reason |
| :--- | :--- |
| **Xbox One** | Protocol not supported |
| **Nintendo Switch licensed pads** (e.g. Cuphead, some HoriPad Switch editions) | Protocol-compatible at HID level but cause a continuous reboot loop on the ESP32 in the units tested. Likely a USB enumeration timing issue. Currently unsupported. |

If you own one of these and want to help investigate, feel free to open an issue with the VID/PID and the raw USB dump from the serial `raw` command.

---

## Known Quirks

**HORI Mini Fighting Stick (PS3/PS4):** May not be recognized on cold boot. Fix: press RESET on the ESP32 after the controller is connected, or unplug/replug the USB cable.

**SNES wireless dongle clones:** Plug the dongle only *after* the adapter has fully booted (LED shows orange or white). Plugging it in during boot may hang the USB host.

**Original PS4 — noise on analogue axes:** The DualShock 4 gyroscope and touchpad generate continuous background data. The firmware's automatic noise blacklist handles this, but if you notice spurious inputs, re-map the controller via the web configurator and let the blacklist rebuild.

---

*Back to [README](README.md)*
