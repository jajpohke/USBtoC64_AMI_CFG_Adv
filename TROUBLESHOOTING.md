# 🆘 Troubleshooting & FAQ

> Not a developer? No problem. This page answers the most common issues in plain language.

---

## 🔌 The adapter doesn't power on / no LED

**Q: I plugged everything in but the LED doesn't light up.**

The adapter is powered by the DB9 port of the Commodore 64 or Amiga — not by the USB-C connector.

- Make sure the DB9 connector is firmly inserted in **joystick port 2** (or port 1, depending on your game).
- The USB-C port is only used to connect the USB controller — it does **not** power the board.
- If you are testing on a desk without a C64/Amiga, you need to power the board through the 4-pin serial connector (5V, TX, RX, GND). See the [Hardware Setup section in the README](README.md#hardware-setup--wiring).

---

## 🟠🔵🟢 I see a weird color or the LED behaves unexpectedly

**Q: The LED shows the wrong color, or every button press shows the wrong color.**

Your board likely has the RGB/GRB channels swapped. This is a hardware variation between WS2812B batches.

**Fix:** Open `led.html` with the adapter connected via USB-C in standalone mode (C64/Amiga disconnected), and change the **LED Format** selector from GRB to RGB (or vice versa), then click SAVE. The LED will immediately show the correct colors after a page refresh.

---

**Q: The LED is always the same color and doesn't change when I press buttons.**

The **Multicolor** option is probably disabled. Open `led.html` and make sure the Multicolor toggle is switched on, then save.

---

## 🕹️ The controller is not recognized

**Q: I connected my USB controller but nothing happens on screen.**

1. Wait a few seconds — the adapter takes a moment to detect and identify the controller.
2. Check the LED: if it shows **2 green blinks** followed by the idle color (orange or white), the controller was detected as a joystick. If it doesn't blink at all, the controller was not recognized at the USB level. Try unplugging and replugging it.
3. If the LED blinks but the C64/Amiga still doesn't respond, the controller may not have a built-in profile. Open `configurator.html` and map it manually — it takes less than a minute.

---

**Q: My controller worked before but stopped working after I updated the firmware.**

The NVS memory (where your custom mapping is saved) survives firmware updates. However, if the internal structure changed, there could be a mismatch. Open `memory.html` and use **Clear → PAD** to erase the saved profile, then remap it via `configurator.html`.

---

**Q: I have a wireless controller with a USB dongle. It's not working.**

Wireless dongles need to be plugged in **after** the adapter has fully booted (LED shows steady orange or white). If you plug the dongle in before the boot sequence completes, the USB host may not initialize it correctly. Unplug the dongle, wait for the idle LED, then plug it back in.

---

**Q: My Xbox controller doesn't work.**

Xbox controllers are currently not supported. This is a known limitation.

---

## 🐭 Mouse issues

**Q: I connected a USB mouse but it's not being detected.**

The adapter will automatically reboot once to switch to mouse mode when it detects a mouse. You will see **2 blue blinks** when detection is complete. If the reboot doesn't happen, unplug the mouse, wait two seconds, and plug it back in.

---

**Q: The mouse cursor moves in the wrong direction or too fast/slow.**

Open `mouse.html` with the adapter in standalone mode (USB-C connected to PC, C64/Amiga disconnected) and adjust the speed settings for C64 or Amiga mode. Save and reconnect.

---

**Q: The mouse works on Amiga but not on C64 (or vice versa).**

Make sure the slide switch on the board is set to the correct position for your machine. Changing the switch while powered will trigger an automatic reboot — this is normal.

---

**Q: I see the orange LED (C64 mode) but I'm using an Amiga.**

The adapter auto-detects the console from the DB9 pull-up voltage. Some Amigas only initialize the joystick port when a game or diagnostic is loaded. In this case, the auto-detection may not fire. Simply flip the slide switch to the Amiga position manually.

---

## 💾 Settings are lost after power off

**Q: Every time I power cycle, my button mapping is gone.**

You are probably using the `configurator.html` page to test the mapping live without saving it. Click the **💾 SAVE** button in the configurator to write the profile to the adapter's permanent memory (NVS). After saving, the profile will survive any power cycle.

---

## 🌐 The Web UI doesn't connect / Serial port not found

**Q: I open the web page but the Connect button doesn't find my adapter.**

- The Web UI requires **Google Chrome** (or a Chromium-based browser). Firefox does not support WebSerial.
- Make sure the adapter is in **standalone mode**: the C64/Amiga must be **disconnected** from the DB9 port. With the console connected, the USB-C port is used as a USB Host for the controller — it cannot be used for configuration at the same time.
- Try a different USB-C cable. Some cables are charge-only and don't carry data.
- On Windows, check Device Manager — the adapter should appear as a COM port. If it doesn't, you may need to install the [CP210x USB to UART driver](https://www.silabs.com/developers/usb-to-uart-bridge-vcp-drivers).

---

**Q: The web page connects but immediately loses the connection.**

This can happen if the adapter is in Developer Mode with a USB controller also connected to the USB-C port. In Developer Mode, the USB-C port is used by the USB Host — the Web UI is not accessible in that configuration. Use the serial terminal (GPIO 43/44) instead, or disable Developer Mode first.

---

## 🔧 How to reset everything to factory defaults

**Q: Something is wrong and I want to start fresh.**

**Option 1 — Via Web UI:** Open `memory.html`, scroll to the Factory Reset section and click **FACTORY RESET**. This erases all saved settings (mapping, LED colors, mouse speed, developer mode) and reboots the adapter.

**Option 2 — Via serial terminal:** Connect via serial (115200 baud), type `service`, then `factory`.

After a factory reset, LED colors return to their defaults, the controller mapping is cleared, and the adapter will be in standard mode.

---

## ⚡ How to update the firmware

**Q: There's a new version — how do I update?**

1. Disconnect the adapter from the C64/Amiga.
2. Hold the **BOOT** button on the ESP32 board, connect the USB-C cable to your PC, wait one second, then release the button.
3. Open the [esptool web flasher](https://espressif.github.io/esptool-js/) in Chrome.
4. Click **Connect**, select the adapter's port, set the Flash Address to `0x0000`, and upload the new `.bin` file.

> ⚠️ Updating the firmware does **not** erase your saved settings. Your controller mapping and LED colors will be preserved.

> ⚠️ If you use Developer Mode and want to upload via the **`flash`** command in the service menu, type `service` → `flash` and the board will reboot directly into programming mode without needing to press the physical BOOT button.

---

## 🆘 Still stuck?

Open an issue on the [GitHub repository](https://github.com/jajpohke/USBtoC64_AMI_CFG_Adv/issues) and include:
- What controller you are using (brand and model)
- What machine you are connecting to (C64, Amiga, which model)
- What LED color you see
- What you already tried
