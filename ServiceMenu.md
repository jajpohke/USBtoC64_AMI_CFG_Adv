# 🛠️ Advanced Service Menu Guide

The Advanced Service Menu is a powerful command-line interface built directly into the ESP32. It allows you to debug hardware, map new controllers, test input lag, and tweak system colors without needing to recompile the code.

### How to access the menu
1. Connect the ESP32 to your PC using a USB-to-TTL Serial Adapter (See hardware setup in the main README).
2. Open a Serial Terminal (e.g., Arduino IDE Serial Monitor, PuTTY) set to **115200 Baud**, `8N1`, with `Newline` enabled.
3. Type `service` and press Enter.

---

## Command Reference

### `new` Command
**Maps a new unknown gamepad manually or auto-converts an HTML profile.**
When you type `new`, the system checks if a generic HTML HID profile is currently active:
* **Auto-Import:** If an HTML profile is detected, it will ask if you want to convert it. It will instantly generate the clean C++ code for your `JoystickProfiles.h`.
* **Manual Wizard (Sniffer):** If no HTML profile is active, it arms the Sniffer Wizard. Do not touch the pad for 1 second (to record the neutral state), then follow the on-screen prompts to press each button sequentially. It will generate a custom C++ profile at the end.

### `raw` Command
**Displays the raw USB hex data stream.**
Useful for low-level debugging. It prints the raw byte array coming from the USB Host shield in real-time. Only values that change from the previous state are printed to avoid flooding the terminal. Type `exit` to leave.

### `test` Command
**Tests logical button mappings.**
This mode translates the raw USB data into logical console actions. Pressing a button on your gamepad will print `[UP]`, `[FIRE 1]`, `[AUTOFIRE]`, etc., to the screen. Perfect for verifying if your custom `PadConfig` is mapped correctly.

### `lag` Command
**Measures USB Polling Rate and Input Lag.**
Starts a highly accurate hardware latency benchmark.
1. Type `lag`. The system enters a "Smart Trigger" waiting state.
2. The timer will not start until you physically move the joystick or press a button.
3. Once triggered, frantically move the sticks and press buttons for **3 seconds**.
4. The system calculates the average milliseconds between packets and outputs a Hz rating:
   * **EXCELLENT 🟢 (400+ Hz):** Under 2.5ms input lag. Perfect for hardcore retrogaming.
   * **GOOD 🟡 (200+ Hz):** Under 5ms input lag. Great response.
   * **ACCEPTABLE 🟠 (100+ Hz):** ~8ms input lag. Standard controller speed.
   * **POOR 🔴 (<100 Hz):** Noticeable lag. Not recommended for fast action games.

### `gpio` Command
**Real-time visual dashboard of DB9 hardware states.**
This creates a live, auto-refreshing table showing the exact electrical state of the DB9 output pins.
* Displays the physical ESP32 GPIO pin number.
* Shows the electrical state (`HIGH` or `LOW`).
* Shows the logical state (`[ IDLE ]` or `[ PRESSED ]` / `[ ACTIVE ]`).
* *Note:* The system automatically adjusts the logic display for Amiga (Pin 9 Active-Low) and C64 (POT Y / POT X logic) depending on your current boot mode.

### `color` Command
**Live RGB Color Mixer.**
A hardware calibration tool to tweak the WS2812B system LED.
1. Type `color` to open the selection menu.
2. Type the number of the state or button you want to tweak (e.g., `10` for Fire 2).
3. **Use your connected gamepad to mix the color live:**
   * **FIRE 1:** Cycle the active channel between RED 🔴, GREEN 🟢, and BLUE 🔵.
   * **D-PAD LEFT / RIGHT:** Decrease or increase the value of the active color channel (0-255).
   * **D-PAD UP / DOWN:** Adjust the global brightness of the LED.
   * **FIRE 2:** Locks in the color and prints the exact `ws2812b.Color(r, g, b)` code to the terminal, ready to be copy-pasted into your `Globals.h` or `JoystickProfiles.h`.
   * **FIRE 3:** Exits the mixer.

### `c64` / `amiga` Commands
**Forces the system logic.**
By default, the adapter detects if it's plugged into an Amiga by checking if Pin 5 is pulled `HIGH` at boot. If you are testing the board on a desk without a console, you can manually force the C64 or Amiga logical routing by typing these commands.

### `reboot` Command
Performs a soft reset of the ESP32, re-initializing the USB Host and the LED state.

### `flash` Command
Reboots the ESP32 directly into **DFU (Device Firmware Upgrade) / Programming Mode**. This is extremely useful if your ESP32 board is inside a 3D-printed case and you cannot easily reach the physical "BOOT" button to upload new firmware via Arduino IDE.

### `exit` Command
Closes the Service Menu, shuts down all serial printing overhead, and returns the device to the standard **Zero-Lag gaming mode**. Always use this command before actually playing a game on real hardware!
