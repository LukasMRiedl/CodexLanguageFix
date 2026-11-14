#include "clf/localization.hpp"
#include "clf/core.hpp"
#include "localization_data.hpp"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <iomanip>
#include <locale>
#include <sstream>
#include <windows.h>

namespace clf {
namespace {
std::atomic<int> language_mode{0};
std::wstring grouped_number(const std::wstring &value) {
  try {
    size_t consumed{};
    auto number = std::stold(value, &consumed);
    if (consumed != value.size() || !std::isfinite(number))
      return value;
    std::wostringstream stream;
    stream.imbue(std::locale::classic());
    stream << std::fixed << std::setprecision(0) << std::nearbyint(number);
    const auto integer = stream.str();
    NUMBERFMTW format{};
    format.NumDigits = 0;
    format.LeadingZero = 1;
    format.Grouping = 3;
    wchar_t decimal[] = L".", comma[] = L",";
    format.lpDecimalSep = is_german() ? comma : decimal;
    format.lpThousandSep = is_german() ? decimal : comma;
    format.NegativeOrder = 1;
    const int size = GetNumberFormatEx(is_german() ? L"de-DE" : L"en-US", 0,
                                       integer.c_str(), &format, nullptr, 0);
    if (size <= 0)
      return value;
    std::wstring formatted(size, L'\0');
    if (!GetNumberFormatEx(is_german() ? L"de-DE" : L"en-US", 0,
                           integer.c_str(), &format, formatted.data(), size))
      return value;
    formatted.resize(size - 1);
    return formatted;
  } catch (...) {
    return value;
  }
}
} // namespace
void set_language(std::string_view language) {
  const auto normalized=normalize_language(language);
  language_mode.store(normalized == "de" ? 1 : normalized == "en" ? 2 : 0);
}
std::string normalize_language(std::string_view language) {
  std::string value(language);
  for(auto& c:value) if(c>='A' && c<='Z') c=static_cast<char>(c-'A'+'a');
  return value=="de"||value=="en"?value:"auto";
}
bool is_german() {
  const auto mode = language_mode.load();
  return mode == 1 || (mode == 0 && PRIMARYLANGID(GetUserDefaultUILanguage()) ==
                                        LANG_GERMAN);
}
std::wstring translate(std::string_view key,
                       const std::vector<std::wstring> &args) {
  const auto &table = is_german() ? german_texts : english_texts;
  auto found = table.find(std::string(key));
  std::wstring text =
      utf16(found == table.end() ? key : std::string_view(found->second));
  for (size_t index = 0; index < args.size(); ++index) {
    const auto marker = L"{" + std::to_wstring(index) + L"}";
    for (size_t offset = 0;
         (offset = text.find(marker, offset)) != std::wstring::npos;
         offset += args[index].size())
      text.replace(offset, marker.size(), args[index]);
    const auto numeric_marker = L"{" + std::to_wstring(index) + L":N0}";
    const auto formatted = grouped_number(args[index]);
    for (size_t offset = 0;
         (offset = text.find(numeric_marker, offset)) != std::wstring::npos;
         offset += formatted.size())
      text.replace(offset, numeric_marker.size(), formatted);
  }
  return text;
}
} // namespace clf
