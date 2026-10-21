#pragma once

// HID Lighting and Illumination (Usage Page 0x59), USB HID Usage Tables 1.4,
// chapter 26. Payloads deliberately exclude the USB HID Report ID byte.
// Pure C++ so that malformed report handling can be exercised on the host.
#include <stddef.h>
#include <stdint.h>
#include <string.h>

namespace lampArray {

static constexpr uint16_t kMaxLamps = 256;
static constexpr uint8_t kMultiSlots = 8;
static constexpr uint8_t kArrayAttributes = 1;
static constexpr uint8_t kLampAttributesRequest = 2;
static constexpr uint8_t kLampAttributesResponse = 3;
static constexpr uint8_t kMultiUpdate = 4;
static constexpr uint8_t kRangeUpdate = 5;
static constexpr uint8_t kControl = 6;

inline uint16_t read16(const uint8_t *p) {
  return static_cast<uint16_t>(p[0]) | (static_cast<uint16_t>(p[1]) << 8);
}

inline void write16(uint8_t *p, uint16_t value) {
  p[0] = static_cast<uint8_t>(value);
  p[1] = static_cast<uint8_t>(value >> 8);
}

inline void write32(uint8_t *p, uint32_t value) {
  p[0] = static_cast<uint8_t>(value);
  p[1] = static_cast<uint8_t>(value >> 8);
  p[2] = static_cast<uint8_t>(value >> 16);
  p[3] = static_cast<uint8_t>(value >> 24);
}

inline uint16_t payloadSize(uint8_t reportId) {
  switch (reportId) {
    case kArrayAttributes: return 22;
    case kLampAttributesRequest: return 2;
    case kLampAttributesResponse: return 28;
    case kMultiUpdate: return 50;
    case kRangeUpdate: return 9;
    case kControl: return 1;
    default: return 0;
  }
}

class Protocol {
 public:
  Protocol(uint16_t count, uint32_t pitchUm, uint32_t stripWidthUm = 10000,
           uint32_t minUpdateUs = 16667)
      : count_(count), pitchUm_(pitchUm), stripWidthUm_(stripWidthUm),
        minUpdateUs_(minUpdateUs) {}

  bool valid() const {
    return count_ > 0 && count_ <= kMaxLamps && pitchUm_ > 0 &&
           stripWidthUm_ > 0 && stripWidthUm_ <= 0x7FFFFFFFUL &&
           static_cast<uint64_t>(count_) * pitchUm_ <= 0x7FFFFFFFUL &&
           minUpdateUs_ > 0 &&
           static_cast<uint64_t>(minUpdateUs_) + count_ * 30UL + 300UL <=
               0x7FFFFFFFUL;
  }

  uint16_t lampCount() const { return count_; }
  bool isAutonomous() const { return autonomous_; }
  bool hasCommittedHostFrame() const { return hasCommittedHostFrame_; }

  // Read-only reports are assembled without packed structs or unaligned casts.
  // A short buffer returns no payload and does not advance the query LampId.
  uint16_t getFeature(uint8_t reportId, uint8_t *out, uint16_t capacity) {
    if (!valid() || !out) return 0;
    const uint16_t size = payloadSize(reportId);
    if (!size || capacity < size) return 0;
    switch (reportId) {
      case kArrayAttributes:
        write16(out, count_);
        write32(out + 2, static_cast<uint32_t>(count_) * pitchUm_);
        write32(out + 6, stripWidthUm_);
        write32(out + 10, 2000);  // Provisional strip bounding depth: 2 mm.
        write32(out + 14, 4);     // LampArrayKindPeripheral.
        write32(out + 18, minUpdateUs_);
        return size;
      case kLampAttributesRequest:
        write16(out, queryId_);
        return size;
      case kLampAttributesResponse:
        write16(out, queryId_);
        write32(out + 2, static_cast<uint32_t>(queryId_) * pitchUm_ + pitchUm_ / 2);
        write32(out + 6, stripWidthUm_ / 2);
        write32(out + 10, 1000);
        // Conservative scheduling + WS2812 serialization estimate; measure the
        // real end-to-end upper bound before treating this as characterized.
        write32(out + 14, minUpdateUs_ + count_ * 30UL + 300UL);
        write32(out + 18, 2);  // LampPurposeAccent.
        out[22] = 255;        // 255 nonzero red levels, plus off.
        out[23] = 255;
        out[24] = 255;
        out[25] = 255;        // Overall intensity is applied in software.
        out[26] = 1;          // Programmable.
        out[27] = 0;          // No keyboard/mouse input binding.
        queryId_ = static_cast<uint16_t>((queryId_ + 1) % count_);
        return size;
      case kControl:
        out[0] = autonomous_ ? 1 : 0;
        return size;
      default:
        return 0;  // Lamp update reports are host-to-device only.
    }
  }

  // Exact payload lengths are intentional. Any invalid update is ignored as a
  // whole, leaving both the preceding pending batch and committed frame intact.
  bool setFeature(uint8_t reportId, const uint8_t *payload, uint16_t length) {
    if (!valid() || !payload || !payloadSize(reportId) ||
        length != payloadSize(reportId)) return false;
    switch (reportId) {
      case kLampAttributesRequest: {
        const uint16_t requested = read16(payload);
        queryId_ = requested < count_ ? requested : 0;
        return true;
      }
      case kControl:
        if (payload[0] > 1) return false;
        if ((payload[0] != 0) == autonomous_) return true;
        autonomous_ = payload[0] != 0;
        if (autonomous_) {
          // This prototype's documented autonomous effect is all lamps OFF.
          // A later transition back to host control preserves this black state.
          clearFrames();
        }
        return true;
      case kMultiUpdate:
        return setMultiple(payload);
      case kRangeUpdate:
        return setRange(payload);
      default:
        return false;
    }
  }

  bool copyCommittedFrame(uint8_t *rgb, size_t capacityBytes,
                          uint32_t &version) const {
    const size_t bytes = static_cast<size_t>(count_) * 3;
    if (!valid() || !hasCommittedHostFrame_ || !rgb || capacityBytes < bytes ||
        version == version_) return false;
    memcpy(rgb, committed_, bytes);
    version = version_;
    return true;
  }

  // Call on a genuine transport reset/disconnect, not on every loop iteration.
  // Old partial batches cannot reappear after reconnecting to another host.
  void resetToAutonomous() {
    autonomous_ = true;
    queryId_ = 0;
    clearFrames();
  }

 private:
  void nextVersion() {
    if (++version_ == 0) ++version_;  // Zero remains a useful reader sentinel.
  }

  void clearFrames() {
    memset(pending_, 0, sizeof(pending_));
    memset(committed_, 0, sizeof(committed_));
    hasCommittedHostFrame_ = false;
    nextVersion();
  }

  void writeColor(uint16_t id, const uint8_t *rgbi) {
    const size_t offset = static_cast<size_t>(id) * 3;
    for (uint8_t channel = 0; channel < 3; ++channel) {
      pending_[offset + channel] = static_cast<uint8_t>(
          (static_cast<uint16_t>(rgbi[channel]) * rgbi[3] + 127U) / 255U);
    }
  }

  void finishUpdate(uint8_t flags) {
    if ((flags & 1U) != 0) {
      memcpy(committed_, pending_, static_cast<size_t>(count_) * 3);
      hasCommittedHostFrame_ = true;
      nextVersion();
    }
  }

  bool setMultiple(const uint8_t *payload) {
    const uint8_t count = payload[0];
    const uint8_t flags = payload[1];
    if (autonomous_ || count > kMultiSlots || (flags & ~1U) != 0) return false;
    // Validate all IDs before changing a single pending pixel. Duplicate IDs are
    // explicitly invalid in HID Usage Tables 26.11.1.
    for (uint8_t i = 0; i < count; ++i) {
      const uint16_t id = read16(payload + 2 + i * 2);
      if (id >= count_) return false;
      for (uint8_t prior = 0; prior < i; ++prior) {
        if (id == read16(payload + 2 + prior * 2)) return false;
      }
    }
    for (uint8_t i = 0; i < count; ++i) {
      writeColor(read16(payload + 2 + i * 2), payload + 18 + i * 4);
    }
    finishUpdate(flags);
    return true;
  }

  bool setRange(const uint8_t *payload) {
    const uint8_t flags = payload[0];
    const uint16_t first = read16(payload + 1);
    const uint16_t last = read16(payload + 3);
    if (autonomous_ || (flags & ~1U) != 0 || first > last ||
        first >= count_ || last >= count_) return false;
    for (uint16_t id = first; id <= last; ++id) writeColor(id, payload + 5);
    finishUpdate(flags);
    return true;
  }

  const uint16_t count_;
  const uint32_t pitchUm_;
  const uint32_t stripWidthUm_;
  const uint32_t minUpdateUs_;
  uint16_t queryId_ = 0;
  bool autonomous_ = true;
  bool hasCommittedHostFrame_ = false;
  uint32_t version_ = 1;
  uint8_t pending_[kMaxLamps * 3] = {};
  uint8_t committed_[kMaxLamps * 3] = {};
};

}  // namespace lampArray
