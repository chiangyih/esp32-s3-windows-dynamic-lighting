#pragma once
#include <stdint.h>

namespace Config {
// Current prototype: USB 5V/GND and GPIO4 -> 330 ohm -> strip DIN.
// No TPS2553 or 74AHCT125 is required by this software configuration.
// Setting true requires assembling the separate switched-power circuit first.
constexpr bool UsePowerSwitch = false;
// Prototype defaults. Match these to the physical strip before building.
constexpr uint16_t LampCount = 8;
constexpr uint32_t LampPitchUm = 16667;  // Example: 60 LEDs / metre.
constexpr uint32_t StripWidthUm = 10000;
constexpr uint32_t MinUpdateUs = 16667;  // Host animation interval; stop/start black frames are separate.
constexpr uint8_t DataPin = 4;
constexpr uint8_t PowerEnablePin = 9;    // TPS2553 EN; external 100k pulldown.
constexpr uint8_t PowerFaultPin = 10;    // TPS2553 /FAULT; external 10k to 3.3V.
constexpr uint8_t OnboardRgbPin = 38;    // Series schematic; verify actual board.

// LED branch estimates only, NOT measured total USB consumption.
constexpr uint16_t LedBudgetMa = 50;
constexpr uint16_t IdleMaPerLamp = 1;
constexpr uint16_t ChannelFullScaleMa = 20;  // Conservative older WS2812 estimate.
constexpr uint8_t MaxChannelValue = 24;     // 9.4% ceiling, even at Windows 100%.
constexpr uint32_t RailStartupMs = 30;
constexpr uint16_t NativeUsbPowerMa = 100;  // Does not describe hub + CH343 aggregate.
}

static_assert(Config::LampCount > 0 && Config::LampCount <= 64,
              "USB prototype supports 1..64 lamps; re-evaluate power before increasing.");
static_assert(Config::LedBudgetMa > Config::LampCount * Config::IdleMaPerLamp,
              "Idle estimate exhausts the LED branch budget.");
