// ==========================================
// USB to C64/Amiga Adapter - Advanced 2.8
// File: AutoDumper.h
// Description: Smart Auto-Dumper (HTML to Native C++ Converter)
// ==========================================
#pragma once

#include <Arduino.h>
#include "Globals.h"

// 🧠 --- AUTO-DUMPER EXECUTION --- 🧠

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