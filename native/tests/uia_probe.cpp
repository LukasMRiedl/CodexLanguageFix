#include <Windows.h>

#include "clf/composer.hpp"
#include <nlohmann/json.hpp>
#include <iostream>
#include <string_view>
#include <array>
#include <optional>

namespace {
using json = nlohmann::json;
struct KnownSynthetic {
  const char *variant;
  const char *framing;
};
std::optional<KnownSynthetic> known_synthetic(std::wstring_view text) {
  struct Frame {
    std::wstring_view suffix;
    const char *name;
  };
  constexpr std::array<Frame, 7> frames{{{L"", "none"},
                                         {L"\n", "LF"},
                                         {L"\r", "CR"},
                                         {L"\r\n", "CRLF"},
                                         {L"\n\r", "LFCR"},
                                         {L"\r\r", "CRCR"},
                                         {L"\n\n", "LFLF"}}};
  for (const auto &frame : frames) {
    for (const auto &candidate :
         std::array<std::pair<std::wstring_view, const char *>, 2>{
             {{L"Das ist korekt.", "original"},
              {L"Das ist korrekt.", "corrected"}}}) {
      std::wstring exact(candidate.first);
      exact.append(frame.suffix);
      if (text == exact)
        return KnownSynthetic{candidate.second, frame.name};
    }
  }
  return std::nullopt;
}
json rectangle(const clf::Rect &value) {
  return {{"x", value.x},
          {"y", value.y},
          {"width", value.width},
          {"height", value.height},
          {"empty", value.empty}};
}
bool foreign_text(const std::optional<clf::Snapshot> &snapshot) {
  return snapshot && !snapshot->text.empty() &&
         !known_synthetic(snapshot->text);
}
json capture_summary(const std::optional<clf::Snapshot> &snapshot,
                     const std::string &status) {
  json result = {{"status", status}, {"snapshot_present", bool(snapshot)}};
  if (!snapshot)
    return result;
  const auto &value = *snapshot;
  result["utf16_length"] = value.text.size();
  const auto known = known_synthetic(value.text);
  result["text_matches_known_synthetic"] = bool(known);
  if (known) {
    result["variant"] = known->variant;
    result["framing"] = known->framing;
  }
  result["identity_valid"] = value.token != 0 && !value.runtime_id.empty() &&
                             value.process_id != 0 && value.host_window != 0;
  result["bounds"] = rectangle(value.bounds);
  result["editor_bounds"] =
      value.editor_bounds ? rectangle(*value.editor_bounds) : json(nullptr);
  result["keyboard_focused"] = value.keyboard_focused;
  result["category"] = value.field_category == "chat"    ? "chat"
                       : value.field_category == "prose" ? "prose"
                                                         : "unknown";
  result["read_method"] = value.read_method == "text_pattern" ? "text_pattern"
                          : value.read_method == "value_pattern"
                              ? "value_pattern"
                              : "unknown";
  return result;
}
} // namespace

int wmain(int argc, wchar_t **argv) {
  if (argc >= 2 && std::wstring_view(argv[1]) == L"--composer-worker") {
    if (argc != 3)
      return 2;
    const std::wstring_view argument(argv[2]);
    if (argument.empty() || argument.size() > 10)
      return 2;
    uint64_t parent{};
    for (wchar_t digit : argument) {
      if (digit < L'0' || digit > L'9')
        return 2;
      parent = parent * 10 + uint64_t(digit - L'0');
    }
    if (!parent || parent > UINT32_MAX)
      return 2;
    return clf::run_composer_worker(static_cast<uint32_t>(parent));
  }
  const bool include_layout =
      argc == 2 && std::wstring_view(argv[1]) == L"--layout";
  if (argc != 1 && !include_layout)
    return 2;
  try {
    clf::ComposerClient client;
    auto snapshot = client.capture(false, true);
    if (foreign_text(snapshot)) {
      client.close();
      std::cout << "{\"status\":\"aborted_non_synthetic\"}\n";
      return 3;
    }
    json output = {{"capture", capture_summary(snapshot, client.status())}};
    if (include_layout && snapshot) {
      auto layout = client.capture(true, true);
      if (foreign_text(layout)) {
        client.close();
        std::cout << "{\"status\":\"aborted_non_synthetic\"}\n";
        return 3;
      }
      output["layout_capture"] = capture_summary(layout, client.status());
    }
    const auto counts = client.diagnostics();
    output["diagnostics"] = {
        {"capture_count", counts.capture_count},
        {"poll_count", counts.poll_count},
        {"full_layout_count", counts.full_layout_count},
        {"geometry_check_count", counts.geometry_check_count},
        {"geometry_node_check_count", counts.geometry_node_check_count},
        {"geometry_walker_step_count", counts.geometry_walker_step_count},
        {"geometry_cache_hit_count", counts.geometry_cache_hit_count},
        {"retained_tokens", counts.retained_tokens},
        {"retained_identity_data_bytes", counts.retained_identity_data_bytes},
        {"idle_owned_com_refs", counts.idle_owned_com_refs},
        {"idle_owned_editor_com_refs", counts.idle_owned_editor_com_refs}};
    client.close();
    std::cout << output.dump() << '\n';
    return 0;
  } catch (...) {
    std::cout << "{\"status\":\"probe_failed\"}\n";
    return 1;
  }
}
