#include <windows.h>
#include <nlohmann/json.hpp>
#include "clf/core.hpp"

#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <string>
#include <unordered_map>
#include <cstdlib>
#include <cstdint>
#include <cwchar>

using json = nlohmann::json;

namespace {
json stats = {
    {"counts", json::object()},
    {"threadStartSafe", true},
    {"allThreadStartsSafe", true},
    {"processArgsOk", false},
    {"modelPages", 0},
    {"serverRequestsSent", 0},
    {"sentinelChecked", false},
    {"sentinelInherited", false}
};
std::unordered_map<std::string, int> repeated_attempts;
int next_thread = 0;
int next_turn = 0;
std::string last_turn_id;
std::string last_turn_thread_id;
bool stall_after_thread_start = false;
std::wstring test_mode;

void save_stats() {
    const auto path = std::filesystem::current_path() / L"fake_app_server_stats.json";
    auto temporary = path;
    temporary += L".tmp";
    const auto data = stats.dump(2);
    {
        std::ofstream file(temporary, std::ios::binary | std::ios::trunc);
        file.write(data.data(), static_cast<std::streamsize>(data.size()));
    }
    MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}

void send(const json& value) {
    std::cout << value.dump(-1, ' ', false) << '\n';
    std::cout.flush();
}

void respond(const json& request, json result) {
    send(json{{"id", request.at("id")}, {"result", std::move(result)}});
}

json model(std::string name) {
    return json{
        {"model", std::move(name)},
        {"supportedReasoningEfforts", json::array({json{{"reasoningEffort", "low"}}})},
        {"serviceTiers", json::array({json{{"id", "priority"}}})}
    };
}

void add_count(const std::string& method) {
    auto& count = stats["counts"][method];
    if (!count.is_number_integer()) count = 0;
    count = count.get<int>() + 1;
}

void increment_stat(const char* name) {
    auto& value = stats[name];
    if (!value.is_number_integer()) value = 0;
    value = value.get<int>() + 1;
}

std::string source_from_turn(const json& params) {
    try {
        const auto input = params.at("input").at(0).at("text").get<std::string>();
        const auto parsed = json::parse(input);
        return parsed.at("source_text").get<std::string>();
    } catch (...) {
        return {};
    }
}

void complete_turn(const std::string& thread_id, const std::string& turn_id,
    std::string message, bool tool = false) {
    if (tool) {
        send(json{
            {"method", "item/completed"},
            {"params", {
                {"threadId", thread_id},
                {"turnId", turn_id},
                {"item", {{"id", "tool-item"}, {"type", "commandExecution"}}}
            }}
        });
    } else {
        send(json{
            {"method", "item/agentMessage/delta"},
            {"params", {{"threadId", thread_id}, {"turnId", turn_id}, {"delta", "proofread"}}}
        });
        send(json{
            {"method", "item/completed"},
            {"params", {
                {"threadId", thread_id},
                {"turnId", turn_id},
                {"item", {{"id", "message-item"}, {"type", "agentMessage"}, {"text", std::move(message)}}}
            }}
        });
    }
    send(json{
        {"method", "turn/completed"},
        {"params", {
            {"threadId", thread_id},
            {"turn", {{"id", turn_id}, {"status", "completed"}}}
        }}
    });
}

void handle(const json& request) {
    const auto method = request.at("method").get<std::string>();
    const auto params = request.value("params", json::object());
    add_count(method);
    save_stats();
    // initialized is a notification; it is counted, but must never receive a response.
    if (method == "initialized") return;
    if (!request.contains("id")) return;

    if (method == "initialize") {
        if (test_mode == L"stall_initialize") Sleep(INFINITE);
        if (test_mode == L"delay_initialize") Sleep(1200);
        respond(request, json::object());
    } else if (method == "config/read") {
        respond(request, json{
            {"config", {
                {"mcp_servers", {
                    {"test_server", {{"enabled", true}, {"command", "fake"}}}
                }}
            }}
        });
    } else if (method == "account/read") {
        respond(request, json{{"requiresOpenaiAuth", false}, {"account", {{"type", "chatgpt"}}}});
    } else if (method == "model/list") {
        increment_stat("modelPages");
        save_stats();
        const auto cursor = params.find("cursor");
        if (cursor == params.end()) {
            respond(request, json{
                {"data", json::array({model("gpt-5.9-luna")})},
                {"nextCursor", "next-page"}
            });
        } else {
            respond(request, json{
                {"data", json::array({model("gpt-6.2-luna")})},
                {"nextCursor", nullptr}
            });
        }
    } else if (method == "thread/start") {
        const auto thread_id = "fake-thread-" + std::to_string(++next_thread);
        const auto& config = params.at("config");
        const auto& features = config.at("features");
        const auto& app_default = config.at("apps").at("_default");
        const auto& mcp = config.at("mcp_servers");
        const bool mcp_disabled = mcp.is_object() && mcp.at("test_server").at("enabled") == false;
        const bool safe = params.value("ephemeral", false)
            && params.value("approvalPolicy", std::string{}) == "never"
            && params.value("sandbox", std::string{}) == "read-only"
            && params.value("serviceName", std::string{}) == "codex-language-fix"
            && config.at("model_reasoning_effort") == "low"
            && config.at("service_tier") == "priority"
            && !features.at("apps").get<bool>()
            && !features.at("plugins").get<bool>()
            && !features.at("multi_agent").get<bool>()
            && !features.at("shell_tool").get<bool>()
            && !app_default.at("enabled").get<bool>()
            && !app_default.at("open_world_enabled").get<bool>()
            && !app_default.at("destructive_enabled").get<bool>();
        const bool full_safe = safe && mcp_disabled;
        stats["threadStartSafe"] = full_safe;
        stats["developerPromptSha256"] = clf::sha256(params.at("developerInstructions").get<std::string>());
        stats["allThreadStartsSafe"] = stats["allThreadStartsSafe"].get<bool>() && full_safe;
        respond(request, json{{"thread", {{"id", thread_id}}}});
        if (stall_after_thread_start) {
            stats["stalledAfterThread"] = true;
            save_stats();
            Sleep(INFINITE);
        }
        save_stats();
    } else if (method == "thread/unsubscribe") {
        respond(request, json::object());
    } else if (method == "turn/interrupt") {
        respond(request, json::object());
    } else if (method == "turn/start") {
        const auto turn_id = "fake-turn-" + std::to_string(++next_turn);
        const auto thread_id = params.at("threadId").get<std::string>();
        const auto source = source_from_turn(params);
        increment_stat("turnStarts");
        save_stats();

        if (source == "server request case") {
            send(json{
                {"id", request.at("id")},
                {"method", "approval/request"},
                {"params", {{"threadId", thread_id}, {"turnId", turn_id}}}
            });
            increment_stat("serverRequestsSent");
            save_stats();
        }
        const json start_result{{"turn", {{"id", turn_id}}}};
        if (source == "late previous completion case" && !last_turn_id.empty()) {
            send(json{
                {"method", "turn/completed"},
                {"params", {{"threadId", last_turn_thread_id},
                    {"turn", {{"id", last_turn_id}, {"status", "completed"}}}}}
            });
        }
        if (source == "early notification case") {
            complete_turn(thread_id, turn_id, json{{"corrected_text", source}}.dump(-1, ' ', false));
            last_turn_id = turn_id;
            last_turn_thread_id = thread_id;
            respond(request, start_result);
            return;
        }
        if (source == "late previous completion case") {
            complete_turn(thread_id, turn_id, json{{"corrected_text", source}}.dump(-1, ' ', false));
            last_turn_id = turn_id;
            last_turn_thread_id = thread_id;
            respond(request, start_result);
            return;
        }
        respond(request, start_result);

        if (source == "wait for cancel") return;
        if (source == "tool case") {
            complete_turn(thread_id, turn_id, {}, true);
            last_turn_id = turn_id;
            last_turn_thread_id = thread_id;
            return;
        }

        std::string corrected = source;
        if (source == "Das ist korekt.") corrected = "Das ist korrekt.";
        if (source == "Gleicher Satz. Gleicher Satz.") {
            const auto attempt = repeated_attempts[source]++;
            if (attempt == 0) corrected = "Gleicher Satz.";
        }
        std::string message;
        if (source == "invalid case") message = "not-json";
        else message = json{{"corrected_text", corrected}}.dump(-1, ' ', false);
        complete_turn(thread_id, turn_id, std::move(message));
        last_turn_id = turn_id;
        last_turn_thread_id = thread_id;
    } else {
        respond(request, json::object());
    }
}

} // namespace

int main() {
    const std::wstring command_line = GetCommandLineW();
    wchar_t mode[128]{};
    const DWORD mode_length = GetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", mode, 128);
    if (mode_length > 0) test_mode.assign(mode, mode_length);
    stall_after_thread_start = test_mode == L"stall_after_thread";
    wchar_t sentinel_text[64]{};
    const DWORD sentinel_length = GetEnvironmentVariableW(L"CLF_FAKE_SERVER_SENTINEL_HANDLE", sentinel_text, 64);
    if (sentinel_length > 0 && sentinel_length < 64) {
        wchar_t* parsed_end = nullptr;
        const auto raw_handle = std::wcstoull(sentinel_text, &parsed_end, 10);
        if (parsed_end != sentinel_text && *parsed_end == L'\0' && raw_handle != 0) {
            DWORD flags{};
            stats["sentinelChecked"] = true;
            stats["sentinelInherited"] = GetHandleInformation(
                reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(raw_handle)), &flags) != FALSE;
        }
    }
    stats["processArgsOk"] = command_line.find(L"-c features.plugins=false") != std::wstring::npos
        && command_line.find(L"-c thread_unload_delay_secs=0") != std::wstring::npos
        && command_line.find(L"app-server") != std::wstring::npos;
    save_stats();

    std::string line;
    while (std::getline(std::cin, line)) {
        if (line.empty()) continue;
        try {
            const auto request = json::parse(line);
            if (request.is_object() && request.contains("method") && request["method"].is_string())
                handle(request);
        } catch (...) {
            return 2;
        }
    }
    return 0;
}
