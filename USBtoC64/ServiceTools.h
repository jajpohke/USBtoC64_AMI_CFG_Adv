// ==========================================
// USB to C64/Amiga Adapter - Advanced v2.12.0
// File: ServiceTools.h
// Description: Unified module for Sniffer, Auto-Dumper, and Serial CLI Menu
// ==========================================
#pragma once

#include <Arduino.h>
#include "soc/rtc_cntl_reg.h" 
#include "Globals.h"

extern Preferences prefs;
extern void configure_console_mode(bool is_amiga); 
extern void run_gpio_diagnostics();
extern void release_all_outputs();
extern bool dev_mode; 
extern bool web_ui_active; // <--- IL SILENZIATORE LCARS

// ==========================================
// 🖨️ DUAL SERIAL ROUTING ENGINE (SILENCED FOR JSON)
// ==========================================
// REL_3_2_MOD: [5] dual_print scrive solo sul canale aperto:
//   dev_mode + !C64 → Serial2 | !dev + !C64 → Serial | C64 → niente
inline void dual_print(const String& msg) {
    if (web_ui_active) return;
    if (C64_Amiga_Connected) return;
    if (dev_mode) { Serial2.print(msg); }
    else { Serial.print(msg); delay(2); }
}

inline void dual_println(const String& msg = "") {
    if (web_ui_active) return;
    if (C64_Amiga_Connected) return;
    if (dev_mode) { Serial2.println(msg); }
    else { Serial.println(msg); delay(2); }
}

inline void dual_printf(const char* format, ...) {
    if (web_ui_active) return;
    if (C64_Amiga_Connected) return;
    char buf[256];
    va_list args;
    va_start(args, format);
    vsnprintf(buf, sizeof(buf), format, args);
    va_end(args);
    if (dev_mode) { Serial2.print(buf); }
    else { Serial.print(buf); delay(2); }
}

// ==========================================
// 🕵️‍♂️ PART 1: UNIFIED JOYSTICK SNIFFER ENGINE
// ==========================================
static uint8_t dat_neutral[64];
static uint8_t dat_thresh[64];
static uint8_t dat_min[64]; 
static uint8_t dat_max[64]; 

static int b_up, b_down, b_left, b_right, b_f1, b_f2, b_f3, b_up_alt, b_auto;
static int b_ls_x = 0, b_ls_y = 0, b_rs_x = 0, b_rs_y = 0;
static uint8_t v_up, v_down, v_left, v_right, v_f1, v_f2, v_f3, v_up_alt, v_auto;
static bool config_printed = false;
static unsigned long sniff_timer = 0; 
static bool first_packet_received = false; 

static bool detected_multiplexer = false;
static uint8_t detected_report_id = 0;

inline void reset_sniffer() {
    sniff_step = S_INIT;
    config_printed = false;
    b_ls_x = 0; b_ls_y = 0; b_rs_x = 0; b_rs_y = 0;
    first_packet_received = false; 
    detected_multiplexer = false;
    detected_report_id = 0;
    sniff_timer = millis();
    for(int i = 0; i < 64; i++) dat_thresh[i] = 2; 
}

inline void run_raw_sniffer(const uint8_t *data, int len) {
    static uint8_t last_raw[64] = {0};
    bool changed = false;
    int check_len = len > 12 ? 12 : len;

    for(int i = 0; i < check_len; i++) {
        if(abs((int)data[i] - (int)last_raw[i]) > 0) { 
            changed = true; break;
        }
    }
    if (changed) {
        dual_print("RAW DATA: ");
        for(int i = 0; i < check_len; i++) {
            dual_printf("[%d]:%3d  ", i, data[i]);
            last_raw[i] = data[i];
        }
        dual_println("");
    }
}

inline void run_sniffer(uint16_t vid, uint16_t pid, const uint8_t *data, int len) {
    if (len <= 0) return;

    if (sniff_step == S_INIT) {
        if (!first_packet_received) {
            first_packet_received = true;
            sniff_timer = millis();
            for(int i=0; i<64; i++) { dat_min[i] = 255; dat_max[i] = 0; }
            dual_println("\n>>> PAD IS AWAKE! Hands off completely.");
            dual_println(">>> Wait 2 seconds while I profile background noise...");
        }

        if (millis() - sniff_timer < 2000) {
            for(int i = 0; i < len; i++) {
                dat_neutral[i] = data[i];
                if (data[i] < dat_min[i]) dat_min[i] = data[i];
                if (data[i] > dat_max[i]) dat_max[i] = data[i];
            }
            return; 
        }

        if (dat_max[0] != dat_min[0]) {
            detected_multiplexer = true;
            detected_report_id = dat_neutral[0]; 
        }

        for(int i = 0; i < len; i++) {
            int noise_level = dat_max[i] - dat_min[i]; 

            if (noise_level > 10 || (noise_level > 0 && i == 0)) { 
                dat_thresh[i] = 255; 
            } else {
                if (dat_neutral[i] >= 100 && dat_neutral[i] <= 155) dat_thresh[i] = 60; 
                else dat_thresh[i] = 0; 
            }
        }
        
        if (detected_multiplexer) {
             dual_printf(">>> MULTIPLEXER DETECTED! Locked to Port/ID: %d\n", detected_report_id);
        }

        sniff_step = S_START;
    }

    if (sniff_step == S_START) {
        dual_println("\n--- SNIFFER ENGINE (GLOBAL NEUTRAL LOCK) ---");
        sniff_step = S_WAIT_UP;
        dual_println(">>> Noise filtered. Smart sensors active.");
        dual_println("[?] PRESS AND HOLD: UP on D-PAD (LED Blu)");
    }

    bool is_neutral = true;
    int changed_byte = -1;
    uint8_t changed_val = 0;

    for (int i = 0; i < len; i++) {
        if (abs((int)data[i] - (int)dat_neutral[i]) > dat_thresh[i]) { 
            if (sniff_step == S_WAIT_LS_Y && i == b_ls_x) continue;
            if (sniff_step == S_WAIT_RS_X && (i == b_ls_x || i == b_ls_y)) continue;
            if (sniff_step == S_WAIT_RS_Y && (i == b_ls_x || i == b_ls_y || i == b_rs_x)) continue;

            is_neutral = false; 
            changed_byte = i; 
            changed_val = data[i]; 
            break; 
        }
    }

    switch (sniff_step) {
        case S_WAIT_UP: 
            if (!is_neutral) { b_up = changed_byte; v_up = changed_val; dual_printf("OK! B:%d, V:%d\n", b_up, v_up); sniff_step = S_REL_UP; } break;
        case S_REL_UP: 
            if (is_neutral) { dual_println("[?] PRESS AND HOLD: DOWN (LED Giallo)"); sniff_step = S_WAIT_DOWN; } break;
        
        case S_WAIT_DOWN: 
            if (!is_neutral) { b_down = changed_byte; v_down = changed_val; dual_printf("OK! B:%d, V:%d\n", b_down, v_down); sniff_step = S_REL_DOWN; } break;
        case S_REL_DOWN: 
            if (is_neutral) { dual_println("[?] PRESS AND HOLD: LEFT (LED Verde)"); sniff_step = S_WAIT_LEFT; } break;
            
        case S_WAIT_LEFT: 
            if (!is_neutral) { b_left = changed_byte; v_left = changed_val; dual_printf("OK! B:%d, V:%d\n", b_left, v_left); sniff_step = S_REL_LEFT; } break;
        case S_REL_LEFT: 
            if (is_neutral) { dual_println("[?] PRESS AND HOLD: RIGHT (LED Rosso)"); sniff_step = S_WAIT_RIGHT; } break;

        case S_WAIT_RIGHT: 
            if (!is_neutral) { b_right = changed_byte; v_right = changed_val; dual_printf("OK! B:%d, V:%d\n", b_right, v_right); sniff_step = S_REL_RIGHT; } break;
        case S_REL_RIGHT: 
            if (is_neutral) { dual_println("[?] PRESS AND HOLD: FIRE 1 (LED Giallo)"); sniff_step = S_WAIT_F1; } break;
        
        case S_WAIT_F1: 
            if (!is_neutral) { b_f1 = changed_byte; v_f1 = changed_val; dual_printf("OK! B:%d, V:%d\n", b_f1, v_f1); sniff_step = S_REL_F1; } break;
        case S_REL_F1: 
            if (is_neutral) { dual_println("[?] PRESS AND HOLD: FIRE 2 (LED Verde)"); sniff_step = S_WAIT_F2; } break;

        case S_WAIT_F2: 
            if (!is_neutral) { b_f2 = changed_byte; v_f2 = changed_val; dual_printf("OK! B:%d, V:%d\n", b_f2, v_f2); sniff_step = S_REL_F2; } break;
        case S_REL_F2: 
            if (is_neutral) { dual_println("[?] PRESS AND HOLD: FIRE 3 (LED Blu)"); sniff_step = S_WAIT_F3; } break;

        case S_WAIT_F3: 
            if (!is_neutral) { b_f3 = changed_byte; v_f3 = changed_val; dual_printf("OK! B:%d, V:%d\n", b_f3, v_f3); sniff_step = S_REL_F3; } break;
        case S_REL_F3: 
            if (is_neutral) { dual_println("[?] PRESS AND HOLD: ALT UP BUTTON (LED Rosso)"); sniff_step = S_WAIT_UPALT; } break;

        case S_WAIT_UPALT: 
            if (!is_neutral) { b_up_alt = changed_byte; v_up_alt = changed_val; dual_printf("OK! B:%d, V:%d\n", b_up_alt, v_up_alt); sniff_step = S_REL_UPALT; } break;
        case S_REL_UPALT: 
            if (is_neutral) { dual_println("[?] PRESS AND HOLD: AUTOFIRE BUTTON (LED Turchese)"); sniff_step = S_WAIT_AUTO; } break;

        case S_WAIT_AUTO: 
            if (!is_neutral) { b_auto = changed_byte; v_auto = changed_val; dual_printf("OK! B:%d, V:%d\n", b_auto, v_auto); sniff_step = S_REL_AUTO; } break;
        
        case S_REL_AUTO: 
            if (is_neutral) { 
                dual_println("\n--- ANALOG STICKS (Optional) ---");
                dual_println("[?] MOVE LEFT STICK FULLY RIGHT (Or press FIRE 1 to skip all analogs)"); 
                sniff_step = S_WAIT_LS_X; 
            } break;

        case S_WAIT_LS_X: 
            if (changed_byte == b_f1) { 
                dual_println(">>> Analog mapping skipped.");
                sniff_step = S_DONE; 
            } else if (!is_neutral && abs((int)data[changed_byte] - (int)dat_neutral[changed_byte]) > 40) {
                b_ls_x = changed_byte;
                dual_printf("OK! Left Stick X = Byte %d\n", b_ls_x);
                sniff_step = S_REL_LS_X;
            } break;

        case S_REL_LS_X: 
            if (is_neutral) { 
                dual_println("[?] MOVE LEFT STICK FULLY DOWN"); 
                sniff_step = S_WAIT_LS_Y; 
            } break;

        case S_WAIT_LS_Y: 
            if (!is_neutral && abs((int)data[changed_byte] - (int)dat_neutral[changed_byte]) > 40) {
                b_ls_y = changed_byte;
                dual_printf("OK! Left Stick Y = Byte %d\n", b_ls_y);
                sniff_step = S_REL_LS_Y;
            } break;

        case S_REL_LS_Y: 
            if (is_neutral) { 
                dual_println("[?] MOVE RIGHT STICK FULLY RIGHT (Or press FIRE 1 to skip right stick)"); 
                sniff_step = S_WAIT_RS_X; 
            } break;

        case S_WAIT_RS_X: 
            if (changed_byte == b_f1) {
                dual_println(">>> Right Stick skipped.");
                sniff_step = S_DONE; 
            } else if (!is_neutral && abs((int)data[changed_byte] - (int)dat_neutral[changed_byte]) > 40) {
                b_rs_x = changed_byte;
                dual_printf("OK! Right Stick X = Byte %d\n", b_rs_x);
                sniff_step = S_REL_RS_X;
            } break;

        case S_REL_RS_X: 
            if (is_neutral) { 
                dual_println("[?] MOVE RIGHT STICK FULLY DOWN"); 
                sniff_step = S_WAIT_RS_Y; 
            } break;

        case S_WAIT_RS_Y: 
            if (!is_neutral && abs((int)data[changed_byte] - (int)dat_neutral[changed_byte]) > 40) {
                b_rs_y = changed_byte;
                dual_printf("OK! Right Stick Y = Byte %d\n", b_rs_y);
                sniff_step = S_REL_RS_Y;
            } break;

        case S_REL_RS_Y: 
            if (is_neutral) { 
                dual_println("\n>>> GENERATING PROFILE... <<<"); 
                sniff_step = S_DONE; 
            } break;

        case S_DONE:
            if (!config_printed) {
                String type_str = "EXACT_VALUE";
                bool same_byte = (b_up == b_left && b_left == b_down && b_down == b_right);
                if (same_byte) {
                    if ((v_up & 0x0F) <= 8) type_str = "HAT_SWITCH"; 
                    else type_str = "BITMASK"; 
                } else {
                    if (dat_neutral[b_up] >= 100 && dat_neutral[b_up] <= 155) type_str = "AXIS";
                    else type_str = "HYBRID_16BIT_BITMASK";
                }

                bool is_exact = (type_str == "HAT_SWITCH" || type_str == "EXACT_VALUE"); 
                uint8_t m_f1 = is_exact ? v_f1 : (v_f1 ^ dat_neutral[b_f1]);
                uint8_t m_f2 = is_exact ? v_f2 : (v_f2 ^ dat_neutral[b_f2]);
                uint8_t m_f3 = is_exact ? v_f3 : (v_f3 ^ dat_neutral[b_f3]); 
                uint8_t m_up_alt = is_exact ? v_up_alt : (v_up_alt ^ dat_neutral[b_up_alt]);
                uint8_t m_auto = is_exact ? v_auto : (v_auto ^ dat_neutral[b_auto]);

                if (sniff_to_nvs) {
                    // --- SALVATAGGIO DIRETTO IN NVS ---
                    NVS_PadProfile blob;
                    memset(&blob, 0, sizeof(NVS_PadProfile));
                    blob.vid = vid; 
                    blob.pid = pid;
                    
                    if (type_str == "HAT_SWITCH") blob.dpad_type = HAT_SWITCH;
                    else if (type_str == "BITMASK") blob.dpad_type = BITMASK;
                    else if (type_str == "AXIS") blob.dpad_type = AXIS;
                    else if (type_str == "HYBRID_16BIT_BITMASK") blob.dpad_type = HYBRID_16BIT_BITMASK;
                    else blob.dpad_type = EXACT_VALUE;

                    blob.byte_x = b_left; 
                    blob.byte_y = b_up;
                    blob.val_up = v_up; 
                    blob.val_down = v_down;
                    blob.val_left = v_left; 
                    blob.val_right = v_right;

                    blob.byte_fire1 = b_f1; blob.val_fire1 = m_f1;
                    blob.byte_fire2 = b_f2; blob.val_fire2 = m_f2;
                    blob.byte_fire3 = b_f3; blob.val_fire3 = m_f3;
                    blob.byte_up_alt = b_up_alt; blob.val_up_alt = m_up_alt;
                    blob.byte_autofire = b_auto; blob.val_autofire = m_auto;

                    blob.color_fire1 = C_GREEN;
                    blob.color_fire2 = C_RED;
                    blob.color_fire3 = C_CYAN;
                    blob.color_up_alt = C_BLUE;
                    blob.color_autofire = C_YELLOW;

                    size_t written = prefs.putBytes("cust_blob", &blob, sizeof(NVS_PadProfile));
                    if (written == sizeof(NVS_PadProfile)) {
                        prefs.putBool("has_custom", true);
                        dual_println(">>> ALL MAPPED! SAVED TO NVS BLOB!");
                        
                        load_custom_profile_from_nvs();
                        current_profile = custom_profile;
                        
                        dual_println(">>> SUCCESS! Normal operation resumed.");
                    } else {
                        dual_println(">>> ERROR: Failed to save to NVS!");
                    }
                    current_mode = MODE_PLAY;
                } else {
                    // --- STAMPA DEL CODICE C++ ---
                    dual_println("\n// --- COPY THIS INTO JoystickProfiles.h ---");
                    dual_println("{");
                    dual_printf("  .name = \"%s\",\n", sniff_profile_name.c_str());
                    dual_printf("  .vid = %d, .pid = %d,\n", vid, pid);
                    dual_printf("  .use_report_id = %s, .report_id_val = %d,\n", detected_multiplexer ? "true" : "false", detected_report_id);
                    dual_printf("  .dpad_type = %s,\n", type_str.c_str());
                    dual_printf("  .byte_x = %d, .byte_y = %d, .byte_analog_x = %d, .byte_analog_y = %d, .byte_analog_right_x = %d, .byte_analog_right_y = %d,\n", b_left, b_up, b_ls_x, b_ls_y, b_rs_x, b_rs_y);
                    dual_printf("  .byte_fire1 = %d, .byte_fire2 = %d, .byte_fire3 = %d, .byte_up_alt = %d, .byte_autofire = %d, .byte_autofire_off = 0,\n", b_f1, b_f2, b_f3, b_up_alt, b_auto);
                    dual_printf("  .val_up = %d, .val_down = %d, .val_left = %d, .val_right = %d,\n", v_up, v_down, v_left, v_right);
                    dual_printf("  .val_fire1 = %d, .val_fire2 = %d, .val_fire3 = %d, .val_up_alt = %d, .val_autofire = %d, .val_autofire_off = 0x00,\n", m_f1, m_f2, m_f3, m_up_alt, m_auto);
                    dual_println("  .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW");
                    dual_println("},");
                    dual_println("// -----------------------------------------");
                }
                config_printed = true;
            }
            break;
    }
}

// ==========================================
// 🎨 PART 2.5: COLORADJ - LED COLOR CALIBRATION
// ==========================================

static int cadj_slot = 0;   // 0-based slot currently being edited
static int cadj_step = 1;   // R/G/B adjustment step

inline uint32_t& cadj_get_slot(int idx) {
    switch(idx) {
        case 0: return sys_colors.idle_c64;
        case 1: return sys_colors.idle_amiga;
        case 2: return sys_colors.dir_glowing;
        case 3: return sys_colors.btn_fire1;
        case 4: return sys_colors.btn_fire2;
        case 5: return sys_colors.btn_fire3;
        case 6: return sys_colors.btn_alt;
        case 7: return sys_colors.btn_auto;
        default: return sys_colors.idle_c64;
    }
}

inline const char* cadj_get_name(int idx) {
    switch(idx) {
        case 0: return "Idle C64";
        case 1: return "Idle Amiga";
        case 2: return "Directions";
        case 3: return "Fire 1";
        case 4: return "Fire 2";
        case 5: return "Fire 3";
        case 6: return "Alt UP";
        case 7: return "Autofire";
        default: return "?";
    }
}

inline uint8_t cadj_clamp(int v) { return (uint8_t)(v < 0 ? 0 : (v > 255 ? 255 : v)); }

inline void cadj_print_menu() {
    dual_println("\n=== 🎨 COLOR CALIBRATION ===");
    for (int i = 0; i < 8; i++) {
        uint32_t c = cadj_get_slot(i);
        uint8_t r = (c >> 16) & 0xFF;
        uint8_t g = (c >> 8) & 0xFF;
        uint8_t b = c & 0xFF;
        char buf[64];
        sprintf(buf, " [%d] %-12s R:%3d G:%3d B:%3d", i+1, cadj_get_name(i), r, g, b);
        dual_println(buf);
    }
    dual_println("----------------------------");
    dual_println(" Enter slot number (1-8), 'save', 'reset' or 'exit'");
}

inline void cadj_print_edit_prompt() {
    uint32_t c = cadj_get_slot(cadj_slot);
    uint8_t r = (c >> 16) & 0xFF;
    uint8_t g = (c >> 8) & 0xFF;
    uint8_t b = c & 0xFF;
    char buf[128];
    sprintf(buf, "\n[%s] R:%3d G:%3d B:%3d  (step:%d)", cadj_get_name(cadj_slot), r, g, b, cadj_step);
    dual_println(buf);
    dual_println(" r+ r- g+ g- b+ b-  |  r++ r-- g++ g-- b++ b--  |  step N  |  save back");
}

inline void handleColorAdj(const String& raw) {
    String cmd = raw;
    cmd.trim();
    cmd.toLowerCase();

    if (cmd_state == CMD_COLOR_ADJ_MENU) {
        if (cmd == "exit") {
            cmd_state = CMD_IDLE;
            current_mode = MODE_PLAY;
            dual_println(">>> Exited coloradj.");
            return;
        }
        if (cmd == "save") {
            prefs.putBytes("col_blob", &sys_colors, sizeof(SystemColors));
            prefs.putBool("has_colors", true);
            dual_println(">>> ✅ Colors saved to NVS.");
            cadj_print_menu();
            return;
        }
        if (cmd == "reset") {
            SystemColors def;
            sys_colors.idle_c64    = def.idle_c64;
            sys_colors.idle_amiga  = def.idle_amiga;
            sys_colors.dir_glowing = def.dir_glowing;
            sys_colors.btn_fire1   = def.btn_fire1;
            sys_colors.btn_fire2   = def.btn_fire2;
            sys_colors.btn_fire3   = def.btn_fire3;
            sys_colors.btn_alt     = def.btn_alt;
            sys_colors.btn_auto    = def.btn_auto;
            prefs.putBytes("col_blob", &sys_colors, sizeof(SystemColors));
            prefs.putBool("has_colors", true);
            dual_println(">>> ✅ Defaults restored and saved.");
            cadj_print_menu();
            return;
        }
        int slot = cmd.toInt();
        if (slot >= 1 && slot <= 8) {
            cadj_slot = slot - 1;
            cadj_step = 1;
            ws2812b.setPixelColor(0, cadj_get_slot(cadj_slot)); ws2812b.show();
            cmd_state = CMD_COLOR_ADJ_EDIT;
            cadj_print_edit_prompt();
        } else {
            dual_println(">>> Invalid. Enter 1-8, 'save', 'reset' or 'exit'.");
        }
        return;
    }

    if (cmd_state == CMD_COLOR_ADJ_EDIT) {
        uint32_t& col = cadj_get_slot(cadj_slot);
        int r = (col >> 16) & 0xFF;
        int g = (col >> 8) & 0xFF;
        int b = col & 0xFF;

        if (cmd == "back") { cmd_state = CMD_COLOR_ADJ_MENU; cadj_print_menu(); return; }
        if (cmd == "save") {
            prefs.putBytes("col_blob", &sys_colors, sizeof(SystemColors));
            prefs.putBool("has_colors", true);
            dual_println(">>> ✅ Saved."); cmd_state = CMD_COLOR_ADJ_MENU; cadj_print_menu(); return;
        }
        if (cmd == "exit") { cmd_state = CMD_IDLE; current_mode = MODE_PLAY; dual_println(">>> Exited coloradj."); return; }

        if (cmd.startsWith("step ")) { cadj_step = constrain(cmd.substring(5).toInt(), 1, 50); cadj_print_edit_prompt(); return; }

        if      (cmd == "r+")  r = cadj_clamp(r + cadj_step);
        else if (cmd == "r-")  r = cadj_clamp(r - cadj_step);
        else if (cmd == "g+")  g = cadj_clamp(g + cadj_step);
        else if (cmd == "g-")  g = cadj_clamp(g - cadj_step);
        else if (cmd == "b+")  b = cadj_clamp(b + cadj_step);
        else if (cmd == "b-")  b = cadj_clamp(b - cadj_step);
        else if (cmd == "r++") r = cadj_clamp(r + 5);
        else if (cmd == "r--") r = cadj_clamp(r - 5);
        else if (cmd == "g++") g = cadj_clamp(g + 5);
        else if (cmd == "g--") g = cadj_clamp(g - 5);
        else if (cmd == "b++") b = cadj_clamp(b + 5);
        else if (cmd == "b--") b = cadj_clamp(b - 5);
        else { dual_println(">>> Unknown command."); cadj_print_edit_prompt(); return; }

        col = ((uint32_t)r << 16) | ((uint32_t)g << 8) | b;
        ws2812b.setPixelColor(0, col); ws2812b.show();
        cadj_print_edit_prompt();
    }
}

// ==========================================
// 🛠️ PART 3: SERIAL COMMANDS & SERVICE MENU
// ==========================================

inline void handleServiceMenu() {
    String input = "";
    
    if (Serial2.available() > 0) {
        input = Serial2.readStringUntil('\n');
    } else if (!C64_Amiga_Connected && !dev_mode && Serial.available() > 0) {
        input = Serial.readStringUntil('\n');
    }

    if (input.length() > 0) {
        input.trim(); 

        if (cmd_state == CMD_COLOR_ADJ_MENU || cmd_state == CMD_COLOR_ADJ_EDIT) {
            handleColorAdj(input);
            return;
        }

        if (cmd_state == CMD_WAIT_NEW_CHOICE) {
            if (input == "1") {
                cmd_state = CMD_IDLE;
                current_mode = MODE_SNIFFER; 
                sniff_to_nvs = true;         
                reset_sniffer();
                if (s_pkt_q) xQueueReset(s_pkt_q);
                dual_println("\n>>> HARDWARE LEARNING WIZARD ARMED! <<<");
                dual_println(">>> Keep hands off for 2 seconds to calibrate neutral position...");
            } else if (input == "2") {
                dual_print("\n>>> Enter a NAME for the new manual C++ profile: ");
                cmd_state = CMD_WAIT_NAME_MANUAL; 
            } else if (input == "exit") {
                cmd_state = CMD_IDLE;
                current_mode = MODE_SERVICE;
                dual_println("\n>> Exited wizard.");
            } else {
                dual_println(">>> Invalid choice. Enter 1 or 2, or type 'exit'.");
            }
            return;
        }
        else if (cmd_state == CMD_WAIT_NAME_MANUAL) {
            sniff_profile_name = input;
            cmd_state = CMD_IDLE; 
            current_mode = MODE_SNIFFER; 
            sniff_to_nvs = false;
            reset_sniffer(); 
            if (s_pkt_q) xQueueReset(s_pkt_q); 
            dual_println("\n>>> WIZARD ARMED! <<<");
            dual_println("⏳ Waiting for neutral position calibration... (DO NOT touch the gamepad)"); 
            dual_println("If nothing happens within 2 seconds, press and release a button to 'wake' it."); 
            return;
        }

        // --- NORMAL COMMAND PARSER ---
        String command = input;
        String args = "";
        
        int spaceIdx = command.indexOf(' ');
        if (spaceIdx > 0) {
            args = command.substring(spaceIdx + 1);
            command = command.substring(0, spaceIdx);
        }
        
        command.toLowerCase(); 

        if (command == "service") {
            current_mode = MODE_SERVICE;
            if (!C64_Amiga_Connected && !dev_mode) {
                dual_println("\n=== 🛠️  STANDALONE MENU (USB-C) 🛠️ ===");
                dual_println("--------------------------------");
                dual_println(" ℹ️ 'info'    : System & Profile Diagnostics");
                dual_println(" ⚠️ 'factory' : Erase ALL Saved Settings & Reset");
                dual_println(" 🎛️ 'gpio'    : Real-time hardware states dashboard");
                dual_println(" 🛠️ 'devmode' : Toggle Developer Mode (on/off)");
                dual_println(" 🔄 'reboot'  : Restart the device softly");
                dual_println(" ⚡ 'flash'   : Reboot into Programming/DFU Mode");
                dual_println(" 🚪 'exit'    : Exit menu");
                dual_println("================================");
            } else {
                dual_println("\n=== 🛠️  SERVICE MENU  🛠️ ==="); 
                dual_println("  ⚙️  PAD: " + String(device_connected ? current_profile.name : "None connected")); 
                dual_println("--------------------------------");
                dual_println(" 🪄 'new'     : Map a new pad (Learn/NVS or C++ Sniffer)"); 
                dual_println(" 👁️ 'raw'     : Show raw USB hex data stream"); 
                dual_println(" 🎮 'test'    : Test logical buttons mapping"); 
                dual_println(" 🐭 'mousetest': Mouse speed and Packets"); 
                dual_println(" ⏱️ 'lag'     : Measure USB Polling Rate and Input Lag"); 
                dual_println(" 🎛️ 'gpio'    : Real-time hardware states dashboard"); 
                dual_println(" 🎨 'coloradj': Interactive LED color calibration");
                if (dev_mode) {
                    dual_println(" 💡 'ledtest' : Cycle all LED palette colors (dev only)");
                    dual_println(" 🛠️ 'devmode' : Toggle Developer Mode (on/off)");
                }
                dual_println(" 🔄 'reboot'  : Restart the device softly");
                dual_println(" ⚡ 'flash'   : Reboot into Programming/DFU Mode"); 
                dual_println(" 🚪 'exit'    : Exit menu and return to normal play"); 
                dual_println("================================");
            }
        }
        else if (command == "devmode") {
            if (args == "on") {
                prefs.putBool("dev_mode", true);
                dual_println("\n>>> 🛠️ DEVELOPER MODE ENABLED! Rebooting... <<<");
                delay(1000); ESP.restart();
            } else if (args == "off") {
                prefs.putBool("dev_mode", false);
                dual_println("\n>>> 🛠️ DEVELOPER MODE DISABLED! Rebooting... <<<");
                delay(1000); ESP.restart();
            } else {
                dual_println(">>> Current Status: " + String(dev_mode ? "ON" : "OFF") + "\n>>> Syntax: devmode on | devmode off");
            }
        }
        else if (!C64_Amiga_Connected && command == "info") {
            dual_println("\n--- ℹ️ SYSTEM INFO (NVS) ---");
            dual_println("Mouse Config Saved: " + String((prefs.isKey("c64_speed") || prefs.isKey("amiga_speed")) ? "YES" : "NO"));
            dual_println("Custom Gamepad Saved: " + String(prefs.getBool("has_custom", false) ? "YES" : "NO"));
            dual_println("Custom LED Palette: " + String(prefs.getBool("has_colors", false) ? "YES" : "NO"));
            dual_println("Developer Mode: " + String(dev_mode ? "ON" : "OFF"));
            dual_println("Switch Invert Logic: " + String(prefs.getBool("inv_switch", false) ? "ACTIVE" : "INACTIVE"));
            dual_println("----------------------------");
        }
        else if (!C64_Amiga_Connected && command == "factory") {
            dual_println("\n>>> ⚠️ FACTORY RESET... Clearing NVS ⚠️ <<<");
            prefs.clear();
            delay(1000);
            ESP.restart();
        }
        else if (current_mode != MODE_PLAY || command == "exit") {
            bool req_bare_metal = (command == "test" || command == "raw" || command == "lag" || command == "mousetest" || command == "gpio" || command == "new");
            
            if (req_bare_metal && !C64_Amiga_Connected && command != "gpio" && !dev_mode) {
                dual_println("\n>>> ERROR: Command unavailable in Standalone Mode. Enable Dev Mode first (devmode on).");
                return;
            }
            
            if (command == "new" && (C64_Amiga_Connected || dev_mode)) { 
                if (current_mode == MODE_PLAY) release_all_outputs();
                dual_println("\n=== NEW GAMEPAD WIZARD ===");
                dual_println("1) Hardware Learning & Save to NVS (Ready to play)");
                dual_println("2) Serial Sniffer to C++ Code (For JoystickProfiles.h)");
                dual_println("Enter choice (1 or 2):");
                cmd_state = CMD_WAIT_NEW_CHOICE;
            }
            else if (command == "raw" && (C64_Amiga_Connected || dev_mode))   { if (current_mode == MODE_PLAY) release_all_outputs(); current_mode = MODE_RAW; dual_println(">>> RAW mode active!"); } 
            else if (command == "test" && (C64_Amiga_Connected || dev_mode))  { if (current_mode == MODE_PLAY) release_all_outputs(); current_mode = MODE_DEBUG; dual_println(">>> TEST mode active!"); } 
            else if (command == "gpio")  { 
                if (current_mode == MODE_PLAY) release_all_outputs();
                current_mode = MODE_GPIO;
                last_gpio_state = 0xFFFF; 
                dual_println(">>> Starting GPIO Dashboard...");
            }
            else if (command == "lag" && (C64_Amiga_Connected || dev_mode)) {
                if (current_mode == MODE_PLAY) release_all_outputs();
                current_mode = MODE_POLLING;
                polling_packet_count = 0; 
                polling_start_time = 0; 
                polling_neutral_saved = false; 
                polling_active = true;
                
                dual_println("\n>>> Starting Polling Tester (Smart Trigger)... <<<"); 
                char buf[128];
                sprintf(buf, "Connected device: %s (VID:%04x PID:%04x)\n\n", device_connected ? current_profile.name : "None", connected_vid, connected_pid); 
                dual_print(buf);
                if (!device_connected) {
                    dual_println("⚠️ No gamepad connected! Connect a pad and try again."); 
                    current_mode = MODE_SERVICE;
                } else {
                    dual_println("🕹️  Timer is waiting... Move the stick and press buttons to trigger it!"); 
                }
            }
            else if (command == "exit")  { 
                if (!C64_Amiga_Connected && !dev_mode) {
                    current_mode = MODE_SERVICE;
                    dual_println("\n>>> Exited menu. Type 'service' to open it again.");
                } else {
                    current_mode = MODE_PLAY;
                    dual_println("\n\n>>> PLAY mode (Zero-Lag) restored! Normal operation resumed. <<<");
                }
            }
            else if (command == "amiga" && C64_Amiga_Connected) { configure_console_mode(true); } 
            else if (command == "c64" && C64_Amiga_Connected)   { configure_console_mode(false); } 
            else if (command == "reboot") {
                dual_println("\n>>> REBOOTING DEVICE... <<<");
                delay(500); 
                ESP.restart();
            }
            else if (command == "flash") {
                dual_println("\n>>> REBOOTING INTO PROGRAMMING/DFU Mode... <<<");
                delay(500); 
                REG_WRITE(RTC_CNTL_OPTION1_REG, RTC_CNTL_FORCE_DOWNLOAD_BOOT);
                ESP.restart();
            }
            else if (command.startsWith("gp")) {
                if (current_mode != MODE_GPIO) {
                    dual_println(">>> ERROR: Command available ONLY in 'gpio' mode. Type 'gpio' first.\n");
                } else {
                    String pinStr = command.substring(2);
                    pinStr.trim();
                    
                    if (pinStr.length() > 0) {
                        int pinNum = pinStr.toInt();
                        if (pinNum >= 0 && pinNum <= 48) {
                            last_gpio_state = 0xFFFF; 
                            run_gpio_diagnostics(); 
                            int val = digitalRead(pinNum);
                            dual_println("===========================");
                            dual_println(" 🔎 LOGIC PROBE: GPIO " + String(pinNum) + " ");
                            dual_println("===========================");
                            dual_println(" STATE: " + String(val ? "HIGH" : "LOW") + " ");
                            dual_println("===========================\n");
                        } else {
                            dual_println("\n>>> ERROR: Invalid GPIO pin (0-48).");
                        }
                    } else {
                        dual_println("\n>>> ERROR: Syntax is 'gp<number>' (e.g., gp10).");
                    }
                }
            }
            else if (command == "ledtest" && dev_mode) {
                dual_println("\n>>> 💡 LED TEST: cycling all palette colors (1.2s each)...");
                uint32_t palette[] = {
                    sys_colors.idle_c64, sys_colors.idle_amiga,
                    sys_colors.dir_glowing, sys_colors.btn_fire1,
                    sys_colors.btn_fire2, sys_colors.btn_fire3,
                    sys_colors.btn_alt, sys_colors.btn_auto
                };
                const char* names[] = { "Idle C64","Idle Amiga","Directions","Fire 1","Fire 2","Fire 3","Alt UP","Autofire" };
                for (int i = 0; i < 8; i++) {
                    dual_printf("  [%d] %s\n", i+1, names[i]);
                    ws2812b.setPixelColor(0, palette[i]); ws2812b.show();
                    delay(1200);
                }
                ws2812b.setPixelColor(0, is_amiga ? sys_colors.idle_amiga : sys_colors.idle_c64);
                ws2812b.show();
                dual_println(">>> LED TEST complete. Idle color restored.");
            }
            else if (command == "coloradj") {
                cmd_state = CMD_COLOR_ADJ_MENU;
                cadj_print_menu();
            }
            else if (command == "mousetest" && (C64_Amiga_Connected || dev_mode)) {
                if (!is_mouse_connected) {
                    dual_println(">>> ERROR: No mouse detected. Connect a USB mouse before running the test.");
                } else {
                    dual_println("\n>>> 🐭 MOUSE BENCHMARK STARTED <<<");
                    dual_println("Move the mouse around in circles on the pad quickly for 5 seconds...");
                    
                    int max_dx = 0;
                    int max_dy = 0;
                    unsigned long start_time = millis();
                    
                    while(millis() - start_time < 5000) {
                        usb_host_client_handle_events(s_client, 1);
                        pkt_t p;
                        if (xQueueReceive(s_pkt_q, &p, 0) == pdTRUE) {
                            int offset = (p.len >= 5) ? 1 : 0;
                            int8_t dx = (int8_t)p.data[1 + offset];
                            int8_t dy = (int8_t)p.data[2 + offset];
                            if (abs(dx) > max_dx) max_dx = abs(dx);
                            if (abs(dy) > max_dy) max_dy = abs(dy);
                        }
                    }
                    
                    dual_println("\n=======================================");
                    dual_println(" 📊 MOUSE BENCHMARK RESULTS ");
                    dual_println("=======================================");
                    char buf[64];
                    sprintf(buf, " Max peak X: %d DPI per packet\n Max peak Y: %d DPI per packet", max_dx, max_dy);
                    dual_println(String(buf));
                    dual_println("---------------------------------------");
                    
                    if (max_dx < 15) {
                        dual_println(" 🎯 Diagnosis: PERFECT Mouse (Retro-Friendly).");
                    } else if (max_dx < 35) {
                        dual_println(" ⚠️ Diagnosis: AVERAGE Mouse.");
                    } else if (max_dx < 80) {
                        dual_println(" 🏎️ Diagnosis: FAST Mouse.");
                    } else {
                        dual_println(" 🚀 Diagnosis: GAMING Mouse (Ultra-High DPI).");
                    }
                    dual_println("=======================================\n");
                }
            }
            else {
                dual_println(">>> Unknown command. Type 'service' for help.");
            }
        } 
    } 
}