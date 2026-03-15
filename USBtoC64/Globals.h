// ==========================================
// USB to C64/Amiga Adapter - Advanced v1.2
// File: Globals.h
// Description: Global variables, Pin definitions, and Shared States (Atomic Blob NVS)
// ==========================================
#pragma once

#include <Arduino.h>
#include <stdint.h>
#include <Preferences.h>
#include "Adafruit_NeoPixel.h"
#include "JoystickProfiles.h"
#include "JoystickMapping.h"

// 🎮 --- HTML CONFIGURATOR ENGINE ---
#define HAS_HTML_CONFIGURATOR 1
extern bool use_html_configurator;

// Dynamic JM rules loaded from NVS (HTML configurator save path)
#define JM_DYN_MAX_RULES 32
#define JM_DYN_MAX_ANALOG 4
extern JM_Rule  jm_rules_dyn[JM_DYN_MAX_RULES];
extern uint8_t  jm_rules_dyn_count;
extern int8_t   jm_dpad_index_dyn;
extern bool     jm_use_analog_dyn;
extern uint8_t  jm_analog_x_dyn[JM_DYN_MAX_ANALOG];
extern uint8_t  jm_analog_y_dyn[JM_DYN_MAX_ANALOG];
extern uint8_t  jm_analog_x_count_dyn;
extern uint8_t  jm_analog_y_count_dyn;

// NVS blob for HTML rules (key: "jm_blob")
typedef struct {
    uint16_t vid;
    uint16_t pid;
    int8_t   dpad_index;
    uint8_t  rule_count;
    uint8_t  use_analog;
    uint8_t  analog_x_count;
    uint8_t  analog_y_count;
    uint8_t  analog_x[JM_DYN_MAX_ANALOG];
    uint8_t  analog_y[JM_DYN_MAX_ANALOG];
    JM_Rule  rules[JM_DYN_MAX_RULES];
} NVS_JM_Blob;

extern Preferences prefs; // <--- SPOSTATA QUI, PRIMA DELL'USO

inline void load_jm_rules_from_nvs() {
    use_html_configurator = false;
    jm_use_analog_dyn = false;
    jm_analog_x_count_dyn = 0;
    jm_analog_y_count_dyn = 0;
    if (!prefs.isKey("jm_blob")) return;
    NVS_JM_Blob blob;
    size_t len = prefs.getBytes("jm_blob", &blob, sizeof(NVS_JM_Blob));
    if (len < sizeof(uint16_t)*2 + 2) return;
    jm_dpad_index_dyn  = blob.dpad_index;
    jm_rules_dyn_count = (blob.rule_count <= JM_DYN_MAX_RULES) ? blob.rule_count : JM_DYN_MAX_RULES;
    memcpy(jm_rules_dyn, blob.rules, jm_rules_dyn_count * sizeof(JM_Rule));
    jm_use_analog_dyn      = blob.use_analog;
    jm_analog_x_count_dyn  = (blob.analog_x_count <= JM_DYN_MAX_ANALOG) ? blob.analog_x_count : JM_DYN_MAX_ANALOG;
    jm_analog_y_count_dyn  = (blob.analog_y_count <= JM_DYN_MAX_ANALOG) ? blob.analog_y_count : JM_DYN_MAX_ANALOG;
    memcpy(jm_analog_x_dyn, blob.analog_x, jm_analog_x_count_dyn);
    memcpy(jm_analog_y_dyn, blob.analog_y, jm_analog_y_count_dyn);
}

//extern Preferences prefs;
// --- MOUSE GLOBAL SETTINGS ---
extern int mouse_amiga_speed;
extern int mouse_c64_speed;
extern bool mouse_wheel_enabled;

extern "C" {
  #include "freertos/FreeRTOS.h"
  #include "freertos/task.h"
  #include "freertos/queue.h"
  #include "usb/usb_host.h"
  #include "usb/usb_helpers.h"
}

// ⚡ --- GPIO PIN CONFIGURATION --- ⚡
#define PIN_WS2812B 21 
#define GP_RX 44
#define GP_TX 43

#define GP_UP 8
#define GP_DOWN 9
#define GP_LEFT 10
#define GP_RIGHT 11
#define GP_FIRE1 7   
#define GP_FIRE2 5       
#define GP_FIRE3 3        
#define GP_POTY 6    
#define GP_POTX 4 
#define GP1 1            

#define SWITCH_MJ 13 
#define PIN_BOOT_BTN 0 

#define LED_OFF 0x000000 // ⚫ Off

struct SystemColors {
    uint8_t brightness = 255;
    bool multicolor_enabled = true; // Required for HTML checkbox state

    // --- SYSTEM STATUS ---
    uint32_t idle_c64    = C_ORANGE; // 🟠
    uint32_t idle_amiga  = C_WHITE;  // ⚪

    // --- ACTIONS ---
    uint32_t dir_glowing = C_PURPLE; // 🟣
    uint32_t btn_fire1   = C_GREEN; // 🟢
    uint32_t btn_fire2   = C_RED;    // 🔴
    uint32_t btn_fire3   = C_CYAN;   // 🩵
    uint32_t btn_alt     = C_BLUE;   // 🔵
    uint32_t btn_auto    = C_YELLOW; // 🟡
};

extern SystemColors sys_colors;

inline void load_sys_colors_from_nvs() {
    bool has_colors = prefs.getBool("has_colors", false);
    if (has_colors) {
        size_t len = prefs.getBytes("col_blob", &sys_colors, sizeof(SystemColors));
        if (len != sizeof(SystemColors)) {
            Serial2.println(">>> NVS Colors BLOB size mismatch! Reverting to defaults.");
        }
    }
}

extern Adafruit_NeoPixel ws2812b;

// 🖥️ --- USB HOST VARIABLES --- 🖥️
static usb_host_client_handle_t s_client = nullptr;
static usb_device_handle_t      s_dev    = nullptr;
static usb_transfer_t* s_in_xfer = nullptr;
static uint8_t s_in_ep = 0, s_if_num = 0;
static uint16_t s_in_mps = 0;
static volatile uint8_t s_new_dev_addr = 0;

// 🕹️ --- SYSTEM MODES & STATES --- 🕹️
enum SystemMode { MODE_PLAY, MODE_SERVICE, MODE_SNIFFER, MODE_RAW, MODE_DEBUG, MODE_GPIO, MODE_POLLING, MODE_LEARNING };
extern SystemMode current_mode;

enum CmdState { CMD_IDLE, CMD_WAIT_NEW_CHOICE, CMD_WAIT_NAME_MANUAL, CMD_COLOR_ADJ_MENU, CMD_COLOR_ADJ_EDIT };
extern CmdState cmd_state;

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

extern SniffStep sniff_step;
extern bool sniff_to_nvs; 
extern String sniff_profile_name; 

extern uint16_t last_gpio_state; 
extern bool is_amiga;
extern PadConfig current_profile; 
extern bool device_connected;

// --- MOUSE & KEYBOARD FIX ---
extern bool is_mouse_connected; 
extern uint8_t c64_mouse_ground_state; 
extern unsigned long last_mouse_activity_time;

extern bool C64_Amiga_Connected; 
extern bool dev_mode; 

extern uint16_t connected_vid;
extern uint16_t connected_pid;

extern bool joy_u, joy_d, joy_l, joy_r;
extern bool joy_f1, joy_f2, joy_f3, joy_up_alt, joy_auto;

struct pkt_t { uint16_t len; uint8_t data[64]; };
static QueueHandle_t s_pkt_q = nullptr;

extern unsigned long polling_start_time;
extern uint32_t polling_packet_count;
extern bool polling_active;
extern uint8_t polling_neutral_data[64]; 
extern bool polling_neutral_saved;

#define AMIGA_MOUSE_SPEED  3
#define C64_MOUSE_SPEED    3

#define C64_POT_SAFE_MODE 1

#define C64_POT_X_MIN 64.0f
#define C64_POT_X_MAX 191.0f
#define C64_POT_Y_MIN 64.0f
#define C64_POT_Y_MAX 191.0f

// 🕐 --- RUNTIME PAL/NTSC TIMING (set by compute_pal_timing() at boot) --- 🕐
// Replaced compile-time #define PAL with runtime variables to allow NVS selection.
extern float    g_STEP_X, g_STEP_Y;
extern uint64_t g_MIN_X,  g_MAX_X,  g_MIN_Y,  g_MAX_Y;

inline void compute_pal_timing(bool pal) {
    float base_min_x, base_max_x, base_min_y, base_max_y;
    if (pal) {
        base_min_x = 2450.0f; base_max_x = 5040.0f;
        base_min_y = 2440.0f; base_max_y = 5100.0f;
        g_STEP_X = 10.16689245f; g_STEP_Y = 10.14384171f;
    } else {
        base_min_x = 2360.0f; base_max_x = 4855.0f;
        base_min_y = 2351.0f; base_max_y = 4913.0f;
        g_STEP_X = 9.794315054f; g_STEP_Y = 9.772109035f;
    }
    #if C64_POT_SAFE_MODE
        g_MIN_X = (uint64_t)(base_min_x + (C64_POT_X_MIN * g_STEP_X));
        g_MAX_X = (uint64_t)(base_min_x + (C64_POT_X_MAX * g_STEP_X));
        g_MIN_Y = (uint64_t)(base_min_y + (C64_POT_Y_MIN * g_STEP_Y));
        g_MAX_Y = (uint64_t)(base_min_y + (C64_POT_Y_MAX * g_STEP_Y));
    #else
        g_MIN_X = (uint64_t)base_min_x; g_MAX_X = (uint64_t)base_max_x;
        g_MIN_Y = (uint64_t)base_min_y; g_MAX_Y = (uint64_t)base_max_y;
    #endif
}

#define PULSE_LENGTH 150

extern uint8_t H[4];
extern uint8_t HQ[4];
extern uint8_t QX;
extern uint8_t QY;

extern volatile uint64_t delayOnX;
extern volatile uint64_t delayOnY;
extern volatile uint64_t delayOffX;
extern volatile uint64_t delayOffY;

extern hw_timer_t *timerOnX;
extern hw_timer_t *timerOnY;
extern hw_timer_t *timerOffX;
extern hw_timer_t *timerOffY;

// ==========================================
// 💾 --- NVS CUSTOM PROFILE MANAGER (BLOB) --- 💾
// ==========================================
extern bool has_custom_profile;
extern PadConfig custom_profile;

typedef struct {
    uint16_t vid;
    uint16_t pid;
    uint8_t dpad_type;
    uint8_t byte_x;
    uint8_t byte_y;
    uint8_t val_up;
    uint8_t val_down;
    uint8_t val_left;
    uint8_t val_right;
    uint8_t byte_fire1;
    uint8_t val_fire1;
    uint8_t byte_fire2;
    uint8_t val_fire2;
    uint8_t byte_fire3;
    uint8_t val_fire3;
    uint8_t byte_up_alt;
    uint8_t val_up_alt;
    uint8_t byte_autofire;
    uint8_t val_autofire;
    uint8_t byte_autofire_off;
    uint8_t val_autofire_off;
    uint8_t byte_autofire_hold;
    uint8_t val_autofire_hold;
    uint32_t color_fire1;
    uint32_t color_fire2;
    uint32_t color_fire3;
    uint32_t color_up_alt;
    uint32_t color_autofire;
} NVS_PadProfile;

inline void load_custom_profile_from_nvs() {
    has_custom_profile = prefs.getBool("has_custom", false);
    
    if (has_custom_profile) {
        NVS_PadProfile nvs_data;
        size_t len = prefs.getBytes("cust_blob", &nvs_data, sizeof(NVS_PadProfile));
        
        if (len == sizeof(NVS_PadProfile)) {
            // Path 1: blob written by 'new' service menu command
            custom_profile.name = "WEB_CUSTOM_PAD";
            custom_profile.vid = nvs_data.vid;
            custom_profile.pid = nvs_data.pid;
            custom_profile.use_report_id = false; 
            custom_profile.report_id_val = 0;
            custom_profile.dpad_type = (DpadType)nvs_data.dpad_type;
            custom_profile.byte_x = nvs_data.byte_x;
            custom_profile.byte_y = nvs_data.byte_y;
            custom_profile.byte_analog_x = 0; 
            custom_profile.byte_analog_y = 0;
            custom_profile.byte_analog_right_x = 0;
            custom_profile.byte_analog_right_y = 0;
            custom_profile.val_up = nvs_data.val_up;
            custom_profile.val_down = nvs_data.val_down;
            custom_profile.val_left = nvs_data.val_left;
            custom_profile.val_right = nvs_data.val_right;
            custom_profile.byte_fire1 = nvs_data.byte_fire1;
            custom_profile.val_fire1 = nvs_data.val_fire1;
            custom_profile.byte_fire2 = nvs_data.byte_fire2;
            custom_profile.val_fire2 = nvs_data.val_fire2;
            custom_profile.byte_fire3 = nvs_data.byte_fire3;
            custom_profile.val_fire3 = nvs_data.val_fire3;
            custom_profile.byte_up_alt = nvs_data.byte_up_alt;
            custom_profile.val_up_alt = nvs_data.val_up_alt;
            custom_profile.byte_autofire = nvs_data.byte_autofire;
            custom_profile.val_autofire = nvs_data.val_autofire;
            custom_profile.byte_autofire_off = nvs_data.byte_autofire_off;
            custom_profile.val_autofire_off = nvs_data.val_autofire_off;
            custom_profile.byte_autofire_hold = nvs_data.byte_autofire_hold;
            custom_profile.val_autofire_hold = nvs_data.val_autofire_hold;
            custom_profile.color_fire1 = nvs_data.color_fire1;
            custom_profile.color_fire2 = nvs_data.color_fire2;
            custom_profile.color_fire3 = nvs_data.color_fire3;
            custom_profile.color_up_alt = nvs_data.color_up_alt;
            custom_profile.color_autofire = nvs_data.color_autofire;
        } else if (prefs.isKey("vid")) {
            // Path 2: individual keys written by HTML configurator save
            custom_profile.name = "WEB_CUSTOM_PAD";
            custom_profile.vid = prefs.getUInt("vid", 0);
            custom_profile.pid = prefs.getUInt("pid", 0);
            custom_profile.use_report_id = false;
            custom_profile.report_id_val = 0;
            custom_profile.dpad_type = (DpadType)prefs.getInt("dpad_type", 0);
            custom_profile.byte_x = prefs.getInt("byte_dpad_x", prefs.getInt("byte_x", 0));
            custom_profile.byte_y = prefs.getInt("byte_dpad_y", prefs.getInt("byte_y", 0));
            custom_profile.byte_analog_x = 0;
            custom_profile.byte_analog_y = 0;
            custom_profile.byte_analog_right_x = 0;
            custom_profile.byte_analog_right_y = 0;
            custom_profile.val_up = prefs.getInt("val_up", 0);
            custom_profile.val_down = prefs.getInt("val_down", 0);
            custom_profile.val_left = prefs.getInt("val_left", 0);
            custom_profile.val_right = prefs.getInt("val_right", 0);
            custom_profile.byte_fire1 = prefs.getInt("byte_f1", -1);
            custom_profile.val_fire1 = prefs.getInt("val_f1", 0);
            custom_profile.byte_fire2 = prefs.getInt("byte_f2", -1);
            custom_profile.val_fire2 = prefs.getInt("val_f2", 0);
            custom_profile.byte_fire3 = prefs.getInt("byte_f3", -1);
            custom_profile.val_fire3 = prefs.getInt("val_f3", 0);
            custom_profile.byte_up_alt = prefs.getInt("byte_up_alt", -1);
            custom_profile.val_up_alt = prefs.getInt("val_up_alt", 0);
            custom_profile.byte_autofire = prefs.getInt("byte_auto", -1);
            custom_profile.val_autofire = prefs.getInt("val_auto", 0);
            custom_profile.byte_autofire_off = prefs.getInt("byte_auto_off", -1);
            custom_profile.val_autofire_off = prefs.getInt("val_auto_off", 0);
            custom_profile.byte_autofire_hold = prefs.getInt("byte_auto_h", -1);
            custom_profile.val_autofire_hold = prefs.getInt("val_auto_h", 0);
            custom_profile.color_fire1 = prefs.getUInt("c_cf1", C_GREEN);
            custom_profile.color_fire2 = prefs.getUInt("c_cf2", C_RED);
            custom_profile.color_fire3 = prefs.getUInt("c_cf3", C_CYAN);
            custom_profile.color_up_alt = prefs.getUInt("c_cua", C_BLUE);
            custom_profile.color_autofire = prefs.getUInt("c_cauto", C_YELLOW);
        } else {
            Serial2.println(">>> NVS BLOB size mismatch! Custom profile corrupted or empty.");
            has_custom_profile = false;
        }
    }
}