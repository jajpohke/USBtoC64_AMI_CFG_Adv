#pragma once
#include <Arduino.h>

#define SNIFFER_SERIAL Serial2

extern String sniff_profile_name; // Declared externally, defined in the .ino file

enum SniffStep { 
    S_INIT, S_START, 
    S_WAIT_UP,   S_REL_UP, 
    S_WAIT_DOWN, S_REL_DOWN, 
    S_WAIT_LEFT, S_REL_LEFT, 
    S_WAIT_RIGHT,S_REL_RIGHT, 
    S_WAIT_F1,   S_REL_F1, 
    S_WAIT_F2,   S_REL_F2, 
    S_WAIT_UPALT,S_REL_UPALT, 
    S_WAIT_AUTO, S_REL_AUTO, 
    S_DONE 
};

static SniffStep sniff_step = S_START;
static uint8_t dat_neutral[64];
static int b_up, b_down, b_left, b_right, b_f1, b_f2, b_up_alt, b_auto;
static uint8_t v_up, v_down, v_left, v_right, v_f1, v_f2, v_up_alt, v_auto;
static bool config_printed = false;
static unsigned long sniff_timer = 0; 

static void reset_sniffer() {
    sniff_step = S_INIT;
    config_printed = false;
    sniff_timer = millis();
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
        // Continuously record packets for the first second to bypass wireless wake-up jitter
        memcpy(dat_neutral, data, len > 64 ? 64 : len);
        
        if (millis() - sniff_timer < 1000) return; 
        sniff_step = S_START;
    }

    if (sniff_step == S_START) {
        SNIFFER_SERIAL.println("\n--- SNIFFER v4 (PRO ENGINE MODE) ---");
        sniff_step = S_WAIT_UP;
        SNIFFER_SERIAL.println(">>> OK! Clean neutral position saved.");
        SNIFFER_SERIAL.println("[?] PRESS AND HOLD: UP on D-PAD");
        return;
    }

    bool is_neutral = true;
    int changed_byte = -1;
    uint8_t changed_val = 0;

    for (int i = 0; i < len; i++) {
        if (i >= 3 && i <= 6) continue; // BLOCK ANALOG STICKS (prevents false positives from jitter)
        if (abs((int)data[i] - (int)dat_neutral[i]) > 2) { 
            is_neutral = false; changed_byte = i; changed_val = data[i]; break; 
        }
    }

    switch (sniff_step) {
        case S_WAIT_UP: if (!is_neutral) { b_up = changed_byte; v_up = changed_val; SNIFFER_SERIAL.printf("OK! Byte %d, Val %d\n", changed_byte, changed_val); sniff_step = S_REL_UP; } break;
        case S_REL_UP: if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: DOWN"); sniff_step = S_WAIT_DOWN; } break;
        case S_WAIT_DOWN: if (!is_neutral) { b_down = changed_byte; v_down = changed_val; SNIFFER_SERIAL.printf("OK! Byte %d, Val %d\n", changed_byte, changed_val); sniff_step = S_REL_DOWN; } break;
        case S_REL_DOWN: if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: LEFT"); sniff_step = S_WAIT_LEFT; } break;
        case S_WAIT_LEFT: if (!is_neutral) { b_left = changed_byte; v_left = changed_val; SNIFFER_SERIAL.printf("OK! Byte %d, Val %d\n", changed_byte, changed_val); sniff_step = S_REL_LEFT; } break;
        case S_REL_LEFT: if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: RIGHT"); sniff_step = S_WAIT_RIGHT; } break;
        case S_WAIT_RIGHT: if (!is_neutral) { b_right = changed_byte; v_right = changed_val; SNIFFER_SERIAL.printf("OK! Byte %d, Val %d\n", changed_byte, changed_val); sniff_step = S_REL_RIGHT; } break;
        case S_REL_RIGHT: if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: FIRE 1"); sniff_step = S_WAIT_F1; } break;
        
        case S_WAIT_F1: if (!is_neutral) { b_f1 = changed_byte; v_f1 = changed_val; SNIFFER_SERIAL.printf("OK! Byte %d, Val %d\n", changed_byte, changed_val); sniff_step = S_REL_F1; } break;
        case S_REL_F1: if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: FIRE 2"); sniff_step = S_WAIT_F2; } break;
        case S_WAIT_F2: if (!is_neutral) { b_f2 = changed_byte; v_f2 = changed_val; SNIFFER_SERIAL.printf("OK! Byte %d, Val %d\n", changed_byte, changed_val); sniff_step = S_REL_F2; } break;
        case S_REL_F2: if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: ALT UP BUTTON"); sniff_step = S_WAIT_UPALT; } break;
        case S_WAIT_UPALT: if (!is_neutral) { b_up_alt = changed_byte; v_up_alt = changed_val; SNIFFER_SERIAL.printf("OK! Byte %d, Val %d\n", changed_byte, changed_val); sniff_step = S_REL_UPALT; } break;
        case S_REL_UPALT: if (is_neutral) { SNIFFER_SERIAL.println("[?] PRESS AND HOLD: AUTOFIRE BUTTON"); sniff_step = S_WAIT_AUTO; } break;
        case S_WAIT_AUTO: if (!is_neutral) { b_auto = changed_byte; v_auto = changed_val; SNIFFER_SERIAL.printf("OK! Byte %d, Val %d\n", changed_byte, changed_val); sniff_step = S_REL_AUTO; } break;
        case S_REL_AUTO: if (is_neutral) { SNIFFER_SERIAL.println("\n>>> PROCESSING PROFILE... <<<"); sniff_step = S_DONE; } break;
        
        case S_DONE:
            if (!config_printed) {
                bool is_exact = (b_up == b_left); 
                uint8_t m_f1   = is_exact ? v_f1   : (v_f1   ^ dat_neutral[b_f1]);
                uint8_t m_f2   = is_exact ? v_f2   : (v_f2   ^ dat_neutral[b_f2]);
                uint8_t m_up_alt = is_exact ? v_up_alt : (v_up_alt ^ dat_neutral[b_up_alt]);
                uint8_t m_auto = is_exact ? v_auto : (v_auto ^ dat_neutral[b_auto]);

                SNIFFER_SERIAL.println("\n// --- COPY THIS INTO YOUR JoystickProfiles.h ---");
                SNIFFER_SERIAL.println("{");
                SNIFFER_SERIAL.printf("  .name = \"%s\",\n", sniff_profile_name.c_str());
                SNIFFER_SERIAL.printf("  .vid = %d, .pid = %d,\n", vid, pid);
                SNIFFER_SERIAL.printf("  .dpad_type = %s,\n", is_exact ? "EXACT_VALUE" : "HYBRID_16BIT_BITMASK");
                SNIFFER_SERIAL.printf("  .byte_x = %d, .byte_y = %d, .byte_analog_x = 0, .byte_analog_y = 0,\n", b_left, b_up);
                
                // Added byte_autofire_off mapped to 0 for manual sniffer fallback
                SNIFFER_SERIAL.printf("  .byte_fire1 = %d, .byte_fire2 = %d, .byte_fire3 = 0, .byte_up_alt = %d, .byte_autofire = %d, .byte_autofire_off = 0,\n", b_f1, b_f2, b_up_alt, b_auto);
                
                SNIFFER_SERIAL.printf("  .val_up = %d, .val_down = %d, .val_left = %d, .val_right = %d,\n", v_up, v_down, v_left, v_right);
                SNIFFER_SERIAL.printf("  .val_fire1 = %d, .val_fire2 = %d, .val_fire3 = 0, .val_up_alt = %d, .val_autofire = %d, .val_autofire_off = 0x00,\n", m_f1, m_f2, m_up_alt, m_auto);
                SNIFFER_SERIAL.println("  .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW");
                SNIFFER_SERIAL.println("},");
                SNIFFER_SERIAL.println("// ------------------------------------------------------");
                config_printed = true;
            }
            break;
    }
}