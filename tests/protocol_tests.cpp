#include <array>
#include <cstdint>
#include <cstdlib>
#include <exception>
#include <functional>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

#include "../01-8_ws2812/LampArrayProtocol.h"
#include "../01-8_ws2812/FrameLimiter.h"

namespace {
constexpr uint8_t kAttributes = 1;
constexpr uint8_t kRequest = 2;
constexpr uint8_t kResponse = 3;
constexpr uint8_t kMulti = 4;
constexpr uint8_t kRange = 5;
constexpr uint8_t kControl = 6;
using Multi = std::array<uint8_t, 50>;
using Range = std::array<uint8_t, 9>;
using Frame = std::array<uint8_t, 12>;

template <typename T, size_t N>
constexpr uint16_t size16(const std::array<T, N>&) {
  static_assert(N <= UINT16_MAX, "HID payload must fit in uint16_t");
  return static_cast<uint16_t>(N);
}

void require(bool condition, const char* message) {
  if (!condition) throw std::runtime_error(message);
}

void write16(uint8_t* out, uint16_t value) {
  out[0] = static_cast<uint8_t>(value);
  out[1] = static_cast<uint8_t>(value >> 8);
}

uint16_t read16(const uint8_t* in) {
  return static_cast<uint16_t>(in[0]) |
         static_cast<uint16_t>(static_cast<uint16_t>(in[1]) << 8);
}

uint32_t read32(const uint8_t* in) {
  return static_cast<uint32_t>(in[0]) |
         (static_cast<uint32_t>(in[1]) << 8) |
         (static_cast<uint32_t>(in[2]) << 16) |
         (static_cast<uint32_t>(in[3]) << 24);
}

void hostControl(lampArray::Protocol& protocol) {
  const uint8_t zero = 0;
  require(protocol.setFeature(kControl, &zero, 1), "host control rejected");
  require(!protocol.isAutonomous(), "host control did not disable autonomous");
}

Multi multi(uint8_t count, bool commit) {
  Multi packet{};
  packet[0] = count;
  packet[1] = commit ? 1 : 0;
  return packet;
}

void lamp(Multi& packet, size_t slot, uint16_t id, uint8_t red,
          uint8_t green, uint8_t blue, uint8_t intensity = 255) {
  write16(packet.data() + 2 + slot * 2, id);
  const size_t offset = 18 + slot * 4;
  packet[offset] = red;
  packet[offset + 1] = green;
  packet[offset + 2] = blue;
  packet[offset + 3] = intensity;
}

Range range(uint16_t first, uint16_t last, bool commit, uint8_t red,
            uint8_t green, uint8_t blue, uint8_t intensity = 255) {
  Range packet{};
  packet[0] = commit ? 1 : 0;
  write16(packet.data() + 1, first);
  write16(packet.data() + 3, last);
  packet[5] = red;
  packet[6] = green;
  packet[7] = blue;
  packet[8] = intensity;
  return packet;
}

Frame snapshot(lampArray::Protocol& protocol, uint32_t& version) {
  Frame frame{};
  require(protocol.copyCommittedFrame(frame.data(), frame.size(), version),
          "expected a new committed frame");
  return frame;
}

void requireNoFrame(lampArray::Protocol& protocol, uint32_t& version,
                    const char* message) {
  Frame frame{};
  require(!protocol.copyCommittedFrame(frame.data(), frame.size(), version), message);
}

void commitEmpty(lampArray::Protocol& protocol) {
  const auto packet = multi(0, true);
  require(protocol.setFeature(kMulti, packet.data(), size16(packet)),
          "empty commit failed");
}

void initialState() {
  lampArray::Protocol protocol(4, 10000);
  require(protocol.valid(), "four-lamp configuration invalid");
  require(protocol.lampCount() == 4, "wrong lamp count");
  require(protocol.isAutonomous(), "initial state must be autonomous");
  uint32_t version = 0;
  requireNoFrame(protocol, version, "initial black buffer must not count as a host frame");
  require(version == 0, "initial read consumed reader version");
  hostControl(protocol);
  requireNoFrame(protocol, version, "ownership alone must not authorize LED power");
  commitEmpty(protocol);
  require(snapshot(protocol, version) == Frame{}, "explicit black host frame differs");
  requireNoFrame(protocol, version, "unchanged committed frame reported as new");
}

void autonomousRejectsHostUpdates() {
  lampArray::Protocol protocol(4, 10000);
  auto packet = multi(1, true);
  lamp(packet, 0, 0, 255, 1, 2);
  require(!protocol.setFeature(kMulti, packet.data(), size16(packet)),
          "autonomous mode accepted host update");
  uint32_t version = 0;
  requireNoFrame(protocol, version, "autonomous update was published");
}

void multiCommitIsAtomic() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  uint32_t version = 0;
  auto first = multi(2, false);
  lamp(first, 0, 0, 255, 0, 0);
  lamp(first, 1, 1, 0, 255, 0);
  require(protocol.setFeature(kMulti, first.data(), size16(first)), "stage failed");
  Frame unchanged{};
  require(!protocol.copyCommittedFrame(unchanged.data(), unchanged.size(), version),
          "uncommitted packet was published");
  auto second = multi(1, true);
  lamp(second, 0, 3, 0, 0, 255);
  require(protocol.setFeature(kMulti, second.data(), size16(second)), "commit failed");
  const Frame expected{255, 0, 0, 0, 255, 0, 0, 0, 0, 0, 0, 255};
  require(snapshot(protocol, version) == expected,
          "multi-packet commit lost staged lamps or published wrong colors");
}

void invalidMultiDoesNotPolluteStaging() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  uint32_t version = 0;
  auto staged = multi(1, false);
  lamp(staged, 0, 0, 255, 0, 0);
  require(protocol.setFeature(kMulti, staged.data(), size16(staged)), "stage failed");
  auto invalid = multi(2, true);
  lamp(invalid, 0, 1, 0, 255, 0);
  lamp(invalid, 1, 4, 255, 255, 255);
  require(!protocol.setFeature(kMulti, invalid.data(), size16(invalid)),
          "out-of-range lamp ID accepted");
  Frame frame{};
  require(!protocol.copyCommittedFrame(frame.data(), frame.size(), version),
          "invalid multi update published a frame");
  auto commit = multi(1, true);
  lamp(commit, 0, 2, 0, 0, 255);
  require(protocol.setFeature(kMulti, commit.data(), size16(commit)), "commit failed");
  const Frame expected{255, 0, 0, 0, 0, 0, 0, 0, 255, 0, 0, 0};
  require(snapshot(protocol, version) == expected,
          "invalid packet partly modified staging");
}

void duplicateIdsAreRejected() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto packet = multi(2, true);
  lamp(packet, 0, 1, 255, 0, 0);
  lamp(packet, 1, 1, 0, 255, 0);
  require(!protocol.setFeature(kMulti, packet.data(), size16(packet)),
          "duplicate lamp IDs accepted");
  uint32_t version = 0;
  requireNoFrame(protocol, version, "duplicate IDs published a frame");
  commitEmpty(protocol);
  require(snapshot(protocol, version) == Frame{}, "duplicate IDs changed frame");
}

void multiCountsAndLengths() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto packet = multi(1, true);
  lamp(packet, 0, 0, 20, 30, 40);
  for (uint16_t length = 0; length < size16(packet); ++length) {
    require(!protocol.setFeature(kMulti, packet.data(), length),
            "truncated multi packet accepted");
  }
  std::array<uint8_t, 51> tooLong{};
  tooLong[0] = 1;
  require(!protocol.setFeature(kMulti, tooLong.data(), size16(tooLong)),
          "oversize multi packet accepted");
  for (const uint8_t count : {uint8_t(9), uint8_t(255)}) {
    packet[0] = count;
    require(!protocol.setFeature(kMulti, packet.data(), size16(packet)),
            "invalid multi lamp count accepted");
  }
}

void inclusiveRangeAndIntensity() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto packet = range(1, 3, true, 200, 100, 50, 128);
  require(protocol.setFeature(kRange, packet.data(), size16(packet)), "range failed");
  uint32_t version = 0;
  const Frame expected{0, 0, 0, 100, 50, 25, 100, 50, 25, 100, 50, 25};
  require(snapshot(protocol, version) == expected,
          "range endpoint or intensity scaling wrong");
}

void invalidRangesDoNotPolluteStaging() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto staged = range(0, 0, false, 255, 0, 0);
  require(protocol.setFeature(kRange, staged.data(), size16(staged)), "stage failed");
  for (const auto bounds : {std::array<uint16_t, 2>{1, 4}, {3, 2}, {65535, 65535}}) {
    auto invalid = range(bounds[0], bounds[1], true, 0, 255, 0);
    require(!protocol.setFeature(kRange, invalid.data(), size16(invalid)),
            "invalid range accepted");
  }
  auto commit = range(3, 3, true, 0, 0, 255);
  require(protocol.setFeature(kRange, commit.data(), size16(commit)), "commit failed");
  uint32_t version = 0;
  const Frame expected{255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255};
  require(snapshot(protocol, version) == expected,
          "invalid range partly modified staging");
}

void rangeLengths() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto packet = range(0, 3, true, 255, 0, 0);
  for (uint16_t length = 0; length < size16(packet); ++length) {
    require(!protocol.setFeature(kRange, packet.data(), length),
            "truncated range accepted");
  }
  std::array<uint8_t, 10> tooLong{};
  require(!protocol.setFeature(kRange, tooLong.data(), size16(tooLong)),
          "oversize range accepted");
}

void multiIntensityBoundaries() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto packet = multi(4, true);
  lamp(packet, 0, 0, 255, 255, 255, 0);
  lamp(packet, 1, 1, 255, 255, 255, 1);
  lamp(packet, 2, 2, 255, 128, 0, 255);
  lamp(packet, 3, 3, 1, 1, 1, 128);
  require(protocol.setFeature(kMulti, packet.data(), size16(packet)), "multi failed");
  uint32_t version = 0;
  const Frame expected{0, 0, 0, 1, 1, 1, 255, 128, 0, 1, 1, 1};
  require(snapshot(protocol, version) == expected, "intensity boundary rounding wrong");
}

void autonomousTransitionClearsPending() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto packet = range(0, 3, true, 255, 0, 0);
  require(protocol.setFeature(kRange, packet.data(), size16(packet)), "range failed");
  uint32_t version = 0;
  snapshot(protocol, version);
  auto staged = range(0, 0, false, 0, 255, 0);
  require(protocol.setFeature(kRange, staged.data(), size16(staged)), "stage failed");
  const uint8_t autonomous = 1;
  require(protocol.setFeature(kControl, &autonomous, 1), "autonomous transition failed");
  require(protocol.isAutonomous(), "autonomous flag not set");
  requireNoFrame(protocol, version, "autonomous reset must not count as fresh host data");
  Frame frame{};
  require(protocol.setFeature(kControl, &autonomous, 1), "repeated control rejected");
  require(!protocol.copyCommittedFrame(frame.data(), frame.size(), version),
          "repeated autonomous control created another frame");
  hostControl(protocol);
  auto commit = range(3, 3, true, 0, 0, 255);
  require(protocol.setFeature(kRange, commit.data(), size16(commit)), "commit failed");
  const Frame expected{0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255};
  require(snapshot(protocol, version) == expected, "old pending colors survived reset");
}

void disconnectReset() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto packet = range(0, 3, true, 50, 60, 70);
  require(protocol.setFeature(kRange, packet.data(), size16(packet)), "range failed");
  uint32_t version = 0;
  snapshot(protocol, version);
  protocol.resetToAutonomous();
  require(protocol.isAutonomous(), "disconnect reset did not enable autonomous");
  requireNoFrame(protocol, version, "disconnect reset must withhold a host frame");
  hostControl(protocol);
  commitEmpty(protocol);
  require(snapshot(protocol, version) == Frame{}, "disconnect reset did not clear colors");
}

void rapidAutonomousRoundTripRequiresFreshCommit() {
  lampArray::Protocol protocol(4, 10000);
  require(!protocol.hasCommittedHostFrame(), "initial state already has host data");
  hostControl(protocol);
  const auto original = range(0, 3, true, 255, 20, 10);
  require(protocol.setFeature(kRange, original.data(), size16(original)),
          "initial colored frame commit failed");
  require(protocol.hasCommittedHostFrame(), "valid commit did not publish host-data state");
  uint32_t version = 0;
  const Frame originalExpected{255, 20, 10, 255, 20, 10, 255, 20, 10, 255, 20, 10};
  require(snapshot(protocol, version) == originalExpected, "initial colored frame differs");
  const uint8_t autonomous = 1;
  const uint8_t host = 0;
  require(protocol.setFeature(kControl, &autonomous, 1), "autonomous control failed");
  require(protocol.setFeature(kControl, &host, 1), "immediate host control failed");
  require(!protocol.isAutonomous(), "rapid round trip did not finish in host control");
  require(!protocol.hasCommittedHostFrame(),
          "rapid autonomous-to-host round trip retained stale committed-host state");
  const uint32_t previousVersion = version;
  requireNoFrame(protocol, version, "rapid round trip exposed the previous colored frame");
  require(version == previousVersion, "missing host frame consumed the reader version");
  const auto staged = range(2, 2, false, 0, 0, 100);
  require(protocol.setFeature(kRange, staged.data(), size16(staged)), "new staging failed");
  require(!protocol.hasCommittedHostFrame(), "partial staging counted as a fresh commit");
  requireNoFrame(protocol, version, "partial staging exposed a frame before commit");
  const auto fresh = range(1, 1, true, 0, 100, 0);
  require(protocol.setFeature(kRange, fresh.data(), size16(fresh)), "fresh commit failed");
  require(protocol.hasCommittedHostFrame(), "fresh commit did not restore host-data state");
  const Frame freshExpected{0, 0, 0, 0, 100, 0, 0, 0, 100, 0, 0, 0};
  require(snapshot(protocol, version) == freshExpected,
          "fresh frame contains stale colors from before the rapid control transition");
  protocol.resetToAutonomous();
  require(!protocol.hasCommittedHostFrame(), "disconnect reset retained committed-host state");
}

void attributesQuery() {
  lampArray::Protocol protocol(4, 10000);
  std::array<uint8_t, 64> out{};
  const auto length = protocol.getFeature(kAttributes, out.data(), size16(out));
  require(length == 22, "array attributes length must be 22");
  require(read16(out.data()) == 4, "array attributes lamp count wrong");
  require(read32(out.data() + 18) == 16667, "minimum update interval wrong");
  uint8_t requested[2]{};
  write16(requested, 2);
  require(protocol.setFeature(kRequest, requested, sizeof(requested)), "request failed");
  require(protocol.getFeature(kResponse, out.data(), size16(out)) == 28,
          "lamp attributes response length wrong");
  require(read16(out.data()) == 2, "wrong requested lamp ID");
  require(read32(out.data() + 2) == 25000, "lamp X center coordinate wrong");
  require(out[22] == 255 && out[23] == 255 && out[24] == 255 && out[25] == 255,
          "level counts wrong");
  require(out[26] == 1, "lamp not programmable");
  require(protocol.getFeature(kResponse, out.data(), size16(out)) == 28,
          "next attributes response failed");
  require(read16(out.data()) == 3, "lamp query did not advance");
  require(protocol.getFeature(kResponse, out.data(), size16(out)) == 28,
          "wrapped attributes response failed");
  require(read16(out.data()) == 0, "lamp query did not wrap");
  write16(requested, 65535);
  require(protocol.setFeature(kRequest, requested, sizeof(requested)),
          "invalid requested ID should fall back to first lamp");
  require(protocol.getFeature(kResponse, out.data(), size16(out)) == 28,
          "fallback query failed");
  require(read16(out.data()) == 0, "invalid request did not fall back to zero");
}

void shortReadDoesNotConsumeVersion() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  commitEmpty(protocol);
  std::array<uint8_t, 11> small{};
  uint32_t version = 0;
  require(!protocol.copyCommittedFrame(small.data(), small.size(), version),
          "undersize frame buffer accepted");
  require(version == 0, "undersize buffer consumed version");
  require(snapshot(protocol, version) == Frame{}, "full read failed after short read");
}

void unknownReportsAndShortAttributes() {
  lampArray::Protocol protocol(4, 10000);
  std::array<uint8_t, 64> buffer{};
  require(protocol.getFeature(255, buffer.data(), size16(buffer)) == 0,
          "unknown GET report accepted");
  require(!protocol.setFeature(255, buffer.data(), 1), "unknown SET report accepted");
  require(protocol.getFeature(kAttributes, buffer.data(), 21) == 0,
          "short array attributes output accepted");
  require(protocol.getFeature(kResponse, buffer.data(), 27) == 0,
          "short lamp attributes output accepted");
  require(!protocol.setFeature(kRequest, buffer.data(), 1),
          "short attributes request accepted");
  require(!protocol.setFeature(kRequest, buffer.data(), 3),
          "oversize attributes request accepted");
  require(!protocol.setFeature(kControl, buffer.data(), 0), "empty control accepted");
  require(!protocol.setFeature(kControl, buffer.data(), 2), "oversize control accepted");
}

void emptyMultiCanCommitPending() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto staged = range(0, 0, false, 10, 20, 30);
  require(protocol.setFeature(kRange, staged.data(), size16(staged)), "stage failed");
  const auto commit = multi(0, true);
  require(protocol.setFeature(kMulti, commit.data(), size16(commit)),
          "empty update-complete packet rejected");
  uint32_t version = 0;
  const Frame expected{10, 20, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0};
  require(snapshot(protocol, version) == expected, "empty commit lost pending frame");
}

void reservedFlagsAndControlValues() {
  lampArray::Protocol protocol(4, 10000);
  hostControl(protocol);
  auto multiPacket = multi(1, true);
  lamp(multiPacket, 0, 0, 255, 0, 0);
  auto rangePacket = range(0, 3, true, 0, 255, 0);
  for (const uint8_t flags : {uint8_t(2), uint8_t(3), uint8_t(128), uint8_t(255)}) {
    multiPacket[1] = flags;
    rangePacket[0] = flags;
    require(!protocol.setFeature(kMulti, multiPacket.data(), size16(multiPacket)),
            "reserved multi flags accepted");
    require(!protocol.setFeature(kRange, rangePacket.data(), size16(rangePacket)),
            "reserved range flags accepted");
    require(!protocol.setFeature(kControl, &flags, 1), "invalid control value accepted");
  }
  uint32_t version = 0;
  requireNoFrame(protocol, version, "invalid flags published a frame");
  commitEmpty(protocol);
  require(snapshot(protocol, version) == Frame{}, "invalid flags changed frame");
  require(!protocol.isAutonomous(), "invalid control changed ownership");
}

void configurationAndNullBoundaries() {
  require(!lampArray::Protocol(0, 10000).valid(), "zero lamp count accepted");
  require(lampArray::Protocol(256, 10000).valid(), "maximum lamp count rejected");
  require(!lampArray::Protocol(257, 10000).valid(), "excessive lamp count accepted");
  require(!lampArray::Protocol(4, 0).valid(), "zero pitch accepted");
  require(!lampArray::Protocol(4, 10000, 0).valid(), "zero width accepted");
  require(!lampArray::Protocol(4, 10000, 10000, 0).valid(), "zero update interval accepted");
  require(!lampArray::Protocol(4, UINT32_MAX).valid(), "overflow geometry accepted");
  require(!lampArray::Protocol(4, 10000, 10000, UINT32_MAX).valid(),
          "overflow latency accepted");
  lampArray::Protocol protocol(4, 10000);
  uint32_t version = 0;
  require(protocol.getFeature(kAttributes, nullptr, 64) == 0, "null output accepted");
  require(!protocol.setFeature(kControl, nullptr, 1), "null input accepted");
  require(!protocol.copyCommittedFrame(nullptr, 12, version), "null frame accepted");
}

void outputBoundsAndFailedReadQuery() {
  lampArray::Protocol protocol(4, 10000);
  std::array<uint8_t, 30> output{};
  output.fill(0xA5);
  uint8_t requested[2]{2, 0};
  require(protocol.setFeature(kRequest, requested, 2), "request failed");
  require(protocol.getFeature(kResponse, output.data() + 1, 27) == 0,
          "short response output accepted");
  require(output.front() == 0xA5 && output.back() == 0xA5,
          "short response overran guard bytes");
  require(protocol.getFeature(kResponse, output.data() + 1, 28) == 28,
          "response after short buffer failed");
  require(read16(output.data() + 1) == 2, "failed short response advanced query");
  require(output.front() == 0xA5 && output.back() == 0xA5,
          "response overran guard bytes");
}

void frameLimiterCeilingAndHue() {
  const Frame input{255, 128, 0, 50, 100, 200, 0, 0, 0, 255, 255, 255};
  Frame output{};
  limitFrame(input.data(), output.data(), 4, 51, 5000, 1, 20);
  const Frame expected{51, 25, 0, 10, 20, 40, 0, 0, 0, 51, 51, 51};
  require(output == expected, "channel ceiling did not scale all channels uniformly");
  Frame alias = input;
  limitFrame(alias.data(), alias.data(), 4, 51, 5000, 1, 20);
  require(alias == expected, "in-place frame limiting differs");
}

void frameLimiterPowerBudget() {
  const std::array<uint8_t, 3> white{255, 255, 255};
  std::array<uint8_t, 3> output{};
  limitFrame(white.data(), output.data(), 1, 255, 31, 1, 20);
  require(output == std::array<uint8_t, 3>{127, 127, 127},
          "current budget scaling wrong");
  limitFrame(white.data(), output.data(), 1, 255, 1, 1, 20);
  require(output == std::array<uint8_t, 3>{}, "idle-only budget not black");
  limitFrame(white.data(), output.data(), 1, 0, 5000, 1, 20);
  require(output == std::array<uint8_t, 3>{}, "zero channel ceiling not black");
  const std::array<uint8_t, 3> black{};
  limitFrame(black.data(), output.data(), 1, 255, 5000, 1, 20);
  require(output == black, "zero-input frame not black");
  std::array<uint8_t, 8 * 3> eightWhite{};
  std::array<uint8_t, 8 * 3> eightOutput{};
  eightWhite.fill(255);
  limitFrame(eightWhite.data(), eightOutput.data(), 8, 24, 50, 1, 20);
  uint64_t sum = 0;
  for (const auto channel : eightOutput) {
    require(channel == 22, "eight-lamp 50mA budget with ceiling24 wrong");
    sum += channel;
  }
  require(sum * 20 <= uint64_t(50 - 8) * 255,
          "eight-lamp frame exceeds arithmetic power budget");
}

void frameLimiterModelInvariant() {
  std::array<uint8_t, 256 * 3> input{};
  std::array<uint8_t, 256 * 3> output{};
  uint32_t random = 0x12345678;
  for (size_t round = 0; round < 500; ++round) {
    for (auto& channel : input) {
      random = random * 1664525U + 1013904223U;
      channel = static_cast<uint8_t>(random >> 24);
    }
    const uint8_t ceiling = static_cast<uint8_t>((round * 37) % 256);
    const uint32_t budget = static_cast<uint32_t>(256 + round * 11);
    limitFrame(input.data(), output.data(), 256, ceiling, budget, 1, 20);
    uint64_t sum = 0;
    for (size_t i = 0; i < input.size(); ++i) {
      require(output[i] <= input[i], "power limiter increased a channel");
      require(output[i] <= ceiling, "power limiter exceeded channel ceiling");
      sum += output[i];
    }
    require(sum * 20 <= uint64_t(budget - 256) * 255,
            "output exceeds the configured arithmetic current model");
  }
}

void frameLimiterWideArithmetic() {
  std::array<uint8_t, 256 * 3> input{};
  std::array<uint8_t, 256 * 3> output{};
  input.fill(255);
  limitFrame(input.data(), output.data(), 256, 255, UINT32_MAX, 1, UINT32_MAX);
  for (const auto channel : output)
    require(channel == 0, "wide current arithmetic overflowed");
  limitFrame(input.data(), output.data(), 256, 255, UINT32_MAX, 1, 20);
  require(output == input, "large sufficient budget unexpectedly dimmed frame");
  limitFrame(input.data(), output.data(), 256, 24, 50, UINT32_MAX, 20);
  for (const auto channel : output)
    require(channel == 0, "wide idle arithmetic overflowed");
  limitFrame(input.data(), output.data(), 256, 24, 5000, 1, 0);
  for (const auto channel : output)
    require(channel == 24, "zero modeled channel current divided by zero or lost ceiling");
  const uint8_t sentinel = 77;
  uint8_t zeroCountOutput = sentinel;
  limitFrame(input.data(), &zeroCountOutput, 0, 255, 50, 1, 20);
  require(zeroCountOutput == sentinel, "zero-lamp limit wrote outside empty output");
}

void configurableLampCountEndpoints() {
  for (const uint16_t count : std::array<uint16_t, 2>{1, 64}) {
    lampArray::Protocol protocol(count, 16667);
    require(protocol.valid(), "supported count endpoint was rejected");
    std::array<uint8_t, 22> attributes{};
    require(protocol.getFeature(kAttributes, attributes.data(), size16(attributes)) ==
                attributes.size(), "array attributes response failed");
    require(read16(attributes.data()) == count, "attributes reported wrong configured count");
    hostControl(protocol);
    uint32_t version = 0;
    std::array<uint8_t, 64 * 3 + 1> frame{};
    frame.back() = 0xa5;
    for (uint16_t first = 0; first < count; first += 8) {
      const uint8_t slots = static_cast<uint8_t>(count - first < 8 ? count - first : 8);
      const bool finalPacket = first + slots == count;
      auto packet = multi(slots, finalPacket);
      for (uint8_t slot = 0; slot < slots; ++slot) {
        const uint16_t id = static_cast<uint16_t>(first + slot);
        lamp(packet, slot, id, static_cast<uint8_t>(id + 1),
             static_cast<uint8_t>(id + 2), static_cast<uint8_t>(id + 3));
      }
      require(protocol.setFeature(kMulti, packet.data(), size16(packet)),
              "configured-count multi-packet update failed");
      if (!finalPacket)
        require(!protocol.copyCommittedFrame(frame.data(), count * 3, version),
                "configured-count frame committed before final packet");
    }
    require(protocol.copyCommittedFrame(frame.data(), count * 3, version),
            "configured-count final frame missing");
    for (uint16_t id = 0; id < count; ++id) {
      const size_t offset = static_cast<size_t>(id) * 3;
      require(frame[offset] == id + 1 && frame[offset + 1] == id + 2 &&
                  frame[offset + 2] == id + 3, "configured-count color/index mismatch");
    }
    require(frame.back() == 0xa5, "configured-count copy exceeded output capacity");
    auto beyond = multi(1, true);
    lamp(beyond, 0, count, 255, 0, 0);
    require(!protocol.setFeature(kMulti, beyond.data(), size16(beyond)),
            "configured-count out-of-range lamp ID was accepted");
  }
}

void sixtyFourLampPowerModel() {
  std::array<uint8_t, 64 * 3> input{};
  std::array<uint8_t, 64 * 3> output{};
  input.fill(255);
  limitFrame(input.data(), output.data(), 64, 24, 80, 1, 20);
  uint64_t sum = 0;
  for (const auto channel : output) {
    require(channel > 0 && channel <= 24, "64-lamp model lost output or exceeded ceiling");
    sum += channel;
  }
  require(sum * 20 <= uint64_t(80 - 64) * 255,
          "64-lamp frame exceeded explicit arithmetic budget");
  limitFrame(input.data(), output.data(), 64, 24, 50, 1, 20);
  for (const auto channel : output)
    require(channel == 0, "insufficient idle budget did not suppress dynamic output");
}
}  // namespace

int main() {
  const std::vector<std::pair<const char*, std::function<void()>>> tests{
      {"initial state and frame version", initialState},
      {"autonomous mode rejects host updates", autonomousRejectsHostUpdates},
      {"multi-packet frame commits atomically", multiCommitIsAtomic},
      {"invalid multi IDs cannot partly mutate staging", invalidMultiDoesNotPolluteStaging},
      {"duplicate multi IDs rejected", duplicateIdsAreRejected},
      {"multi packet counts and exact lengths", multiCountsAndLengths},
      {"inclusive range and intensity", inclusiveRangeAndIntensity},
      {"invalid ranges cannot partly mutate staging", invalidRangesDoNotPolluteStaging},
      {"range exact lengths", rangeLengths},
      {"multi intensity boundaries", multiIntensityBoundaries},
      {"autonomous transition clears committed and staged colors", autonomousTransitionClearsPending},
      {"disconnect reset returns to black autonomous", disconnectReset},
      {"rapid autonomous round trip requires fresh host commit", rapidAutonomousRoundTripRequiresFreshCommit},
      {"attributes request, geometry, advance and wrap", attributesQuery},
      {"small frame buffer does not consume version", shortReadDoesNotConsumeVersion},
      {"unknown reports and truncated attributes", unknownReportsAndShortAttributes},
      {"empty multi packet commits pending colors", emptyMultiCanCommitPending},
      {"reserved flags and control values rejected", reservedFlagsAndControlValues},
      {"invalid configuration and null pointers", configurationAndNullBoundaries},
      {"output guard bytes and failed-query position", outputBoundsAndFailedReadQuery},
      {"frame ceiling preserves common scale", frameLimiterCeilingAndHue},
      {"frame arithmetic power budget", frameLimiterPowerBudget},
      {"frame limiter current-model invariant over 500 frames", frameLimiterModelInvariant},
      {"frame limiter wide arithmetic and zero count", frameLimiterWideArithmetic},
      {"one/64-lamp attributes, packet commits and index boundaries", configurableLampCountEndpoints},
      {"64-lamp explicit power model and insufficient idle budget", sixtyFourLampPowerModel},
  };
  size_t failed = 0;
  for (const auto& test : tests) {
    try {
      test.second();
      std::cout << "PASS " << test.first << '\n';
    } catch (const std::exception& error) {
      ++failed;
      std::cerr << "FAIL " << test.first << ": " << error.what() << '\n';
    }
  }
  std::cout << (tests.size() - failed) << "/" << tests.size() << " tests passed\n";
  return failed == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
