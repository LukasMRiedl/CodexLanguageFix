#include "clf/backend.hpp"
#include "clf/core.hpp"
#include "core_cost_probe.hpp"
#include <iostream>
#include <nlohmann/json.hpp>
#include <string>
using nlohmann::json;
using namespace clf;
int main() {
  std::string line;
  while (std::getline(std::cin, line)) {
    try {
      auto input = json::parse(line);
      if (input.contains("settingsJson")) {
        const auto settings = parse_settings_json(input.at("settingsJson").get<std::string>());
        std::cout << json{
            {"settingsVersion", settings.version},
            {"enabled", settings.enabled},
            {"startWithWindows", settings.start_with_windows},
            {"prepareLunaCorrections", settings.prepare},
            {"firstRunNoticeShown", settings.notice_shown},
            {"language", settings.language},
            {"shortcutModifiers", settings.shortcut_modifiers},
            {"shortcutVirtualKey", settings.shortcut_vk},
            {"shortcutResetOnLoad", settings.shortcut_reset_on_load}
        }.dump() << '\n';
        continue;
      }
      auto original = utf16(input.at("original").get<std::string>());
      auto corrected = utf16(input.at("corrected").get<std::string>());
      if (input.contains("coreCostProbe")) {
        std::cout << clf::test::Run(original, corrected).dump() << '\n';
        continue;
      }
      json result;
      result["spans"] = json::array();
      for (auto span : protected_spans(original))
        result["spans"].push_back(
            {{"start", span.start}, {"length", span.length}});
      auto protected_text = ProtectedText::create(original);
      result["selfRestore"] = protected_text.restore(protected_text.text) ==
                              std::optional<std::wstring>(original);
      result["damagedRestore"] =
          protected_text
              .restore(protected_text.text +
                       L"\u27e6CLF_PROTECTED_FF_0001\u27e7")
              .has_value();
      result["structure"] = structure_preserved(original, corrected);
      auto restored = restore_layout(original, corrected);
      result["restored"] = restored ? json(utf8(*restored)) : json(nullptr);
      auto plan = make_edit_plan(original, corrected);
      auto edits = [](const EditPlan &value) {
        json array = json::array();
        for (const auto &e : value.edits)
          array.push_back({{"start", e.start},
                           {"original", utf8(e.original)},
                           {"replacement", utf8(e.replacement)}});
        return array;
      };
      result["plan"] = plan ? edits(*plan) : json(nullptr);
      result["reverse"] = plan ? edits(reverse_plan(*plan)) : json(nullptr);
      result["repeated"] = repeated_sentences_preserved(original, corrected);
      result["graphemes"] = grapheme_boundaries(original);
      std::string profile = "model";
      profile += '\0';
      profile += "low";
      profile += '\0';
      profile += "priority";
      profile += '\0';
      profile += "test";
      profile += '\0';
      result["cacheKey"] = sha256(profile + utf8(original));
      std::cout << result.dump() << '\n';
    } catch (...) {
      std::cout << "{\"error\":\"invalid_input\"}\n";
    }
  }
}
