// ==========================================
// USB to C64/Amiga Adapter - Advanced v1.1
// File: JoystickProfiles.h
// Description: Controller profiles database and color palette
// ==========================================
#ifndef JOYSTICK_PROFILES_H
#define JOYSTICK_PROFILES_H

#include <stdint.h>

// 🎨 --- LED COLOR PALETTE (RGB Format from Live Mixer) --- 🎨
#define RGB_COLOR(r, g, b) (((uint32_t)(r) << 16) | ((uint32_t)(g) << 8) | (b))

#define C_RED    RGB_COLOR(85, 0, 0)      // 🔴 Red
#define C_GREEN  RGB_COLOR(0, 85, 0)      // 🟢 Green
#define C_BLUE   RGB_COLOR(0, 0, 85)      // 🔵 Blue
#define C_YELLOW RGB_COLOR(85, 85, 0)     // 🟡 Yellow
#define C_PURPLE RGB_COLOR(45, 0, 45)     // 🟣 Purple
#define C_PINK   RGB_COLOR(85, 0, 85)     // 🩷 Pink
#define C_CYAN   RGB_COLOR(0, 85, 85)     // 🩵 Cyan
#define C_WHITE  RGB_COLOR(60, 60, 60)    // ⚪ White
#define C_GRAY   RGB_COLOR(20, 20, 20)    // 🔘 Gray
#define C_ORANGE RGB_COLOR(85, 8, 0)      // 🟠 Custom C64 Orange
#define C_BLACK  RGB_COLOR(0, 0, 0)       // ⚫ Off

enum DpadType { BITMASK, HAT_SWITCH, AXIS, EXACT_VALUE, HYBRID_16BIT_BITMASK };

struct PadConfig {
    const char* name;
    uint16_t vid;
    uint16_t pid;
    bool use_report_id;
    uint8_t report_id_val;
    DpadType dpad_type;

    int byte_x;        
    int byte_y;        
    int byte_analog_x; 
    int byte_analog_y; 
    int byte_analog_right_x; 
    int byte_analog_right_y; 
    
    int byte_fire1;
    int byte_fire2;
    int byte_fire3;
    int byte_up_alt;
    int byte_autofire;
    int byte_autofire_off; 

    uint8_t val_up;
    uint8_t val_down;
    uint8_t val_left;
    uint8_t val_right;

    uint8_t val_fire1;
    uint8_t val_fire2;
    uint8_t val_fire3;
    uint8_t val_up_alt;
    uint8_t val_autofire;
    uint8_t val_autofire_off; 

    uint32_t color_fire1;
    uint32_t color_fire2;
    uint32_t color_fire3;
    uint32_t color_up_alt;
    uint32_t color_autofire;
};

// --- INTERNAL CONTROLLER PROFILES ---
const PadConfig PROFILES[] = {
    {
        .name = "HORI Mini 4",
        .vid = 0x0f0d, .pid = 0x00ed,
        .dpad_type = BITMASK,
        .byte_x = 2, .byte_y = 0, .byte_analog_x = 0, .byte_analog_y = 0, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 3, .byte_fire2 = 3, .byte_fire3 = 5, .byte_up_alt = 3, .byte_autofire = 3, .byte_autofire_off = 0,
        .val_up = 0x01, .val_down = 0x02, .val_left = 0x04, .val_right = 0x08,
        .val_fire1 = 0x10, .val_fire2 = 0x40, .val_fire3 = 0xFF, .val_up_alt = 0x20, .val_autofire = 0x80, .val_autofire_off = 0x00,
        .color_fire1 = C_BLUE, .color_fire2 = C_PINK, .color_fire3 = C_CYAN, .color_up_alt = C_RED, .color_autofire = C_GREEN
    },
    {
        .name = "Buffalo Classic",
        .vid = 0x0583, .pid = 0x2060,
        .dpad_type = AXIS,
        .byte_x = 0, .byte_y = 1, .byte_analog_x = 0, .byte_analog_y = 0, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 2, .byte_fire2 = 2, .byte_fire3 = 2, .byte_up_alt = 2, .byte_autofire = 2, .byte_autofire_off = 0,
        .val_up = 0, .val_down = 0, .val_left = 0, .val_right = 0,
        .val_fire1 = 0x02, .val_fire2 = 0x08, .val_fire3 = 0x20, .val_up_alt = 0x01, .val_autofire = 0x04, .val_autofire_off = 0x00,
        .color_fire1 = C_YELLOW, .color_fire2 = C_GREEN, .color_fire3 = C_CYAN, .color_up_alt = C_RED, .color_autofire = C_BLUE
    },
    {
        .name = "Sony PS3 Clone",
        .vid = 2064, .pid = 1,
        .dpad_type = AXIS,
        .byte_x = 3, .byte_y = 4, .byte_analog_x = 0, .byte_analog_y = 0, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 5, .byte_fire2 = 5, .byte_fire3 = 6, .byte_up_alt = 5, .byte_autofire = 5, .byte_autofire_off = 0,
        .val_up = 0, .val_down = 255, .val_left = 0, .val_right = 255,
        .val_fire1 = 64, .val_fire2 = 128, .val_fire3 = 2, .val_up_alt = 32, .val_autofire = 16, .val_autofire_off = 0x00,
        .color_fire1 = C_BLUE, .color_fire2 = C_PINK, .color_fire3 = C_CYAN, .color_up_alt = C_RED, .color_autofire = C_GREEN
    },
    {
        .name = "HoriPad GameCube Pokemon",
        .vid = 3853, .pid = 220,
        .dpad_type = HYBRID_16BIT_BITMASK, 
        .byte_x = 2, .byte_y = 0, .byte_analog_x = 6, .byte_analog_y = 8, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 3, .byte_fire2 = 3, .byte_fire3 = 3, .byte_up_alt = 3, .byte_autofire = 3, .byte_autofire_off = 0,
        .val_up = 1, .val_down = 2, .val_left = 4, .val_right = 8,
        .val_fire1 = 16, .val_fire2 = 32, .val_fire3 = 2, .val_up_alt = 128, .val_autofire = 64, .val_autofire_off = 0x00,
        .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_GRAY, .color_autofire = C_ORANGE
    },
    {
        .name = "HoriPad GameCube Peach",
        .vid = 3695, .pid = 389,
        .dpad_type = EXACT_VALUE,
        .byte_x = 2, .byte_y = 2, .byte_analog_x = 3, .byte_analog_y = 4, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 0, .byte_fire2 = 0, .byte_fire3 = 0, .byte_up_alt = 0, .byte_autofire = 0, .byte_autofire_off = 0,
        .val_up = 0, .val_down = 4, .val_left = 6, .val_right = 2,
        .val_fire1 = 2, .val_fire2 = 4, .val_fire3 = 32, .val_up_alt = 8, .val_autofire = 1, .val_autofire_off = 0x00,
        .color_fire1 = C_PINK, .color_fire2 = C_PURPLE, .color_fire3 = C_CYAN, .color_up_alt = C_PINK, .color_autofire = C_ORANGE
    },
    {
        .name = "SNES_Wificlone_Dinput",
        .vid = 10093, .pid = 291,
        .dpad_type = AXIS,
        .byte_x = 0, .byte_y = 1, .byte_analog_x = 0, .byte_analog_y = 0, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 2, .byte_fire2 = 2, .byte_fire3 = 2, .byte_up_alt = 2, .byte_autofire = 2, .byte_autofire_off = 0,
        .val_up = 1, .val_down = 255, .val_left = 1, .val_right = 255,
        .val_fire1 = 1, .val_fire2 = 4, .val_fire3 = 32, .val_up_alt = 2, .val_autofire = 8, .val_autofire_off = 0x00,
        .color_fire1 = C_ORANGE, .color_fire2 = C_GREEN, .color_fire3 = C_CYAN, .color_up_alt = C_RED, .color_autofire = C_BLUE
    },
    {
        .name = "PS4 Orig",
        .vid = 1356, .pid = 1476,
        .dpad_type = HAT_SWITCH,
        .byte_x = 5, .byte_y = 5, .byte_analog_x = 1, .byte_analog_y = 2, .byte_analog_right_x = 3, .byte_analog_right_y = 4,
        .byte_fire1 = 5, .byte_fire2 = 5, .byte_fire3 = 5, .byte_up_alt = 5, .byte_autofire = 6, .byte_autofire_off = 6,
        .val_up = 0, .val_down = 4, .val_left = 6, .val_right = 2,
        .val_fire1 = 40, .val_fire2 = 24, .val_fire3 = 136, .val_up_alt = 72, .val_autofire = 1, .val_autofire_off = 2,
        .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW
    },
    {
        .name = "China Arcade PS3 PC",
        .vid = 2064, .pid = 3,
        .dpad_type = AXIS,
        .byte_x = 3, .byte_y = 4, .byte_analog_x = 0, .byte_analog_y = 0, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 5, .byte_fire2 = 5, .byte_fire3 = 5, .byte_up_alt = 5, .byte_autofire = 6, .byte_autofire_off = 0,
        .val_up = 0, .val_down = 255, .val_left = 0, .val_right = 255,
        .val_fire1 = 64, .val_fire2 = 128, .val_fire3 = 16, .val_up_alt = 32, .val_autofire = 4, .val_autofire_off = 0x00,
        .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW
    },
    {
        .name = "Zero_Lag_China",
        .vid = 121, .pid = 6,
        .dpad_type = AXIS,
        .byte_x = 0, .byte_y = 1, .byte_analog_x = 0, .byte_analog_y = 0, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 5, .byte_fire2 = 5, .byte_fire3 = 5, .byte_up_alt = 5, .byte_autofire = 5, .byte_autofire_off = 0,
        .val_up = 0, .val_down = 255, .val_left = 0, .val_right = 255,
        .val_fire1 = 16, .val_fire2 = 32, .val_fire3 = 48, .val_up_alt = 64, .val_autofire = 128, .val_autofire_off = 0x00,
        .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW
    },
    {
        .name = "NES2USB_RetroBit",
        .vid = 4754, .pid = 17987,
        .dpad_type = BITMASK,
        .byte_x = 0, .byte_y = 0, .byte_analog_x = 0, .byte_analog_y = 0, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        .byte_fire1 = 1, .byte_fire2 = 1, .byte_fire3 = 1, .byte_up_alt = 1, .byte_autofire = 1, .byte_autofire_off = 0,
        .val_up = 8, .val_down = 4, .val_left = 2, .val_right = 1,
        .val_fire1 = 2, .val_fire2 = 8, .val_fire3 = 4, .val_up_alt = 1, .val_autofire = 4, .val_autofire_off = 0x00,
        .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW
    },
    {
        .name = "USB2SNES Mayflower",
        .vid = 3727, .pid = 12307,
        .use_report_id = true, .report_id_val = 1,
        .dpad_type = AXIS,
        .byte_x = 3, .byte_y = 4, .byte_analog_x = 0, .byte_analog_y = 0, .byte_analog_right_x = 0, .byte_analog_right_y = 0,
        
        // Buttons bytes mapping
        .byte_fire1 = 5, .byte_fire2 = 5, .byte_fire3 = 0, .byte_up_alt = 5, .byte_autofire = 6, .byte_autofire_off = 6,
        
        .val_up = 0, .val_down = 255, .val_left = 0, .val_right = 255,
        
        // Values swapped based on your request
        // Fire 1 = B (64), Fire 2 = Y (128), Alt Up = A (32)
        // Autofire ON = L (4), Autofire OFF = R (8)
        .val_fire1 = 64, .val_fire2 = 128, .val_fire3 = 0, .val_up_alt = 32, .val_autofire = 4, .val_autofire_off = 8,
        
        .color_fire1 = C_GREEN, .color_fire2 = C_RED, .color_fire3 = C_CYAN, .color_up_alt = C_BLUE, .color_autofire = C_YELLOW
    }
};

const int NUM_PROFILES = sizeof(PROFILES) / sizeof(PadConfig);

#endif