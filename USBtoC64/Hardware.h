// ==========================================
// USB to C64/Amiga Adapter - Advanced 2.8
// File: Hardware.h
// Description: Hardware pin management and console mode (Amiga/C64)
// ==========================================
#pragma once

#include <Arduino.h>
#include "Globals.h"

// 🔌 --- HARDWARE PIN MANAGEMENT --- 🔌

void configure_console_mode(bool amiga_mode) {
    is_amiga = amiga_mode;
    if (is_amiga) {
        pinMode(GP_POTY, INPUT); 
        pinMode(GP_POTY_GND, INPUT); 
        pinMode(GP_C64_SIG_MODE_SW, INPUT); 
        Serial2.println("\n>>> SYSTEM SET TO: AMIGA (Fire 2 on Pin 9, Fire 3 on Pin 5) <<<");
    } else {
        pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW);
        pinMode(GP_POTY_GND, OUTPUT); digitalWrite(GP_POTY_GND, LOW);
        pinMode(GP_C64_SIG_MODE_SW, OUTPUT); digitalWrite(GP_C64_SIG_MODE_SW, LOW);
        Serial2.println("\n>>> SYSTEM SET TO: COMMODORE 64 (Fire 2 on POT X, Fire 3 on POT Y) <<<");
    }
}

void set_joy_pin(int pin, bool pressed) {
    if (pressed) { 
        pinMode(pin, OUTPUT); digitalWrite(pin, LOW); 
    } else { 
        digitalWrite(pin, LOW); pinMode(pin, INPUT_PULLUP); 
    }
    
    // C64 Fire 2 special handling (Injection)
    if (!is_amiga && pin == GP_FIRE2) {
        digitalWrite(GP_C64_SIG_MODE_SW, pressed ? HIGH : LOW);
        pinMode(pin, INPUT_PULLUP); 
    } 
}

void set_fire3_pin(bool pressed) {
    if (is_amiga) {
        if (pressed) { 
            pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW); 
        } else { 
            pinMode(GP_POTY, INPUT); 
        }
    } else {
        if (pressed) {
            pinMode(GP_POTY, INPUT); 
            pinMode(GP_POTY_GND, OUTPUT); 
            digitalWrite(GP_POTY_GND, HIGH);       
        } else {
            pinMode(GP_POTY_GND, OUTPUT); digitalWrite(GP_POTY_GND, LOW); 
            pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW);         
        }
    }
}

String get_pin_status(int pin, bool is_active_low) {
    int val = digitalRead(pin);
    if (is_active_low) return (val == LOW) ? "[ PRESSED ]" : "[ IDLE    ]";
    else return (val == HIGH) ? "[ ACTIVE  ]" : "[ IDLE    ]";
}