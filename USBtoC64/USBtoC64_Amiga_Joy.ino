// ==========================================
// USB to C64/Amiga Adapter - Advanced 2.8
// File: USBtoC64_Amiga_Joy.ino
// Description: Main Application and Service Menu
// ==========================================
#include <Arduino.h>
#include <stdint.h>
#include "soc/rtc_cntl_reg.h" 

// --- Custom Modular Architecture ---
#include "Globals.h"
#include "Hardware.h"
#include "AutoDumper.h"
#include "JoystickEngine.h"

// 🖥️ --- USB HOST CALLBACKS & SETUP --- 🖥️
static void in_transfer_cb(usb_transfer_t *xfer) {
    if (xfer->status == USB_TRANSFER_STATUS_COMPLETED) {
        pkt_t p;
        p.len = xfer->actual_num_bytes;
        memcpy(p.data, xfer->data_buffer, p.len > 64 ? 64 : p.len);
        xQueueSendFromISR(s_pkt_q, &p, nullptr);
        usb_host_transfer_submit(xfer);
    }
}

void start_sniff(uint8_t addr) {
    if (device_connected) return;
    if (usb_host_device_open(s_client, addr, &s_dev) != ESP_OK) return;
    
    const usb_device_desc_t *dev_desc;
    usb_host_get_device_descriptor(s_dev, &dev_desc);

    connected_vid = dev_desc->idVendor; connected_pid = dev_desc->idProduct;
    
    bool found_internal = false;
    for (int i = 0; i < NUM_PROFILES; i++) {
        if (connected_vid == PROFILES[i].vid && connected_pid == PROFILES[i].pid) {
            current_profile = PROFILES[i];
            found_internal = true; break;
        }
    }
    
    Serial2.printf("\n*** CONNECTED: %s (VID:%04x PID:%04x) ***\n", 
                   found_internal ? current_profile.name : "UNKNOWN PAD", 
                   connected_vid, connected_pid);

#if HAS_HTML_CONFIGURATOR
    if (found_internal) {
        use_html_configurator = false;
        Serial2.println(">>> ⚙️ Internal Profile Matched! Ignoring HTML Configurator for this pad. <<<");
    } else {
        use_html_configurator = true;
        Serial2.println(">>> 🟢 Unknown Pad: Using HTML HID Configurator Profile! <<<");
    }
#else
    if (!found_internal) current_profile = PROFILES[1];
    use_html_configurator = false;
    Serial2.println(">>> ⚙️ Internal Native Engine Active! <<<");
#endif

    const usb_config_desc_t *cfg_desc;
    usb_host_get_active_config_descriptor(s_dev, &cfg_desc);
    int offset = 0; const usb_standard_desc_t *next_desc = (const usb_standard_desc_t *)cfg_desc;
    while (next_desc) {
        if (next_desc->bDescriptorType == USB_B_DESCRIPTOR_TYPE_INTERFACE) s_if_num = ((const usb_intf_desc_t *)next_desc)->bInterfaceNumber;
        if (next_desc->bDescriptorType == USB_B_DESCRIPTOR_TYPE_ENDPOINT) {
            const usb_ep_desc_t *ep = (const usb_ep_desc_t *)next_desc;
            if ((ep->bmAttributes & 0x03) == 0x03 && (ep->bEndpointAddress & 0x80)) {
                s_in_ep = ep->bEndpointAddress;
                s_in_mps = ep->wMaxPacketSize; break; 
            }
        }
        next_desc = usb_parse_next_descriptor(next_desc, cfg_desc->wTotalLength, &offset);
    }
    
    if (s_in_ep) {
        usb_host_interface_claim(s_client, s_dev, s_if_num, 0);
        usb_host_transfer_alloc(s_in_mps, 0, &s_in_xfer);
        s_in_xfer->device_handle = s_dev; s_in_xfer->callback = in_transfer_cb;
        s_in_xfer->bEndpointAddress = s_in_ep; s_in_xfer->num_bytes = s_in_mps;
        usb_host_transfer_submit(s_in_xfer); device_connected = true;
    }
}

static void client_event_cb(const usb_host_client_event_msg_t *msg, void *arg) {
    if (msg->event == USB_HOST_CLIENT_EVENT_NEW_DEV) s_new_dev_addr = msg->new_dev.address;
}

void usb_lib_task(void *arg) { 
    while (1) { uint32_t f; usb_host_lib_handle_events(portMAX_DELAY, &f); } 
}

// 🚀 --- MAIN SETUP --- 🚀
void setup() {
    Serial2.begin(115200, SERIAL_8N1, GP_RX, GP_TX);
    
    delay(1000); 
    Serial2.println("USB Initialization...");

    pinMode(19, OUTPUT); pinMode(20, OUTPUT);
    digitalWrite(19, LOW); digitalWrite(20, LOW);
    delay(200);
    
    pinMode(19, INPUT); pinMode(20, INPUT);
    delay(100);

    pinMode(GP_FIRE2, INPUT); 
    delay(50);
    
    bool avvio_amiga = (digitalRead(GP_FIRE2) == HIGH);
    
    Serial2.println("\n=================================");
    Serial2.println("     USB -> DB9 ADAPTER v2.8     ");
    Serial2.println("=================================");
    configure_console_mode(avvio_amiga); 
    
    Serial2.println(">> Type 'service' to open the advanced menu <<\n");
    
    ws2812b.begin(); 
    ws2812b.setBrightness(40);
    ws2812b.show();

    set_joy_pin(GP_UP, false); set_joy_pin(GP_DOWN, false);
    set_joy_pin(GP_LEFT, false); set_joy_pin(GP_RIGHT, false);
    set_joy_pin(GP_FIRE1, false); set_joy_pin(GP_FIRE2, false);
    set_fire3_pin(false); 
    
    s_pkt_q = xQueueCreate(16, sizeof(pkt_t));
    usb_host_config_t host_cfg = { .skip_phy_setup = false, .intr_flags = ESP_INTR_FLAG_LEVEL1 };
    usb_host_install(&host_cfg);
    xTaskCreatePinnedToCore(usb_lib_task, "usb_lib", 4096, nullptr, 10, nullptr, 0);
    usb_host_client_config_t client_cfg = { .is_synchronous = false, .max_num_event_msg = 5, .async = { .client_event_callback = client_event_cb, .callback_arg = nullptr } };
    usb_host_client_register(&client_cfg, &s_client);
}

// 🔄 --- MAIN LOOP --- 🔄
void loop() {
    
    if (Serial2.available() > 0) {
        String input = Serial2.readStringUntil('\n');
        input.trim(); 
        
        // 🗣️ --- INTERACTIVE SERIAL PROMPTS --- 🗣️
        if (cmd_state == CMD_WAIT_COLOR_CHOICE) {
            if (input == "exit") {
                cmd_state = CMD_IDLE;
                current_mode = MODE_SERVICE;
                ws2812b.setBrightness(40);
                Serial2.println("\n>> Exited Color Test. Returned to Service Menu.");
            } else {
                int choice = input.toInt();
                if (choice >= 1 && choice <= 13) {
                    
                    cmd_state = CMD_IDLE; // <-- FIX: Sblocca la lettura del joystick
                    
                    switch(choice) {
                        case 1:  mix_r = 255; mix_g = 255; mix_b = 255; break; // Amiga Idle
                        case 2:  mix_r = 255; mix_g = 128; mix_b = 0;   break; // C64 Idle
                        case 3:  mix_r = 0;   mix_g = 100; mix_b = 0;   break; // HTML Config
                        case 4:  mix_r = 180; mix_g = 0;   mix_b = 255; break; // D-Pad Up
                        case 5:  mix_r = 120; mix_g = 0;   mix_b = 180; break; // D-Pad Right
                        case 6:  mix_r = 20;  mix_g = 0;   mix_b = 40;  break; // D-Pad Down
                        case 7:  mix_r = 60;  mix_g = 0;   mix_b = 100; break; // D-Pad Left
                        case 8:  mix_r = 5;   mix_g = 0;   mix_b = 8;   break; // D-Pad Idle
                        case 9:  mix_r = 0;   mix_g = 255; mix_b = 0;   break; // Fire 1
                        case 10: mix_r = 255; mix_g = 0;   mix_b = 0;   break; // Fire 2
                        case 11: mix_r = 0;   mix_g = 255; mix_b = 255; break; // Fire 3
                        case 12: mix_r = 0;   mix_g = 0;   mix_b = 255; break; // Up Alt
                        case 13: mix_r = 255; mix_g = 255; mix_b = 0;   break; // Autofire
                    }
                    ws2812b.setPixelColor(0, mix_r, mix_g, mix_b);
                    ws2812b.show();
                    Serial2.printf("\n>>> COLOR SET! Base Values -> R:%d, G:%d, B:%d\n", mix_r, mix_g, mix_b);
                    Serial2.println(">>> LIVE TWEAK ACTIVE: Move Joystick UP/DOWN for brightness, FIRE 1 to switch RGB, LEFT/RIGHT to change color.");
                } else {
                    Serial2.println(">>> Invalid choice. Enter a number between 1 and 13, or 'exit'.");
                }
            }
            return;
        }
        else if (cmd_state == CMD_WAIT_IMPORT) {
            String ans = input; ans.toLowerCase();
            if (ans == "y" || ans == "yes") {
                Serial2.print(">>> Enter a NAME for this profile: ");
                cmd_state = CMD_WAIT_NAME_IMPORT;
            } else {
                Serial2.print(">>> Enter a NAME for the new manual profile: ");
                cmd_state = CMD_WAIT_NAME_MANUAL;
            }
            return;
        } 
        else if (cmd_state == CMD_WAIT_NAME_IMPORT) {
            sniff_profile_name = input;
            cmd_state = CMD_IDLE;
            execute_html_dump(); 
            return;
        } 
        else if (cmd_state == CMD_WAIT_NAME_MANUAL) {
            sniff_profile_name = input;
            cmd_state = CMD_IDLE;
            current_mode = MODE_SNIFFER; 
            reset_sniffer(); 
            xQueueReset(s_pkt_q); 
            Serial2.println("\n>>> WIZARD ARMED! <<<");
            Serial2.println("⏳ Waiting for neutral position calibration... (DO NOT touch the gamepad)");
            Serial2.println("If nothing happens within 2 seconds, press and release a button to 'wake' it.");
            return;
        }

        // --- NORMAL COMMAND PARSER ---
        String comando = input; comando.toLowerCase();

if (comando == "service") {
            current_mode = MODE_SERVICE;
            Serial2.println("\n=== 🛠️  SERVICE MENU  🛠️ ===");
            Serial2.printf("  ⚙️  ENGINE: %s\n", use_html_configurator ? "HTML HID Configurator" : "Internal Profiler");
            Serial2.println("--------------------------------");
            Serial2.println(" 🪄 'new'     : Map a new pad or Auto-Import HTML");
            Serial2.println(" 👁️ 'raw'     : Show raw USB hex data stream");
            Serial2.println(" 🎮 'test'    : Test logical buttons mapping (Up, Fire...)");
            Serial2.println(" ⏱️ 'lag'     : Measure USB Polling Rate and Input Lag");
            Serial2.println(" 🎛️ 'gpio'    : Real-time dashboard of hardware states");
            Serial2.println(" 🟠 'c64'     : Force C64 mode (Bench testing only)");
            Serial2.println(" ⚪ 'amiga'   : Force Amiga mode (Bench testing only)");
            Serial2.println(" 🎨 'color'   : Live RGB Color Mixer (Use gamepad)");
            Serial2.println(" 🔄 'reboot'  : Restart the device softly");
            Serial2.println(" ⚡ 'flash'   : Reboot into Programming/DFU Mode");
            Serial2.println(" 🚪 'exit'    : Exit menu and return to normal play");
            Serial2.println("================================\n");
        }
        else if (current_mode != MODE_PLAY || comando == "exit") {
            if (comando == "new") { 
                if (device_connected && use_html_configurator) {
                    Serial2.println("\n>>> HTML Profile detected! Do you want to Auto-Import it? (Y/N)");
                    cmd_state = CMD_WAIT_IMPORT;
                } else {
                    Serial2.print("\n>>> Enter a NAME for the new manual profile: ");
                    cmd_state = CMD_WAIT_NAME_MANUAL;
                }
            }
            else if (comando == "raw")   { current_mode = MODE_RAW; Serial2.println(">>> RAW mode active!"); }
            else if (comando == "test")  { current_mode = MODE_DEBUG; Serial2.println(">>> TEST mode active!"); }
            else if (comando == "gpio")  { 
                current_mode = MODE_GPIO; last_gpio_state = 0xFFFF; 
                Serial2.println(">>> Starting GPIO Dashboard..."); 
            }
            else if (comando == "lag") {
                current_mode = MODE_POLLING;
                polling_packet_count = 0;
                polling_start_time = 0; 
                polling_neutral_saved = false; 
                polling_active = true;
                
                Serial2.println("\n>>> Starting Polling Tester (Smart Trigger)... <<<");
                Serial2.printf("Connected device: %s (VID:%04x PID:%04x)\n\n", device_connected ? (use_html_configurator ? "HTML Config Pad" : current_profile.name) : "None", connected_vid, connected_pid);
                if (!device_connected) {
                    Serial2.println("⚠️ No gamepad connected! Connect a pad and try again.");
                    current_mode = MODE_SERVICE;
                } else {
                    Serial2.println("🕹️  Timer is waiting... Move the stick and press buttons to trigger it!");
                }
            }
            else if (comando == "color") {
                current_mode = MODE_COLOR_MIXER;
                cmd_state = CMD_WAIT_COLOR_CHOICE;
                
                Serial2.println("\n=== LED COLOR TEST MENU ===");
                Serial2.println("Select a color to test on your WS2812B:\n");
                
                Serial2.println("[ SYSTEM STATES ]");
                Serial2.println("1.  Amiga Idle     (White)");
                Serial2.println("2.  C64 Idle       (Orange)");
                Serial2.println("3.  HTML Config    (Green)\n");
                
                Serial2.println("[ DIRECTIONAL PAD ]");
                Serial2.println("4.  D-Pad Up       (Bright Purple)");
                Serial2.println("5.  D-Pad Right    (Purple)");
                Serial2.println("6.  D-Pad Down     (Very Dark Purple)");
                Serial2.println("7.  D-Pad Left     (Dark Purple)");
                Serial2.println("8.  D-Pad Idle     (Dark)\n");
                
                Serial2.println("[ ACTION BUTTONS ]");
                Serial2.println("9.  Fire 1         (Green)");
                Serial2.println("10. Fire 2         (Red)");
                Serial2.println("11. Fire 3         (Cyan)");
                Serial2.println("12. Up Alt         (Blue)");
                Serial2.println("13. Autofire       (Yellow)\n");
                
                Serial2.println("Type a number (1-13) to set the color.");
                Serial2.println("Type 'exit' to return to the Service Menu.\n");
                
                Serial2.println("[!] LIVE BRIGHTNESS CONTROL:");
                Serial2.println("Move your connected gamepad UP or DOWN to adjust the LED brightness dynamically!");
                Serial2.println("[!] LIVE RGB COLOR TWEAKING:");
                Serial2.println("Press FIRE 1 to switch between R, G, B channels, and move LEFT or RIGHT to change the color value!");
                Serial2.println("Press FIRE 2 to print the final C++ code.");
                Serial2.println("===========================");
            }
            else if (comando == "exit")  { 
                current_mode = MODE_PLAY; 
                ws2812b.setBrightness(40); 
                Serial2.println("\n\n>>> PLAY mode (Zero-Lag) restored! Normal operation resumed. <<<"); 
            }
            else if (comando == "amiga") { configure_console_mode(true); }
            else if (comando == "c64")   { configure_console_mode(false); }
            else if (comando == "reboot") {
                Serial2.println("\n>>> REBOOTING DEVICE... <<<");
                delay(500);
                ESP.restart();
            }
            else if (comando == "flash") {
                Serial2.println("\n>>> REBOOTING INTO PROGRAMMING/DFU Mode... <<<");
                delay(500);
                REG_WRITE(RTC_CNTL_OPTION1_REG, RTC_CNTL_FORCE_DOWNLOAD_BOOT);
                ESP.restart();
            }
        }
    }

    if (current_mode == MODE_POLLING && polling_active && polling_start_time > 0) {
        unsigned long elapsed = millis() - polling_start_time;
        static unsigned long last_dot = 0;
        if (millis() - last_dot > 500) { Serial2.print("."); last_dot = millis(); }

        if (elapsed >= 3000) { 
            polling_active = false;
            Serial2.println(" 100%");
            
            float hz = (float)polling_packet_count / 3.0; 
            float ms_delay = hz > 0 ? (1000.0 / hz) : 0;  
            
            Serial2.println("\n=== TEST RESULT ===");
            Serial2.printf("Packets received: %d\n", polling_packet_count);
            Serial2.printf("Average interval: %.2f ms\n", ms_delay);
            Serial2.printf("Polling Rate    : ~%.0f Hz\n\n", hz);
            
            if (hz >= 400) {
                Serial2.println("[ RATING: EXCELLENT 🟢 ]");
                Serial2.println("Perfect for pure retrogaming. Input lag is under 2.5ms (imperceptible).");
            } else if (hz >= 200) {
                Serial2.println("[ RATING: GOOD 🟡 ]");
                Serial2.println("Great response, latency under 5ms. Ideal for most games.");
            } else if (hz >= 100) {
                Serial2.println("[ RATING: ACCEPTABLE 🟠 ]");
                Serial2.println("Standard pad (latency ~8ms). Fine for RPGs/Adventures.");
            } else {
                Serial2.println("[ RATING: POOR 🔴 ]");
                Serial2.println("High latency (over 10ms). Not recommended for Action/Fighting games.");
            }
            Serial2.println("======================");
            Serial2.println(">> Returning to PLAY mode...\n");
            current_mode = MODE_PLAY; 
        }
    }

    usb_host_client_handle_events(s_client, 1);
    if (s_new_dev_addr) { uint8_t a = s_new_dev_addr; s_new_dev_addr = 0; start_sniff(a); }
    pkt_t p;
    if (xQueueReceive(s_pkt_q, &p, 1) == pdTRUE) { process_joystick(p.data, p.len); }

    if (current_mode == MODE_GPIO) {
        uint16_t current_gpio_state = 0;
        current_gpio_state |= (digitalRead(GP_UP) << 0);
        current_gpio_state |= (digitalRead(GP_DOWN) << 1);
        current_gpio_state |= (digitalRead(GP_LEFT) << 2);
        current_gpio_state |= (digitalRead(GP_RIGHT) << 3);
        current_gpio_state |= (digitalRead(GP_FIRE1) << 4);
        current_gpio_state |= (digitalRead(GP_FIRE2) << 5);
        current_gpio_state |= (digitalRead(GP_POTY) << 6); 
        if (!is_amiga) current_gpio_state |= (digitalRead(GP_C64_SIG_MODE_SW) << 7);

        if (current_gpio_state != last_gpio_state) {
            last_gpio_state = current_gpio_state;
            
            Serial2.print("\x1b[2J\x1b[H");
            Serial2.println("\n==========================================");
            Serial2.println("    HARDWARE DIAGNOSTICS - GPIO STATES    ");
            Serial2.println("==========================================");
            Serial2.printf(" ENGINE : %s\n", use_html_configurator ? "HTML HID Configurator" : "Internal Profiler");
            Serial2.printf(" MODE   : %s\n", is_amiga ? "AMIGA (Fire 2 on Pin 9)" : "COMMODORE 64 (Fire 2 on POT X)");
            Serial2.printf(" STATUS : %s\n", device_connected ? current_profile.name : "WAITING...");
            Serial2.println("------------------------------------------");
            Serial2.println(" DB9 PIN   | GPIO | ELECTRICAL| STATE     ");
            Serial2.println("------------------------------------------");
            Serial2.printf(" UP        |  %02d  |   %s    | %s\n", GP_UP, digitalRead(GP_UP) ? "HIGH" : "LOW ", get_pin_status(GP_UP, true).c_str());
            Serial2.printf(" DOWN      |  %02d  |   %s    | %s\n", GP_DOWN, digitalRead(GP_DOWN) ? "HIGH" : "LOW ", get_pin_status(GP_DOWN, true).c_str());
            Serial2.printf(" LEFT      |  %02d  |   %s    | %s\n", GP_LEFT, digitalRead(GP_LEFT) ? "HIGH" : "LOW ", get_pin_status(GP_LEFT, true).c_str());
            Serial2.printf(" RIGHT     |  %02d  |   %s    | %s\n", GP_RIGHT, digitalRead(GP_RIGHT) ? "HIGH" : "LOW ", get_pin_status(GP_RIGHT, true).c_str());
            Serial2.printf(" FIRE 1    |  %02d  |   %s    | %s\n", GP_FIRE1, digitalRead(GP_FIRE1) ? "HIGH" : "LOW ", get_pin_status(GP_FIRE1, true).c_str());
            Serial2.printf(" FIRE 2    |  %02d  |   %s    | %s\n", GP_FIRE2, digitalRead(GP_FIRE2) ? "HIGH" : "LOW ", get_pin_status(GP_FIRE2, true).c_str());
            Serial2.printf(" FIRE 3    |  %02d  |   %s    | %s\n", GP_POTY, digitalRead(GP_POTY) ? "HIGH" : "LOW ", get_pin_status(GP_POTY, is_amiga).c_str());
            
            if (!is_amiga) {
                Serial2.printf(" C64_SIG   |  %02d  |   %s    | %s\n", GP_C64_SIG_MODE_SW, digitalRead(GP_C64_SIG_MODE_SW) ? "HIGH" : "LOW ", get_pin_status(GP_C64_SIG_MODE_SW, false).c_str());
            }
            Serial2.println("------------------------------------------");
            Serial2.println(">> Type 'exit' to return to normal operation <<\n");
        }
    }

    if (device_connected) {
        
        // 🎨 --- LIVE COLOR MIXER LOGIC --- 🎨
        if (current_mode == MODE_COLOR_MIXER) {
            if (cmd_state == CMD_IDLE) {
                static bool last_f1_mix = false, last_f2_mix = false;
                static unsigned long last_move_time = 0;

                // Cycle Red, Green, Blue on Fire 1 press
                if (joy_f1 && !last_f1_mix) {
                    mix_channel = (mix_channel + 1) % 3;
                    String ch_name = (mix_channel == 0) ? "RED 🔴" : (mix_channel == 1) ? "GREEN 🟢" : "BLUE 🔵";
                    Serial2.printf("\n>>> ACTIVE MIX CHANNEL: %s\n", ch_name.c_str());
                }
                
                // Print the final code with Fire 2
                if (joy_f2 && !last_f2_mix) {
                    Serial2.printf("\n>>> COPY THIS: ws2812b.Color(%d, %d, %d)\n", mix_r, mix_g, mix_b);
                }

                // Hardware live update with joystick movement
                if (millis() - last_move_time > 20) { 
                    bool changed = false;
                    if (joy_l) {
                        if (mix_channel == 0 && mix_r > 0) { mix_r--; changed = true; }
                        if (mix_channel == 1 && mix_g > 0) { mix_g--; changed = true; }
                        if (mix_channel == 2 && mix_b > 0) { mix_b--; changed = true; }
                    }
                    if (joy_r) {
                        if (mix_channel == 0 && mix_r < 255) { mix_r++; changed = true; }
                        if (mix_channel == 1 && mix_g < 255) { mix_g++; changed = true; }
                        if (mix_channel == 2 && mix_b < 255) { mix_b++; changed = true; }
                    }
                    if (joy_d) {
                        if (mix_brightness > 0) { mix_brightness--; changed = true; }
                    }
                    if (joy_u) {
                        if (mix_brightness < 255) { mix_brightness++; changed = true; }
                    }

                    if (changed) {
                        last_move_time = millis();
                        
                        ws2812b.setBrightness(mix_brightness);
                        ws2812b.setPixelColor(0, mix_r, mix_g, mix_b);
                        ws2812b.show(); 
                        
                        static unsigned long last_print = 0;
                        if (millis() - last_print > 200) { 
                            Serial2.printf("Live Mix: R:%d G:%d B:%d | Brightness: %d\n", mix_r, mix_g, mix_b, mix_brightness);
                            last_print = millis();
                        }
                    }
                }

                last_f1_mix = joy_f1;
                last_f2_mix = joy_f2;
            }
            
            return; // <-- FIX: Blocca il motore di gioco normale sempre, finché sei nel mixer!
        }
        
        // --- NORMAL GAMING LOGIC ---
        bool final_up = joy_u || joy_up_alt;
        bool out_fire = joy_f1;
        
        static bool toggle = false; 
        static unsigned long last_ms = 0;
        if (joy_auto) {
            if (millis() - last_ms > 70) { toggle = !toggle; last_ms = millis(); }
            out_fire = out_fire || toggle; 
        }
        
        static bool last_up = false, last_down = false, last_left = false, last_right = false;
        static bool last_fire = false, last_f2 = false, last_f3 = false;

        if (final_up != last_up || joy_d != last_down || joy_l != last_left || joy_r != last_right || out_fire != last_fire || joy_f2 != last_f2 || joy_f3 != last_f3) {
            
            if (current_mode == MODE_DEBUG) {
                Serial2.print("ACTION: ");
                if (!final_up && !joy_d && !joy_l && !joy_r && !out_fire && !joy_f2 && !joy_f3 && !joy_up_alt && !joy_auto) Serial2.print("All released");
                if(final_up) Serial2.print("[UP] "); if(joy_d) Serial2.print("[DOWN] "); 
                if(joy_l) Serial2.print("[LEFT] "); if(joy_r) Serial2.print("[RIGHT] ");
                if(out_fire) Serial2.print("[FIRE 1] "); if(joy_f2) Serial2.print("[FIRE 2] ");
                if(joy_f3) Serial2.print("[FIRE 3] "); 
                if(joy_up_alt) Serial2.print("[ALT UP] "); if(joy_auto) Serial2.print("[AUTOFIRE] ");
                Serial2.println();
            } 
            else if (current_mode == MODE_PLAY || current_mode == MODE_GPIO) {
                set_joy_pin(GP_UP, final_up);
                set_joy_pin(GP_DOWN, joy_d);
                set_joy_pin(GP_LEFT, joy_l); 
                set_joy_pin(GP_RIGHT, joy_r);
                set_joy_pin(GP_FIRE1, out_fire); 
                set_joy_pin(GP_FIRE2, joy_f2);
                set_fire3_pin(joy_f3); 
            }

            uint32_t led_color = LED_OFF;
            if (use_html_configurator) {
                led_color = LED_HTML_MODE; 
            } else {
                if (joy_f1) led_color = current_profile.color_fire1;
                else if (joy_f2) led_color = current_profile.color_fire2;
                else if (joy_f3) led_color = current_profile.color_fire3; 
                else if (joy_up_alt) led_color = current_profile.color_up_alt;
                else if (joy_auto) led_color = toggle ? current_profile.color_autofire : LED_OFF;
                else {
                    if (final_up) led_color = LED_DIR_UP;         
                    else if (joy_r) led_color = LED_DIR_RIGHT;    
                    else if (joy_l) led_color = LED_DIR_LEFT;     
                    else if (joy_d) led_color = LED_DIR_DOWN;     
                    else led_color = LED_DIR_IDLE;                
                }
            }

            static uint32_t last_led_color = 0xFFFFFFFF;
            if (led_color != last_led_color) {
                ws2812b.setPixelColor(0, led_color);
                ws2812b.show(); 
                last_led_color = led_color;
            }

            last_up = final_up; last_down = joy_d;
            last_left = joy_l; last_right = joy_r;
            last_fire = out_fire; last_f2 = joy_f2; last_f3 = joy_f3;
        }
    } 
    else if (current_mode != MODE_COLOR_MIXER) { // <-- FIX: Non rimettere il colore di base se sei nel mixer!
        uint32_t idle_color = is_amiga ? LED_IDLE_AMIGA : LED_IDLE_C64; 
        static uint32_t last_idle_color = 0;
        if (idle_color != last_idle_color) {
            ws2812b.setPixelColor(0, idle_color);
            ws2812b.show();
            last_idle_color = idle_color;
        }
    }
}