// ==========================================
// USB to C64/Amiga Adapter - Advanced 2.8.1
// File: JoystickEngine.h
// Description: Core input processing engine (HTML & Native)
// ==========================================
#pragma once

#include <Arduino.h>
#include "Globals.h"
#include "JoySniffer.h"

// 🎮 --- JOYSTICK PROCESSING ENGINE --- 🎮
inline void process_joystick(const uint8_t *raw_data, int len) {
    if (current_mode == MODE_POLLING) {
        if (polling_active) {
            if (!polling_neutral_saved) {
                memcpy(polling_neutral_data, raw_data, len > 64 ? 64 : len);
                polling_neutral_saved = true;
                return; 
            }
            if (polling_start_time == 0) {
                bool changed = false;
                for (int i = 0; i < len; i++) {
                    if (abs((int)raw_data[i] - (int)polling_neutral_data[i]) > 2) { changed = true; break; }
                }
                if (changed) {
                    polling_start_time = millis();
                    polling_packet_count = 1;      
                    Serial2.println("\n[!] Input detected! Starting 3-second test...");
                }
            } else { polling_packet_count++; }
        }
        return; 
    }

    if (current_mode == MODE_SNIFFER) { run_sniffer(connected_vid, connected_pid, raw_data, len); return; }
    if (current_mode == MODE_RAW)     { run_raw_sniffer(raw_data, len); return; }
    if (current_mode == MODE_SERVICE) return; 
    if (!device_connected || len < 3) return;

    // --- Variables declaration ---
    bool u = false, d = false, l = false, r = false;
    bool f1 = false, f2 = false, f3 = false, f_alt = false, auto_btn = false;

#if HAS_HTML_CONFIGURATOR
    if (use_html_configurator) {
        bool f3_html = false;
        bool html_auto_on = false;
        bool html_auto_off = false;

        for (size_t i = 0; i < JM_JOY_RULES_COUNT; i++) {
            const JM_Rule &rule = JM_JOY_RULES[i];
            if (rule.index >= len) continue;
            uint8_t raw_val = raw_data[rule.index];
            bool matched = false;

            if (rule.op == JM_BITANY) { matched = ((raw_val & rule.value) != 0); } 
            else if (rule.op == JM_EQ) {
                if (raw_val == rule.value) { matched = true; } 
                else if (rule.index == JM_DPAD_INDEX) {
                    uint8_t hat_val = raw_val & 0x0F; 
                    uint8_t btn_val = raw_val & 0xF0; 
                    if (rule.value <= 15) { if (hat_val == rule.value) matched = true; } 
                    else {
                        uint8_t rule_btn_bits = rule.value & 0xF0;
                        if (rule_btn_bits != 0 && (btn_val & rule_btn_bits) == rule_btn_bits) matched = true;
                    }
                }
            }

            if (matched) {
                if (rule.func == JM_UP || rule.func == JM_UP_RIGHT || rule.func == JM_LEFT_UP) u = true;
                if (rule.func == JM_DOWN || rule.func == JM_RIGHT_DOWN || rule.func == JM_DOWN_LEFT) d = true;
                if (rule.func == JM_LEFT || rule.func == JM_DOWN_LEFT || rule.func == JM_LEFT_UP) l = true;
                if (rule.func == JM_RIGHT || rule.func == JM_UP_RIGHT || rule.func == JM_RIGHT_DOWN) r = true;
                
                if (rule.func == JM_FIRE) f1 = true;
                if (rule.func == JM_FIRE2) f2 = true;
                if (rule.func == JM_FIRE3) f3_html = true;
                if (rule.func == JM_AUTOFIRE_ON) html_auto_on = true;
                if (rule.func == JM_AUTOFIRE_OFF) html_auto_off = true;
            }
        }
        f3 = f3_html; f_alt = false; 
        
        // --- SMART HTML AUTOFIRE ---
        static bool html_autofire_latch = false;
        if (html_auto_on) html_autofire_latch = true;
        if (html_auto_off) html_autofire_latch = false;
        
        bool has_html_off_btn = false;
        for (size_t i = 0; i < JM_JOY_RULES_COUNT; i++) {
            if (JM_JOY_RULES[i].func == JM_AUTOFIRE_OFF) { has_html_off_btn = true; break; }
        }
        auto_btn = has_html_off_btn ? html_autofire_latch : html_auto_on;
        
        #if JM_USE_ANALOG_MOUSE == 1
        for (size_t i = 0; i < (sizeof(JM_MOUSE_X_INDEXES)/sizeof(JM_MOUSE_X_INDEXES[0])); i++) {
            uint8_t idx = JM_MOUSE_X_INDEXES[i];
            if (idx < len) {
                uint8_t val = raw_data[idx];
                if (val < JM_ANALOG_DEAD_LOW) l = true;
                if (val > JM_ANALOG_DEAD_HIGH) r = true;
            }
        }
        for (size_t i = 0; i < (sizeof(JM_MOUSE_Y_INDEXES)/sizeof(JM_MOUSE_Y_INDEXES[0])); i++) {
            uint8_t idx = JM_MOUSE_Y_INDEXES[i];
            if (idx < len) {
                uint8_t val = raw_data[idx];
                if (val < JM_ANALOG_DEAD_LOW) u = true;
                if (val > JM_ANALOG_DEAD_HIGH) d = true;
            }
        }
        #endif

    } else {
#endif
        // --- NATIVE ENGINE ---
        
        // Step 1: Independently compute Analog values
        bool a_u = false, a_d = false, a_l = false, a_r = false;
        
        if (current_profile.byte_analog_x != 0 || current_profile.byte_analog_y != 0) {
            if (len > current_profile.byte_analog_x) {
                uint8_t ax = raw_data[current_profile.byte_analog_x]; 
                if (ax < 64) a_l = true; if (ax > 192) a_r = true;
            }
            if (len > current_profile.byte_analog_y) {
                uint8_t ay = raw_data[current_profile.byte_analog_y];
                if (ay < 64) a_u = true; if (ay > 192) a_d = true;
            }
        }
        if (current_profile.byte_analog_right_x != 0 || current_profile.byte_analog_right_y != 0) {
            if (len > current_profile.byte_analog_right_x) {
                uint8_t rx = raw_data[current_profile.byte_analog_right_x]; 
                if (rx < 64) a_l = true; if (rx > 192) a_r = true;
            }
            if (len > current_profile.byte_analog_right_y) {
                uint8_t ry = raw_data[current_profile.byte_analog_right_y];
                if (ry < 64) a_u = true; if (ry > 192) a_d = true;
            }
        }

        // Step 2: Independently compute Digital D-Pad values
        bool d_u = false, d_d = false, d_l = false, d_r = false;

        if (current_profile.dpad_type == HYBRID_16BIT_BITMASK) {
            int idx_x = current_profile.byte_analog_x; int idx_y = current_profile.byte_analog_y;
            if (len > idx_y + 1) { 
                int16_t axis_x = (int16_t)(raw_data[idx_x] | (raw_data[idx_x + 1] << 8));
                int16_t axis_y = (int16_t)(raw_data[idx_y] | (raw_data[idx_y + 1] << 8));
                a_u = a_u || (axis_y > 16000); a_d = a_d || (axis_y < -16000);
                a_r = a_r || (axis_x > 16000); a_l = a_l || (axis_x < -16000);
                
                uint8_t dpad = raw_data[current_profile.byte_x];
                d_u = (dpad & current_profile.val_up) != 0; d_d = (dpad & current_profile.val_down) != 0;
                d_l = (dpad & current_profile.val_left) != 0; d_r = (dpad & current_profile.val_right) != 0;
            }
        }
        else if (current_profile.dpad_type == AXIS) {
            if (len > current_profile.byte_y) {
                uint8_t x = raw_data[current_profile.byte_x]; uint8_t y = raw_data[current_profile.byte_y];
                d_l = (x < 64); d_r = (x > 192); d_u = (y < 64); d_d = (y > 192);
            }
        } 
        else if (current_profile.dpad_type == HAT_SWITCH) {
            if (len > current_profile.byte_x) {
                uint8_t hat = raw_data[current_profile.byte_x] & 0x0F;
                if (hat <= 7) {
                    d_u = (hat == 0 || hat == 1 || hat == 7); d_d = (hat == 3 || hat == 4 || hat == 5);
                    d_l = (hat == 5 || hat == 6 || hat == 7); d_r = (hat == 1 || hat == 2 || hat == 3);
                }
            }
        }
        else if (current_profile.dpad_type == EXACT_VALUE) {
            if (len > current_profile.byte_x) {
                uint8_t val = raw_data[current_profile.byte_x];
                d_u = (val == current_profile.val_up); d_d = (val == current_profile.val_down);
                d_l = (val == current_profile.val_left); d_r = (val == current_profile.val_right);
            }
        }
        else if (current_profile.dpad_type == BITMASK) {
            if (len > current_profile.byte_x) {
                d_u = (raw_data[current_profile.byte_x] & current_profile.val_up) != 0;
                d_d = (raw_data[current_profile.byte_x] & current_profile.val_down) != 0;
                d_l = (raw_data[current_profile.byte_x] & current_profile.val_left) != 0;
                d_r = (raw_data[current_profile.byte_x] & current_profile.val_right) != 0;
            }
        }

        // Step 3: MERGE Analog and Digital properly!
        u = a_u || d_u;
        d = a_d || d_d;
        l = a_l || d_l;
        r = a_r || d_r;

        // --- Process Buttons (With ZERO-BYTE Protection) ---
        bool f_auto_on = false;
        bool f_auto_off = false;

        if (current_profile.dpad_type == EXACT_VALUE || current_profile.dpad_type == HAT_SWITCH) {
            if (current_profile.byte_fire1 != 0 && len > current_profile.byte_fire1)   f1 = (raw_data[current_profile.byte_fire1] == current_profile.val_fire1);
            if (current_profile.byte_fire2 != 0 && len > current_profile.byte_fire2)   f2 = (raw_data[current_profile.byte_fire2] == current_profile.val_fire2);
            if (current_profile.byte_fire3 != 0 && len > current_profile.byte_fire3)   f3 = (raw_data[current_profile.byte_fire3] == current_profile.val_fire3);
            if (current_profile.byte_up_alt != 0 && len > current_profile.byte_up_alt)  f_alt = (raw_data[current_profile.byte_up_alt] == current_profile.val_up_alt);
            if (current_profile.byte_autofire != 0 && len > current_profile.byte_autofire) f_auto_on = (raw_data[current_profile.byte_autofire] == current_profile.val_autofire);
            if (current_profile.byte_autofire_off != 0 && len > current_profile.byte_autofire_off) f_auto_off = (raw_data[current_profile.byte_autofire_off] == current_profile.val_autofire_off);
        } else {
            if (current_profile.byte_fire1 != 0 && len > current_profile.byte_fire1)   f1 = (raw_data[current_profile.byte_fire1] & current_profile.val_fire1) != 0;
            if (current_profile.byte_fire2 != 0 && len > current_profile.byte_fire2)   f2 = (raw_data[current_profile.byte_fire2] & current_profile.val_fire2) != 0;
            if (current_profile.byte_fire3 != 0 && len > current_profile.byte_fire3)   f3 = (raw_data[current_profile.byte_fire3] & current_profile.val_fire3) != 0;
            if (current_profile.byte_up_alt != 0 && len > current_profile.byte_up_alt)  f_alt = (raw_data[current_profile.byte_up_alt] & current_profile.val_up_alt) != 0;
            if (current_profile.byte_autofire != 0 && len > current_profile.byte_autofire) f_auto_on = (raw_data[current_profile.byte_autofire] & current_profile.val_autofire) != 0;
            if (current_profile.byte_autofire_off != 0 && len > current_profile.byte_autofire_off) f_auto_off = (raw_data[current_profile.byte_autofire_off] & current_profile.val_autofire_off) != 0;
        }

        // --- SMART NATIVE AUTOFIRE ---
        static bool native_autofire_latch = false;
        
        if (current_profile.byte_autofire != 0 && current_profile.byte_autofire_off != 0) {
            if (f_auto_on) native_autofire_latch = true;
            if (f_auto_off) native_autofire_latch = false;
            auto_btn = native_autofire_latch;
        } 
        else if (current_profile.byte_autofire != 0) {
            auto_btn = f_auto_on; 
            native_autofire_latch = false;
        }

#if HAS_HTML_CONFIGURATOR
    }
#endif

    // --- SMART MULTIPORT MERGE (CO-PILOT MODE) ---
    if (current_profile.use_report_id) {
        static bool p_u[3], p_d[3], p_l[3], p_r[3], p_f1[3], p_f2[3], p_f3[3], p_alt[3], p_auto[3];
        uint8_t id = raw_data[0]; 

        if (id == 1 || id == 2) {
            p_u[id] = u; p_d[id] = d; p_l[id] = l; p_r[id] = r;
            p_f1[id] = f1; p_f2[id] = f2; p_f3[id] = f3; 
            p_alt[id] = f_alt; p_auto[id] = auto_btn;
        }

        joy_u = p_u[1] | p_u[2];
        joy_d = p_d[1] | p_d[2];
        joy_l = p_l[1] | p_l[2];
        joy_r = p_r[1] | p_r[2];
        joy_f1 = p_f1[1] | p_f1[2];
        joy_f2 = p_f2[1] | p_f2[2];
        joy_f3 = p_f3[1] | p_f3[2];
        joy_up_alt = p_alt[1] | p_alt[2];
        joy_auto = p_auto[1] | p_auto[2];

    } else {
        joy_u = u; joy_d = d; joy_l = l; joy_r = r;
        joy_f1 = f1; joy_f2 = f2; joy_f3 = f3; joy_up_alt = f_alt; joy_auto = auto_btn;
    }
}