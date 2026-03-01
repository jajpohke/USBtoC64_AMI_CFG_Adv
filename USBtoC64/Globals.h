// ==========================================
// USB to C64/Amiga Adapter - Advanced 1.1
// File: Globals.h
// Description: Global variables, Pin definitions, and Shared States
// ==========================================
#pragma once

#include <Arduino.h>
#include <stdint.h>
#include "Adafruit_NeoPixel.h"
#include "JoystickProfiles.h" 

// 🪄 --- HTML CONFIGURATOR FILE DETECTION --- 🪄
#if __has_include("JoystickMapping.h")
#include "JoystickMapping.h"
#endif

#if defined(JOY_MAPPING_MODE) && (JOY_MAPPING_MODE == JOY_MAP_CUSTOM)
#define HAS_HTML_CONFIGURATOR 1
#else
#define HAS_HTML_CONFIGURATOR 0
#endif

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
#define GP_POTY 3        
#define GP_POTY_GND 6    
#define GP_C64_SIG_MODE_SW 4 
#define GP1 1            

// --- CONSOLE HARDWARE SWITCH ---
#define SWITCH_MJ 13 // HIGH = AMIGA, LOW = COMMODORE 64

// 🎨 --- SYSTEM LED COLORS DEFINITIONS --- 🎨
// 🎨 --- SYSTEM LED COLORS DEFINITIONS (LOW POWER) --- 🎨
#define LED_IDLE_AMIGA  ws2812b.Color(30, 30, 30)    // ⚪ Soft White
#define LED_IDLE_C64    ws2812b.Color(85, 12, 0)     // 🟠 Soft C64 Orange
#define LED_DIR_UP      ws2812b.Color(50, 0, 85)     // 🟣 Bright Purple
#define LED_DIR_RIGHT   ws2812b.Color(30, 0, 50)     // 🟣 Medium Purple
#define LED_DIR_LEFT    ws2812b.Color(30, 0, 50)     // 🟣 Medium Purple
#define LED_DIR_DOWN    ws2812b.Color(15, 0, 25)     // 🟣 Dark Purple
#define LED_DIR_IDLE    ws2812b.Color(5, 0, 8)       // ⚫ Almost Off (Dark Purple/Black)
#define LED_HTML_MODE   ws2812b.Color(0, 85, 0)      // 🟢 Soft Green
#define LED_JOY_MOUSE   ws2812b.Color(0, 0, 40)      // 🔵 Soft Blue (USB Mouse connected)
#define LED_OFF         ws2812b.Color(0, 0, 0)       // ⚫ Black / Off

Adafruit_NeoPixel ws2812b(1, PIN_WS2812B, NEO_GRB + NEO_KHZ800);    //play board
//Adafruit_NeoPixel ws2812b(1, PIN_WS2812B, NEO_RGB + NEO_KHZ800);  //test board

// 🛡️ --- SAFETY FEATURES --- 🛡️
// true = Flashes red if an Amiga is connected but the switch is set to C64
// false = Disables the hardware switch mismatch watchdog entirely
#define ENABLE_SWITCH_WATCHDOG true

// 🖥️ --- USB HOST VARIABLES --- 🖥️
static usb_host_client_handle_t s_client = nullptr;
static usb_device_handle_t      s_dev    = nullptr;
static usb_transfer_t* s_in_xfer = nullptr;
static uint8_t s_in_ep = 0, s_if_num = 0;
static uint16_t s_in_mps = 0;
static volatile uint8_t s_new_dev_addr = 0;

// 🕹️ --- SYSTEM MODES & STATES --- 🕹️
enum SystemMode { MODE_PLAY, MODE_SERVICE, MODE_SNIFFER, MODE_RAW, MODE_DEBUG, MODE_GPIO, MODE_POLLING, MODE_COLOR_MIXER };
SystemMode current_mode = MODE_PLAY;

enum CmdState { CMD_IDLE, CMD_WAIT_IMPORT, CMD_WAIT_NAME_IMPORT, CMD_WAIT_NAME_MANUAL, CMD_WAIT_COLOR_CHOICE };
CmdState cmd_state = CMD_IDLE;

String sniff_profile_name = "NEW_PAD"; 

uint16_t last_gpio_state = 0xFFFF; 
bool is_amiga = false;
PadConfig current_profile; 
bool device_connected = false;
bool is_mouse_connected = false; 
bool use_html_configurator = false; 
bool ground_stabilized = false;

uint16_t connected_vid = 0;
uint16_t connected_pid = 0;

bool joy_u = false, joy_d = false, joy_l = false, joy_r = false;
bool joy_f1 = false, joy_f2 = false, joy_f3 = false, joy_up_alt = false, joy_auto = false;

struct pkt_t { uint16_t len; uint8_t data[64]; };
static QueueHandle_t s_pkt_q = nullptr;

// ⏱️ --- POLLING TESTER VARIABLES --- ⏱️
unsigned long polling_start_time = 0;
uint32_t polling_packet_count = 0;
bool polling_active = false;
uint8_t polling_neutral_data[64]; 
bool polling_neutral_saved = false;

// 🎨 --- LIVE COLOR MIXER VARIABLES --- 🎨
uint8_t mix_r = 128, mix_g = 128, mix_b = 128;
uint8_t mix_channel = 0; 
uint8_t mix_brightness = 40;

// 🖱️ --- MOUSE SPEED CONFIGURATION --- 🖱️
// Insert value from 1 to 5 where 1 is slow, 3 is normal, 5 is fast
#define AMIGA_MOUSE_SPEED  3
#define C64_MOUSE_SPEED    3

// 🖱️ --- MOUSE EMULATION VARIABLES (C64/AMIGA) --- 🖱️
#define PAL 0 // 0 = NTSC, 1 = PAL

// 🛡️ --- C64 SAFE POTENTIOMETER MODE --- 🛡️
// 1 = Use the central window to ignore SID chip noise (1351 Style)
// 0 = Use the full 0-255 range
#define C64_POT_SAFE_MODE 1 

// --- SID WINDOW CALIBRATION ---
#define C64_POT_X_MIN 64.0f
#define C64_POT_X_MAX 191.0f

#define C64_POT_Y_MIN 64.0f
#define C64_POT_Y_MAX 191.0f

#if PAL
  #define BASE_MIN_X 2450.0f
  #define BASE_MAX_X 5040.0f
  #define BASE_MIN_Y 2440.0f
  #define BASE_MAX_Y 5100.0f
  #define STEPdelayOnX    10.16689245f
  #define STEPdelayOnY    10.14384171f
#else
  #define BASE_MIN_X 2360.0f
  #define BASE_MAX_X 4855.0f
  #define BASE_MIN_Y 2351.0f
  #define BASE_MAX_Y 4913.0f
  #define STEPdelayOnX    9.794315054f
  #define STEPdelayOnY    9.772109035f
#endif

// Dynamic application of limits
#if C64_POT_SAFE_MODE
  #define MINdelayOnX   (uint64_t)(BASE_MIN_X + (C64_POT_X_MIN * STEPdelayOnX))
  #define MAXdelayOnX   (uint64_t)(BASE_MIN_X + (C64_POT_X_MAX * STEPdelayOnX))
  #define MINdelayOnY   (uint64_t)(BASE_MIN_Y + (C64_POT_Y_MIN * STEPdelayOnY))
  #define MAXdelayOnY   (uint64_t)(BASE_MIN_Y + (C64_POT_Y_MAX * STEPdelayOnY))
#else
  #define MINdelayOnX   (uint64_t)BASE_MIN_X
  #define MAXdelayOnX   (uint64_t)BASE_MAX_X
  #define MINdelayOnY   (uint64_t)BASE_MIN_Y
  #define MAXdelayOnY   (uint64_t)BASE_MAX_Y
#endif

// --- HARDWARE TIMERS & STATE VARIABLES ---
#define PULSE_LENGTH 150

uint8_t H[4]  = { LOW, LOW, HIGH, HIGH };
uint8_t HQ[4] = { LOW, HIGH, HIGH, LOW };
uint8_t QX = 3;
uint8_t QY = 3;

volatile uint64_t delayOnX = MINdelayOnX;
volatile uint64_t delayOnY = MINdelayOnY;
volatile uint64_t delayOffX = 10;
volatile uint64_t delayOffY = 10;

hw_timer_t *timerOnX = NULL;
hw_timer_t *timerOnY = NULL;
hw_timer_t *timerOffX = NULL;
hw_timer_t *timerOffY = NULL;