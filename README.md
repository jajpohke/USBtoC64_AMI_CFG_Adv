### Sperimental ### 

 This fork is about use fire 2 and 3 on C64 and Amiga. Base board is the same.
 Mouse funcions are at the moment non implemented.
 The Aim of this fork is use Original HTML configurator and give precompiled joystick profiles; its also possible to map some joystick like Hori Fighting Stich, WiFiDongle Snes.
 THIS PROJECT IS SPERIMENTAL AND NOT COMMERCIAL SO USE ONLY AT YOUR RISK!!!!! Please refear to original prokect for more informations <a href="https://github.com/emanuelelaface/USBtoC64/">https://github.com/emanuelelaface/USBtoC64/</a>

## Pre-assembled and Tested Board

<div style="display: flex; justify-content: space-between;">
  <a href="https://www.tindie.com/products/burglar_ot/usbtoc64/"><img src="https://github.com/emanuelelaface/USBtoC64/blob/main/images/tindie-logo.png" alt="Tindie Logo Link" width="150" height="78"></a>
</div>

If you like this project and want a fully assembled and tested board, you can purchase it on [Tindie](https://www.tindie.com/products/burglar_ot/usbtoc64/). By doing so, you can also benefit from a customized configuration and support the future development of the project.

## Components

- **ESP32 S3 Mini**: I use the [Waveshare](https://www.waveshare.com/esp32-s3-zero.htm) version. There are other boards with a similar form factor, but the pinout may be different, which would require redesigning the board.
- **2N3904 NPN transistor**.
- **PCB Slide Switch, 3-pin**.
- **Two 1 kOhm resistors, 1% tolerance**.
- **Two 150 Ohm resistors, 1% tolerance**.
- **One 5.1 kOhm resistor, 1% tolerance**.
- **Two BAT 43 Schottky Diodes**.
- **DE-9 (also known as D-SUB 9 or DB 9) female connector**: It's good practice to remove the metallic enclosure because it can easily short the +5V line of the C64 when inserted, potentially damaging your computer.

---

## Installation From Arduino IDE

To install the code from the source file **USBtoC64_Amiga_Joy.ino**, you will need the Arduino IDE. Ensure that the ESP32 board is installed, specifically the ESP32S3 Dev Module.  

Additionally, the ESP32 USB HID HOST library is required. This library is not available in the official repository. You can download the ZIP file of the repository from [ESP32_USB_Host_HID](https://github.com/esp32beans/ESP32_USB_Host_HID). To install it, go to `Sketch -> Include Library -> Add .ZIP Library` in the Arduino IDE.

To set the board in upload mode, hold the **BOOT** button while the board is disconnected from the USB port. Then, connect the board to the USB port and after one second, the USB port should appear in the list of ports in the Arduino IDE. You can then upload the code.

To select PAL or NTSC timing the `#define PAL` line has to be set as true or false.

## Installation From the Binary File

Alternatively, you can load the binary file **USBtoC64_Amiga_Joy.bin** located in the `BIN` folder.
The tool to upload the binary is `esptool`. This is available as a web page or as python. The web page should be compatible with Chrome browser or similar, probably not with Firefox, but on some operating system (like Mac OS) there can be a problem of binding the port to the web page. Anyway my suggestion is to try the web page first because it is very fast, and if it does not work try with the python installation.

### From the web page

1. Disconnect the adapter from the Commodore 64 / AMIGA.
2. Press and hold the **BOOT** button before connecting the board to the USB cable on the computer. Then, connect the board, wait a second, and release the button.
3. Go to the [esptool](https://espressif.github.io/esptool-js/) website, click on **Connect**, select the port for your adapter, change the Flash Address into `0x0000` and upload the firmware.

### From Python

1. Install the esptool with `pip install esptool`.
2. Disconnect the adapter from the Commodore 64.
3. Press and hold the **BOOT** button before connecting the board to the USB cable on the computer. Then, connect the board, wait a second, and release the button.
4. On the computer, run:

   `esptool.py -b 921600 -c esp32s3 -p <PORT> write_flash --flash_freq 80m 0x00000 USBtoC64_Amiga_Joy.bin`
 

# 🕹️ USB to Commodore 64 & Amiga Joystick Adapter (Advanced Edition)

An advanced, high-performance ESP32-S3 firmware to connect modern USB controllers to classic Commodore 64 and Amiga computers. 

This project is an advanced fork of the excellent [Original USBtoC64 Project](https://github.com/emanuelelaface/USBtoC64). It supercharges the original WebHID HTML configurator concept by introducing a **Zero-Lag Native C++ Engine**, **Advanced Diagnostics**, and a **Smart Auto-Dumper**.

## ✨ Advanced "Killer" Features

* 🚀 **Zero-Lag Hybrid Engine:** Use the flexible HTML Web configurator to test unknown pads, or rely on the blazing-fast Native C++ Engine for permanently mapped controllers.
* 🧠 **Smart Auto-Dumper:** Created a great profile via the Web HTML tool? Open the serial monitor, type `sniffer`, and the ESP32 will automatically translate your HTML rules into pure C++ code ready to be pasted into the Native Engine!
* ⏱️ **Polling Rate Tester:** Ever wonder if your cheap USB pad is causing input lag? Type `polling` in the serial monitor to run a 3-second hardware benchmark. The ESP32 will calculate the exact Polling Rate (Hz) and input latency (ms) of your controller.
* 🎮 **Dual-Analog & Hat-Switch Support:** Fully supports modern controllers (PS3/PS4/GameCube clones), mapping both left/right analog sticks and complex D-Pads simultaneously.

---

## 🔌 Hardware Setup & Wiring

For the complete schematic, wiring instructions, and crucial safety precautions (such as using diodes to protect the C64's delicate SID chip), **please refer exclusively to the original project's documentation**: 

👉 **[Original Project Hardware Guide](https://github.com/emanuelelaface/USBtoC64)**

---

## 🚥 LED Status & Visual Feedback

The WS2812B RGB LED provides instant visual feedback on the adapter's state and your inputs. It's your primary diagnostic tool without needing a PC.

### 1. Boot & Auto-Detection (Idle State)
Once you have finished the assembly, you can instantly verify if the firmware and auto-detection are working correctly. Power on your classic computer **without connecting any USB joystick yet**:
* 🟠 **Orange/Amber:** Detected a **Commodore 64** (Breadbin style).
* ⚪ **White:** Detected an **Amiga**.

### 2. Engine Mode Indication
The adapter features two different processing engines, and the LED tells you which one is currently driving your controller:
* 🟢 **Solid Green (`0, 100, 0`):** The controller is running via the **HTML WebHID Engine** (`JoystickMapping.h`). 
* 🌈 **Multi-Color Feedback:** The controller has been recognized by the **Native C++ Engine** (`JoystickProfiles.h`). In this mode, the LED is off (`Black`) while idle and changes color instantly based on your inputs.

### 3. Native Engine Color Mapping (Action Feedback)
When using a Native C++ profile, the LED provides zero-latency visual confirmation of your actions. 

*💡 **Customization Tip:** You can easily personalize the LED colors for each individual gamepad! Just open the `JoystickProfiles.h` file and modify the hex color definitions assigned to each specific controller profile.*

| Action | Default LED Color |
| :--- | :--- |
| **UP** | 🟣 Bright Purple |
| **RIGHT** | 🟣 Purple |
| **LEFT** | 🟣 Dark Purple |
| **DOWN** | 🟣 Very Dark Purple |
| **FIRE 1** | 🟢 Green |
| **FIRE 2** | 🔴 Red |
| **FIRE 3** | 🩵 Cyan |
| **ALT UP (Jump)** | 🔵 Blue |
| **AUTOFIRE (Active)** | 🟡 Yellow (Blinking on firing rhythm) |

*Tip: If you press multiple buttons simultaneously, the LED prioritizes Action Buttons over Directional inputs.*

## 💻 The Interactive Service Menu (Serial Console)

Connect the ESP32 to your PC, open a Serial Terminal (115200 baud), and type `service` to access the advanced dashboard.

### Available Commands:
* `sniffer` - **The Magic trial Tool.** If you have an HTML profile active, it auto-converts it to C++. If no profile is active, it starts a step-by-step wizard to map a new unknown gamepad manually.
* `polling` - Starts the hardware latency benchmark. Move the sticks for 3 seconds to get your controller's exact Hz and ms rating.
* `gpio` - Opens a real-time visual dashboard showing the electrical state (HIGH/LOW) of every single DB9 pin.
* `debug` - Prints logical button presses to the screen (useful for testing mappings).
* `play` - Returns the device to standard Zero-Lag gaming mode.
* `reboot` - Soft reboots the ESP32.
* `flash` - Reboots the ESP32 directly into **DFU/Programming Mode** (No need to press the physical BOOT button on the board!).

---
## 🛠️ How to map a Controller Permanently (Auto-Dump)

1. Connect your unknown USB controller.
2. Use the **[HTML WebHID Configurator](https://raw.githack.com/emanuelelaface/USBtoC64/main/configurator/config.html)** to map your buttons and verify they work perfectly in-game.
3. Open the Serial Monitor and type `sniffer`.
4. The ESP32 will ask: `HTML Profile detected! Do you want to Auto-Import it? (Y/N)`
5. Type `Y`, give your controller a name, and press Enter.
6. Copy the generated C++ block and paste it into the `JoystickProfiles.h` file.
7. Recompile. Your controller is now running on the pure Native Engine!

---

## ⚠️ Known Issues & Hardware Quirks

When dealing with a vast array of third-party USB controllers, some specific devices may exhibit unusual initialization behaviors due to their internal hardware design. Here are the known quirks and their simple workarounds:

* **Hori Mini Fighting Stick (PS3/PS4):**
  * **The Issue:** If plugged in before powering on the ESP32, or if cold-booted, the stick might not be recognized or might fail to send inputs.
  * **The Fix:** Simply press the physical **RESET** button on the ESP32 board *after* the controller is plugged in, or quickly unplug and re-plug the controller's USB cable while the system is already powered on.

* **SNES Pad Clones with 2.4GHz Wi-Fi/Wireless Dongles:**
  * **The Issue:** Some cheap wireless clones fail to initialize their USB descriptor correctly if their proprietary USB receiver (dongle) is plugged into the adapter *before* the system is powered up. The ESP32's USB Host might get stuck waiting for a response that never arrives.
  * **The Fix:** Always turn on your C64/Amiga and wait for the ESP32 to fully boot (the LED will show the Orange or White idle state). **Only after the boot sequence is complete**, plug the proprietary USB dongle into the adapter.
 
* **Xbox One Controller not supported ATM**

---

## 🤝 Credits
* Original hardware concept and base firmware by **[Emanuele Laface](https://github.com/emanuelelaface/USBtoC64)**.
* This Advanced fork (Native Engine, Auto-Dumper, and Polling Diagnostics) is the work of **Jahpohke** and **Thelowest**.

## Pre-assembled and Tested Board

<div style="display: flex; justify-content: space-between;">
  <a href="https://www.tindie.com/products/burglar_ot/usbtoc64/"><img src="https://github.com/emanuelelaface/USBtoC64/blob/main/images/tindie-logo.png" alt="Tindie Logo Link" width="150" height="78"></a>
</div>

If you like this project and want a fully assembled and tested board, you can purchase it on [Tindie](https://www.tindie.com/products/burglar_ot/usbtoc64/). By doing so, you can also benefit from a customized configuration and support the future development of the project.

## Components

- **ESP32 S3 Mini**: I use the [Waveshare](https://www.waveshare.com/esp32-s3-zero.htm) version. There are other boards with a similar form factor, but the pinout may be different, which would require redesigning the board.
- **2N3904 NPN transistor**.
- **PCB Slide Switch, 3-pin**.
- **Two 1 kOhm resistors, 1% tolerance**.
- **Two 150 Ohm resistors, 1% tolerance**.
- **One 5.1 kOhm resistor, 1% tolerance**.
- **Two BAT 43 Schottky Diodes**.
- **DE-9 (also known as D-SUB 9 or DB 9) female connector**: It's good practice to remove the metallic enclosure because it can easily short the +5V line of the C64 when inserted, potentially damaging your computer.

---

## Installation From Arduino IDE

To install the code from the source file **USBtoC64.ino**, you will need the Arduino IDE. Ensure that the ESP32 board is installed, specifically the ESP32S3 Dev Module.  
The flag `PAL` is for C64 PAL / NTSC selection.

Additionally, the ESP32 USB HID HOST library is required. This library is not available in the official repository. You can download the ZIP file of the repository from [ESP32_USB_Host_HID](https://github.com/esp32beans/ESP32_USB_Host_HID). To install it, go to `Sketch -> Include Library -> Add .ZIP Library` in the Arduino IDE.

To set the board in upload mode, hold the **BOOT** button while the board is disconnected from the USB port. Then, connect the board to the USB port and after one second, the USB port should appear in the list of ports in the Arduino IDE. You can then upload the code.

To select PAL or NTSC timing the `#define PAL` line has to be set as true or false.

## Installation From the Binary File

Alternatively, you can load the binary file **USBtoC64_Amiga_Joy.bin** located in the `BIN` folder.
The tool to upload the binary is `esptool`. This is available as a web page or as python. The web page should be compatible with Chrome browser or similar, probably not with Firefox, but on some operating system (like Mac OS) there can be a problem of binding the port to the web page. Anyway my suggestion is to try the web page first because it is very fast, and if it does not work try with the python installation.

### From the web page

1. Disconnect the adapter from the Commodore 64 / AMIGA.
2. Press and hold the **BOOT** button before connecting the board to the USB cable on the computer. Then, connect the board, wait a second, and release the button.
3. Go to the [esptool](https://espressif.github.io/esptool-js/) website, click on **Connect**, select the port for your adapter, change the Flash Address into `0x0000` and upload the firmware.

### From Python

1. Install the esptool with `pip install esptool`.
2. Disconnect the adapter from the Commodore 64.
3. Press and hold the **BOOT** button before connecting the board to the USB cable on the computer. Then, connect the board, wait a second, and release the button.
4. On the computer, run:

   `esptool.py -b 921600 -c esp32s3 -p <PORT> write_flash --flash_freq 80m 0x00000 USBtoC64_Amiga_Joy.bin`

   where `<PORT>` is the USB port created once the board is connected. On Windows, it is probably COM3 or something similar. On Linux and Mac, it will be `/dev/tty.USBsomething` or `/dev/cu.usbsomething`. `<PAL|NTSC>` is the version with the timings for PAL or for NTSC version of the Commodore 64.


WARNING: Some controllers may use the USB port to charge a battery (especially if they are also Bluetooth), and this could draw more than 100 mA from the C64, potentially shutting down the Commodore (and possibly damaging it). If you use a controller with a battery, you should remove the battery before connecting it or disable the charging functionality if possible.

---

WARNING: DON'T CONNECT THE COMMODORE 64 AND THE USB PORT TO A SOURCE OF POWER AT THE SAME TIME.  
THE POWER WILL ARRIVE DIRECTLY TO THE SID OF THE COMMODORE AND MAY DESTROY IT.

---

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

