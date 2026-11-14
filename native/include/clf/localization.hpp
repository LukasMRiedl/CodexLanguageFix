#pragma once
#include <exception>
#include <string>
#include <string_view>
#include <vector>
#include <utility>
namespace clf {
void set_language(std::string_view language);
std::string normalize_language(std::string_view language);
bool is_german();
std::wstring translate(std::string_view key, const std::vector<std::wstring>& args = {});
struct AppError : std::exception {
    std::string key;
    std::vector<std::wstring> args;
    explicit AppError(std::string message_key, std::vector<std::wstring> arguments = {})
        : key(std::move(message_key)), args(std::move(arguments)) {}
    const char* what() const noexcept override { return key.c_str(); }
};
}
