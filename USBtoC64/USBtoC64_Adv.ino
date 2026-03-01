// ==========================================
// USB to C64/Amiga Adapter - Advanced v2.12.0 (COMBO DONGLE FIX)
// File: USBtoC64_Adv.ino
// ==========================================
#include <Arduino.h>
#include <stdint.h>
#include "soc/rtc_cntl_reg.h" 
#include <WiFi.h>
#include "esp_bt.h"
#include <Preferences.h>

#include "usb/usb_host.h"
#include "hid_host.h"
#include "hid_usage_mouse.h"

Preferences prefs;
int active_driver = 0; 

#include "Globals.h"
#include "Hardware.h"
#include "ServiceTools.h"
#include "InputEngine.h"
#include "CoreTasks.h"

void IRAM_ATTR switchMJHandler() {
    static unsigned long last_interrupt_time = 0;
    unsigned long interrupt_time = millis();
    if (interrupt_time - last_interrupt_time > 200) { 
        esp_restart();
    }
    last_interrupt_time = interrupt_time;
}

// ==========================================
// 🕹️ ENGINE 0: RAW USB HOST (JOYSTICK & GLOBAL INSPECTOR)
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

// This function now parses the entire device to decide which driver handles it
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

    // Scan all interfaces present on the device
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

    // --- CROSS-REBOOT LOGIC ---
    if (has_mouse && active_driver == 0) {
        Serial2.println("\n>>> MOUSE DETECTED! SWITCHING TO HID DRIVER... <<<");
        usb_host_device_close(s_client, temp_dev);
        prefs.putInt("drv_mode", 1);
        delay(100);
        esp_restart();
        return;
    }

    if (!has_mouse && active_driver == 1) {
        Serial2.println("\n>>> JOYSTICK DETECTED! SWITCHING TO RAW DRIVER... <<<");
        usb_host_device_close(s_client, temp_dev);
        prefs.putInt("drv_mode", 0);
        delay(100);
        esp_restart();
        return;
    }

    if (active_driver == 1) {
        // It's a mixed device or mouse.
        // We close the raw channel so we don't interfere.
        // Leave the field open for the hid_host engine.
        usb_host_device_close(s_client, temp_dev);
        return;
    }

    // --- JOYSTICK ALLOCATION ---
    s_dev = temp_dev;
    connected_vid = vid;
    connected_pid = pid;
    s_if_num = temp_if_num;
    s_in_ep = temp_in_ep;
    s_in_mps = temp_in_mps;
    is_mouse_connected = false;

    bool found_internal = false;
    for (int i = 0; i < NUM_PROFILES; i++) {
        if (connected_vid == PROFILES[i].vid && connected_pid == PROFILES[i].pid) {
            current_profile = PROFILES[i];
            found_internal = true;
            break;
        }
    }

    Serial2.printf("\n*** CONNECTED: %s (VID:%04x PID:%04x) ***\n",
                   found_internal ? current_profile.name : "UNKNOWN PAD",
                   connected_vid, connected_pid);
    use_html_configurator = false;

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
        Serial2.println("\n*** DISCONNECTED: NATIVE HID MOUSE ***");
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
            Serial2.println("\n*** CONNECTED: NATIVE HID MOUSE/DONGLE ***");
            is_mouse_connected = true;
            device_connected = true;
            ESP_ERROR_CHECK(hid_host_device_open(hid_device_handle, &dev_config));
            ESP_ERROR_CHECK(hid_class_request_set_protocol(hid_device_handle, HID_REPORT_PROTOCOL_BOOT)); 
            ESP_ERROR_CHECK(hid_host_device_start(hid_device_handle));
        } else {
            // Peacefully ignore anything that is not a native mouse (e.g., keyboards or media keys on dongles)
            Serial2.println("\n>>> NON-MOUSE INTERFACE DETECTED (IGNORED BY HID) <<<");
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
   
    Serial2.begin(115200, SERIAL_8N1, GP_RX, GP_TX);
    WiFi.mode(WIFI_OFF);
    btStop();
    delay(1000);

    prefs.begin("usbconfig", false);
    active_driver = prefs.getInt("drv_mode", 0);
    
    Serial2.println("\n=================================");
    Serial2.println("  USB -> DB9 ADAPTER v3.0.5 (DUAL) ");
    Serial2.println("=================================");
    Serial2.printf(">> ACTIVE DRIVER: %s <<\n", active_driver == 1 ? "HID (MOUSE)" : "RAW (JOYSTICK)");

    pinMode(19, OUTPUT); pinMode(20, OUTPUT);
    digitalWrite(19, LOW);
    digitalWrite(20, LOW);
    delay(200);
    pinMode(19, INPUT); pinMode(20, INPUT);
    delay(100);

    pinMode(SWITCH_MJ, INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(SWITCH_MJ), switchMJHandler, CHANGE);

    
    bool amiga_boot = (digitalRead(SWITCH_MJ) == LOW);
 
    
    ws2812b.begin();
    ws2812b.setBrightness(40);
    ws2812b.setPixelColor(0, amiga_boot ? LED_IDLE_AMIGA : LED_IDLE_C64);
    ws2812b.show();

    configure_console_mode(amiga_boot);
    delay(600);

    if (!is_amiga) {
        timerOnX = timerBegin(10000000); timerAlarm(timerOnX, delayOnX, false, 0);
        timerOffX = timerBegin(10000000);
        timerAlarm(timerOffX, delayOffX, false, 0);
        timerOnY = timerBegin(10000000); timerAlarm(timerOnY, delayOnY, false, 0);
        timerOffY = timerBegin(10000000); timerAlarm(timerOffY, delayOffY, false, 0);

        timerAttachInterrupt(timerOnX, &turnOnPotX);
        timerAttachInterrupt(timerOffX, &turnOffPotX);
        timerAttachInterrupt(timerOnY, &turnOnPotY); timerAttachInterrupt(timerOffY, &turnOffPotY);
        
        pinMode(GP1, INPUT_PULLUP);
        attachInterrupt(digitalPinToInterrupt(GP1), handleInterrupt, RISING);
    } else {
        timerOffX = timerBegin(10000000); timerAlarm(timerOffX, delayOffX, false, 0);
        timerOffY = timerBegin(10000000);
        timerAlarm(timerOffY, delayOffY, false, 0);
        timerAttachInterrupt(timerOffX, &turnOffJoyX); timerAttachInterrupt(timerOffY, &turnOffJoyY);
    }

    set_joy_pin(GP_UP, false); set_joy_pin(GP_DOWN, false);
    set_joy_pin(GP_LEFT, false); set_joy_pin(GP_RIGHT, false);
    set_joy_pin(GP_FIRE1, false); set_joy_pin(GP_FIRE2, false);
    set_fire3_pin(false);
    //if (!amiga_boot) pinMode(GP_POTY, INPUT);

    s_pkt_q = xQueueCreate(16, sizeof(pkt_t));

    // The Global Watchdog always listens
    usb_host_config_t host_cfg = { .skip_phy_setup = false, .intr_flags = ESP_INTR_FLAG_LEVEL1 };
    ESP_ERROR_CHECK(usb_host_install(&host_cfg));
    xTaskCreatePinnedToCore(usb_lib_task, "usb_lib", 4096, nullptr, 10, nullptr, 0);

    usb_host_client_config_t client_cfg = { .is_synchronous = false, .max_num_event_msg = 5, .async = { .client_event_callback = client_event_cb, .callback_arg = nullptr } };
    usb_host_client_register(&client_cfg, &s_client);

    // The specialized engine is started only if needed
    if (active_driver == 1) {
        xTaskCreatePinnedToCore(hid_lib_task, "hid_lib", 4096, nullptr, 10, nullptr, 0);
    }
}

void loop() {
    handleServiceMenu();
    check_polling_timer();
    
    // We always process global events for hotplug
    usb_host_client_handle_events(s_client, 1);
    if (s_new_dev_addr) { 
        uint8_t a = s_new_dev_addr; 
        s_new_dev_addr = 0;
        start_sniff(a);
    }
    
    pkt_t p;
    while (xQueueReceive(s_pkt_q, &p, 0) == pdTRUE) {
        process_usb_packet(p);
    }

    run_gpio_diagnostics();
    update_hardware_and_leds();
    
    // 🛡️ HARDWARE WATCHDOG
    check_switch_mismatch(); 
}