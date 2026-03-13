// ==========================================
// USB to C64/Amiga Adapter - Advanced v1.2
// File: CoreTasks.h
// Description: Main Loop Helpers and Task Orchestrator (Optimized for C64 Mouse)
// ==========================================
#pragma once

#include <Arduino.h>
#include "Globals.h"
#include "Hardware.h"
#include "ServiceTools.h"
#include "InputEngine.h"

extern int active_driver; 
static unsigned long last_mouse_action_time = 0;

inline void check_polling_timer() {
    if (current_mode == MODE_POLLING && polling_active && polling_start_time > 0) {
        if (millis() - polling_start_time >= 3000) { 
            polling_active = false;
            float hz = (float)polling_packet_count / 3.0;
            dual_printf("\n=== RESULT: ~%.0f Hz ===\n", hz);
            current_mode = MODE_PLAY;
        }
    }
}

inline void process_usb_packet(pkt_t &p) {
    if (active_driver == 1) { 
        // 🐭 HID MOUSE PROCESSING ENGINE
        if (current_mode == MODE_DEBUG) {
            dual_print("[HID MOUSE] Len: " + String(p.len) + " -> ");
            for(int i = 0; i < p.len; i++) { dual_printf("%02X ", p.data[i]); }
            dual_println();
        }

        uint8_t btns = 0;
        int8_t dx = 0, dy = 0, wheel = 0;

        // PROTOCOL-AWARE PARSER:
        // Protocol is known at connect time via mouse_wheel_enabled flag.
        // BOOT protocol  (wheel off): [Buttons, X, Y]              — data[0] = buttons always
        // REPORT protocol (wheel on): [Report ID, Buttons, X, Y, Wheel] — data[0] = Report ID
        // Never guess from data[0]: left button press sets buttons=0x01, which the old
        // data[0]==1 heuristic misidentified as a Report ID, shifting all fields by 1 byte.
        if (mouse_wheel_enabled) {
            if (p.len >= 5) { btns=p.data[1]; dx=(int8_t)p.data[2]; dy=(int8_t)p.data[3]; wheel=(int8_t)p.data[4]; }
            else if (p.len >= 4) { btns=p.data[1]; dx=(int8_t)p.data[2]; dy=(int8_t)p.data[3]; }
        } else {
            if (p.len >= 3) { btns=p.data[0]; dx=(int8_t)p.data[1]; dy=(int8_t)p.data[2];
                              if (p.len >= 4) wheel=(int8_t)p.data[3]; }
        }
        
        if (dx != 0 || dy != 0 || btns != 0 || wheel != 0) last_mouse_action_time = millis();

        if (current_mode == MODE_PLAY || current_mode == MODE_DEBUG || current_mode == MODE_GPIO) {
            process_mouse(btns, dx, dy, wheel);
            
            if (current_mode == MODE_DEBUG && (dx != 0 || dy != 0 || btns != 0 || wheel != 0)) {
                dual_printf("MOUSE ACTION: X:%3d | Y:%3d | BTN:%02x | WHL:%3d\n", dx, dy, btns, wheel);
                dual_println("--------------------------------------------------");
            }
        }
        
    } else {
        // 🕹️ RAW JOYSTICK PROCESSING ENGINE
        if (current_mode == MODE_SNIFFER) run_sniffer(connected_vid, connected_pid, p.data, p.len);
        else if (current_mode == MODE_RAW) run_raw_sniffer(p.data, p.len); 
        else process_joystick(p.data, p.len); 
    }
}

inline void run_gpio_diagnostics() {
    if (current_mode == MODE_GPIO) {
        uint16_t current_gpio_state = 0;
        current_gpio_state |= (digitalRead(GP_UP) << 0);
        current_gpio_state |= (digitalRead(GP_DOWN) << 1);
        current_gpio_state |= (digitalRead(GP_LEFT) << 2);
        current_gpio_state |= (digitalRead(GP_RIGHT) << 3);
        current_gpio_state |= (digitalRead(GP_FIRE1) << 4);
        current_gpio_state |= (digitalRead(GP_FIRE2) << 5);
        current_gpio_state |= (digitalRead(GP_FIRE3) << 6); 
        if (!is_amiga) current_gpio_state |= (digitalRead(GP_POTX) << 7);

        if (current_gpio_state != last_gpio_state) {
            last_gpio_state = current_gpio_state;
            
            dual_print("\x1b[2J\x1b[H");
            dual_println("\n==========================================");
            dual_println("    HARDWARE DIAGNOSTICS - GPIO STATES    ");
            dual_println("==========================================");
            dual_println(" ENGINE : " + String(current_profile.name[0] ? current_profile.name : "Unknown"));
            dual_println(" MODE   : " + String(is_amiga ? "AMIGA" : "COMMODORE 64"));
            
            if (device_connected) dual_println(" STATUS : " + String(current_profile.name));
            else dual_println(" STATUS : WAITING...");
            
            dual_println("------------------------------------------");
            
            auto print_pin_line = [&](const char* name, int pin, bool is_amiga_opt) {
                String status_str = get_pin_status(pin, is_amiga_opt);
                String line = " "; line += name;
                line += " |  "; if (pin < 10) line += "0"; line += pin;
                line += "  |   "; line += (digitalRead(pin) ? "HIGH" : "LOW ");
                line += "    | "; line += status_str;
                dual_println(line);
            };
            
            print_pin_line("UP       ", GP_UP, true);
            print_pin_line("DOWN     ", GP_DOWN, true);
            print_pin_line("LEFT     ", GP_LEFT, true);
            print_pin_line("RIGHT    ", GP_RIGHT, true);
            print_pin_line("FIRE 1   ", GP_FIRE1, true);
            print_pin_line("FIRE 2   ", GP_FIRE2, true);
            print_pin_line("FIRE 3   ", GP_FIRE3, is_amiga);
            
            if (!is_amiga) print_pin_line("C64_SIG  ", GP_POTX, false);
            dual_println("------------------------------------------");
            dual_println(">> Type 'exit' to return to normal operation <<\n");
        }
    }
}

inline void update_hardware_and_leds() {
// --- HARDWARE LEARNING / SNIFFER VISUAL FEEDBACK ---
    if (current_mode == MODE_LEARNING || current_mode == MODE_SNIFFER) {
        uint32_t l_color = LED_OFF;
        if (current_mode == MODE_LEARNING) {
            l_color = (millis() % 1000 < 500) ? C_RED : LED_OFF; // 🔴
        } else {
            switch (sniff_step) {
                case S_INIT:       l_color = (millis() % 200 < 100) ? C_PURPLE : LED_OFF; break; // 🟣
                case S_START:      l_color = LED_OFF; break; 
                case S_WAIT_UP:    l_color = C_BLUE; break; // 🔵
                case S_WAIT_DOWN:  l_color = C_YELLOW; break; // 🟡
                case S_WAIT_LEFT:  l_color = C_GREEN; break; // 🟢
                case S_WAIT_RIGHT: l_color = C_RED; break; // 🔴
                case S_WAIT_F1:    l_color = C_YELLOW; break; // 🟡
                case S_WAIT_F2:    l_color = C_GREEN; break; // 🟢
                case S_WAIT_F3:    l_color = C_CYAN; break; // 🩵
                case S_WAIT_UPALT: l_color = C_BLUE; break; // 🔵
                case S_WAIT_AUTO:  l_color = C_PURPLE; break; // 🟣
                case S_WAIT_LS_X:
                case S_WAIT_LS_Y:
                case S_WAIT_RS_X:
                case S_WAIT_RS_Y:  l_color = C_WHITE; break; // ⚪
                case S_DONE:       l_color = (millis() % 400 < 200) ? C_GREEN : LED_OFF; break; // 🟢
                default:           l_color = LED_OFF; break;
            }
        }
        static uint32_t last_l_color = 0xFFFFFFFF;
        if (l_color != last_l_color) {
            ws2812b.setPixelColor(0, l_color); ws2812b.show();
            last_l_color = l_color;
        }
        return; 
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
                dual_print("ACTION: ");
                if (!final_up && !joy_d && !joy_l && !joy_r && !out_fire && !joy_f2 && !joy_f3 && !joy_up_alt && !joy_auto) dual_print("All released");
                if(final_up) dual_print("[UP] "); if(joy_d) dual_print("[DOWN] "); 
                if(joy_l) dual_print("[LEFT] "); if(joy_r) dual_print("[RIGHT] ");
                if(out_fire) dual_print("[FIRE 1] "); if(joy_f2) dual_print("[FIRE 2] ");
                if(joy_f3) dual_print("[FIRE 3] "); 
                if(joy_up_alt) dual_print("[ALT UP] "); if(joy_auto) dual_print("[AUTOFIRE] ");
                dual_println();
            } 
            else if (current_mode == MODE_PLAY || current_mode == MODE_GPIO) {
                set_joy_pin(GP_UP, final_up); set_joy_pin(GP_DOWN, joy_d);
                set_joy_pin(GP_LEFT, joy_l); set_joy_pin(GP_RIGHT, joy_r);
                set_joy_pin(GP_FIRE1, out_fire); set_joy_pin(GP_FIRE2, joy_f2);
                set_fire3_pin(joy_f3);
            }

            uint32_t led_color = LED_OFF;
            if (!sys_colors.multicolor_enabled) {
                led_color = is_amiga ? sys_colors.idle_amiga : sys_colors.idle_c64;
            } else {
                if (joy_f1) led_color = sys_colors.btn_fire1;
                else if (joy_f2) led_color = sys_colors.btn_fire2;
                else if (joy_f3) led_color = sys_colors.btn_fire3; 
                else if (joy_up_alt) led_color = sys_colors.btn_alt;
                else if (joy_auto) led_color = toggle ? sys_colors.btn_auto : LED_OFF;
                else {
                    if (final_up || joy_r || joy_l || joy_d) led_color = sys_colors.dir_glowing;         
                    else { led_color = is_amiga ? sys_colors.idle_amiga : sys_colors.idle_c64; }                
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
        // MOUSE MODE: LED stays idle to prevent C64 interrupt drops
        uint32_t idle_color = is_amiga ? sys_colors.idle_amiga : sys_colors.idle_c64;
        
        static uint32_t last_idle_color = 0xFFFFFFFF;
        if (idle_color != last_idle_color) {
            ws2812b.setPixelColor(0, idle_color); ws2812b.show();
            last_idle_color = idle_color;
        }
    }
}
// EOF