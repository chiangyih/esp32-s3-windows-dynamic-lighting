#include "LampArrayHid.h"
#include "LampArrayReportDescriptor.h"
#include <string.h>

LampArrayHid::LampArrayHid(uint16_t lampCount, uint32_t pitchUm,
                         uint32_t stripWidthUm, uint32_t minUpdateUs)
    : hid_(HID_ITF_PROTOCOL_NONE),
      protocol_(lampCount, pitchUm, stripWidthUm, minUpdateUs) {
  if (protocol_.valid()) {
    registered_ = USBHID::addDevice(this, sizeof(LampArrayReportDescriptor));
  }
}

bool LampArrayHid::begin() {
  if (!registered_ || !protocol_.valid()) return false;
  hid_.begin();
  return true;
}

bool LampArrayHid::isAutonomous() const {
  portENTER_CRITICAL(&lock_);
  const bool result = protocol_.isAutonomous();
  portEXIT_CRITICAL(&lock_);
  return result;
}

bool LampArrayHid::hasCommittedHostFrame() const {
  portENTER_CRITICAL(&lock_);
  const bool result = protocol_.hasCommittedHostFrame();
  portEXIT_CRITICAL(&lock_);
  return result;
}

bool LampArrayHid::copyCommittedFrame(uint8_t *rgb, size_t capacityBytes,
                                     uint32_t &version) const {
  portENTER_CRITICAL(&lock_);
  const bool copied = protocol_.copyCommittedFrame(rgb, capacityBytes, version);
  portEXIT_CRITICAL(&lock_);
  return copied;
}

void LampArrayHid::resetToAutonomous() {
  portENTER_CRITICAL(&lock_);
  protocol_.resetToAutonomous();
  portEXIT_CRITICAL(&lock_);
}

uint16_t LampArrayHid::_onGetDescriptor(uint8_t *buffer) {
  if (!buffer) return 0;
  memcpy(buffer, LampArrayReportDescriptor, sizeof(LampArrayReportDescriptor));
  return sizeof(LampArrayReportDescriptor);
}

uint16_t LampArrayHid::_onGetFeature(uint8_t reportId, uint8_t *buffer,
                                   uint16_t length) {
  // TinyUSB supplies Report ID separately and prepends it on the wire.
  portENTER_CRITICAL(&lock_);
  const uint16_t size = protocol_.getFeature(reportId, buffer, length);
  portEXIT_CRITICAL(&lock_);
  return size;
}

void LampArrayHid::_onSetFeature(uint8_t reportId, const uint8_t *buffer,
                                uint16_t length) {
  // Arduino-ESP32 3.1.1 passes TinyUSB's control payload without Report ID.
  // Windows HID callers allocate FeatureReportByteLength for the entire top-
  // level collection. Accommodate that maximum-size zero padding if it reaches
  // the device, while the protocol parser continues to require exact lengths.
  // Never infer a missing/extra Report ID from byte zero: it can be valid data.
  const uint16_t expected = lampArray::payloadSize(reportId);
  if (!buffer || !expected) return;
  if (length != expected) {
    if (length != lampArray::payloadSize(lampArray::kMultiUpdate) ||
        length < expected) return;
    for (uint16_t index = expected; index < length; ++index) {
      if (buffer[index] != 0) return;
    }
    length = expected;
  }
  portENTER_CRITICAL(&lock_);
  protocol_.setFeature(reportId, buffer, length);
  portEXIT_CRITICAL(&lock_);
}
