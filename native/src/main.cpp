#include <windows.h>
#include <objbase.h>
#include <shellapi.h>

#include "clf/composer.hpp"
#include "clf/desktop.hpp"
#include <charconv>
#include <string>
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
  int count{};
  auto args = CommandLineToArgvW(GetCommandLineW(), &count);
  if (!args)
    return 2;
  if (count > 1 && std::wstring_view(args[1]) == L"--composer-worker") {
    uint32_t parent{};
    bool valid = count == 3;
    if (valid) {
      const std::wstring value = args[2];
      valid = !value.empty() && value.size() <= 10;
      uint64_t parsed{};
      for (auto c : value) {
        if (c < L'0' || c > L'9') {
          valid = false;
          break;
        }
        parsed = parsed * 10 + uint64_t(c - L'0');
      }
      valid = valid && parsed > 0 && parsed <= UINT32_MAX;
      parent = static_cast<uint32_t>(parsed);
    }
    LocalFree(args);
    return valid ? clf::run_composer_worker(parent) : 2;
  }
  LocalFree(args);
  return clf::run_desktop();
}
