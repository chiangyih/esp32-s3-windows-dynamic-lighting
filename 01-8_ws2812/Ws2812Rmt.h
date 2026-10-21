#pragma once
#include <Arduino.h>
#include "esp32-hal-rmt.h"

template <size_t Count> class Ws2812Rmt {
 public:
  explicit Ws2812Rmt(uint8_t pin) : pin_(pin) {}
  bool begin() {
    pinMode(pin_, OUTPUT);
    digitalWrite(pin_, LOW);
    return rmtInit(pin_, RMT_TX_MODE, RMT_MEM_NUM_BLOCKS_1, 20000000) &&
           rmtSetEOT(pin_, LOW);
  }
  bool show(const uint8_t *rgb) {
    size_t index = 0;
    for (size_t lamp = 0; lamp < Count; ++lamp) {
      // Wire order is GRB; USB protocol and all buffers use RGB.
      const uint8_t order[] = {1, 0, 2};
      for (uint8_t channel : order) {
        const uint8_t value = rgb[lamp * 3 + channel];
        for (uint8_t mask = 0x80; mask; mask >>= 1) {
          const bool one = (value & mask) != 0;
          symbols_[index].level0 = 1;
          symbols_[index].duration0 = one ? 16 : 8;  // 0.8 / 0.4 us.
          symbols_[index].level1 = 0;
          symbols_[index].duration1 = one ? 9 : 17;  // Total bit = 1.25 us.
          ++index;
        }
      }
    }
    symbols_[index].level0 = 0;
    symbols_[index].duration0 = 6000;  // 300 us latch for multiple WS2812 revisions.
    symbols_[index].level1 = 0;
    symbols_[index].duration1 = 1;
    return rmtWrite(pin_, symbols_, Count * 24 + 1, 100);
  }
 private:
  uint8_t pin_;
  rmt_data_t symbols_[Count * 24 + 1] = {};
};
