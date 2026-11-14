#include "clf/backend.hpp"
#include "clf/core.hpp"
#include "clf/localization.hpp"

#include <windows.h>
#include <knownfolders.h>
#include <shlobj.h>
#include <winreg.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <array>
#include <charconv>
#include <filesystem>
#include <fstream>
#include <limits>
#include <optional>
#include <string>
#include <string_view>
#include <system_error>
#include <type_traits>

namespace clf {
namespace {
using ordered_json = nlohmann::ordered_json;

[[noreturn]] void throw_error(const char* key) {
    throw AppError(key);
}

enum class Property : std::size_t {
    settings_version,
    enabled,
    start_with_windows,
    prepare_luna_corrections,
    first_run_notice_shown,
    language,
    shortcut_modifiers,
    shortcut_virtual_key,
    correction_provider,
    count
};

constexpr std::array<std::wstring_view, static_cast<std::size_t>(Property::count)> property_names{
    L"settingsVersion", L"enabled", L"startWithWindows", L"prepareLunaCorrections",
    L"firstRunNoticeShown", L"language", L"shortcutModifiers", L"shortcutVirtualKey",
    L"correctionProvider"
};

struct CapturedProperties {
    std::array<std::optional<ordered_json>, static_cast<std::size_t>(Property::count)> first;
    std::array<std::optional<ordered_json>, static_cast<std::size_t>(Property::count)> last;
    bool invalid_deserialization{};
};

std::optional<Property> property_from_key(const std::string& key) {
    const auto wide_key = utf16(key);
    for (std::size_t index = 0; index < property_names.size(); ++index) {
        const auto expected = property_names[index];
        if (wide_key.size() <= static_cast<std::size_t>(std::numeric_limits<int>::max())
            && expected.size() <= static_cast<std::size_t>(std::numeric_limits<int>::max())
            && CompareStringOrdinal(wide_key.data(), static_cast<int>(wide_key.size()),
                expected.data(), static_cast<int>(expected.size()), TRUE) == CSTR_EQUAL) {
            return static_cast<Property>(index);
        }
    }
    return std::nullopt;
}

void remember_property(CapturedProperties& properties, Property property, ordered_json value) {
    const auto index = static_cast<std::size_t>(property);
    if (!properties.first[index]) properties.first[index] = value;
    properties.last[index] = std::move(value);
}

template <typename T>
void read_value(const CapturedProperties& properties, Property property, T& destination) {
    const auto& captured = properties.last[static_cast<std::size_t>(property)];
    if (!captured) return;
    const auto* value = &*captured;
    if constexpr (std::is_same_v<T, bool>) {
        if (!value->is_boolean()) throw nlohmann::json::type_error::create(302, "expected boolean", value);
        destination = value->get<bool>();
    } else if constexpr (std::is_same_v<T, int>) {
        if (value->is_string()) {
            const auto& text = value->get_ref<const std::string&>();
            int parsed{};
            const auto result = std::from_chars(text.data(), text.data() + text.size(), parsed, 10);
            if (result.ec == std::errc::result_out_of_range)
                throw nlohmann::json::out_of_range::create(406, "integer out of range", value);
            if (result.ec != std::errc{} || result.ptr != text.data() + text.size())
                throw nlohmann::json::type_error::create(302, "expected integer", value);
            destination = parsed;
            return;
        }
        if (!value->is_number_integer() && !value->is_number_unsigned())
            throw nlohmann::json::type_error::create(302, "expected integer", value);
        if (value->is_number_unsigned()
            && value->get<std::uint64_t>() > static_cast<std::uint64_t>(std::numeric_limits<int>::max()))
            throw nlohmann::json::out_of_range::create(406, "integer out of range", value);
        const auto parsed = value->get<std::int64_t>();
        if (parsed < std::numeric_limits<int>::min() || parsed > std::numeric_limits<int>::max())
            throw nlohmann::json::out_of_range::create(406, "integer out of range", value);
        destination = static_cast<int>(parsed);
    } else if constexpr (std::is_same_v<T, std::uint32_t>) {
        if (value->is_string()) {
            const auto& text = value->get_ref<const std::string&>();
            std::uint32_t parsed{};
            const auto result = std::from_chars(text.data(), text.data() + text.size(), parsed, 10);
            if (result.ec == std::errc::result_out_of_range)
                throw nlohmann::json::out_of_range::create(406, "integer out of range", value);
            if (result.ec != std::errc{} || result.ptr != text.data() + text.size())
                throw nlohmann::json::type_error::create(302, "expected unsigned integer", value);
            destination = parsed;
            return;
        }
        if (!value->is_number_integer() && !value->is_number_unsigned())
            throw nlohmann::json::type_error::create(302, "expected unsigned integer", value);
        if (value->is_number_integer()) {
            const auto parsed = value->get<std::int64_t>();
            if (parsed < 0 || static_cast<std::uint64_t>(parsed) > std::numeric_limits<std::uint32_t>::max())
                throw nlohmann::json::out_of_range::create(406, "integer out of range", value);
            destination = static_cast<std::uint32_t>(parsed);
        } else {
            const auto parsed = value->get<std::uint64_t>();
            if (parsed > std::numeric_limits<std::uint32_t>::max())
                throw nlohmann::json::out_of_range::create(406, "integer out of range", value);
            destination = static_cast<std::uint32_t>(parsed);
        }
    } else if constexpr (std::is_same_v<T, std::string>) {
        if (value->is_null()) {
            destination = "auto";
        } else if (value->is_string()) {
            destination = value->get<std::string>();
        } else {
            throw nlohmann::json::type_error::create(302, "expected string", value);
        }
    }
}

bool invalid_for_deserializer(Property property, const ordered_json& value) {
    if (property == Property::correction_provider) return false;
    CapturedProperties captured{};
    captured.last[static_cast<std::size_t>(property)] = value;
    Settings probe;
    try {
        switch (property) {
        case Property::settings_version:
            read_value(captured, property, probe.version);
            break;
        case Property::enabled:
            read_value(captured, property, probe.enabled);
            break;
        case Property::start_with_windows:
            read_value(captured, property, probe.start_with_windows);
            break;
        case Property::prepare_luna_corrections:
            read_value(captured, property, probe.prepare);
            break;
        case Property::first_run_notice_shown:
            read_value(captured, property, probe.notice_shown);
            break;
        case Property::language:
            read_value(captured, property, probe.language);
            break;
        case Property::shortcut_modifiers:
            read_value(captured, property, probe.shortcut_modifiers);
            break;
        case Property::shortcut_virtual_key:
            read_value(captured, property, probe.shortcut_vk);
            break;
        case Property::correction_provider:
        case Property::count:
            break;
        }
        return false;
    } catch (...) {
        return true;
    }
}

bool parse_int32(const ordered_json* value, int& result) {
    if (!value || (!value->is_number_integer() && !value->is_number_unsigned())) return false;
    try {
        if (value->is_number_unsigned()) {
            const auto parsed = value->get<std::uint64_t>();
            if (parsed > static_cast<std::uint64_t>(std::numeric_limits<std::int32_t>::max())) return false;
            result = static_cast<int>(parsed);
            return true;
        }
        const auto parsed = value->get<std::int64_t>();
        if (parsed < std::numeric_limits<std::int32_t>::min()
            || parsed > std::numeric_limits<std::int32_t>::max()) return false;
        result = static_cast<int>(parsed);
        return true;
    } catch (...) {
        return false;
    }
}

const ordered_json* first_property(const CapturedProperties& properties, Property property) {
    const auto& captured = properties.first[static_cast<std::size_t>(property)];
    return captured ? &*captured : nullptr;
}

bool preserve_prepare_choice(const CapturedProperties& properties) {
    if (const auto* legacy = first_property(properties, Property::correction_provider)) {
        int provider{};
        return parse_int32(legacy, provider) && provider == 1;
    }
    int version{};
    return parse_int32(first_property(properties, Property::settings_version), version) && version == 2;
}

bool is_modifier_key(std::uint32_t key) {
    return key == 0x10 || key == 0x11 || key == 0x12 || key == 0x5B || key == 0x5C
        || (key >= 0xA0 && key <= 0xA5);
}

bool is_supported_key(std::uint32_t key) {
    if (key > 0xFE) return false;
    return key == 0x08 || key == 0x09 || key == 0x0C || key == 0x0D
        || (key >= 0x13 && key <= 0x2F)
        || (key >= 0x30 && key <= 0x39)
        || (key >= 0x41 && key <= 0x5A)
        || key == 0x5D || key == 0x5F
        || (key >= 0x60 && key <= 0x6F)
        || (key >= 0x70 && key <= 0x87)
        || key == 0x90 || key == 0x91
        || (key >= 0xA6 && key <= 0xB7)
        || (key >= 0xBA && key <= 0xC0)
        || (key >= 0xDB && key <= 0xE4)
        || key == 0xE6
        || (key >= 0xE9 && key <= 0xFE);
}

bool valid_shortcut(const Settings& settings) {
    constexpr std::uint32_t allowed = 0x1 | 0x2 | 0x4;
    if ((settings.shortcut_modifiers & ~allowed) != 0
        || !is_supported_key(settings.shortcut_vk)
        || is_modifier_key(settings.shortcut_vk)
        || settings.shortcut_vk == 0x7B
        || settings.shortcut_vk == 0xE5
        || settings.shortcut_vk == 0xE7) return false;
    const bool control_or_alt = (settings.shortcut_modifiers & 0x3) != 0;
    const bool function_key = settings.shortcut_vk >= 0x70 && settings.shortcut_vk <= 0x87;
    return control_or_alt || function_key;
}

std::wstring module_path() {
    std::wstring buffer(512, L'\0');
    for (;;) {
        const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0) throw AppError("ApplicationPathUnavailable");
        if (length < buffer.size() - 1) {
            buffer.resize(length);
            return buffer;
        }
        if (buffer.size() >= 32768) throw AppError("ApplicationPathUnavailable");
        buffer.resize(std::min<std::size_t>(buffer.size() * 2, 32768));
    }
}

void check_registry(LSTATUS status) {
    if (status != ERROR_SUCCESS) throw AppError("AutostartChangeFailed");
}
} // namespace

Settings parse_settings_json(std::string_view json_bytes) {
    Settings settings{};
    if (json_bytes.size() >= 3
        && static_cast<unsigned char>(json_bytes[0]) == 0xEF
        && static_cast<unsigned char>(json_bytes[1]) == 0xBB
        && static_cast<unsigned char>(json_bytes[2]) == 0xBF) {
        json_bytes.remove_prefix(3);
    }

    CapturedProperties properties{};
    std::optional<Property> pending_property;
    const auto capture_property = [&](ordered_json value) {
        const auto property = *pending_property;
        if (invalid_for_deserializer(property, value)) properties.invalid_deserialization = true;
        remember_property(properties, property, std::move(value));
        pending_property.reset();
    };
    const auto callback = [&](int depth, ordered_json::parse_event_t event, ordered_json& parsed) {
        if (depth == 1 && event == ordered_json::parse_event_t::key) {
            pending_property = property_from_key(parsed.get<std::string>());
        } else if (depth == 1 && pending_property) {
            if (event == ordered_json::parse_event_t::value) {
                capture_property(parsed);
            } else if (event == ordered_json::parse_event_t::object_start) {
                capture_property(ordered_json::object());
            } else if (event == ordered_json::parse_event_t::array_start) {
                capture_property(ordered_json::array());
            }
        }
        return true;
    };

    try {
        const auto root = ordered_json::parse(json_bytes.begin(), json_bytes.end(), callback);
        if (!root.is_object() || properties.invalid_deserialization) return Settings{};
        read_value(properties, Property::settings_version, settings.version);
        read_value(properties, Property::enabled, settings.enabled);
        read_value(properties, Property::start_with_windows, settings.start_with_windows);
        read_value(properties, Property::prepare_luna_corrections, settings.prepare);
        read_value(properties, Property::first_run_notice_shown, settings.notice_shown);
        read_value(properties, Property::language, settings.language);
        read_value(properties, Property::shortcut_modifiers, settings.shortcut_modifiers);
        read_value(properties, Property::shortcut_virtual_key, settings.shortcut_vk);
        if (!preserve_prepare_choice(properties)) settings.prepare = false;
        if (!valid_shortcut(settings)) {
            settings.shortcut_modifiers = 3;
            settings.shortcut_vk = 0xBC;
            settings.shortcut_reset_on_load = true;
        }
        return settings;
    } catch (const std::exception&) {
        return Settings{};
    }
}

std::wstring application_directory() {
    PWSTR local_app_data = nullptr;
    const HRESULT result = SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &local_app_data);
    if (FAILED(result) || local_app_data == nullptr) throw AppError("ApplicationPathUnavailable");
    std::filesystem::path directory(local_app_data);
    CoTaskMemFree(local_app_data);
    directory /= L"CodexLanguageFix";
    return directory.wstring();
}

Settings load_settings() {
    Settings settings;
    const auto path = std::filesystem::path(application_directory()) / L"settings.json";
    std::error_code exists_error;
    if (!std::filesystem::exists(path, exists_error) || exists_error) {
        set_language(settings.language);
        return settings;
    }

    try {
        std::ifstream file(path, std::ios::binary);
        if (!file) {
            set_language(settings.language);
            return settings;
        }
        const std::string bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        settings = parse_settings_json(bytes);
    } catch (const std::exception&) {
        settings = Settings{};
    }
    set_language(settings.language);
    return settings;
}

void save_settings(const Settings& settings) {
    try {
        const auto directory = std::filesystem::path(application_directory());
        std::filesystem::create_directories(directory);
        ordered_json root = ordered_json::object();
        root["settingsVersion"] = 2;
        root["enabled"] = settings.enabled;
        root["startWithWindows"] = settings.start_with_windows;
        root["prepareLunaCorrections"] = settings.prepare;
        root["firstRunNoticeShown"] = settings.notice_shown;
        root["language"] = settings.language;
        root["shortcutModifiers"] = settings.shortcut_modifiers;
        root["shortcutVirtualKey"] = settings.shortcut_vk;
        const auto path = directory / L"settings.json";
        auto temporary = path;
        temporary += L".tmp";
        const auto content = root.dump(2, ' ', false) + "\n";
        {
            std::ofstream file(temporary, std::ios::binary | std::ios::trunc);
            if (!file) throw std::system_error(std::make_error_code(std::errc::io_error));
            file.write(content.data(), static_cast<std::streamsize>(content.size()));
            file.flush();
            if (!file) throw std::system_error(std::make_error_code(std::errc::io_error));
        }
        if (!MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            throw std::system_error(static_cast<int>(GetLastError()), std::system_category());
        }
    } catch (const AppError&) {
        throw;
    } catch (...) {
        throw AppError("ShortcutSaveFailed");
    }
}

void set_autostart(bool enabled) {
    constexpr wchar_t registry_path[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    constexpr wchar_t value_name[] = L"CodexLanguageFix";
    HKEY key = nullptr;
    const auto open_status = RegCreateKeyExW(HKEY_CURRENT_USER, registry_path, 0, nullptr, 0,
        KEY_SET_VALUE | KEY_QUERY_VALUE, nullptr, &key, nullptr);
    if (open_status != ERROR_SUCCESS) throw AppError("AutostartChangeFailed");
    const auto close_key = [&] { RegCloseKey(key); };
    try {
        if (enabled) {
            const auto executable = module_path();
            const std::wstring command = L"\"" + executable + L"\" --autostart";
            check_registry(RegSetValueExW(key, value_name, 0, REG_SZ,
                reinterpret_cast<const BYTE*>(command.c_str()),
                static_cast<DWORD>((command.size() + 1) * sizeof(wchar_t))));
        } else {
            const auto status = RegDeleteValueW(key, value_name);
            if (status != ERROR_SUCCESS && status != ERROR_FILE_NOT_FOUND) check_registry(status);
        }
        close_key();
    } catch (...) {
        close_key();
        throw;
    }
}

bool autostart_enabled() {
    constexpr wchar_t registry_path[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    constexpr wchar_t value_name[] = L"CodexLanguageFix";
    HKEY key = nullptr;
    const auto open_status = RegOpenKeyExW(HKEY_CURRENT_USER, registry_path, 0, KEY_QUERY_VALUE, &key);
    if (open_status == ERROR_FILE_NOT_FOUND) return false;
    if (open_status != ERROR_SUCCESS) throw AppError("AutostartChangeFailed");
    DWORD type = 0;
    DWORD size = 0;
    const auto status = RegQueryValueExW(key, value_name, nullptr, &type, nullptr, &size);
    RegCloseKey(key);
    return status == ERROR_SUCCESS && (type == REG_SZ || type == REG_EXPAND_SZ);
}
} // namespace clf
