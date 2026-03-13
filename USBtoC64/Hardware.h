// ==========================================
// USB to C64/Amiga Adapter - Advanced 1.2
// File: Hardware.h
// Description: Hardware pin management, interrupts and console mode
// ==========================================
#pragma once

#include <Arduino.h>
#include "driver/gpio.h"
#include "soc/gpio_struct.h"
#include "Globals.h"

extern int active_driver; // Used to separate Mouse and Joystick logic safely

// ⚡ --- FAST GPIO INTERRUPTS FOR C64 MOUSE (SID 1351) --- ⚡

void IRAM_ATTR handleInterrupt() {
    if (!((GPIO.in >> GP1) & 1)) return; 
    timerWrite(timerOnX, 0);
    timerAlarm(timerOnX, delayOnX, false, 0);
    timerWrite(timerOnY, 0);
    timerAlarm(timerOnY, delayOnY, false, 0);
}

void IRAM_ATTR turnOnPotX() {
    GPIO.out_w1ts = (1 << GP_POTX); 
    timerWrite(timerOffX, 0);
    timerAlarm(timerOffX, delayOffX, false, 0);
}

void IRAM_ATTR turnOffPotX() { 
    GPIO.out_w1tc = (1 << GP_POTX); 
}

void IRAM_ATTR turnOnPotY() {
    GPIO.out_w1ts = (1 << GP_POTY); 
    timerWrite(timerOffY, 0);
    timerAlarm(timerOffY, delayOffY, false, 0);
}

void IRAM_ATTR turnOffPotY() { 
    GPIO.out_w1tc = (1 << GP_POTY); 
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

// REL_3_2_MOD: [CPU] deinit/init timer pair — implements Laface pattern:
// setCpuFrequencyMhz is called AFTER deinit and BEFORE timerBegin/timerAttach,
// so all timers are always created at the final CPU frequency.
// Amiga mode does not change CPU frequency (no POT scanning needed).

inline void deinit_c64_timers() {
    // GP1 interrupt solo in C64 mouse mode
    if (!is_amiga && active_driver == 1) detachInterrupt(digitalPinToInterrupt(GP1));
    if (timerOnX)  { timerStop(timerOnX);  timerDetachInterrupt(timerOnX);  timerEnd(timerOnX);  timerOnX  = nullptr; }
    if (timerOffX) { timerStop(timerOffX); timerDetachInterrupt(timerOffX); timerEnd(timerOffX); timerOffX = nullptr; }
    if (timerOnY)  { timerStop(timerOnY);  timerDetachInterrupt(timerOnY);  timerEnd(timerOnY);  timerOnY  = nullptr; }
    if (timerOffY) { timerStop(timerOffY); timerDetachInterrupt(timerOffY); timerEnd(timerOffY); timerOffY = nullptr; }
}

inline void init_c64_timers() {
    if (!is_amiga) {
        // NOTA: setCpuFrequencyMhz NON va qui — questa funzione viene chiamata
        // anche da setup() prima che il device sia connesso. La frequenza viene
        // impostata solo nei punti del loop dove il device è già identificato.
        if (active_driver == 1) {
            // C64 Mouse: 4 timers + GP1 sync interrupt
            pinMode(GP_POTX, OUTPUT); pinMode(GP_POTY, OUTPUT);
            timerOnX  = timerBegin(10000000); timerAlarm(timerOnX,  delayOnX,  false, 0);
            timerOffX = timerBegin(10000000); timerAlarm(timerOffX, delayOffX, false, 0);
            timerOnY  = timerBegin(10000000); timerAlarm(timerOnY,  delayOnY,  false, 0);
            timerOffY = timerBegin(10000000); timerAlarm(timerOffY, delayOffY, false, 0);
            timerAttachInterrupt(timerOnX,  &turnOnPotX);  timerAttachInterrupt(timerOffX, &turnOffPotX);
            timerAttachInterrupt(timerOnY,  &turnOnPotY);  timerAttachInterrupt(timerOffY, &turnOffPotY);
            pinMode(GP1, INPUT_PULLUP);
            attachInterrupt(digitalPinToInterrupt(GP1), handleInterrupt, RISING);
        } else {
            // C64 Joystick: 2 timers only
            timerOffX = timerBegin(10000000); timerAlarm(timerOffX, delayOffX, false, 0);
            timerOffY = timerBegin(10000000); timerAlarm(timerOffY, delayOffY, false, 0);
            timerAttachInterrupt(timerOffX, &turnOffJoyX); timerAttachInterrupt(timerOffY, &turnOffJoyY);
        }
    } else {
        // Amiga: 2 timers, no CPU freq change
        timerOffX = timerBegin(10000000); timerAlarm(timerOffX, delayOffX, false, 0);
        timerOffY = timerBegin(10000000); timerAlarm(timerOffY, delayOffY, false, 0);
        timerAttachInterrupt(timerOffX, &turnOffJoyX); timerAttachInterrupt(timerOffY, &turnOffJoyY);
    }
}

void configure_console_mode(bool amiga_mode) {
    is_amiga = amiga_mode;
    if (is_amiga) {
        pinMode(GP_FIRE3, INPUT_PULLUP); 
        pinMode(GP_POTY, INPUT); 
        pinMode(GP_POTX, INPUT); 
        Serial2.println("\n>>> SYSTEM SET TO: AMIGA (Fire 2 on Pin 9, Fire 3 on Pin 5) <<<");
    } else {
        if (active_driver == 1) { 
            // Mouse Mode: Keep Pin 3 and 5 out of the way for C64 Mouse SYNC
            pinMode(GP_FIRE2, INPUT_PULLDOWN); 
            pinMode(GP_FIRE3, INPUT_PULLDOWN);  
            pinMode(GP_POTY, OUTPUT); 
            pinMode(GP_POTX, OUTPUT); 
            Serial2.println("\n>>> SYSTEM SET TO: COMMODORE 64 MOUSE <<<");
        } else {
            // Joystick Mode: Fire 2 and 3 always grounded by default
            pinMode(GP_FIRE2, OUTPUT); digitalWrite(GP_FIRE2, LOW); 
            pinMode(GP_FIRE3, OUTPUT);  digitalWrite(GP_FIRE3, LOW);  
            pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW);
            pinMode(GP_POTX, OUTPUT); digitalWrite(GP_POTX, LOW);
            Serial2.println("\n>>> SYSTEM SET TO: COMMODORE 64 JOYSTICK (Fire 2 on POT X, Fire 3 on POT Y) <<<");
        }
    }
}

void set_joy_pin(int pin, bool pressed) {
    // Safe handling for Fire 2 (Pin 5) on C64
    if (!is_amiga && pin == GP_FIRE2) {
        if (active_driver == 1) {
            // Mouse mode: do not interfere
            pinMode(pin, INPUT_PULLDOWN);
        } else {
            // Joystick mode
            if (pressed) {
                pinMode(pin, INPUT); // Floating
                pinMode(GP_POTX, OUTPUT); digitalWrite(GP_POTX, HIGH); // GP4 High
            } else {
                pinMode(pin, OUTPUT); digitalWrite(pin, LOW); // Grounded
                pinMode(GP_POTX, OUTPUT); digitalWrite(GP_POTX, LOW); // GP4 Low
            }
        }
        return; 
    } 

    if (pressed) { 
        pinMode(pin, OUTPUT); digitalWrite(pin, LOW); 
    } else { 
        digitalWrite(pin, LOW); pinMode(pin, INPUT); 
    }
}

void set_fire3_pin(bool pressed) {
    if (is_amiga) {
        if (pressed) { 
            pinMode(GP_FIRE3, OUTPUT); digitalWrite(GP_FIRE3, LOW); 
        } else { 
            digitalWrite(GP_FIRE3, LOW); pinMode(GP_FIRE3, INPUT); 
        }
    } else {
        // Safe handling for Fire 3 (Pin 3) on C64
        if (active_driver == 1) {
            // Mouse mode: do not interfere
            pinMode(GP_FIRE3, INPUT_PULLDOWN); 
        } else {
            // Joystick mode
            if (pressed) {
                pinMode(GP_FIRE3, INPUT); // Floating
                pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, HIGH); // GP6 High
            } else {
                pinMode(GP_FIRE3, OUTPUT); digitalWrite(GP_FIRE3, LOW); // Grounded
                pinMode(GP_POTY, OUTPUT); digitalWrite(GP_POTY, LOW); // GP6 Low
            }
        }
    }
}

// 🛡️ --- SAFETY: rilascia tutti i pin DB9 (prevenzione stuck inputs) --- 🛡️
inline void release_all_outputs() {
    set_joy_pin(GP_UP, false);    set_joy_pin(GP_DOWN, false);
    set_joy_pin(GP_LEFT, false);  set_joy_pin(GP_RIGHT, false);
    set_joy_pin(GP_FIRE1, false); set_joy_pin(GP_FIRE2, false);
    set_fire3_pin(false);
}

String get_pin_status(int pin, bool is_active_low) {
    int val = digitalRead(pin);
    if (is_active_low) return (val == LOW) ? "[ PRESSED ]" : "[ IDLE    ]";
    else return (val == HIGH) ? "[ ACTIVE  ]" : "[ IDLE    ]";
}
