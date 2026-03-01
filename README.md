### Version 1.1 (need some more tests and love, so consider experimental!) ### 

 This fork has been born mainly to use fire 2 and 3 on C64 and Amiga with configurable buttons. Base board is the same of Main Project.
 Release note!
 
 Change log: 

 
 - v.1.1: Mouse functions implemented; if an mouse is connected it will be autodetected in C64 and Amiga Mode.

 The Aim of this fork is use Original HTML configurator and give precompiled joystick profiles; its also possible to map some joystick like Hori Fighting Stich, WiFiDongle Snes.

🕹️ **Supported Gamepads:** Want to know if your usb game controller is actually preconfigured for plug & play?  
👉 [Click here to quickly consult the list of pre-included, ready-to-use USB controllers](https://github.com/jajpohke/USBtoC64_AMI_CFG_Adv/blob/main/SUPPORTED_PADS.md).
 
 THIS PROJECT IS SPERIMENTAL AND NOT COMMERCIAL SO USE ONLY AT YOUR RISK!!!!! 
 Please refer to original project for more informations <a href="https://github.com/emanuelelaface/USBtoC64/">https://github.com/emanuelelaface/USBtoC64/</a>

 !!!) Must Read me first WARNING:
 - Some controllers may use the USB port to charge a battery (especially if they are also Bluetooth), and this could draw more than 100 mA from the C64, potentially shutting down the Commodore (and possibly damaging it). If you use a controller with a battery, you should remove the battery before connecting it or disable the charging functionality if possible.
   
  - WARNING: DON'T CONNECT THE COMMODORE 64 AND THE USB PORT TO A SOURCE OF POWER AT THE SAME TIME.  
THE POWER WILL ARRIVE DIRECTLY TO THE SID OF THE COMMODORE AND MAY DESTROY IT.

 - THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

 - If you are testing the board standalone on your desk, connect all 4 pins (including 5V) to power the ESP32 from the adapter. 
   However, if your ESP32 is **already plugged into the Commodore 64 / Amiga DB9 port** or powered via its own USB-C port, **DO NOT connect the 5V pin** from the serial adapter to avoid power conflicts. In this case, only     connect TX, RX, and GND.

## Pre-assembled and Tested Board

If you like this project and want a fully assembled and tested board, you can purchase it from its creator, Emanuele, on [Tindie](https://www.tindie.com/products/burglar_ot/usbtoc64/). By doing so, you can also benefit from his customized configuration and support the future development of the project and this current fork.

The PCB are in two versions: THT (version 3.2) and SMD (version 4.1). The functionalities are identical; at the moment the switch implemented in original project is unused since mouse functions are not yet available.

<p align="center">
  <img src="https://github.com/emanuelelaface/USBtoC64/blob/main/images/schematic.jpeg" alt="Schematic" style="width: 50%;">
</p>

<div style="display: flex; justify-content: space-between;">
  <img src="https://github.com/emanuelelaface/USBtoC64/blob/main/images/adapter-smd.JPG" alt="SMD" style="width: 32%;">
</div>


## Components

- **ESP32 S3 Mini**: I use the [Waveshare](https://www.waveshare.com/esp32-s3-zero.htm) version. There are other boards with a similar form factor, but the pinout may be different, which would require redesigning the board.
- **2N3904 NPN transistor**.
- **PCB Slide Switch, 3-pin**.
- **Two 1 kOhm resistors, 1% tolerance**.
- **Two 150 Ohm resistors, 1% tolerance**.
- **One 5.1 kOhm resistor, 1% tolerance**.
- **Two BAT 43 Schottky Diodes**.
- **DE-9 (also known as D-SUB 9 or DB 9) female connector**: It's good practice to remove the metallic enclosure because it can easily short the +5V line of the C64 when inserted, potentially damaging your computer.

## 🔌 Hardware Setup: Connecting the Serial Debugger
To access the advanced **Service Menu**, view live diagnostics, or use the RGB Color Mixer, you must install on your PC Arduino IDE and you need to connect a USB-to-TTL Serial Adapter to the ESP32. 

The adapter officially used and tested for this project is [this FTDI module from Amazon](https://amzn.eu/d/06kwXx67).

### Serial Monitor Comunication Wiring Instructions (optional, but required to user service menu' features).
Connect the adapter to your ESP32 following this pinout (Remember: TX goes to RX, and RX goes to TX):

| USB Serial Adapter | ESP32 Board | Description |
| :--- | :--- | :--- |
| **TX** | **RX (GPIO 44)** | Transmits data to the ESP32 |
| **RX** | **TX (GPIO 43)** | Receives data from the ESP32 |
| **GND** | **GND** | Common Ground (Mandatory) |
| **5V / VCC** | **5V / VIN** | Power Supply (See warning below) |

> **⚠️ Important Power Warning:** > If you are testing the board standalone on your desk, connect all 4 pins (including 5V) to power the ESP32 from the adapter. 
> However, if your ESP32 is **already plugged into the Commodore 64 / Amiga DB9 port** or powered via its own USB-C port, **DO NOT connect the 5V pin** from the serial adapter to avoid power conflicts. In this case, only connect TX, RX, and GND.

### Serial Terminal Settings
Open your favorite Serial Monitor (e.g., Arduino IDE, PuTTY, or Termite) and use the following settings to interact with the Service Menu:
* **Baud Rate:** `115200`
* **Data Bits:** `8`
* **Parity:** `None`
* **Stop Bits:** `1`
* **Line Ending:** `Newline` (`\n`)

## Installation From Arduino IDE

To install the code from the source file **USBtoC64_Amiga_Joy.ino**, you will need the Arduino IDE. Ensure that the ESP32 board is installed, specifically the ESP32S3 Dev Module.  

Additionally, the ESP32 USB HID HOST library is required. This library is not available in the official repository. You can download the ZIP file of the repository from [ESP32_USB_Host_HID](https://github.com/esp32beans/ESP32_USB_Host_HID). To install it, go to `Sketch -> Include Library -> Add .ZIP Library` in the Arduino IDE.

To set the board in upload mode, hold the **BOOT** button while the board is disconnected from the USB port. Then, connect the board to the USB port and after one second, the USB port should appear in the list of ports in the Arduino IDE. You can then upload the code.

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

where `<PORT>` is the USB port created once the board is connected. On Windows, it is probably COM3 or something similar. On Linux and Mac, it will be `/dev/tty.USBsomething` or `/dev/cu.usbsomething`. 
 

# 🕹️ USB to Commodore 64 & Amiga Joystick Adapter (Advanced Edition)

An advanced, high-performance ESP32-S3 firmware to connect modern USB controllers to classic Commodore 64 and Amiga computers. 

This project if a modified version of the  [Original USBtoC64 Project](https://github.com/emanuelelaface/USBtoC64). 

It supercharges the original with:
- you can have multiple joystick configuration without reprogram its logic or recompile again with a HTML generated file;
- use original WebHID HTML configurator and/or use it to create a new internal configuration that will be saved,
- Advanced Diagnostics and utilities **, see service menù.


## 🔌 Hardware Setup & Wiring

For the complete schematic, wiring instructions, and crucial safety precautions (such as using diodes to protect the C64's delicate SID chip), **please refer exclusively to the original project's documentation**: 

👉 **[Original Project Hardware Guide](https://github.com/emanuelelaface/USBtoC64)**


## 🚥 LED Status & Visual Feedback

The WS2812B RGB LED provides instant visual feedback on the adapter's state and your inputs. It's your primary diagnostic tool without needing a PC; if your behaviour is wrong maybe your board has RGB driver swapped with GBR....just change it in global.h

### 1. Boot & Auto-Detection (Idle State)
Once you have finished the assembly, you can use the installed slide switch to select: 
* 🟠 **Orange/Amber:** Detected a **Commodore 64** (just like a yellowed breadbin case).
* ⚪ **White:** Detected an **Amiga** (I know that CD32 and CDTV have a black case but....).

NOTE: A watchdog has been implemeted if you forget C64 mode plugged to Amiga; it will go to Amiga mode. Some Amigas however will trigger this watchdog only when you load a game or a diagnostic that initialize joy1/2 ports; in this scenario watchdog cant work if you plan to use a mouse (you'll see the orange light, so just unplug and move switch to amiga mode and led will become correctly white).
  If you experience issues you can turnoff watchdog from Globals.h.

### 2. Engine Mode Indication
The adapter features two different processing engines, and the LED tells you which one is currently driving your controller:
* 🟢 **Solid Green:** The controller is running via the **HTML WebHID Engine** (`JoystickMapping.h`). 
* 🌈 **Multi-Color Feedback:** The controller has been recognized by the **Native C++ Engine** (`JoystickProfiles.h`). In this mode, the LED is off (`Black`) while idle and changes color instantly based on your inputs.

### 3. Native Engine Color Mapping (Action Feedback)
When using a Native C++ profile, the LED provides zero-latency visual confirmation of your actions. 

*💡 **Customization Tip:** You can easily personalize the LED colors for each individual gamepad! Just open the `JoystickProfiles.h` file and modify the hex color definitions assigned to each specific controller profile.*

| Action | Default LED Color |
| :--- | :--- |
| **Directions** | 🟣 GLowing Purple |
| **FIRE 1** | 🟢 Green |
| **FIRE 2** | 🔴 Red |
| **FIRE 3** | 🩵 Cyan |
| **ALT UP (Jump)** | 🔵 Blue |
| **AUTOFIRE (Active)** | 🟡 Yellow (Blinking on firing rhythm) |
|**MOUSE moves and buttonss** | 🔵 Blue |

*Tip: If you press multiple buttons simultaneously, the LED prioritizes Action Buttons over Directional inputs.*
** Note: some Pads in presets have different colors, link Hori Game Cube Peach USB Controller has everything in pink and variants!

## 💻 The Interactive Service Menu (Serial Console)

Connect the ESP32 to your PC, open a Serial Terminal (115200 baud), and type `service` to access the advanced dashboard.

### 🛠️ Available Service Commands:
* **`new`** - Map a new unknown gamepad manually via wizard or auto-convert an active HTML profile to C++. [[📖 Read more](ServiceMenu.md#new-command)]
* **`raw`** - Displays the raw USB hex data stream coming from the controller. [[📖 Read more](ServiceMenu.md#raw-command)]
* **`test`** - Prints logical button outputs to the screen to verify your current mappings. [[📖 Read more](ServiceMenu.md#test-command)]
* **`lag`** - Starts the hardware latency benchmark to get your controller's exact polling rate (Hz) and input lag (ms). [[📖 Read more](ServiceMenu.md#lag-command)]
* **`gpio`** - Opens a real-time visual dashboard showing the electrical state (HIGH/LOW) of every DB9 pin. [[📖 Read more](ServiceMenu.md#gpio-command)]
* **`mousetest`'**: Mouse speed and Packets"); 
* **`color`** - Opens the Live RGB Color Mixer to tweak system LED colors in real-time using your gamepad. [[📖 Read more](ServiceMenu.md#color-command)]
* **`c64` / `amiga`** - Forces the system into Commodore 64 or Amiga logic mode for bench testing without the physical console. [[📖 Read more](ServiceMenu.md#c64-amiga-commands)]
* **`reboot`** - Soft reboots the ESP32. [[📖 Read more](ServiceMenu.md#reboot-command)]
* **`flash`** - Reboots the ESP32 directly into **DFU/Programming Mode** (No need to press the physical BOOT button on the board!). [[📖 Read more](ServiceMenu.md#flash-command)]
* **`exit`** - Closes the service menu and returns the device to standard Zero-Lag gaming mode. [[📖 Read more](ServiceMenu.md#exit-command)]

---
## 🛠️ How to map a Controller Permanently (Auto-Dump)

1. Connect your unknown USB controller.
2. Use the **[HTML WebHID Configurator](https://raw.githack.com/emanuelelaface/USBtoC64/main/configurator/config.html)** to map your buttons and verify they work perfectly in-game.
3. Open the Serial Monitor and type `new`.
4. The ESP32 will ask: `HTML Profile detected! Do you want to Auto-Import it? (Y/N)`
5. Type `Y`, give your controller a name, and press Enter.
6. Copy the generated C++ block and paste it into the `JoystickProfiles.h` file. (note: JoystickProfiles.h in this fork is just for exampled grabbed from an actual PS4 joypad).
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

* **LED COLORS**
  * Some Boards have different internal RGB driver that can vary beahiours; this can be fixed changing in Globals.h from Adafruit_NeoPixel ws2812b(1, PIN_WS2812B, NEO_RGB + NEO_KHZ800) to Adafruit_NeoPixel ws2812b(1, PIN_WS2812B, NEO_GRB + NEO_KHZ800) and viceversa. 

---

## 🤝 Credits
* Original hardware concept and base firmware by **[Emanuele Laface](https://github.com/emanuelelaface/USBtoC64)**: He did a very deep and professional investigations with SID signals to make the magic works. I hope to not mess up all his work.
* Thanks to:
* - Kenobisboch productions, featuring Andrea Babich in Commodore 64 Advent Show 2025, giving to my mind the existance of a configurable USB2C64 adapters.
* - Davide Bottino: pushing and implementing 2nd button option in his Bubble Bobble Remastered and Lost Cave
* This Advanced fork is the work of **Jahpohke** and **Thelowest** but seriously written with Gemini Pro AI assistance.



*** WARNING: Some controllers may use the USB port to charge a battery (especially if they are also Bluetooth), and this could draw more than 100 mA from the C64, potentially shutting down the Commodore (and possibly damaging it). If you use a controller with a battery, you should remove the battery before connecting it or disable the charging functionality if possible.

---

*** WARNING: DON'T CONNECT THE COMMODORE 64 AND THE USB PORT TO A SOURCE OF POWER AT THE SAME TIME.  
THE POWER WILL ARRIVE DIRECTLY TO THE SID OF THE COMMODORE AND MAY DESTROY IT.

---

** THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

