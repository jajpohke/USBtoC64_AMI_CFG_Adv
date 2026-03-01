// ==========================================
// USB to C64/Amiga Adapter - Advanced 1.1
// File: Hardware.h
// Description: Hardware pin management, interrupts and console mode
// ==========================================
#pragma once

#include <Arduino.h>
#include "driver/gpio.h"
#include "soc/gpio_struct.h"
#include "Globals.h"

// ⚡ --- FAST GPIO INTERRUPTS FOR C64 MOUSE (SID 1351) --- ⚡

void IRAM_ATTR handleInterrupt() {
    // Workaround: sometimes the interrupt triggers twice, check pin level
    if (!((GPIO.in >> GP1) & 1)) return; 
    timerWrite(timerOnX, 0);
    timerAlarm(timerOnX, delayOnX, false, 0);
    timerWrite(timerOnY, 0);
    timerAlarm(timerOnY, delayOnY, false, 0);
}

void IRAM_ATTR turnOnPotX() {
    // Pin 4 (GP_C64_SIG_MODE_SW) -> DB9 Pin 5 (POT X)
    GPIO.out_w1ts = (1 << GP_C64_SIG_MODE_SW); 
    timerWrite(timerOffX, 0);
    timerAlarm(timerOffX, delayOffX, false, 0);
}

void IRAM_ATTR turnOffPotX() { 
    GPIO.out_w1tc = (1 << GP_C64_SIG_MODE_SW); 
}

void IRAM_ATTR turnOnPotY() {
    // Pin 6 (GP_POTY_GND) -> DB9 Pin 9 (POT Y)
    GPIO.out_w1ts = (1 << GP_POTY_GND); 
    timerWrite(timerOffY, 0);
    timerAlarm(timerOffY, delayOffY, false, 0);
}

void IRAM_ATTR turnOffPotY() { 
    GPIO.out_w1tc = (1 << GP_POTY_GND); 
}

void IRAM_ATTR turnOffJoyX() {
    digitalWrite(GP_LEFT, LOW);
    digitalWrite(GP_RIGHT, LOW);
    pinMode(GP_LEFT, INPUT);
    pinMode(GP_RIGHT, INPUT);
}

void IRAM_ATTR turnOffJoyY() {
    digitalWrite(GP_UP, LOW);
    digitalWrite(GP_DOWN, LOW);
    pinMode(GP_UP, INPUT);
    pinMode(GP_DOWN, INPUT);
}

// 🔌 --- HARDWARE PIN MANAGEMENT --- 🔌

void configure_console_mode(bool amiga_mode) {
    is_amiga = amiga_mode;
    if (is_amiga) {
        pinMode(GP_POTY, INPUT_PULLUP); 
        pinMode(GP_POTY_GND, INPUT); 
        pinMode(GP_C64_SIG_MODE_SW, INPUT); 
        Serial2.println("\n>>> SYSTEM SET TO: AMIGA (Fire 2 on Pin 9, Fire 3 on Pin 5) <<<");
    } else {
        // C64: Initialize native pins pulled LOW (GND) to kill noise and simulate RELEASED state (255)
        pinMode(GP_FIRE2, OUTPUT); digitalWrite(GP_FIRE2, LOW); // GP5 LOW = Released
        pinMode(GP_POTY, OUTPUT);  digitalWrite(GP_POTY, LOW);  // GP3 LOW = Released
        
        // Turn off signal injectors
        pinMode(GP_POTY_GND, OUTPUT); digitalWrite(GP_POTY_GND, LOW); // GP6 off
        pinMode(GP_C64_SIG_MODE_SW, OUTPUT); digitalWrite(GP_C64_SIG_MODE_SW, LOW); // GP4 off
        
        Serial2.println("\n>>> SYSTEM SET TO: COMMODORE 64 (Fire 2 on POT X, Fire 3 on POT Y) <<<");
    }
}

void set_joy_pin(int pin, bool pressed) {
    // 🛡️ C64 Fire 2 (POT X / GP5)
    if (!is_amiga && pin == GP_FIRE2) {
        if (pressed) {
            pinMode(pin, OUTPUT); digitalWrite(pin, HIGH); // HIGH = PRESSED (SID reads 0)
        } else {
            // Restore LOW (Released) ONLY if NOT using the mouse
            if (!is_mouse_connected) {
                pinMode(pin, OUTPUT); digitalWrite(pin, LOW); 
            } else {
                pinMode(pin, INPUT); // Mouse mode: must float for hardware timers
            }
        }
        return; 
    } 

    // Standard Logic (Directions, Fire 1 and Amiga)
    if (pressed) { 
        pinMode(pin, OUTPUT); digitalWrite(pin, LOW); 
    } else { 
        digitalWrite(pin, LOW); pinMode(pin, INPUT_PULLUP); 
    }
}

void set_fire3_pin(bool pressed) {
    if (is_amiga) {
        // Amiga Fire 3 (Pure digital Middle Button on Pin 5)
        if (pressed) { 
            pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW); 
        } else { 
            digitalWrite(GP_POTY, LOW); pinMode(GP_POTY, INPUT_PULLUP); 
        }
    } else {
        // 🛡️ C64 Fire 3 (POT Y / GP3)
        if (pressed) {
            pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, HIGH); // HIGH = PRESSED (SID reads 0)
        } else {
            // Restore LOW (Released) ONLY if NOT using the mouse
            if (!is_mouse_connected) {
                pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW); 
            } else {
                pinMode(GP_POTY, INPUT); // Mouse mode: must float for hardware timers
            }
        }
    }
}

String get_pin_status(int pin, bool is_active_low) {
    int val = digitalRead(pin);
    if (is_active_low) return (val == LOW) ? "[ PRESSED ]" : "[ IDLE    ]";
    else return (val == HIGH) ? "[ ACTIVE  ]" : "[ IDLE    ]";
}