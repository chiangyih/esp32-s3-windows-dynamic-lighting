#pragma once
#include <stddef.h>
#include <stdint.h>

// Pure C++ so power-model arithmetic can also be tested on the development PC.
// This is a colour scaling estimate; only hardware can measure/limit real current.
inline void limitFrame(const uint8_t *rgb, uint8_t *output, size_t lampCount,
                       uint8_t channelCeiling, uint32_t budgetMa,
                       uint32_t idleMaPerLamp, uint32_t fullChannelMa) {
  uint64_t channelSum = 0;
  for (size_t i = 0; i < lampCount * 3; ++i) channelSum += rgb[i];
  const uint64_t idle = uint64_t(lampCount) * idleMaPerLamp;
  if (budgetMa <= idle || channelSum == 0) {
    for (size_t i = 0; i < lampCount * 3; ++i) output[i] = 0;
    return;
  }
  // One common scale preserves hue and inter-lamp brightness relationships.
  // Ceiling maps full-scale input to channelCeiling, not a per-channel clip.
  uint64_t numerator = channelCeiling;
  uint64_t denominator = 255;
  if (fullChannelMa && channelSum * fullChannelMa * numerator >
                           uint64_t(budgetMa - idle) * 255 * denominator) {
    numerator = uint64_t(budgetMa - idle) * 255;
    denominator = channelSum * fullChannelMa;
  }
  for (size_t i = 0; i < lampCount * 3; ++i)
    output[i] = uint8_t(uint64_t(rgb[i]) * numerator / denominator);
}
