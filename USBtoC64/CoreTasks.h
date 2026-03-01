// ==========================================
// USB to C64/Amiga Adapter - Advanced 1.1
// File: CoreTasks.h
// Description: Main Loop Helpers and Task Orchestrator (with Auto-Switch Watchdog)
// ==========================================
#pragma once

#include <Arduino.h>
#include "Globals.h"
#include "Hardware.h"
#include "ServiceTools.h"
#include "InputEngine.h"

// Link to the RTC memory state from the main file
extern int active_driver; 

// Variable to track the last mouse movement/click time
static unsigned long last_mouse_action_time = 0;

// 1. Timer and Benchmark Control
inline void check_polling_timer() {
    if (current_mode == MODE_POLLING && polling_active && polling_start_time > 0) {
        if (millis() - polling_start_time >= 3000) { 
            polling_active = false;
            float hz = (float)polling_packet_count / 3.0;
            Serial2.printf("\n=== RESULT: ~%.0f Hz ===\n", hz);
            current_mode = MODE_PLAY;
        }
    }
}

// 2. USB Packet Routing and Processing
inline void process_usb_packet(pkt_t &p) {
    if (active_driver == 1) {
        // --- NATIVE HID MOUSE MODE (STRICT BOOT PROTOCOL) ---
        if (current_mode == MODE_DEBUG) {
            Serial2.print("[HID BOOT] Len: "); 
            Serial2.print(p.len); 
            Serial2.print(" -> Data: ");
            for(int i = 0; i < p.len; i++) {
                Serial2.printf("%02X ", p.data[i]);
            }
            Serial2.println();
        }

        if (p.len < 3) return;

        uint8_t btns = p.data[0];
        int8_t dx = (int8_t)p.data[1];
        int8_t dy = (int8_t)p.data[2];

        if (dx != 0 || dy != 0 || btns != 0) {
            last_mouse_action_time = millis();
        }

        if (current_mode == MODE_PLAY || current_mode == MODE_DEBUG || current_mode == MODE_GPIO) {
            process_mouse(btns, dx, dy);
            
            if (current_mode == MODE_DEBUG && (dx != 0 || dy != 0 || btns != 0)) {
                Serial2.printf("MOUSE ACTION: X:%3d | Y:%3d | BTN:%02x\n", dx, dy, btns);
                Serial2.println("--------------------------------------------------");
            }

            if (!ground_stabilized && !is_amiga) {
                if (btns & 0x03) { 
                    pinMode(GP_LEFT, OUTPUT); digitalWrite(GP_LEFT, LOW);
                    pinMode(GP_RIGHT, OUTPUT); digitalWrite(GP_RIGHT, LOW);
                    ground_stabilized = true;
                }
            }
        }
    } 
    else {
        // --- RAW JOYSTICK MODE ---
        if (current_mode == MODE_SNIFFER) {
            run_sniffer(connected_vid, connected_pid, p.data, p.len);
        }
        else if (current_mode == MODE_RAW) {
            run_raw_sniffer(p.data, p.len); 
        }
        else { 
            process_joystick(p.data, p.len); 
        }
    }
}

// 3. Hardware Diagnostics
inline void run_gpio_diagnostics() {
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
            Serial2.printf(" MODE   : %s\n", is_amiga ? "AMIGA" : "COMMODORE 64");
            Serial2.printf(" STATUS : %s\n", device_connected ? current_profile.name : "WAITING...");
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
}

// 4. Update Pin Output and LEDs
inline void update_hardware_and_leds() {
    static bool last_mouse_state = false;
    static bool first_run_clock = true;
    
    if (is_mouse_connected != last_mouse_state || first_run_clock) {
        if (is_mouse_connected) {
            setCpuFrequencyMhz(240); 
        } else {
            setCpuFrequencyMhz(80); 
        }
        last_mouse_state = is_mouse_connected;
        first_run_clock = false;
    }

    if (device_connected && !is_mouse_connected) {
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
                set_joy_pin(GP_UP, final_up); set_joy_pin(GP_DOWN, joy_d);
                set_joy_pin(GP_LEFT, joy_l); set_joy_pin(GP_RIGHT, joy_r);
                set_joy_pin(GP_FIRE1, out_fire); set_joy_pin(GP_FIRE2, joy_f2);
                set_fire3_pin(joy_f3);
            }

            uint32_t led_color = LED_OFF;
            if (use_html_configurator) { led_color = LED_HTML_MODE; } 
            else {
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
                    else { led_color = is_amiga ? LED_IDLE_AMIGA : LED_IDLE_C64; }                
                }
            }

            static uint32_t last_led_color = 0xFFFFFFFF;
            if (led_color != last_led_color) {
                ws2812b.setPixelColor(0, led_color); ws2812b.show(); 
                last_led_color = led_color;
            }

            last_up = final_up; last_down = joy_d; last_left = joy_l; last_right = joy_r;
            last_fire = out_fire; last_f2 = joy_f2; last_f3 = joy_f3;
        }
    } 
    else { 
        uint32_t idle_color = is_amiga ? LED_IDLE_AMIGA : LED_IDLE_C64;
        if (is_mouse_connected && (millis() - last_mouse_action_time < 100)) {
            idle_color = LED_JOY_MOUSE; 
        }
        static uint32_t last_idle_color = 0xFFFFFFFF;
        if (idle_color != last_idle_color) {
            ws2812b.setPixelColor(0, idle_color); ws2812b.show();
            last_idle_color = idle_color;
        }
    }
}

// 5. Hardware Switch Safety Watchdog (WITH STABILITY SAMPLING & MOUSE PROTECTION)
inline void check_switch_mismatch() {
    if (!ENABLE_SWITCH_WATCHDOG) return;

    // Run only if on C64, Fire 2 is NOT pressed, AND NO MOUSE is connected!
    if (!is_amiga && !joy_f2 && !is_mouse_connected && current_mode == MODE_PLAY) { 
        static unsigned long last_probe_time = 0;
        
        if (millis() - last_probe_time > 2000) { 
            
            // 1. Release the pin to listen (Float)
            pinMode(GP_FIRE2, INPUT);
            
            // Give it 50us to let the Amiga pull-up snap into position
            delayMicroseconds(50);  
            
            int stable_high_count = 0;
            const int num_samples = 10;
            
            // 2. High-Speed Sampling Loop (1 millisecond total duration)
            for (int i = 0; i < num_samples; i++) {
                if (digitalRead(GP_FIRE2) == HIGH) {
                    stable_high_count++;
                }
                delayMicroseconds(100); 
            }
            
            // 3. Immediately restore C64 ground state (Output LOW) to kill the SID noise
            pinMode(GP_FIRE2, OUTPUT); 
            digitalWrite(GP_FIRE2, LOW);

            // 4. Evaluate the stability
            // If the signal is a rock-solid 10/10 HIGH, it's definitely an Amiga.
            // If it fluctuates (antenna noise) or rises slowly (SID capacitor), it's a C64.
            if (stable_high_count == num_samples) {
                
                // STABLE AMIGA DETECTED ON C64 SWITCH POSITION!
                Serial2.println("\n[!] SMART CHECK: Rock-solid Amiga pull-up detected on GP5!");
                Serial2.println("[!] AUTO-SWITCHING to Amiga Mode...");

                // Visual Feedback: Triple Purple Flash
                for(int i=0; i<3; i++) {
                    ws2812b.setPixelColor(0, C_PURPLE); ws2812b.show(); delay(100);
                    ws2812b.setPixelColor(0, LED_OFF); ws2812b.show(); delay(100);
                }

                // Force Amiga hardware configuration
                configure_console_mode(true); 
                
                // Final State: Steady White (Amiga Idle)
                ws2812b.setPixelColor(0, LED_IDLE_AMIGA); ws2812b.show();
                return; 
            }
            
            last_probe_time = millis();
        }
    }
}