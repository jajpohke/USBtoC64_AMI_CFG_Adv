// ==========================================
// USB to C64/Amiga Adapter - Advanced 2.8
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

// 🎨 --- SYSTEM LED COLORS DEFINITIONS --- 🎨
#define LED_IDLE_AMIGA  ws2812b.Color(255, 255, 255) // ⚪ White
#define LED_IDLE_C64    ws2812b.Color(255, 35, 0)   // 🟠 Orange
#define LED_DIR_UP      ws2812b.Color(180, 0, 255)   // 🟣 Bright Purple
#define LED_DIR_RIGHT   ws2812b.Color(120, 0, 180)   // 🟣 Purple
#define LED_DIR_LEFT    ws2812b.Color(60, 0, 100)    // 🟣 Dark Purple
#define LED_DIR_DOWN    ws2812b.Color(20, 0, 40)     // 🟣 Very Dark Purple
#define LED_DIR_IDLE    ws2812b.Color(5, 0, 8)       // ⚫ Almost Off (Dark)
#define LED_HTML_MODE   ws2812b.Color(0, 100, 0)     // 🟢 Green
#define LED_OFF         ws2812b.Color(0, 0, 0)       // ⚫ Black / Off

Adafruit_NeoPixel ws2812b(1, PIN_WS2812B, NEO_RGB + NEO_KHZ800); //My Test Board
//Adafruit_NeoPixel ws2812b(1, PIN_WS2812B, NEO_GRB + NEO_KHZ800); //My Play Board (could be the same, check led behaviour!)

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
bool use_html_configurator = false; 

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