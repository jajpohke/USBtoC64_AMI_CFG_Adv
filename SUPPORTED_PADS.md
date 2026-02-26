# 🕹️ Supported Joysticks List (Native Engine)

Welcome to the official list of USB controllers tested and natively supported by our Commodore/Amiga adapter! Thanks to our internal engine and advanced sniffer, the adapter can process and translate a huge variety of pads with "zero latency".

---

### ⚠️ Warning: Complex Joypads (e.g., Original PS4)
For some modern controllers with particularly complex protocols or multiple sensors (like the **Original Sony DualShock 4**), the native engine might not correctly interpret all signals. 
Currently, to use these joypads, **you must proceed via the Web HTML Configurator** developed in the original project. 
👉 **[(https://raw.githack.com/emanuelelaface/USBtoC64/main/configurator/config.html)]**

---

### 🎮 Plug & Play Controllers
The following pads are already mapped within the firmware (`JoystickProfiles.h` file). Just plug them in and start playing!

| Controller Name | VID | PID | D-Pad Type | Notes & Special Features |
| :--- | :--- | :--- | :--- | :--- |
| **HORI Mini 4** | `0x0f0d` | `0x00ed` | `BITMASK` | Tested and working |
| **Buffalo Classic** | `0x0583` | `0x2060` | `AXIS` | Pure SNES profile |
| **Sony PS3 Clone** | `2064` | `1` | `AXIS` | Integrated anti-noise support |
| **HoriPad GameCube Pokemon** | `3853` | `220` | `HYBRID` | |
| **HoriPad GameCube Peach** | `3695` | `389` | `EXACT_VALUE` | |
| **SNES Wificlone Dinput** | `10093` | `291` | `AXIS` | |
| **Original PS4** | `1356` | `1476` | `HAT_SWITCH` | *See warning above. Use HTML Config.* |
| **China Arcade PS3 PC** | `2064` | `3` | `AXIS` | Perfect for arcade cabinets |
| **Zero Lag China** | `121` | `6` | `AXIS` | |
| **NES2USB RetroBit** | `4754` | `17987` | `BITMASK` | Optimized profile to fix the silent diagonals bug |
| **USB2SNES Mayflower** | `3727` | `12307` | `AXIS` | **Smart Multiport:** Supports two SNES pads simultaneously (Co-Pilot Mode) and filters false contacts from the internal multiplexer |

---

### 🛠️ Is your joypad not listed? Use the Sniffer!
No problem. We have developed an **Advanced Sniffer (v2.4.2)** directly onboard the ESP32. 
If you connect an unknown joypad and open the serial terminal, the adapter will guide you step-by-step to press the buttons. Our system:
1. Automatically isolates background "noise" and jittery axes.
2. Identifies and neutralizes dual-port multiplexed pads.
3. Upon completion, it **automatically generates the C++ code** ready to be copy-pasted into the `JoystickProfiles.h` file to make your pad supported for life!
