// ==========================================
// USB to C64/Amiga Adapter - Advanced v1.1
// File: ServiceTools.h
// Description: Unified module for Sniffer, Auto-Dumper, and Serial CLI Menu
// ==========================================
#pragma once

#include <Arduino.h>
#include "soc/rtc_cntl_reg.h" // Required for the 'flash' command
#include "Globals.h"

// --- FORWARD DECLARATIONS ---
// These are still needed because they are defined in Hardware.h / CoreTasks.h
extern void configure_console_mode(bool is_amiga); 
extern void run_gpio_diagnostics();


// ==========================================
// 🕵️‍♂️ PART 1: JOYSTICK SNIFFER ENGINE
// ==========================================

#define SNIFFER_SERIAL Serial2

enum SniffStep { 
    S_INIT, S_START, 
    S_WAIT_UP,   S_REL_UP, 
    S_WAIT_DOWN, S_REL_DOWN, 
    S_WAIT_LEFT, S_REL_LEFT, 
    S_WAIT_RIGHT,S_REL_RIGHT, 
    S_WAIT_F1,   S_REL_F1, 
    S_WAIT_F2,   S_REL_F2, 
    S_WAIT_F3,   S_REL_F3,      
    S_WAIT_UPALT,S_REL_UPALT, 
    S_WAIT_AUTO, S_REL_AUTO, 
    S_WAIT_LS_X, S_REL_LS_X,
    S_WAIT_LS_Y, S_REL_LS_Y,
    S_WAIT_RS_X, S_REL_RS_X,
    S_WAIT_RS_Y, S_REL_RS_Y,
    S_DONE 
};

static SniffStep sniff_step = S_START;
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

// --- Multiplexer Detection Variables ---
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
        SNIFFER_SERIAL.print("RAW DATA: ");
        for(int i = 0; i < check_len; i++) {
            SNIFFER_SERIAL.printf("[%d]:%3d  ", i, data[i]);
            last_raw[i] = data[i];
        }
        SNIFFER_SERIAL.println();
    }
}

inline void run_sniffer(uint16_t vid, uint16_t pid, const uint8_t *data, int len) {
    if (len <= 0) return;

    if (sniff_step == S_INIT) {
        if (!first_packet_received) {
            first_packet_received = true;
            sniff_timer = millis();
            for(int i=0; i<64; i++) { dat_min[i] = 255; dat_max[i] = 0; }
            SNIFFER_SERIAL.println("\n>>> PAD IS AWAKE! Hands off completely.");
            SNIFFER_SERIAL.println(">>> Wait 2 seconds while I profile background noise...");
        }

        if (millis() - sniff_timer < 2000) {
            for(int i = 0; i < len; i++) {
                dat_neutral[i] = data[i];
                if (data[i] < dat_min[i]) dat_min[i] = data[i];
                if (data[i] > dat_max[i]) dat_max[i] = data[i];
            }
            return; 
        }

        // --- Multiplexer Check ---
        if (dat_max[0] != dat_min[0]) {
            detected_multiplexer = true;
            detected_report_id = dat_neutral[0]; // Lock to one of the alternating IDs
        }

        for(int i = 0; i < len; i++) {
            int noise_level = dat_max[i] - dat_min[i]; 

            // Ignore jittery axes (>10) OR multiplexer alternating IDs on byte 0 (>0)
            if (noise_level > 10 || (noise_level > 0 && i == 0)) { 
                dat_thresh[i] = 255; 
            } else {
                if (dat_neutral[i] >= 100 && dat_neutral[i] <= 155) dat_thresh[i] = 60; 
                else dat_thresh[i] = 0; 
            }
        }
        
        if (detected_multiplexer) {
             SNIFFER_SERIAL.printf(">>> MULTIPLEXER DETECTED! Locked to Port/ID: %d\n", detected_report_id);
        }

        sniff_step = S_START;
    }

    if (sniff_step == S_START) {
        SNIFFER_SERIAL.println("\n--- SNIFFER ENGINE (GLOBAL NEUTRAL LOCK) ---");
        sniff_step = S_WAIT_UP;
        SNIFFER_SERIAL.println(">>> Noise filtered. Smart sensors active.");
        SNIFFER_SERIAL.println("[?] PRESS AND HOLD: UP on D-PAD");
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
            if (!is_neutral) { b_up = changed_byte; v_up = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_up, v_up); sniff_step = S_REL_UP; } break;
        case S_REL_UP: 
            if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: DOWN"); sniff_step = S_WAIT_DOWN; } break;
        
        case S_WAIT_DOWN: 
            if (!is_neutral) { b_down = changed_byte; v_down = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_down, v_down); sniff_step = S_REL_DOWN; } break;
        case S_REL_DOWN: 
            if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: LEFT"); sniff_step = S_WAIT_LEFT; } break;
            
        case S_WAIT_LEFT: 
            if (!is_neutral) { b_left = changed_byte; v_left = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_left, v_left); sniff_step = S_REL_LEFT; } break;
        case S_REL_LEFT: 
            if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: RIGHT"); sniff_step = S_WAIT_RIGHT; } break;

        case S_WAIT_RIGHT: 
            if (!is_neutral) { b_right = changed_byte; v_right = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_right, v_right); sniff_step = S_REL_RIGHT; } break;
        case S_REL_RIGHT: 
            if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: FIRE 1"); sniff_step = S_WAIT_F1; } break;
        
        case S_WAIT_F1: 
            if (!is_neutral) { b_f1 = changed_byte; v_f1 = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_f1, v_f1); sniff_step = S_REL_F1; } break;
        case S_REL_F1: 
            if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: FIRE 2"); sniff_step = S_WAIT_F2; } break;

        case S_WAIT_F2: 
            if (!is_neutral) { b_f2 = changed_byte; v_f2 = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_f2, v_f2); sniff_step = S_REL_F2; } break;
        case S_REL_F2: 
            if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: FIRE 3"); sniff_step = S_WAIT_F3; } break;

        case S_WAIT_F3: 
            if (!is_neutral) { b_f3 = changed_byte; v_f3 = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_f3, v_f3); sniff_step = S_REL_F3; } break;
        case S_REL_F3: 
            if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: ALT UP BUTTON"); sniff_step = S_WAIT_UPALT; } break;

        case S_WAIT_UPALT: 
            if (!is_neutral) { b_up_alt = changed_byte; v_up_alt = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_up_alt, v_up_alt); sniff_step = S_REL_UPALT; } break;
        case S_REL_UPALT: 
            if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: AUTOFIRE BUTTON"); sniff_step = S_WAIT_AUTO; } break;

        case S_WAIT_AUTO: 
            if (!is_neutral) { b_auto = changed_byte; v_auto = changed_val; SNIFFER_SERIAL.printf("OK! B:%d, V:%d\n", b_auto, v_auto); sniff_step = S_REL_AUTO; } break;
        
        case S_REL_AUTO: 
            if (is_neutral) { 
                SNIFFER_SERIAL.println("\n--- ANALOG STICKS (Optional) ---");
                SNIFFER_SERIAL.println("[?] MOVE LEFT STICK FULLY RIGHT (Or press FIRE 1 to skip all analogs)"); 
                sniff_step = S_WAIT_LS_X; 
            } break;

        case S_WAIT_LS_X: 
            if (changed_byte == b_f1) { 
                SNIFFER_SERIAL.println(">>> Analog mapping skipped.");
                sniff_step = S_DONE; 
            } else if (!is_neutral && abs((int)data[changed_byte] - (int)dat_neutral[changed_byte]) > 40) {
                b_ls_x = changed_byte;
                SNIFFER_SERIAL.printf("OK! Left Stick X = Byte %d\n", b_ls_x);
                sniff_step = S_REL_LS_X;
            } break;

        case S_REL_LS_X: 
            if (is_neutral) { 
                SNIFFER_SERIAL.println("[?] MOVE LEFT STICK FULLY DOWN"); 
                sniff_step = S_WAIT_LS_Y; 
            } break;

        case S_WAIT_LS_Y: 
            if (!is_neutral && abs((int)data[changed_byte] - (int)dat_neutral[changed_byte]) > 40) {
                b_ls_y = changed_byte;
                SNIFFER_SERIAL.printf("OK! Left Stick Y = Byte %d\n", b_ls_y);
                sniff_step = S_REL_LS_Y;
            } break;

        case S_REL_LS_Y: 
            if (is_neutral) { 
                SNIFFER_SERIAL.println("[?] MOVE RIGHT STICK FULLY RIGHT (Or press FIRE 1 to skip right stick)"); 
                sniff_step = S_WAIT_RS_X; 
            } break;

        case S_WAIT_RS_X: 
            if (changed_byte == b_f1) {
                SNIFFER_SERIAL.println(">>> Right Stick skipped.");
                sniff_step = S_DONE; 
            } else if (!is_neutral && abs((int)data[changed_byte] - (int)dat_neutral[changed_byte]) > 40) {
                b_rs_x = changed_byte;
                SNIFFER_SERIAL.printf("OK! Right Stick X = Byte %d\n", b_rs_x);
                sniff_step = S_REL_RS_X;
            } break;

        case S_REL_RS_X: 
            if (is_neutral) { 
                SNIFFER_SERIAL.println("[?] MOVE RIGHT STICK FULLY DOWN"); 
                sniff_step = S_WAIT_RS_Y; 
            } break;

        case S_WAIT_RS_Y: 
            if (!is_neutral && abs((int)data[changed_byte] - (int)dat_neutral[changed_byte]) > 40) {
                b_rs_y = changed_byte;
                SNIFFER_SERIAL.printf("OK! Right Stick Y = Byte %d\n", b_rs_y);
                sniff_step = S_REL_RS_Y;
            } break;

        case S_REL_RS_Y: 
            if (is_neutral) { 
                SNIFFER_SERIAL.println("\n>>> GENERATING PROFILE... <<<"); 
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

                SNIFFER_SERIAL.println("\n// --- COPY THIS INTO JoystickProfiles.h ---");
                SNIFFER_SERIAL.println("{");
                SNIFFER_SERIAL.printf("  .name = \"%s\",\n", sniff_profile_name.c_str());
                SNIFFER_SERIAL.printf("  .vid = %d, .pid = %d,\n", vid, pid);
                SNIFFER_SERIAL.printf("  .use_report_id = %s, .report_id_val = %d,\n", detected_multiplexer ? "true" : "false", detected_report_id);
                SNIFFER_SERIAL.printf("  .dpad_type = %s,\n", type_str.c_str());
                SNIFFER_SERIAL.printf("  .byte_x = %d, .byte_y = %d, .byte_analog_x = %d, .byte_analog_y = %d, .byte_analog_right_x = %d, .byte_analog_right_y = %d,\n", b_left, b_up, b_ls_x, b_ls_y, b_rs_x, b_rs_y);
                SNIFFER_SERIAL.printf("  .byte_fire1 = %d, .byte_fire2 = %d, .byte_fire3 = %d, .byte_up_alt = %d, .byte_autofire = %d, .byte_autofire_off = 0,\n", b_f1, b_f2, b_f3, b_up_alt, b_auto);
                SNIFFER_SERIAL.printf("  .val_up = %d, .val_down = %d, .val_left = %d, .val_right = %d,\n", v_up, v_down, v_left, v_right);
                SNIFFER_SERIAL.printf("  .val_fire1 = %d, .val_fire2 = %d, .val_fire3 = %d, .val_up_alt = %d, .val_autofire = %d, .val_autofire_off = 0x00,\n", m_f1, m_f2, m_f3, m_up_alt, m_auto);
                SNIFFER_SERIAL.println("  .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW");
                SNIFFER_SERIAL.println("},");
                SNIFFER_SERIAL.println("// -----------------------------------------");
                config_printed = true;
            }
            break;
    }
}


// ==========================================
// 🧠 PART 2: SMART AUTO-DUMPER (HTML -> NATIVE)
// ==========================================

inline void execute_html_dump() {
    Serial2.println("\n>>> HTML TO NATIVE AUTO-CONVERTER RUNNING... <<<");
    int b_up=0, b_down=0, b_left=0, b_right=0;
    int b_f1=0, b_f2=0, b_f3=0, b_up_alt=0, b_auto=0, b_auto_off=0;
    uint8_t v_up=0, v_down=0, v_left=0, v_right=0;
    uint8_t v_f1=0, v_f2=0, v_f3=0, v_up_alt=0, v_auto=0, v_auto_off=0;
    int b_analog_x=0, b_analog_y=0, b_analog_rx=0, b_analog_ry=0;
    bool is_bitmask = false;

#if HAS_HTML_CONFIGURATOR
    // Extract Analog Sticks properly
#if JM_USE_ANALOG_MOUSE == 1
    size_t count_x = sizeof(JM_MOUSE_X_INDEXES)/sizeof(JM_MOUSE_X_INDEXES[0]);
    size_t count_y = sizeof(JM_MOUSE_Y_INDEXES)/sizeof(JM_MOUSE_Y_INDEXES[0]);
    if (count_x > 0) b_analog_x = JM_MOUSE_X_INDEXES[0];
    if (count_y > 0) b_analog_y = JM_MOUSE_Y_INDEXES[0];
    if (count_x > 1) b_analog_rx = JM_MOUSE_X_INDEXES[1]; 
    if (count_y > 1) b_analog_ry = JM_MOUSE_Y_INDEXES[1]; 
#endif

    for (size_t i = 0; i < JM_JOY_RULES_COUNT; i++) {
        const JM_Rule &r = JM_JOY_RULES[i];
        if (r.op == JM_BITANY) is_bitmask = true;

        if (r.func == JM_UP) { 
            if (r.index == JM_DPAD_INDEX && r.value <= 15) { b_up = r.index; v_up = r.value; }
            else { b_up_alt = r.index; v_up_alt = r.value; }
        }
        if (r.func == JM_DOWN && r.index == JM_DPAD_INDEX && r.value <= 15) { b_down = r.index; v_down = r.value; }
        if (r.func == JM_LEFT && r.index == JM_DPAD_INDEX && r.value <= 15) { b_left = r.index; v_left = r.value; }
        if (r.func == JM_RIGHT && r.index == JM_DPAD_INDEX && r.value <= 15) { b_right = r.index; v_right = r.value; }
        
        if (r.func == JM_FIRE) { b_f1 = r.index; v_f1 = r.value; }
        if (r.func == JM_FIRE2) { b_f2 = r.index; v_f2 = r.value; }
        if (r.func == JM_FIRE3) { b_f3 = r.index; v_f3 = r.value; }
        if (r.func == JM_AUTOFIRE_ON) { b_auto = r.index; v_auto = r.value; }
        if (r.func == JM_AUTOFIRE_OFF) { b_auto_off = r.index; v_auto_off = r.value; }
    }
#endif

    String type_str = is_bitmask ? "BITMASK" : "EXACT_VALUE";
    if (!is_bitmask && v_up == 0 && v_right == 2 && v_down == 4 && v_left == 6) {
        type_str = "HAT_SWITCH";
    }

    Serial2.println("\n// --- COPY THIS INTO YOUR JoystickProfiles.h ---");
    Serial2.println("{");
    Serial2.printf("  .name = \"%s\",\n", sniff_profile_name.c_str());
    Serial2.printf("  .vid = %d, .pid = %d,\n", connected_vid, connected_pid);
    Serial2.printf("  .dpad_type = %s,\n", type_str.c_str());
    Serial2.printf("  .byte_x = %d, .byte_y = %d, .byte_analog_x = %d, .byte_analog_y = %d, .byte_analog_right_x = %d, .byte_analog_right_y = %d,\n", b_left, b_up, b_analog_x, b_analog_y, b_analog_rx, b_analog_ry);
    Serial2.printf("  .byte_fire1 = %d, .byte_fire2 = %d, .byte_fire3 = %d, .byte_up_alt = %d, .byte_autofire = %d, .byte_autofire_off = %d,\n", b_f1, b_f2, b_f3, b_up_alt, b_auto, b_auto_off);
    Serial2.printf("  .val_up = %d, .val_down = %d, .val_left = %d, .val_right = %d,\n", v_up, v_down, v_left, v_right);
    Serial2.printf("  .val_fire1 = %d, .val_fire2 = %d, .val_fire3 = %d, .val_up_alt = %d, .val_autofire = %d, .val_autofire_off = %d,\n", v_f1, v_f2, v_f3, v_up_alt, v_auto, v_auto_off);
    Serial2.println("  .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW");
    Serial2.println("},");
    Serial2.println("// ----------------------------------------------\n");
}


// ==========================================
// 🛠️ PART 3: SERIAL COMMANDS & SERVICE MENU
// ==========================================

inline void handleServiceMenu() {
    if (Serial2.available() > 0) {
        String input = Serial2.readStringUntil('\n');
        input.trim(); 
        
        // --- INTERACTIVE SERIAL PROMPTS --- 
        if (cmd_state == CMD_WAIT_COLOR_CHOICE) {
            if (input == "exit") {
                cmd_state = CMD_IDLE;
                current_mode = MODE_SERVICE; 
                ws2812b.setBrightness(40); // (Or 89 if you prefer energy saving)
                Serial2.println("\n>> Exited Color Test. Returned to Service Menu."); 
            } else {
                int choice = input.toInt();
                if (choice >= 1 && choice <= 13) { 
                    cmd_state = CMD_IDLE; 
                    
                    switch(choice) {
                        case 1:  mix_r = 255; mix_g = 255; mix_b = 255; break; 
                        case 2:  mix_r = 255; mix_g = 128; mix_b = 0;   break; 
                        case 3:  mix_r = 0;   mix_g = 100; mix_b = 0;   break; 
                        case 4:  mix_r = 180; mix_g = 0;   mix_b = 255; break; 
                        case 5:  mix_r = 120; mix_g = 0;   mix_b = 180; break; 
                        case 6:  mix_r = 20;  mix_g = 0;   mix_b = 40;  break; 
                        case 7:  mix_r = 60;  mix_g = 0;   mix_b = 100; break; 
                        case 8:  mix_r = 5;   mix_g = 0;   mix_b = 8;   break; 
                        case 9:  mix_r = 0;   mix_g = 255; mix_b = 0;   break; 
                        case 10: mix_r = 255; mix_g = 0;   mix_b = 0;   break; 
                        case 11: mix_r = 0;   mix_g = 255; mix_b = 255; break; 
                        case 12: mix_r = 0;   mix_g = 0;   mix_b = 255; break; 
                        case 13: mix_r = 255; mix_g = 255; mix_b = 0;   break; 
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
            String ans = input;
            ans.toLowerCase(); 
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
        String command = input;
        command.toLowerCase(); 

        if (command == "service") {
            current_mode = MODE_SERVICE;
            Serial2.println("\n=== 🛠️  SERVICE MENU  🛠️ ==="); 
            Serial2.printf("  ⚙️  ENGINE: %s\n", use_html_configurator ? "HTML HID Configurator" : "Internal Profiler"); 
            Serial2.println("--------------------------------");
            Serial2.println(" 🪄 'new'     : Map a new pad or Auto-Import HTML"); 
            Serial2.println(" 👁️ 'raw'     : Show raw USB hex data stream"); 
            Serial2.println(" 🎮 'test'    : Test logical buttons mapping (Up, Fire...)"); 
            Serial2.println(" 🐭 'mousetest': Mouse speed and Packets"); 
            Serial2.println(" ⏱️ 'lag'     : Measure USB Polling Rate and Input Lag"); 
            Serial2.println(" 🎛️ 'gpio'    : Real-time dashboard of hardware states"); 
            Serial2.println(" 🎨 'color'   : Live RGB Color Mixer (Use gamepad)");  
            Serial2.println(" 🔄 'reboot'  : Restart the device softly");
            Serial2.println(" ⚡ 'flash'   : Reboot into Programming/DFU Mode"); 
            Serial2.println(" 🚪 'exit'    : Exit menu and return to normal play"); 
            Serial2.println("================================\n");
        }
        else if (current_mode != MODE_PLAY || command == "exit") {
            if (command == "new") { 
                if (device_connected && use_html_configurator) { 
                    Serial2.println("\n>>> HTML Profile detected! Do you want to Auto-Import it? (Y/N)");
                    cmd_state = CMD_WAIT_IMPORT; 
                } else {
                    Serial2.print("\n>>> Enter a NAME for the new manual profile: ");
                    cmd_state = CMD_WAIT_NAME_MANUAL; 
                }
            }
            else if (command == "raw")   { current_mode = MODE_RAW; Serial2.println(">>> RAW mode active!"); } 
            else if (command == "test")  { current_mode = MODE_DEBUG; Serial2.println(">>> TEST mode active!"); } 
            else if (command == "gpio")  { 
                current_mode = MODE_GPIO;
                last_gpio_state = 0xFFFF; 
                Serial2.println(">>> Starting GPIO Dashboard..."); 
            }
            else if (command == "lag") {
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
            else if (command == "color") {
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
            else if (command == "exit")  { 
                current_mode = MODE_PLAY;
                ws2812b.setBrightness(40); 
                Serial2.println("\n\n>>> PLAY mode (Zero-Lag) restored! Normal operation resumed. <<<");
            }
            else if (command == "amiga") { configure_console_mode(true); } 
            else if (command == "c64")   { configure_console_mode(false); } 
            else if (command == "reboot") {
                Serial2.println("\n>>> REBOOTING DEVICE... <<<");
                delay(500); 
                ESP.restart();
            }
            else if (command == "flash") {
                Serial2.println("\n>>> REBOOTING INTO PROGRAMMING/DFU Mode... <<<");
                delay(500); 
                REG_WRITE(RTC_CNTL_OPTION1_REG, RTC_CNTL_FORCE_DOWNLOAD_BOOT);
                ESP.restart();
            }
            // --- VIRTUAL LOGIC PROBE (ONLY IN GPIO MODE) ---
            else if (command.startsWith("gp")) {
                if (current_mode != MODE_GPIO) {
                    Serial2.println(">>> ERROR: Command available ONLY in 'gpio' mode. Type 'gpio' first.");
                } else {
                    String pinStr = command.substring(2);
                    pinStr.trim();
                    
                    if (pinStr.length() > 0) {
                        int pinNum = pinStr.toInt();
                        
                        if (pinNum >= 0 && pinNum <= 48) {
                            
                            // 1. Force refresh and print the TABLE NOW
                            last_gpio_state = 0xFFFF; 
                            run_gpio_diagnostics(); 

                            // 2. Read the pin and print the RESULT BELOW the table
                            int val = digitalRead(pinNum);
                            Serial2.println("===========================");
                            Serial2.printf(" 🔎 LOGIC PROBE: GPIO %02d \n", pinNum);
                            Serial2.println("===========================");
                            Serial2.printf(" STATE: %s \n", val ? "HIGH" : "LOW");
                            Serial2.println("===========================\n");
                            
                        } else {
                            Serial2.println("\n>>> ERROR: Invalid GPIO pin (0-48).");
                        }
                    } else {
                        Serial2.println("\n>>> ERROR: Syntax is 'gp<number>' (e.g., gp10).");
                    }
                }
            }
            // --- MOUSE BENCHMARK (FLUIDITY TEST) ---
            else if (command == "mousetest") {
                if (!is_mouse_connected) {
                    Serial2.println(">>> ERROR: No mouse detected. Connect a USB mouse before running the test.");
                } else {
                    Serial2.println("\n>>> 🐭 MOUSE BENCHMARK STARTED <<<");
                    Serial2.println("Move the mouse around in circles on the pad quickly for 5 seconds...");
                    
                    int max_dx = 0;
                    int max_dy = 0;
                    unsigned long start_time = millis();
                    
                    // 5-second loop that drains USB packets
                    while(millis() - start_time < 5000) {
                        // Keep USB enumeration alive
                        usb_host_client_handle_events(s_client, 1);
                        
                        pkt_t p;
                        // Read from USB queue
                        if (xQueueReceive(s_pkt_q, &p, 0) == pdTRUE) {
                            int offset = (p.len >= 5) ? 1 : 0;
                            int8_t dx = (int8_t)p.data[1 + offset];
                            int8_t dy = (int8_t)p.data[2 + offset];
                            
                            // Record absolute maximum peaks
                            if (abs(dx) > max_dx) max_dx = abs(dx);
                            if (abs(dy) > max_dy) max_dy = abs(dy);
                        }
                    }
                    
                    // Print Diagnosis
                    Serial2.println("\n=======================================");
                    Serial2.println(" 📊 MOUSE BENCHMARK RESULTS ");
                    Serial2.println("=======================================");
                    Serial2.printf(" Max peak X: %d DPI per packet\n", max_dx);
                    Serial2.printf(" Max peak Y: %d DPI per packet\n", max_dy);
                    Serial2.println("---------------------------------------");
                    
                    if (max_dx < 15) {
                        Serial2.println(" 🎯 Diagnosis: PERFECT Mouse (Retro-Friendly).");
                        Serial2.println("    No divider needed (dpi_divider = 1.0).");
                    } 
                    else if (max_dx < 35) {
                        Serial2.println(" ⚠️ Diagnosis: AVERAGE Mouse.");
                        Serial2.println("    Recommended: dpi_divider = 2.0");
                    } 
                    else if (max_dx < 80) {
                        Serial2.println(" 🏎️ Diagnosis: FAST Mouse.");
                        Serial2.println("    Recommended: dpi_divider = 3.0 or 4.0");
                    } 
                    else {
                        Serial2.println(" 🚀 Diagnosis: GAMING Mouse (Ultra-High DPI).");
                        Serial2.println("    Recommended: dpi_divider = 5.0 or higher!");
                    }
                    Serial2.println("=======================================\n");
                }
            }
            else {
                Serial2.println(">>> Unknown command. Type 'service' for help.");
            }
        } 
    } 
}