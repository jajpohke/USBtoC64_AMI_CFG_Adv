### Sperimental ### 

 This fork is about use fire 2 and 3 on C64 and Amiga. Base board is the same.
 Mouse funcions are at the moment non implemented.
 The Aim of this fork is use Original HTML configurator and give precompiled joystick profiles; its also possible to map some joystick like Hori Fighting Stich, WiFiDongle Snes.
 THIS PROCJECT IS SPERIMENTAL AND NOT COMMERCIAL SO USE ONLY AT YOUR RISK!!!!! Please refear to original prokect for more informations <a href="https://github.com/emanuelelaface/USBtoC64/">https://github.com/emanuelelaface/USBtoC64/</a>


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

---

## Joystick Configuration



you can discover how your values are mapped following this [Configurator](https://raw.githack.com/emanuelelaface/USBtoC64/main/configurator/config.html) (it works on Chrome and similar browsers, not on Firefox) and once you download the JoystickMapping.h file you can code your controls directly in the firmware source replacing the example JoystickMapping.h file and upload to your controller.

If you connect your ESP to SerialMonitor and type "service" then the following dialog txt will provide some features:
- 'sniffer'  : Map a new pad via wizard OR Auto-Convert HTML
- 'raw'      : Show raw USB matrix
- 'debug'    : Print logical buttons to screen
- 'gpio'     : Real-time visual dashboard of hardware states
- 'polling'  : Measure USB Polling Rate (Input Lag)
- 'play'     : Return to ZERO-LAG gaming
- 'c64'      : Set Fire 2 to POT (Default) (for bench test or Amiga disconnected)
- 'amiga'    : Set Fire 2 to Pin 9 (for bench test or C64 disconnected)
- 'reboot'   : Restart the device softly
- 'flash'    : Reboot into Programming/DFU Mode




---

WARNING: Some controllers may use the USB port to charge a battery (especially if they are also Bluetooth), and this could draw more than 100 mA from the C64, potentially shutting down the Commodore (and possibly damaging it). If you use a controller with a battery, you should remove the battery before connecting it or disable the charging functionality if possible.

---

WARNING: DON'T CONNECT THE COMMODORE 64 AND THE USB PORT TO A SOURCE OF POWER AT THE SAME TIME.  
THE POWER WILL ARRIVE DIRECTLY TO THE SID OF THE COMMODORE AND MAY DESTROY IT.

---

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

