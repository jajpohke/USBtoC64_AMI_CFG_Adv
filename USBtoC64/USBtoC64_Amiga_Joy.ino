#include <Arduino.h>
#include <stdint.h>
#include "soc/rtc_cntl_reg.h" 
#include "Adafruit_NeoPixel.h"
#include "JoystickProfiles.h" 

String sniff_profile_name = "NEW_PAD"; 

#include "JoySniffer.h"

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

Adafruit_NeoPixel ws2812b(1, PIN_WS2812B, NEO_GRB + NEO_KHZ800);

// 🎨 --- SYSTEM LED COLORS DEFINITIONS --- 🎨
#define LED_IDLE_AMIGA  ws2812b.Color(255, 255, 255) 
#define LED_IDLE_C64    ws2812b.Color(255, 128, 0)   
#define LED_DIR_UP      ws2812b.Color(180, 0, 255)   
#define LED_DIR_RIGHT   ws2812b.Color(120, 0, 180)   
#define LED_DIR_LEFT    ws2812b.Color(60, 0, 100)    
#define LED_DIR_DOWN    ws2812b.Color(20, 0, 40)     
#define LED_DIR_IDLE    ws2812b.Color(5, 0, 8)       
#define LED_HTML_MODE   ws2812b.Color(0, 100, 0)     
#define LED_OFF         ws2812b.Color(0, 0, 0)       

// 🖥️ --- USB HOST VARIABLES --- 🖥️
static usb_host_client_handle_t s_client = nullptr;
static usb_device_handle_t      s_dev    = nullptr;
static usb_transfer_t* s_in_xfer = nullptr;
static uint8_t s_in_ep = 0, s_if_num = 0;
static uint16_t s_in_mps = 0;
static volatile uint8_t s_new_dev_addr = 0;

// 🕹️ --- SYSTEM MODES & VARIABLES --- 🕹️
enum SystemMode { MODE_PLAY, MODE_SERVICE, MODE_SNIFFER, MODE_RAW, MODE_DEBUG, MODE_GPIO, MODE_POLLING };
SystemMode current_mode = MODE_PLAY;

enum CmdState { CMD_IDLE, CMD_WAIT_IMPORT, CMD_WAIT_NAME_IMPORT, CMD_WAIT_NAME_MANUAL };
CmdState cmd_state = CMD_IDLE;

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

// 🔌 --- HARDWARE PIN MANAGEMENT --- 🔌
void configure_console_mode(bool amiga_mode) {
    is_amiga = amiga_mode;
    if (is_amiga) {
        pinMode(GP_POTY, INPUT); pinMode(GP_POTY_GND, INPUT); pinMode(GP_C64_SIG_MODE_SW, INPUT); 
        Serial2.println("\n>>> SYSTEM SET TO: AMIGA (Fire 2 on Pin 9, Fire 3 on Pin 5) <<<");
    } else {
        pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW);
        pinMode(GP_POTY_GND, OUTPUT); digitalWrite(GP_POTY_GND, LOW);
        pinMode(GP_C64_SIG_MODE_SW, OUTPUT); digitalWrite(GP_C64_SIG_MODE_SW, LOW);
        Serial2.println("\n>>> SYSTEM SET TO: COMMODORE 64 (Fire 2 on POT X, Fire 3 on POT Y) <<<");
    }
}

void set_joy_pin(int pin, bool pressed) {
    if (pressed) { pinMode(pin, OUTPUT); digitalWrite(pin, LOW); } 
    else { digitalWrite(pin, LOW); pinMode(pin, INPUT_PULLUP); }
    if (!is_amiga && pin == GP_FIRE2) {
        digitalWrite(GP_C64_SIG_MODE_SW, pressed ? HIGH : LOW);
        pinMode(pin, INPUT_PULLUP); 
    } 
}

void set_fire3_pin(bool pressed) {
    if (is_amiga) {
        if (pressed) { pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW); } 
        else { pinMode(GP_POTY, INPUT); }
    } else {
        if (pressed) {
            pinMode(GP_POTY, INPUT); pinMode(GP_POTY_GND, OUTPUT); digitalWrite(GP_POTY_GND, HIGH);       
        } else {
            pinMode(GP_POTY_GND, OUTPUT); digitalWrite(GP_POTY_GND, LOW); 
            pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW);         
        }
    }
}

// 🧠 --- AUTO-DUMPER EXECUTION --- 🧠
void execute_html_dump() {
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


// 🎮 --- JOYSTICK PROCESSING --- 🎮
void process_joystick(const uint8_t *raw_data, int len) {
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

    bool u = false, d = false, l = false, r = false;
    bool f1 = false, f2 = false, f3 = false, f_alt = false, auto_btn = false;

#if HAS_HTML_CONFIGURATOR
    if (use_html_configurator) {
        bool f3_html = false;
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
                if (rule.func == JM_AUTOFIRE_ON) JM_autofire = true;
                if (rule.func == JM_AUTOFIRE_OFF) JM_autofire = false;
            }
        }
        f3 = f3_html; f_alt = false; auto_btn = JM_autofire;
        
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

        // --- Process Buttons ---
        bool f_auto_on = false;
        bool f_auto_off = false;

        if (current_profile.dpad_type == EXACT_VALUE || current_profile.dpad_type == HAT_SWITCH) {
            if (len > current_profile.byte_fire1)   f1 = (raw_data[current_profile.byte_fire1] == current_profile.val_fire1);
            if (len > current_profile.byte_fire2)   f2 = (raw_data[current_profile.byte_fire2] == current_profile.val_fire2);
            if (len > current_profile.byte_fire3)   f3 = (raw_data[current_profile.byte_fire3] == current_profile.val_fire3);
            if (len > current_profile.byte_up_alt)  f_alt = (raw_data[current_profile.byte_up_alt] == current_profile.val_up_alt);
            if (len > current_profile.byte_autofire) f_auto_on = (raw_data[current_profile.byte_autofire] == current_profile.val_autofire);
            if (len > current_profile.byte_autofire_off) f_auto_off = (raw_data[current_profile.byte_autofire_off] == current_profile.val_autofire_off);
        } else {
            if (len > current_profile.byte_fire1)   f1 = (raw_data[current_profile.byte_fire1] & current_profile.val_fire1) != 0;
            if (len > current_profile.byte_fire2)   f2 = (raw_data[current_profile.byte_fire2] & current_profile.val_fire2) != 0;
            if (len > current_profile.byte_fire3)   f3 = (raw_data[current_profile.byte_fire3] & current_profile.val_fire3) != 0;
            if (len > current_profile.byte_up_alt)  f_alt = (raw_data[current_profile.byte_up_alt] & current_profile.val_up_alt) != 0;
            if (len > current_profile.byte_autofire) f_auto_on = (raw_data[current_profile.byte_autofire] & current_profile.val_autofire) != 0;
            if (len > current_profile.byte_autofire_off) f_auto_off = (raw_data[current_profile.byte_autofire_off] & current_profile.val_autofire_off) != 0;
        }

        static bool autofire_latch = false;
        if (f_auto_on) autofire_latch = true;
        if (f_auto_off) autofire_latch = false;
        auto_btn = autofire_latch;

#if HAS_HTML_CONFIGURATOR
    }
#endif

    joy_u = u; joy_d = d; joy_l = l; joy_r = r;
    joy_f1 = f1; joy_f2 = f2; joy_f3 = f3; joy_up_alt = f_alt; joy_auto = auto_btn;
}

static void in_transfer_cb(usb_transfer_t *xfer) {
    if (xfer->status == USB_TRANSFER_STATUS_COMPLETED) {
        pkt_t p;
        p.len = xfer->actual_num_bytes;
        memcpy(p.data, xfer->data_buffer, p.len > 64 ? 64 : p.len);
        xQueueSendFromISR(s_pkt_q, &p, nullptr);
        usb_host_transfer_submit(xfer);
    }
}

void start_sniff(uint8_t addr) {
    if (device_connected) return;
    if (usb_host_device_open(s_client, addr, &s_dev) != ESP_OK) return;
    
    const usb_device_desc_t *dev_desc;
    usb_host_get_device_descriptor(s_dev, &dev_desc);

    connected_vid = dev_desc->idVendor; connected_pid = dev_desc->idProduct;
    
    bool found_internal = false;
    for (int i = 0; i < NUM_PROFILES; i++) {
        if (connected_vid == PROFILES[i].vid && connected_pid == PROFILES[i].pid) {
            current_profile = PROFILES[i];
            found_internal = true; break;
        }
    }
    
    Serial2.printf("\n*** CONNECTED: %s (VID:%04x PID:%04x) ***\n", 
                   found_internal ? current_profile.name : "UNKNOWN PAD", 
                   connected_vid, connected_pid);

#if HAS_HTML_CONFIGURATOR
    if (found_internal) {
        use_html_configurator = false;
        Serial2.println(">>> ⚙️ Internal Profile Matched! Ignoring HTML Configurator for this pad. <<<");
    } else {
        use_html_configurator = true;
        Serial2.println(">>> 🟢 Unknown Pad: Using HTML HID Configurator Profile! <<<");
    }
#else
    if (!found_internal) current_profile = PROFILES[1];
    use_html_configurator = false;
    Serial2.println(">>> ⚙️ Internal Native Engine Active! <<<");
#endif

    const usb_config_desc_t *cfg_desc;
    usb_host_get_active_config_descriptor(s_dev, &cfg_desc);
    int offset = 0; const usb_standard_desc_t *next_desc = (const usb_standard_desc_t *)cfg_desc;
    while (next_desc) {
        if (next_desc->bDescriptorType == USB_B_DESCRIPTOR_TYPE_INTERFACE) s_if_num = ((const usb_intf_desc_t *)next_desc)->bInterfaceNumber;
        if (next_desc->bDescriptorType == USB_B_DESCRIPTOR_TYPE_ENDPOINT) {
            const usb_ep_desc_t *ep = (const usb_ep_desc_t *)next_desc;
            if ((ep->bmAttributes & 0x03) == 0x03 && (ep->bEndpointAddress & 0x80)) {
                s_in_ep = ep->bEndpointAddress;
                s_in_mps = ep->wMaxPacketSize; break; 
            }
        }
        next_desc = usb_parse_next_descriptor(next_desc, cfg_desc->wTotalLength, &offset);
    }
    
    if (s_in_ep) {
        usb_host_interface_claim(s_client, s_dev, s_if_num, 0);
        usb_host_transfer_alloc(s_in_mps, 0, &s_in_xfer);
        s_in_xfer->device_handle = s_dev; s_in_xfer->callback = in_transfer_cb;
        s_in_xfer->bEndpointAddress = s_in_ep; s_in_xfer->num_bytes = s_in_mps;
        usb_host_transfer_submit(s_in_xfer); device_connected = true;
    }
}

static void client_event_cb(const usb_host_client_event_msg_t *msg, void *arg) {
    if (msg->event == USB_HOST_CLIENT_EVENT_NEW_DEV) s_new_dev_addr = msg->new_dev.address;
}

void usb_lib_task(void *arg) { 
    while (1) { uint32_t f; usb_host_lib_handle_events(portMAX_DELAY, &f); } 
}

// 🚀 --- MAIN SETUP --- 🚀
void setup() {
    Serial2.begin(115200, SERIAL_8N1, GP_RX, GP_TX);
    
    delay(1000); 
    Serial2.println("USB Initialization...");

    pinMode(19, OUTPUT); pinMode(20, OUTPUT);
    digitalWrite(19, LOW); digitalWrite(20, LOW);
    delay(200);
    
    pinMode(19, INPUT); pinMode(20, INPUT);
    delay(100);

    pinMode(GP_FIRE2, INPUT); 
    delay(50);
    
    bool avvio_amiga = (digitalRead(GP_FIRE2) == HIGH);
    
    Serial2.println("\n=================================");
    Serial2.println("     USB -> DB9 ADAPTER v2.8     ");
    Serial2.println("=================================");
    configure_console_mode(avvio_amiga); 
    
    Serial2.println(">> Type 'service' to open the advanced menu <<\n");
    
    ws2812b.begin(); 
    ws2812b.setBrightness(40);
    ws2812b.show();

    set_joy_pin(GP_UP, false); set_joy_pin(GP_DOWN, false);
    set_joy_pin(GP_LEFT, false); set_joy_pin(GP_RIGHT, false);
    set_joy_pin(GP_FIRE1, false); set_joy_pin(GP_FIRE2, false);
    set_fire3_pin(false); 
    
    s_pkt_q = xQueueCreate(16, sizeof(pkt_t));
    usb_host_config_t host_cfg = { .skip_phy_setup = false, .intr_flags = ESP_INTR_FLAG_LEVEL1 };
    usb_host_install(&host_cfg);
    xTaskCreatePinnedToCore(usb_lib_task, "usb_lib", 4096, nullptr, 10, nullptr, 0);
    usb_host_client_config_t client_cfg = { .is_synchronous = false, .max_num_event_msg = 5, .async = { .client_event_callback = client_event_cb, .callback_arg = nullptr } };
    usb_host_client_register(&client_cfg, &s_client);
}

String get_pin_status(int pin, bool is_active_low) {
    int val = digitalRead(pin);
    if (is_active_low) return (val == LOW) ? "[ PRESSED ]" : "[ IDLE    ]";
    else return (val == HIGH) ? "[ ACTIVE  ]" : "[ IDLE    ]";
}

// 🔄 --- MAIN LOOP --- 🔄
void loop() {
    
    if (Serial2.available() > 0) {
        String input = Serial2.readStringUntil('\n');
        input.trim(); 
        
        // 🗣️ --- INTERACTIVE SERIAL PROMPTS --- 🗣️
        if (cmd_state == CMD_WAIT_IMPORT) {
            String ans = input; ans.toLowerCase();
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
            Serial2.println("\n>>> SNIFFER WIZARD ARMED! <<<");
            Serial2.println("⏳ Waiting for neutral position calibration... (DO NOT touch the gamepad)");
            Serial2.println("If nothing happens within 2 seconds, press and release a button to 'wake' it.");
            return;
        }

        // --- NORMAL COMMAND PARSER ---
        String comando = input; comando.toLowerCase();

        if (comando == "service") {
            current_mode = MODE_SERVICE;
            Serial2.println("\n=== SERVICE MENU ===");
            Serial2.printf("  ACTIVE ENGINE: %s\n", use_html_configurator ? "HTML HID Configurator" : "Internal Profiler");
            Serial2.println("--------------------");
            Serial2.println("- 'sniffer'  : Map a new pad via wizard OR Auto-Convert HTML");
            Serial2.println("- 'raw'      : Show raw USB matrix");
            Serial2.println("- 'debug'    : Print logical buttons to screen");
            Serial2.println("- 'gpio'     : Real-time visual dashboard of hardware states");
            Serial2.println("- 'polling'  : Measure USB Polling Rate (Input Lag)");
            Serial2.println("- 'play'     : Return to ZERO-LAG gaming");
            Serial2.println("- 'c64'      : Set Fire 2 to POT (Default)");
            Serial2.println("- 'amiga'    : Set Fire 2 to Pin 9");
            Serial2.println("- 'reboot'   : Restart the device softly");
            Serial2.println("- 'flash'    : Reboot into Programming/DFU Mode");
            Serial2.println("====================\n");
        } 
        else if (current_mode != MODE_PLAY || comando == "play") {
            if (comando == "sniffer") { 
                if (device_connected && use_html_configurator) {
                    Serial2.println("\n>>> HTML Profile detected! Do you want to Auto-Import it? (Y/N)");
                    cmd_state = CMD_WAIT_IMPORT;
                } else {
                    Serial2.print("\n>>> Enter a NAME for the new manual profile: ");
                    cmd_state = CMD_WAIT_NAME_MANUAL;
                }
            }
            else if (comando == "raw")   { current_mode = MODE_RAW; Serial2.println(">>> RAW mode active!"); }
            else if (comando == "debug") { current_mode = MODE_DEBUG; Serial2.println(">>> DEBUG mode active!"); }
            else if (comando == "gpio")  { 
                current_mode = MODE_GPIO; last_gpio_state = 0xFFFF; 
                Serial2.println(">>> Starting GPIO Dashboard..."); 
            }
            else if (comando == "polling") {
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
            else if (comando == "play")  { current_mode = MODE_PLAY; Serial2.println("\n\n>>> PLAY mode (Zero-Lag) restored! <<<"); }
            else if (comando == "amiga") { configure_console_mode(true); }
            else if (comando == "c64")   { configure_console_mode(false); }
            else if (comando == "reboot") {
                Serial2.println("\n>>> REBOOTING DEVICE... <<<");
                delay(500);
                ESP.restart();
            }
            else if (comando == "flash") {
                Serial2.println("\n>>> REBOOTING INTO PROGRAMMING/DFU Mode... <<<");
                delay(500);
                REG_WRITE(RTC_CNTL_OPTION1_REG, RTC_CNTL_FORCE_DOWNLOAD_BOOT);
                ESP.restart();
            }
        }
    }

    if (current_mode == MODE_POLLING && polling_active && polling_start_time > 0) {
        unsigned long elapsed = millis() - polling_start_time;
        static unsigned long last_dot = 0;
        if (millis() - last_dot > 500) { Serial2.print("."); last_dot = millis(); }

        if (elapsed >= 3000) { 
            polling_active = false;
            Serial2.println(" 100%");
            
            float hz = (float)polling_packet_count / 3.0; 
            float ms_delay = hz > 0 ? (1000.0 / hz) : 0;  
            
            Serial2.println("\n=== TEST RESULT ===");
            Serial2.printf("Packets received: %d\n", polling_packet_count);
            Serial2.printf("Average interval: %.2f ms\n", ms_delay);
            Serial2.printf("Polling Rate    : ~%.0f Hz\n\n", hz);
            
            if (hz >= 400) {
                Serial2.println("[ RATING: EXCELLENT 🟢 ]");
                Serial2.println("Perfect for pure retrogaming. Input lag is under 2.5ms (imperceptible).");
            } else if (hz >= 200) {
                Serial2.println("[ RATING: GOOD 🟡 ]");
                Serial2.println("Great response, latency under 5ms. Ideal for most games.");
            } else if (hz >= 100) {
                Serial2.println("[ RATING: ACCEPTABLE 🟠 ]");
                Serial2.println("Standard pad (latency ~8ms). Fine for RPGs/Adventures.");
                Serial2.println("Hardcore players might notice lag in fast platformers.");
            } else {
                Serial2.println("[ RATING: POOR 🔴 ]");
                Serial2.println("High latency (over 10ms). Not recommended for Action/Fighting games.");
            }
            Serial2.println("======================");
            Serial2.println(">> Returning to PLAY mode...\n");
            current_mode = MODE_PLAY; 
        }
    }

    usb_host_client_handle_events(s_client, 1);
    if (s_new_dev_addr) { uint8_t a = s_new_dev_addr; s_new_dev_addr = 0; start_sniff(a); }
    pkt_t p;
    if (xQueueReceive(s_pkt_q, &p, 1) == pdTRUE) { process_joystick(p.data, p.len); }

    if (current_mode == MODE_GPIO) {
        uint16_t current_gpio_state = 0;
        current_gpio_state |= (digitalRead(GP_UP) << 0);
        current_gpio_state |= (digitalRead(GP_DOWN) << 1);
        current_gpio_state |= (digitalRead(GP_LEFT) << 2);
        current_gpio_state |= (digitalRead(GP_RIGHT) << 3);
        current_gpio_state |= (digitalRead(GP_FIRE1) << 4);
        current_gpio_state |= (digitalRead(GP_FIRE2) << 5);
        current_gpio_state |= (digitalRead(GP_POTY) << 6); 
        if (!is_amiga) current_gpio_state |= (digitalRead(GP_C64_SIG_MODE_SW) << 7);

        if (current_gpio_state != last_gpio_state) {
            last_gpio_state = current_gpio_state;
            
            Serial2.print("\x1b[2J\x1b[H");
            Serial2.println("\n==========================================");
            Serial2.println("    HARDWARE DIAGNOSTICS - GPIO STATES    ");
            Serial2.println("==========================================");
            Serial2.printf(" ENGINE : %s\n", use_html_configurator ? "HTML HID Configurator" : "Internal Profiler");
            Serial2.printf(" MODE   : %s\n", is_amiga ? "AMIGA (Fire 2 on Pin 9)" : "COMMODORE 64 (Fire 2 on POT X)");
            Serial2.printf(" STATUS : %s\n", device_connected ? current_profile.name : "WAITING...");
            Serial2.println("------------------------------------------");
            Serial2.println(" DB9 PIN   | GPIO | ELECTRICAL| STATE     ");
            Serial2.println("------------------------------------------");
            Serial2.printf(" UP        |  %02d  |   %s    | %s\n", GP_UP, digitalRead(GP_UP) ? "HIGH" : "LOW ", get_pin_status(GP_UP, true).c_str());
            Serial2.printf(" DOWN      |  %02d  |   %s    | %s\n", GP_DOWN, digitalRead(GP_DOWN) ? "HIGH" : "LOW ", get_pin_status(GP_DOWN, true).c_str());
            Serial2.printf(" LEFT      |  %02d  |   %s    | %s\n", GP_LEFT, digitalRead(GP_LEFT) ? "HIGH" : "LOW ", get_pin_status(GP_LEFT, true).c_str());
            Serial2.printf(" RIGHT     |  %02d  |   %s    | %s\n", GP_RIGHT, digitalRead(GP_RIGHT) ? "HIGH" : "LOW ", get_pin_status(GP_RIGHT, true).c_str());
            Serial2.printf(" FIRE 1    |  %02d  |   %s    | %s\n", GP_FIRE1, digitalRead(GP_FIRE1) ? "HIGH" : "LOW ", get_pin_status(GP_FIRE1, true).c_str());
            Serial2.printf(" FIRE 2    |  %02d  |   %s    | %s\n", GP_FIRE2, digitalRead(GP_FIRE2) ? "HIGH" : "LOW ", get_pin_status(GP_FIRE2, true).c_str());
            Serial2.printf(" FIRE 3    |  %02d  |   %s    | %s\n", GP_POTY, digitalRead(GP_POTY) ? "HIGH" : "LOW ", get_pin_status(GP_POTY, true).c_str());
            
            if (!is_amiga) {
                Serial2.printf(" C64_SIG   |  %02d  |   %s    | %s\n", GP_C64_SIG_MODE_SW, digitalRead(GP_C64_SIG_MODE_SW) ? "HIGH" : "LOW ", get_pin_status(GP_C64_SIG_MODE_SW, false).c_str());
            }
            Serial2.println("------------------------------------------");
            Serial2.println(">> Type 'play' to return to game <<\n");
        }
    }

    if (device_connected) {
        bool final_up = joy_u || joy_up_alt;
        bool out_fire = joy_f1;
        
        static bool toggle = false; 
        static unsigned long last_ms = 0;
        if (joy_auto) {
            if (millis() - last_ms > 70) { toggle = !toggle; last_ms = millis(); }
            out_fire = out_fire || toggle; 
        }
        
        static bool last_up = false, last_down = false, last_left = false, last_right = false;
        static bool last_fire = false, last_f2 = false, last_f3 = false;

        if (final_up != last_up || joy_d != last_down || joy_l != last_left || joy_r != last_right || out_fire != last_fire || joy_f2 != last_f2 || joy_f3 != last_f3) {
            
            if (current_mode == MODE_DEBUG) {
                Serial2.print("ACTION: ");
                if (!final_up && !joy_d && !joy_l && !joy_r && !out_fire && !joy_f2 && !joy_f3 && !joy_up_alt && !joy_auto) Serial2.print("All released");
                if(final_up) Serial2.print("[UP] "); if(joy_d) Serial2.print("[DOWN] "); 
                if(joy_l) Serial2.print("[LEFT] "); if(joy_r) Serial2.print("[RIGHT] ");
                if(out_fire) Serial2.print("[FIRE 1] "); if(joy_f2) Serial2.print("[FIRE 2] ");
                if(joy_f3) Serial2.print("[FIRE 3] "); 
                if(joy_up_alt) Serial2.print("[ALT UP] "); if(joy_auto) Serial2.print("[AUTOFIRE] ");
                Serial2.println();
            } 
            else if (current_mode == MODE_PLAY || current_mode == MODE_GPIO) {
                set_joy_pin(GP_UP, final_up);
                set_joy_pin(GP_DOWN, joy_d);
                set_joy_pin(GP_LEFT, joy_l); 
                set_joy_pin(GP_RIGHT, joy_r);
                set_joy_pin(GP_FIRE1, out_fire); 
                set_joy_pin(GP_FIRE2, joy_f2);
                set_fire3_pin(joy_f3); 
            }

            uint32_t led_color = LED_OFF;
            if (use_html_configurator) {
                led_color = LED_HTML_MODE; 
            } else {
                if (joy_f1) led_color = current_profile.color_fire1;
                else if (joy_f2) led_color = current_profile.color_fire2;
                else if (joy_f3) led_color = current_profile.color_fire3; 
                else if (joy_up_alt) led_color = current_profile.color_up_alt;
                else if (joy_auto) led_color = toggle ? current_profile.color_autofire : LED_OFF;
                else {
                    if (final_up) led_color = LED_DIR_UP;         
                    else if (joy_r) led_color = LED_DIR_RIGHT;    
                    else if (joy_l) led_color = LED_DIR_LEFT;     
                    else if (joy_d) led_color = LED_DIR_DOWN;     
                    else led_color = LED_DIR_IDLE;                
                }
            }

            static uint32_t last_led_color = 0xFFFFFFFF;
            if (led_color != last_led_color) {
                ws2812b.setPixelColor(0, led_color);
                ws2812b.show(); 
                last_led_color = led_color;
            }

            last_up = final_up; last_down = joy_d;
            last_left = joy_l; last_right = joy_r;
            last_fire = out_fire; last_f2 = joy_f2; last_f3 = joy_f3;
        }
    } 
    else {
        uint32_t idle_color = is_amiga ? LED_IDLE_AMIGA : LED_IDLE_C64; 
        static uint32_t last_idle_color = 0;
        if (idle_color != last_idle_color) {
            ws2812b.setPixelColor(0, idle_color);
            ws2812b.show();
            last_idle_color = idle_color;
        }
    }
}