#pragma once

#include <USBHID.h>
#include <freertos/FreeRTOS.h>
#include "LampArrayProtocol.h"

// Instantiate once at global scope, before USB.begin(). Do not combine reports
// 1..6 with the built-in keyboard/mouse/vendor devices in this HID interface.
class LampArrayHid : public USBHIDDevice {
 public:
  LampArrayHid(uint16_t lampCount, uint32_t pitchUm,
               uint32_t stripWidthUm = 10000, uint32_t minUpdateUs = 16667);
  bool begin();
  bool isAutonomous() const;
  bool hasCommittedHostFrame() const;
  bool copyCommittedFrame(uint8_t *rgb, size_t capacityBytes, uint32_t &version) const;
  void resetToAutonomous();

  uint16_t _onGetDescriptor(uint8_t *buffer) override;
  uint16_t _onGetFeature(uint8_t reportId, uint8_t *buffer, uint16_t length) override;
  void _onSetFeature(uint8_t reportId, const uint8_t *buffer, uint16_t length) override;

 private:
  USBHID hid_;
  lampArray::Protocol protocol_;
  bool registered_ = false;
  // Arduino's TinyUSB service task executes the feature callbacks; loop() and
  // USB event callbacks may run on another task/core. Short critical sections
  // protect state and the frame copy. No RMT calls, delays, logging, allocation,
  // or FreeRTOS waits occur while this lock is held.
  mutable portMUX_TYPE lock_ = portMUX_INITIALIZER_UNLOCKED;
};
