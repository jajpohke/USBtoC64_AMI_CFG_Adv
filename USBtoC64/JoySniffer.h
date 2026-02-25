#pragma once
#include <Arduino.h>

#define SNIFFER_SERIAL Serial2

extern String sniff_profile_name; 

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

static void reset_sniffer() {
    sniff_step = S_INIT;
    config_printed = false;
    b_ls_x = 0; b_ls_y = 0; b_rs_x = 0; b_rs_y = 0;
    first_packet_received = false; 
    sniff_timer = millis();
    for(int i = 0; i < 64; i++) dat_thresh[i] = 2; 
}

static void run_raw_sniffer(const uint8_t *data, int len) {
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

static void run_sniffer(uint16_t vid, uint16_t pid, const uint8_t *data, int len) {
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

        for(int i = 0; i < len; i++) {
            int noise_level = dat_max[i] - dat_min[i]; 

            if (noise_level > 10) { 
                dat_thresh[i] = 255; 
            } else {
                if (dat_neutral[i] >= 100 && dat_neutral[i] <= 155) dat_thresh[i] = 60; 
                else dat_thresh[i] = 0; 
            }
        }
        sniff_step = S_START;
    }

    if (sniff_step == S_START) {
        SNIFFER_SERIAL.println("\n--- SNIFFER v2.4.1 (GLOBAL NEUTRAL LOCK) ---");
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

    // FIX v2.4.1: Tutti gli stati di "Release" ora pretendono un "is_neutral == true" globale.
    // Nessun comando successivo verrà stampato se il pad non è tornato in quiete totale.
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