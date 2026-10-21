#include <Arduino.h>
#include "USB.h"
#include "tusb.h"
#include "esp_mac.h"
#include "Config.h"
#include "FrameLimiter.h"
#include "LampArrayHid.h"
#include "Ws2812Rmt.h"

#if ARDUINO_USB_MODE
#error "Select USB Mode: USB-OTG (TinyUSB), not Hardware CDC and JTAG."
#endif
#if ARDUINO_USB_CDC_ON_BOOT
#error "Disable USB CDC On Boot. Use the separate CH343 UART port for logs."
#endif

LampArrayHid lamps(Config::LampCount, Config::LampPitchUm,
                   Config::StripWidthUm, Config::MinUpdateUs);
Ws2812Rmt<Config::LampCount> strip(Config::DataPin);
uint8_t committed[Config::LampCount * 3] = {};
uint8_t limited[Config::LampCount * 3] = {};
uint32_t frameVersion = 0;
bool initialized = false;
bool rmtReady = false;
bool blackLatched = false;
bool railOn = false;
bool railPrimed = false;
bool faultLatched = false;
bool hadMount = false;
bool wasAutonomous = true;
bool framePending = false;
bool needFreshFrame = true;
uint32_t railEnabledAt = 0;
uint32_t lastShowAt = 0;

void stopLighting() {
  if (Config::UsePowerSwitch) {
    digitalWrite(Config::PowerEnablePin, LOW);
  } else if (rmtReady && !blackLatched) {
    // Direct wiring cannot disconnect power: send one black frame on stopping.
    // Suppress repeated writes while idle, including after an RMT failure.
    const uint8_t black[Config::LampCount * 3] = {};
    blackLatched = true;
    if (!strip.show(black)) faultLatched = true;
  }
  railOn = false;
  railPrimed = false;
}

void setup() {
  if (Config::UsePowerSwitch) {
    // The external pulldown keeps the optional switch disabled at boot.
    digitalWrite(Config::PowerEnablePin, LOW);
    pinMode(Config::PowerEnablePin, OUTPUT);
    pinMode(Config::PowerFaultPin, INPUT_PULLUP);
  }
  Serial.begin(115200);  // UART0 via CH343, never the native HID connection.
  setCpuFrequencyMhz(80);  // Wi-Fi/Bluetooth are not initialized.
  rmtReady = strip.begin();
  if (!rmtReady) { Serial.println("RMT initialization failed; LED output not started."); return; }
  stopLighting();  // Clear a directly powered strip before starting USB.

  // Use the board-family GPIO38 RGB only to clear its startup state.
  // No ongoing status animation or unbudgeted onboard LED brightness.
  rgbLedWrite(Config::OnboardRgbPin, 0, 0, 0);

  uint8_t mac[6];
  esp_read_mac(mac, ESP_MAC_WIFI_STA);
  char serial[20];
  snprintf(serial, sizeof(serial), "S3-%02X%02X%02X%02X%02X%02X",
           mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  // Keep the Arduino core development VID/PID defaults for this local prototype.
  // They do not grant a product VID/PID allocation.
  USB.productName("ESP32-S3 USB Light Strip");
  USB.manufacturerName("Local prototype");
  USB.serialNumber(serial);
  USB.usbClass(0);  // Class specified by the HID interface.
  USB.usbSubClass(0);
  USB.usbProtocol(0);
  USB.usbAttributes(0x80);  // Bus powered; remote wakeup not advertised.
  USB.usbPower(Config::NativeUsbPowerMa);
  if (!lamps.begin()) {
    initialized = false;
    Serial.println("HID configuration failed; LED output not started.");
    return;
  }
  initialized = USB.begin();
  Serial.println(initialized ? "LampArray USB started; waiting for Windows."
                            : "USB initialization failed; LED output not started.");
}

void loop() {
  const bool mounted = tud_mounted();
  const bool suspended = tud_suspended();
  if (!initialized || !mounted) {
    stopLighting();
    // Invalidate host buffers on disconnect. Retain a fault until remount.
    if (hadMount) {
      lamps.resetToAutonomous();
      frameVersion = 0;
      framePending = false;
      needFreshFrame = true;
    }
    hadMount = false;
    wasAutonomous = true;
    if (!mounted) faultLatched = false;
    delay(1);
    return;
  }
  hadMount = true;
  if (suspended) {
    stopLighting();
    framePending = true;  // Restore the last committed host frame on resume.
    delay(1);
    return;
  }
  const bool autonomous = lamps.isAutonomous();
  if (autonomous) {
    stopLighting();  // Direct mode sends black; the strip remains powered.
    needFreshFrame = true;
    framePending = false;
    wasAutonomous = true;
    delay(1);
    return;
  }
  if (!lamps.hasCommittedHostFrame()) {
    // Also catch a true->false mode change that occurs entirely between loops.
    // Never reuse an older host buffer after the protocol cleared its frame.
    needFreshFrame = true;
    framePending = false;
    stopLighting();
    delay(1);
    return;
  }
  const bool changed = lamps.copyCommittedFrame(committed, sizeof(committed), frameVersion);
  if (wasAutonomous) {
    needFreshFrame = true;
    wasAutonomous = false;
  }
  if (changed) { framePending = true; needFreshFrame = false; }
  if (faultLatched || needFreshFrame) { stopLighting(); delay(1); return; }
  if (!railOn) {
    if (Config::UsePowerSwitch) digitalWrite(Config::PowerEnablePin, HIGH);
    railEnabledAt = millis();
    railOn = true;
  }
  // The optional switch needs soft-start time; direct mode has no rail control.
  if (Config::UsePowerSwitch && uint32_t(millis() - railEnabledAt) < Config::RailStartupMs) { delay(1); return; }
  if (Config::UsePowerSwitch && digitalRead(Config::PowerFaultPin) == LOW) {
    faultLatched = true;
    stopLighting();
    Serial.println("LED branch fault; inspect wiring, then reconnect USB.");
    delay(1);
    return;
  }
  if (!railPrimed) {
    const uint8_t black[Config::LampCount * 3] = {};
    if (!strip.show(black)) {
      faultLatched = true;
      stopLighting();
      delay(1);
      return;
    }
    railPrimed = true;
    blackLatched = true;
  }
  if (framePending && uint32_t(micros() - lastShowAt) >= Config::MinUpdateUs) {
    limitFrame(committed, limited, Config::LampCount, Config::MaxChannelValue,
               Config::LedBudgetMa, Config::IdleMaPerLamp, Config::ChannelFullScaleMa);
    const bool sent = strip.show(limited);
    blackLatched = false;
    if (!sent) {
      faultLatched = true;
      stopLighting();
      Serial.println("RMT transfer failed; output halted. Direct wiring remains powered.");
    }
    lastShowAt = micros();
    framePending = false;
  }
  delay(1);
}
