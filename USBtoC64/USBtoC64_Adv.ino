// ==========================================
// USB to C64/Amiga Adapter - Advanced v1.2
// File: USBtoC64_Adv.ino
// Description: Main Logic with 3-Tier Gamepad Hierarchy, Smart USB Routing & Dev Mode (WebSerial Ready)
// ==========================================
#include <Arduino.h>
#include <stdint.h>
#include "soc/rtc_cntl_reg.h" 
#include "esp_bt.h"
#include <Preferences.h>
#include <ArduinoJson.h>
#include "usb/usb_host.h"
#include "hid_host.h"
#include "hid_usage_mouse.h"

// 1. INCLUDE GLOBALS.H FIRST
#include "Globals.h"

// 2. DEFINE GLOBAL VARIABLES
Preferences prefs;
SystemColors sys_colors;
Adafruit_NeoPixel ws2812b(1, PIN_WS2812B, NEO_GRB + NEO_KHZ800);

int active_driver = 0;
// Global Mouse Settings
int mouse_amiga_speed = 3;
int mouse_c64_speed = 3;
bool mouse_wheel_enabled = false;
bool mouse_pal_timing = true; // PAL=true, NTSC=false — loaded from NVS

// Runtime PAL/NTSC timing values (populated by compute_pal_timing())
float    g_STEP_X = 0, g_STEP_Y = 0;
uint64_t g_MIN_X  = 0, g_MAX_X  = 0, g_MIN_Y = 0, g_MAX_Y = 0;

QueueHandle_t wheel_queue = NULL;

unsigned long pending_mouse_time = 0;
bool mouse_is_pending = false;

// LCARS Silencer Flag
bool web_ui_active = false; 

SystemMode current_mode = MODE_PLAY;
CmdState cmd_state = CMD_IDLE;
SniffStep sniff_step = S_START;
bool sniff_to_nvs = false;
String sniff_profile_name = "NEW_PAD";

uint16_t last_gpio_state = 0xFFFF; 
bool is_amiga = false;
bool device_connected = false;

bool is_mouse_connected = false;
uint8_t c64_mouse_ground_state = 0;
unsigned long last_mouse_activity_time = 0;

bool C64_Amiga_Connected = false;
bool dev_mode = false;

uint16_t connected_vid = 0;
uint16_t connected_pid = 0;
bool joy_u = false, joy_d = false, joy_l = false, joy_r = false;
bool joy_f1 = false, joy_f2 = false, joy_f3 = false, joy_up_alt = false, joy_auto = false;
unsigned long polling_start_time = 0;
uint32_t polling_packet_count = 0;
bool polling_active = false;
uint8_t polling_neutral_data[64]; 
bool polling_neutral_saved = false;
uint8_t H[4]  = { LOW, LOW, HIGH, HIGH };
uint8_t HQ[4] = { LOW, HIGH, HIGH, LOW };
uint8_t QX = 3;
uint8_t QY = 3;

volatile uint64_t delayOnX  = 0; // set in setup() after compute_pal_timing()
volatile uint64_t delayOnY  = 0; // set in setup() after compute_pal_timing()
volatile uint64_t delayOffX = 10;
volatile uint64_t delayOffY = 10;

hw_timer_t *timerOnX = NULL;
hw_timer_t *timerOnY = NULL;
hw_timer_t *timerOffX = NULL;
hw_timer_t *timerOffY = NULL;

bool has_custom_profile = false;
PadConfig custom_profile;
PadConfig current_profile;

// 3. INCLUDE OPERATIONAL MODULES
#include "Hardware.h"
#include "ServiceTools.h"
#include "InputEngine.h"
#include "CoreTasks.h"

void IRAM_ATTR switchMJHandler() {
    if (millis() < 3000) return;
    static unsigned long last_interrupt_time = 0;
    unsigned long interrupt_time = millis();
    if (interrupt_time - last_interrupt_time > 200) { 
        esp_restart();
    }
    last_interrupt_time = interrupt_time;
}

// ==========================================
// 🖱️ ASYNCHRONOUS WHEEL TASK (Micromys & Amiga)
// ==========================================
void wheelTask(void *pvParameters) {
    int8_t wheel_movement;
    while (true) {
        if (xQueueReceive(wheel_queue, &wheel_movement, portMAX_DELAY)) {
            if (is_amiga) {
                // AMIGA EMULATION: Uses UP/DOWN for Wheel
                if (wheel_movement > 0) {
                    for(int i=0; i<wheel_movement; i++) {
                        pinMode(GP_UP, OUTPUT);
                        digitalWrite(GP_UP, LOW);
                        vTaskDelay(pdMS_TO_TICKS(20));
                        pinMode(GP_UP, INPUT);
                        vTaskDelay(pdMS_TO_TICKS(20));
                    }
                } else {
                    for(int i=0; i<-wheel_movement; i++) {
                        pinMode(GP_DOWN, OUTPUT);
                        digitalWrite(GP_DOWN, LOW);
                        vTaskDelay(pdMS_TO_TICKS(20));
                        pinMode(GP_DOWN, INPUT);
                        vTaskDelay(pdMS_TO_TICKS(20));
                    }
                }
            } else {
                // C64 MICROMYS: Uses LEFT/RIGHT for Wheel
                if (wheel_movement > 0) {
                    for (int i = 0; i < wheel_movement; i++) {
                        pinMode(GP_RIGHT, OUTPUT);
                        digitalWrite(GP_RIGHT, LOW);
                        vTaskDelay(pdMS_TO_TICKS(50));
                        pinMode(GP_RIGHT, INPUT);
                        vTaskDelay(pdMS_TO_TICKS(50));
                    }
                } else {
                    for (int i = 0; i < -wheel_movement; i++) {
                        pinMode(GP_LEFT, OUTPUT);
                        digitalWrite(GP_LEFT, LOW);
                        vTaskDelay(pdMS_TO_TICKS(50));
                        pinMode(GP_LEFT, INPUT);
                        vTaskDelay(pdMS_TO_TICKS(50));
                    }
                }
            }
        }
    }
}

// ==========================================
// 🕹️ ENGINE 0: RAW USB HOST (JOYSTICK)
// ==========================================
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
    if (active_driver == 0 && device_connected) return;
    
    usb_device_handle_t temp_dev;
    if (usb_host_device_open(s_client, addr, &temp_dev) != ESP_OK) return;

    const usb_device_desc_t *dev_desc;
    usb_host_get_device_descriptor(temp_dev, &dev_desc);
    uint16_t vid = dev_desc->idVendor;
    uint16_t pid = dev_desc->idProduct;

    const usb_config_desc_t *cfg_desc;
    usb_host_get_active_config_descriptor(temp_dev, &cfg_desc);

    bool has_mouse = false;
    int offset = 0;
    const usb_standard_desc_t *next_desc = (const usb_standard_desc_t *)cfg_desc;

    uint8_t temp_if_num = 0;
    uint8_t temp_in_ep = 0;
    uint16_t temp_in_mps = 0;

    while (next_desc) {
        if (next_desc->bDescriptorType == USB_B_DESCRIPTOR_TYPE_INTERFACE) {
            const usb_intf_desc_t *intf = (const usb_intf_desc_t *)next_desc;
            if (intf->bInterfaceClass == 3 && intf->bInterfaceProtocol == 2) {
                has_mouse = true;
            }
            if (!has_mouse) {
                temp_if_num = intf->bInterfaceNumber;
            }
        }
        if (next_desc->bDescriptorType == USB_B_DESCRIPTOR_TYPE_ENDPOINT) {
            const usb_ep_desc_t *ep = (const usb_ep_desc_t *)next_desc;
            if ((ep->bmAttributes & 0x03) == 0x03 && (ep->bEndpointAddress & 0x80)) {
                if (temp_in_ep == 0) {
                    temp_in_ep = ep->bEndpointAddress;
                    temp_in_mps = ep->wMaxPacketSize;
                }
            }
        }
        next_desc = usb_parse_next_descriptor(next_desc, cfg_desc->wTotalLength, &offset);
    }

    if (has_mouse && active_driver == 0) {
        dual_println("\n>>> MOUSE DETECTED! SWITCHING TO HID DRIVER... <<<");
        usb_host_device_close(s_client, temp_dev);
        prefs.putInt("drv_mode", 1);
        delay(100);
        esp_restart();
        return;
    }

    if (!has_mouse && active_driver == 1) {
        dual_println("\n>>> JOYSTICK DETECTED! SWITCHING TO RAW DRIVER... <<<");
        usb_host_device_close(s_client, temp_dev);
        prefs.putInt("drv_mode", 0);
        delay(100);
        esp_restart();
        return;
    }

    if (active_driver == 1) {
        usb_host_device_close(s_client, temp_dev);
        return;
    }

    s_dev = temp_dev;
    connected_vid = vid;
    connected_pid = pid;
    s_if_num = temp_if_num;
    s_in_ep = temp_in_ep;
    s_in_mps = temp_in_mps;
    is_mouse_connected = false;
    
    bool found_custom = false;
    bool found_internal = false;

    // TIER 1 — NVS custom profile (explicit user configuration, highest priority)
    if (has_custom_profile) {
        if (connected_vid == custom_profile.vid && connected_pid == custom_profile.pid) {
            current_profile = custom_profile;
            found_custom = true;
        }
    }

    // TIER 2 — Internal C++ profile (factory fallback)
    if (!found_custom) {
        for (int i = 0; i < NUM_PROFILES; i++) {
            if (connected_vid == PROFILES[i].vid && connected_pid == PROFILES[i].pid) {
                current_profile = PROFILES[i];
                found_internal = true;
                break;
            }
        }
    }

    // Connection blink feedback (before configure_console_mode to avoid DB9 glitch)
    auto do_blink = [](uint32_t color, int times) {
        for (int i = 0; i < times; i++) {
            ws2812b.setPixelColor(0, color); ws2812b.show(); delay(120);
            ws2812b.setPixelColor(0, LED_OFF); ws2812b.show(); delay(120);
        }
    };
    do_blink(active_driver == 1 ? C_BLUE : C_GREEN, 2);

    dual_printf("\n*** CONNECTED: VID:%04x PID:%04x ***\n", connected_vid, connected_pid);
    if (found_custom) {
        dual_println(">>> TIER 1 MATCH: Loaded NVS Custom Profile");
    } else if (found_internal) {
        dual_printf(">>> TIER 2 MATCH: Loaded Internal Profile [%s]\n", current_profile.name);
    } else {
        dual_println(">>> UNKNOWN PAD: No matching profile found! Use the Web Configurator to map and save.");
    }

    if (s_in_ep) {
        usb_host_interface_claim(s_client, s_dev, s_if_num, 0);
        usb_host_transfer_alloc(s_in_mps, 0, &s_in_xfer);
        s_in_xfer->device_handle = s_dev;
        s_in_xfer->callback = in_transfer_cb;
        s_in_xfer->bEndpointAddress = s_in_ep;
        s_in_xfer->num_bytes = s_in_mps;
        usb_host_transfer_submit(s_in_xfer);
        device_connected = true;
    }
}

static void client_event_cb(const usb_host_client_event_msg_t *msg, void *arg) {
    if (msg->event == USB_HOST_CLIENT_EVENT_NEW_DEV) {
        s_new_dev_addr = msg->new_dev.address;
    } 
    else if (msg->event == USB_HOST_CLIENT_EVENT_DEV_GONE) {
        if (s_dev) {
            if (s_in_xfer) {
                usb_host_endpoint_clear(s_dev, s_in_ep);
                usb_host_transfer_free(s_in_xfer);
                s_in_xfer = nullptr;
            }
            usb_host_interface_release(s_client, s_dev, s_if_num);
            usb_host_device_close(s_client, s_dev);
            s_dev = nullptr;
            device_connected = false;
        }
    }
}

void usb_lib_task(void *arg) { 
    while (1) { 
        uint32_t f;
        usb_host_lib_handle_events(portMAX_DELAY, &f);
    }
}

// ==========================================
// 🐭 ENGINE 1: HID HOST (MOUSE / DONGLE)
// ==========================================
QueueHandle_t hid_host_event_queue;

typedef struct {
    hid_host_device_handle_t hid_device_handle;
    hid_host_driver_event_t event;
    void *arg;
} hid_host_event_queue_t;

void hid_host_interface_callback(hid_host_device_handle_t hid_device_handle, const hid_host_interface_event_t event, void *arg) {
    uint8_t data[64] = {0};
    size_t data_length = 0;

    if (event == HID_HOST_INTERFACE_EVENT_INPUT_REPORT) {
        ESP_ERROR_CHECK(hid_host_device_get_raw_input_report_data(hid_device_handle, data, 64, &data_length));
        
        pkt_t p;
        p.len = data_length;
        memcpy(p.data, data, data_length > 64 ? 64 : data_length);
        xQueueSendFromISR(s_pkt_q, &p, nullptr);

    } 
    else if (event == HID_HOST_INTERFACE_EVENT_DISCONNECTED) {
        ESP_ERROR_CHECK(hid_host_device_close(hid_device_handle));
        dual_println("\n*** DISCONNECTED: NATIVE HID MOUSE ***");
        is_mouse_connected = false;
        device_connected = false;
    }
}

void hid_host_device_event(hid_host_device_handle_t hid_device_handle, const hid_host_driver_event_t event, void *arg) {
    hid_host_dev_params_t dev_params;
    ESP_ERROR_CHECK(hid_host_device_get_params(hid_device_handle, &dev_params));

    const hid_host_device_config_t dev_config = {.callback = hid_host_interface_callback, .callback_arg = NULL};

    if (event == HID_HOST_DRIVER_EVENT_CONNECTED) {
        if (HID_SUBCLASS_BOOT_INTERFACE == dev_params.sub_class && HID_PROTOCOL_MOUSE == dev_params.proto) {
            dual_println("\n*** CONNECTED: NATIVE HID MOUSE/DONGLE ***");
            dual_println("*** WAITING 2.5s SAFETY WINDOW FOR AMIGA CHECK... ***");
            
            mouse_is_pending = true;
            pending_mouse_time = millis();
            device_connected = true;
            
            ESP_ERROR_CHECK(hid_host_device_open(hid_device_handle, &dev_config));

            // 🔄 DYNAMIC PROTOCOL SWITCH
            if (mouse_wheel_enabled) {
                dual_println(">>> WHEEL ENABLED: Requesting Dynamic REPORT Protocol <<<");
                ESP_ERROR_CHECK(hid_class_request_set_protocol(hid_device_handle, HID_REPORT_PROTOCOL_REPORT));
            } else {
                dual_println(">>> WHEEL DISABLED: Requesting Standard BOOT Protocol <<<");
                ESP_ERROR_CHECK(hid_class_request_set_protocol(hid_device_handle, HID_REPORT_PROTOCOL_BOOT)); 
            }
            
            ESP_ERROR_CHECK(hid_host_device_start(hid_device_handle));

        } else {
            dual_println("\n>>> NON-MOUSE INTERFACE DETECTED (IGNORED BY HID) <<<");
        }
    }
}

void hid_host_device_callback(hid_host_device_handle_t hid_device_handle, const hid_host_driver_event_t event, void *arg) {
    const hid_host_event_queue_t evt_queue = {.hid_device_handle = hid_device_handle, .event = event, .arg = arg};
    xQueueSend(hid_host_event_queue, &evt_queue, 0);
}

void hid_lib_task(void *arg) {
    hid_host_event_queue = xQueueCreate(10, sizeof(hid_host_event_queue_t));

    const hid_host_driver_config_t hid_host_driver_config = {
        .create_background_task = true,
        .task_priority = 5,
        .stack_size = 4096,
        .core_id = 0,
        .callback = hid_host_device_callback,
        .callback_arg = NULL
    };

    ESP_ERROR_CHECK(hid_host_install(&hid_host_driver_config));
    
    hid_host_event_queue_t evt_queue;
    while (true) {
        if (xQueueReceive(hid_host_event_queue, &evt_queue, portMAX_DELAY)) {
            hid_host_device_event(evt_queue.hid_device_handle, evt_queue.event, evt_queue.arg);
        }
    }
}

// ==========================================
// 🚀 MAIN SETUP
// ==========================================
void setup() {
    // REL_3_2_MOD: [7] Unused GPIO → INPUT_PULLDOWN (no floating).
    // Excludes strapping pins 45,46 and input-only 35,36,37 (no pulldown hw).
    btStop();
    const uint8_t unused_pins[] = {2,12,14,15,16,17,18,38,39,40,41,42,47,48};
    for (uint8_t p : unused_pins) { pinMode(p, INPUT_PULLDOWN); }
    delay(1000);
    
    pinMode(0, INPUT_PULLUP); // 🚨 Inizializzazione Pin per il Tasto BOOT

    prefs.begin("usbconfig", false);
    active_driver = prefs.getInt("drv_mode", 0);
    dev_mode = prefs.getBool("dev_mode", false);
    
    mouse_amiga_speed = prefs.getInt("amiga_speed", 3);
    mouse_c64_speed = prefs.getInt("c64_speed", 3);
    mouse_wheel_enabled = prefs.getBool("en_wheel", false);
    mouse_pal_timing = prefs.getBool("pal_timing", true);
    compute_pal_timing(mouse_pal_timing);
    delayOnX = g_MIN_X;
    delayOnY = g_MIN_Y;

    load_custom_profile_from_nvs();
    load_sys_colors_from_nvs();
    
    pinMode(19, OUTPUT); pinMode(20, OUTPUT);
    digitalWrite(19, LOW); digitalWrite(20, LOW);
    delay(200);
    pinMode(19, INPUT); pinMode(20, INPUT);
    delay(100);

    pinMode(SWITCH_MJ, INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(SWITCH_MJ), switchMJHandler, CHANGE);

    pinMode(GP_UP, INPUT);
    pinMode(GP_DOWN, INPUT);
    pinMode(GP_RIGHT, INPUT);
    pinMode(GP_LEFT, INPUT);
    pinMode(GP_FIRE1, INPUT);
    delay(50);

    if (digitalRead(GP_UP) == HIGH || digitalRead(GP_DOWN) == HIGH || 
        digitalRead(GP_RIGHT) == HIGH || digitalRead(GP_LEFT) == HIGH || digitalRead(GP_FIRE1) == HIGH) {
        C64_Amiga_Connected = true;
    } else {
        C64_Amiga_Connected = false;
    }

    bool physical_high = (digitalRead(SWITCH_MJ) == HIGH);
    bool is_inv = prefs.getBool("inv_switch", false);
    bool amiga_boot = is_inv ? !physical_high : physical_high;

    if (!C64_Amiga_Connected && dev_mode) {
        amiga_boot = prefs.getBool("dev_amiga", false);
    }
    
    int led_format = prefs.getInt("led_fmt", 0);
    if (led_format == 1) ws2812b.updateType(NEO_RGB + NEO_KHZ800);
    else ws2812b.updateType(NEO_GRB + NEO_KHZ800);
    
    ws2812b.begin();
    ws2812b.setBrightness(sys_colors.brightness);
    ws2812b.setPixelColor(0, amiga_boot ? sys_colors.idle_amiga : sys_colors.idle_c64);
    ws2812b.show();

    // REL_3_2_MOD: [4] Serial aperta solo se utile:
    //   !C64 + !dev → Serial  (WebUI via USB-C)
    //   !C64 +  dev → Serial2 (USB-C occupata da USB Host, adattatore su 43/44)
    //    C64 + !dev → niente  (nessuno in ascolto)
    //    C64 +  dev → niente  (fisicamente impossibile collegare adattatore)
    if (!C64_Amiga_Connected) {
        if (!dev_mode) {
            Serial.begin(115200);
            delay(2000);
            Serial.println("\n=================================");
            Serial.println("  USB -> DB9 ADAPTER v1.2        ");
            Serial.println("--- Booting: Configuration Mode ---");
            Serial.println("=================================");
            current_mode = MODE_SERVICE;
        } else {
            Serial2.begin(115200, SERIAL_8N1, GP_RX, GP_TX);
            Serial2.println("\n=================================");
            Serial2.println("  USB -> DB9 ADAPTER v1.2        ");
            Serial2.println("--- Booting: DEVELOPER MODE ---");
            Serial2.println("--- Serial2 su GPIO 43/44 ---");
            Serial2.println("=================================");
            Serial2.println(">>> Type 'service' for the advanced configuration menu <<<");
            current_mode = MODE_SERVICE;
        }
    }

    if (C64_Amiga_Connected || dev_mode) {
        Serial2.println("[BOOT] Initializing USB Host...");

        s_pkt_q = xQueueCreate(16, sizeof(pkt_t));
        usb_host_config_t host_cfg = { .skip_phy_setup = false, .intr_flags = ESP_INTR_FLAG_LEVEL1 };
        esp_err_t err = usb_host_install(&host_cfg);

        if (err != ESP_OK) Serial2.println(">>> FATAL ERROR: Failed to install USB Host!");
        else Serial2.println(">>> USB Host installed successfully.");

        xTaskCreatePinnedToCore(usb_lib_task, "usb_lib", 4096, nullptr, 10, nullptr, 0);

        usb_host_client_config_t client_cfg = { .is_synchronous = false, .max_num_event_msg = 5, .async = { .client_event_callback = client_event_cb, .callback_arg = nullptr } };
        usb_host_client_register(&client_cfg, &s_client);

        if (active_driver == 1) {
            // 🌀 START WHEEL TASK ONLY IN MOUSE MODE
            wheel_queue = xQueueCreate(10, sizeof(int8_t));
            xTaskCreatePinnedToCore(wheelTask, "wheel_task", 2048, NULL, 5, NULL, 1);
            xTaskCreatePinnedToCore(hid_lib_task, "hid_lib", 4096, nullptr, 10, nullptr, 0);
        }
    }

    if (C64_Amiga_Connected || dev_mode) {
        configure_console_mode(amiga_boot);
        delay(600);
        
        // REL_3_2_MOD: [CPU] 10MHz solo se C64 connesso (non Amiga, non standalone)
        // Fatto qui in setup dove C64_Amiga_Connected e is_amiga sono già noti,
        // prima di init_c64_timers() così i timer nascono alla frequenza finale.
        // Laface pattern: 10MHz joystick, 240MHz mouse (timer POT incompatibili con 10MHz)
        if (C64_Amiga_Connected) {
            if (active_driver == 1) setCpuFrequencyMhz(160); // Mouse: CPU veloce per i timer POT
            else                    setCpuFrequencyMhz(80);  // Joystick/Amiga: risparmio energetico
        }
        init_c64_timers();

        set_joy_pin(GP_UP, false); set_joy_pin(GP_DOWN, false);
        set_joy_pin(GP_LEFT, false); set_joy_pin(GP_RIGHT, false);
        set_joy_pin(GP_FIRE1, false); set_joy_pin(GP_FIRE2, false);
        set_fire3_pin(false);
    }
}

// ==========================================
// 🔄 MAIN LOOP WITH JSON & CLI ROUTING
// ==========================================
void loop() {
    
    // --- 🚨 HARD RESET VIA BOOT BUTTON (ONLY IN STANDALONE) ---
    if (!C64_Amiga_Connected) {
        static unsigned long boot_press_time = 0;
        static bool boot_is_pressed = false;

        if (digitalRead(0) == LOW) {
            if (!boot_is_pressed) {
                boot_is_pressed = true;
                boot_press_time = millis();
            } else if (millis() - boot_press_time > 5000) {
                dual_println("\n>>> ⚠️ HARDWARE FACTORY RESET TRIGGERED! ⚠️ <<<");
// 🔴 Factory Reset warning flash
                for (int i = 0; i < 5; i++) {
                    ws2812b.setPixelColor(0, C_RED);
                    ws2812b.show();
                    delay(200);
                    ws2812b.setPixelColor(0, C_BLACK);
                    ws2812b.show();
                    delay(200);
                }
                prefs.clear(); // Brasa la NVS
                delay(500);
                esp_restart();
            }
        } else {
            boot_is_pressed = false;
        }
    }
    // -----------------------------------------------------------

    String incoming = "";
    Stream* activePort = nullptr;

    // Standalone senza dev_mode: Web UI su USB-C (Serial CDC)
    // Dev_mode (standalone o console): service menu su Serial2 (GPIO 43/44)
    // In standalone+dev_mode la USB-C è occupata dall'USB Host → Serial CDC non init
    if (!C64_Amiga_Connected && !dev_mode && Serial.available() > 0) {
        activePort = &Serial;
    } else if (dev_mode && Serial2.available() > 0) {
        activePort = &Serial2;
    }

    if (activePort != nullptr) {
        // Pulisce la sporcizia prima di leggere il vero carattere
        while (activePort->available() > 0) {
            char c = activePort->peek();
            if (c == '\r' || c == '\n' || c == ' ') activePort->read();
            else break; 
        }

        if (activePort->available() > 0) {
            if (activePort->peek() == '{') {
                web_ui_active = true; // 🤫 SILENZIA IL DUAL_PRINT TESTUALE
                incoming = activePort->readStringUntil('\n');
                incoming.trim();
            } else {
                handleServiceMenu(); 
            }
        }
    } else if (C64_Amiga_Connected && !dev_mode && Serial2.available() > 0) {
        // Modalità Normale non-dev: ascoltiamo solo la Serial2 per i comandi testuali
        handleServiceMenu();
    }

    // 2. MOTORE JSON
    if (incoming.length() > 0) {
        if (incoming.startsWith("{\"cmd\":\"save\"")) {
            StaticJsonDocument<1024> doc;
            DeserializationError error = deserializeJson(doc, incoming);
            if (!error) {
                prefs.putUInt("vid", doc["vid"] | 0);
                prefs.putUInt("pid", doc["pid"] | 0);
                prefs.putInt("dpad_type", doc["dpad_type"] | 0);
                prefs.putInt("byte_x", doc["byte_x"] | 0);
                prefs.putInt("byte_y", doc["byte_y"] | 0);
                prefs.putInt("val_up", doc["val_up"] | 0);
                prefs.putInt("val_down", doc["val_down"] | 0);
                prefs.putInt("val_left", doc["val_left"] | 0);
                prefs.putInt("val_right", doc["val_right"] | 0);
                
                prefs.putInt("byte_f1", doc["byte_f1"] | 0);
                prefs.putInt("val_f1", doc["val_f1"] | 0);
                if(doc.containsKey("c_cf1")) prefs.putUInt("c_cf1", doc["c_cf1"].as<uint32_t>());
                
                prefs.putInt("byte_f2", doc["byte_f2"] | 0);
                prefs.putInt("val_f2", doc["val_f2"] | 0);
                if(doc.containsKey("c_cf2")) prefs.putUInt("c_cf2", doc["c_cf2"].as<uint32_t>());
                
                prefs.putInt("byte_f3", doc["byte_f3"] | 0);
                prefs.putInt("val_f3", doc["val_f3"] | 0);
                if(doc.containsKey("c_cf3")) prefs.putUInt("c_cf3", doc["c_cf3"].as<uint32_t>());
                
                prefs.putInt("byte_up_alt", doc["byte_up_alt"] | 0);
                prefs.putInt("val_up_alt", doc["val_up_alt"] | 0);
                if(doc.containsKey("c_cua")) prefs.putUInt("c_cua", doc["c_cua"].as<uint32_t>());
                
                prefs.putInt("byte_auto", doc["byte_auto"] | 0);
                prefs.putInt("val_auto", doc["val_auto"] | 0);
                if(doc.containsKey("c_cauto")) prefs.putUInt("c_cauto", doc["c_cauto"].as<uint32_t>());
                
                prefs.putInt("byte_auto_off", doc["byte_auto_off"] | 0);
                prefs.putInt("val_auto_off", doc["val_auto_off"] | 0);
                prefs.putInt("byte_auto_h", doc["byte_auto_h"] | 0);
                prefs.putInt("val_auto_h", doc["val_auto_h"] | 0);

                prefs.putBool("has_custom", true);
                activePort->print("{\"status\":\"ok\"}\n");
                delay(200); esp_restart();
            }
        }
        else if (incoming.startsWith("{\"cmd\":\"get_info\"")) {
            bool d_mode = prefs.getBool("dev_mode", false);
            bool amg_ovr = prefs.getBool("dev_amiga", false);
            bool is_inv = prefs.getBool("inv_switch", false);
            
            bool physical_high = (digitalRead(SWITCH_MJ) == HIGH);
            bool is_amg = d_mode ? amg_ovr : (is_inv ? !physical_high : physical_high);
            
            activePort->printf("{\"is_amiga\":%s,\"dev_mode\":%s,\"inv_switch\":%s,\"dev_amiga\":%s}\n",
                               is_amg ? "true" : "false",
                               d_mode ? "true" : "false",
                               is_inv ? "true" : "false",
                               amg_ovr ? "true" : "false");
        }
        else if (incoming.startsWith("{\"cmd\":\"mouse_get\"")) {
            activePort->printf("{\"amiga_speed\":%d,\"c64_speed\":%d,\"enable_wheel\":%d,\"pal_timing\":%d}\n",
                               mouse_amiga_speed, mouse_c64_speed, mouse_wheel_enabled ? 1 : 0, mouse_pal_timing ? 1 : 0);
        }
        else if (incoming.startsWith("{\"cmd\":\"mouse_save\"")) {
            StaticJsonDocument<256> doc;
            if (!deserializeJson(doc, incoming)) {
                if (doc.containsKey("amiga_speed")) { mouse_amiga_speed = doc["amiga_speed"]; prefs.putInt("amiga_speed", mouse_amiga_speed); }
                if (doc.containsKey("c64_speed")) { mouse_c64_speed = doc["c64_speed"]; prefs.putInt("c64_speed", mouse_c64_speed); }
                if (doc.containsKey("enable_wheel")) { mouse_wheel_enabled = (doc["enable_wheel"] == 1); prefs.putBool("en_wheel", mouse_wheel_enabled); }
                if (doc.containsKey("pal_timing")) {
                    mouse_pal_timing = (doc["pal_timing"] == 1);
                    prefs.putBool("pal_timing", mouse_pal_timing);
                    compute_pal_timing(mouse_pal_timing); // apply immediately
                }
                activePort->print("{\"status\":\"ok\"}\n");
            }
        }
  else if (incoming.startsWith("{\"cmd\":\"led_get\"")) {
            // Map physical dim colors back to bright HEX for WebUI
            auto mapDimmedToHex = [](uint32_t c) -> String {
                if (c == C_WHITE) return "#FFFFFF";      // ⚪
                if (c == C_RED) return "#FF0000";        // 🔴
                if (c == C_ORANGE) return "#FF9900";     // 🟠
                if (c == C_YELLOW) return "#FFFF00";     // 🟡
                if (c == C_GREEN) return "#00FF00"; // 🟢 (Web Green)
                if (c == C_BLUE) return "#0000FF";       // 🔵
                if (c == C_CYAN) return "#00FFFF";       // 🩵
                if (c == C_PURPLE) return "#FF00FF";     // 🟣
                if (c == C_PINK) return "#FF99CC";       // 🩷
                return "#000000";                        // ⚫
            };
            
            StaticJsonDocument<1024> doc;
            doc["led_fmt"] = prefs.getInt("led_fmt", 0);
            doc["multicolor"] = sys_colors.multicolor_enabled ? 1 : 0;
            
            doc["idle_amiga"] = mapDimmedToHex(sys_colors.idle_amiga); 
            doc["idle_c64"] = mapDimmedToHex(sys_colors.idle_c64);
            doc["dir_glowing"] = mapDimmedToHex(sys_colors.dir_glowing);
            doc["btn_fire1"] = mapDimmedToHex(sys_colors.btn_fire1);
            doc["btn_fire2"] = mapDimmedToHex(sys_colors.btn_fire2);
            doc["btn_fire3"] = mapDimmedToHex(sys_colors.btn_fire3);
            doc["btn_alt"] = mapDimmedToHex(sys_colors.btn_alt); 
            doc["btn_auto"] = mapDimmedToHex(sys_colors.btn_auto);
            
            serializeJson(doc, *activePort);
            activePort->println();
        }
        else if (incoming.startsWith("{\"cmd\":\"led_save\"")) {
            StaticJsonDocument<2048> doc;
            if (!deserializeJson(doc, incoming)) {
                // Map bright WebUI HEX to physical dim colors
                auto mapHexToDimmed = [](const char* hex) -> uint32_t { 
                    if (!hex || strlen(hex) < 7) return C_BLACK; // ⚫
                    uint32_t rgb = strtol(hex + 1, NULL, 16);
                    switch(rgb) {
                        case 0xFFFFFF: return C_WHITE;       // ⚪
                        case 0xFF0000: return C_RED;         // 🔴
                        case 0xFF9900: return C_ORANGE;      // 🟠
                        case 0xFFFF00: return C_YELLOW;      // 🟡
                        case 0x00FF00: return C_GREEN;       // 🟢
                        case 0x0000FF: return C_BLUE;        // 🔵
                        case 0x00FFFF: return C_CYAN;        // 🩵
                        case 0xFF00FF: return C_PURPLE;      // 🟣
                        case 0xFF99CC: return C_PINK;        // 🩷
                        default: return C_BLACK;             // ⚫
                    }
                };

                if (doc.containsKey("led_fmt")) prefs.putInt("led_fmt", doc["led_fmt"].as<int>());
                if (doc.containsKey("multicolor")) sys_colors.multicolor_enabled = (doc["multicolor"].as<int>() == 1);

                if(doc.containsKey("idle_amiga")) sys_colors.idle_amiga = mapHexToDimmed(doc["idle_amiga"]);
                if(doc.containsKey("idle_c64")) sys_colors.idle_c64 = mapHexToDimmed(doc["idle_c64"]);
                if(doc.containsKey("dir_glowing")) sys_colors.dir_glowing = mapHexToDimmed(doc["dir_glowing"]);
                if(doc.containsKey("btn_fire1")) sys_colors.btn_fire1 = mapHexToDimmed(doc["btn_fire1"]);
                if(doc.containsKey("btn_fire2")) sys_colors.btn_fire2 = mapHexToDimmed(doc["btn_fire2"]);
                if(doc.containsKey("btn_fire3")) sys_colors.btn_fire3 = mapHexToDimmed(doc["btn_fire3"]);
                if(doc.containsKey("btn_alt")) sys_colors.btn_alt = mapHexToDimmed(doc["btn_alt"]);
                if(doc.containsKey("btn_auto")) sys_colors.btn_auto = mapHexToDimmed(doc["btn_auto"]);

                prefs.putBytes("col_blob", &sys_colors, sizeof(SystemColors));
                prefs.putBool("has_colors", true);
                activePort->print("{\"status\":\"ok\"}\n");
                
                int led_fmt = prefs.getInt("led_fmt", 0);
                ws2812b.updateType((led_fmt == 1) ? NEO_RGB + NEO_KHZ800 : NEO_GRB + NEO_KHZ800);
                ws2812b.setPixelColor(0, is_amiga ? sys_colors.idle_amiga : sys_colors.idle_c64);
                ws2812b.show();
            }
        }
        else if (incoming.startsWith("{\"cmd\":\"led_reset\"")) {
            prefs.remove("has_colors");
            prefs.remove("col_blob");
            prefs.remove("led_fmt");
            activePort->print("{\"status\":\"ok\"}\n");
            delay(200); esp_restart();
        }
        else if (incoming.startsWith("{\"cmd\":\"devmode_toggle\"}")) {
            bool current = prefs.getBool("dev_mode", false);
            prefs.putBool("dev_mode", !current);
            activePort->print("{\"status\":\"ok\"}\n");
        }
        else if (incoming.startsWith("{\"cmd\":\"invert_toggle\"}")) {
            bool current = prefs.getBool("inv_switch", false);
            prefs.putBool("inv_switch", !current);
            activePort->print("{\"status\":\"ok\"}\n");
            
            // APPLICAZIONE ISTANTANEA (LIVE TUNING)
            bool physical_high = (digitalRead(SWITCH_MJ) == HIGH);
            is_amiga = (!current) ? !physical_high : physical_high;
            if (prefs.getBool("dev_mode", false)) is_amiga = prefs.getBool("dev_amiga", false);
            
            configure_console_mode(is_amiga);
            ws2812b.setPixelColor(0, is_amiga ? sys_colors.idle_amiga : sys_colors.idle_c64);
            ws2812b.show();
        }
        else if (incoming.startsWith("{\"cmd\":\"override_toggle\"}")) {
            bool current = prefs.getBool("dev_amiga", false);
            prefs.putBool("dev_amiga", !current);
            activePort->print("{\"status\":\"ok\"}\n");
            
            // APPLICAZIONE ISTANTANEA (LIVE TUNING) IN DEV MODE
            is_amiga = !current;
            configure_console_mode(is_amiga);
            ws2812b.setPixelColor(0, is_amiga ? sys_colors.idle_amiga : sys_colors.idle_c64);
            ws2812b.show();
        }
        else if (incoming.startsWith("{\"cmd\":\"mem_status\"}")) {
            bool has_mouse = prefs.isKey("c64_speed") || prefs.isKey("amiga_speed");
            bool has_pad = prefs.getBool("has_custom", false);
            bool has_led = prefs.getBool("has_colors", false);
            bool has_dev = prefs.isKey("dev_mode");
            bool has_inv = prefs.isKey("inv_switch");
            
            activePort->printf("{\"mouse\":%s,\"pad\":%s,\"led\":%s,\"dev\":%s,\"inv\":%s}\n",
                              has_mouse ? "true" : "false", has_pad ? "true" : "false",
                              has_led ? "true" : "false", has_dev ? "true" : "false",
                              has_inv ? "true" : "false");
        }
        else if (incoming.startsWith("{\"cmd\":\"clear_mem\"")) {
            StaticJsonDocument<128> doc;
            if (!deserializeJson(doc, incoming) && doc.containsKey("target")) {
                String t = doc["target"].as<String>();
                if (t == "mouse") { prefs.remove("amiga_speed"); prefs.remove("c64_speed"); prefs.remove("en_wheel"); prefs.remove("pal_timing"); }
                else if (t == "pad") { prefs.remove("has_custom"); prefs.remove("cust_blob"); }
                else if (t == "led") { prefs.remove("has_colors"); prefs.remove("col_blob"); prefs.remove("led_fmt"); }
                else if (t == "dev") { prefs.remove("dev_mode"); prefs.remove("dev_amiga"); }
                else if (t == "inv") { prefs.remove("inv_switch"); }
                activePort->print("{\"status\":\"ok\"}\n");
            }
        }
        else if (incoming.startsWith("{\"cmd\":\"reboot_cmd\"}")) {
            activePort->print("{\"status\":\"ok\"}\n"); delay(500); esp_restart();
        }
        else if (incoming.startsWith("{\"cmd\":\"factory_reset\"}")) {
            prefs.clear();
            activePort->print("{\"status\":\"ok\"}\n"); delay(500); esp_restart();
        }
        
        web_ui_active = false; // Rilascia il silenziatore dopo l'esecuzione
    } 

    // --- FINE GESTIONE SERIALI, INIZIO LOGICA HARDWARE NORMALE ---
    
    if (!C64_Amiga_Connected) {
        if (!dev_mode) return;
    }

    check_polling_timer();
    
    usb_host_client_handle_events(s_client, 1);
    if (s_new_dev_addr) { 
        uint8_t a = s_new_dev_addr; s_new_dev_addr = 0;
        start_sniff(a);
    }
    
    pkt_t p;
    while (xQueueReceive(s_pkt_q, &p, 0) == pdTRUE) {
        process_usb_packet(p);
    }

    run_gpio_diagnostics();
    update_hardware_and_leds();

    if (mouse_is_pending && (millis() - pending_mouse_time > 2500)) {
        is_mouse_connected = true;
        mouse_is_pending = false;
        dual_println("\n[+] MOUSE FULLY ENGAGED!");
    }
}
// EOF