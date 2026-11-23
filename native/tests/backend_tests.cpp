#include "clf/backend.hpp"
#include "clf/localization.hpp"

#include <windows.h>
#include <nlohmann/json.hpp>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <stop_token>
#include <stdexcept>
#include <string>
#include <system_error>
#include <thread>
#include <vector>

using json = nlohmann::json;
using namespace std::chrono_literals;

namespace {
const char* current_stage = "startup";

void stage(const char* name) {
    current_stage = name;
    std::cout << "[stage] " << name << std::endl;
}

void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

std::wstring make_test_directory() {
    static std::atomic<unsigned long long> sequence{};
    wchar_t temp[MAX_PATH]{};
    const DWORD length = GetTempPathW(MAX_PATH, temp);
    if (length == 0 || length >= MAX_PATH) throw std::runtime_error("temp-path");
    const auto unique = std::to_wstring(GetCurrentProcessId()) + L"-"
        + std::to_wstring(GetTickCount64()) + L"-" + std::to_wstring(++sequence);
    const auto path = std::filesystem::path(temp) / (L"clf-backend-" + unique);
    if(!std::filesystem::create_directory(path)) throw std::runtime_error("test-directory-already-exists");
    return path.wstring();
}

json read_stats(const std::filesystem::path& directory) {
    std::ifstream file(directory / L"luna-runtime" / L"fake_app_server_stats.json", std::ios::binary);
    if (!file) return json::object();
    try { return json::parse(file); }
    catch (...) { return json::object(); }
}

bool wait_for_stats(const std::filesystem::path& directory,
    const std::function<bool(const json&)>& predicate, std::chrono::milliseconds timeout = 5000ms) {
    const auto end = std::chrono::steady_clock::now() + timeout;
    while (std::chrono::steady_clock::now() < end) {
        const auto stats = read_stats(directory);
        if (predicate(stats)) return true;
        std::this_thread::sleep_for(10ms);
    }
    return predicate(read_stats(directory));
}

bool wait_until(const std::function<bool()>& predicate, std::chrono::milliseconds timeout = 4000ms) {
    const auto end = std::chrono::steady_clock::now() + timeout;
    while (std::chrono::steady_clock::now() < end) {
        if (predicate()) return true;
        std::this_thread::sleep_for(10ms);
    }
    return predicate();
}

int count(const json& stats, const char* key) {
    try { return stats.at("counts").value(key, 0); }
    catch (...) { return 0; }
}

std::wstring utf16_arg(const wchar_t* value) { return value ? std::wstring(value) : std::wstring{}; }

void test_stalled_stdin_cancellation(const std::wstring& executable) {
    const auto directory = make_test_directory();
    const auto directory_path = std::filesystem::path(directory);
    require(SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", L"stall_after_thread") != 0, "set fake-server test mode");
    try {
        clf::CodexClient client(executable, directory);
        client.warm_up();
        require(wait_for_stats(directory_path, [](const json& stats) {
            return stats.value("stalledAfterThread", false);
        }), "fake child stalls after prepared thread");

        std::stop_source cancellation;
        std::atomic<bool> finished{};
        std::atomic<bool> cancelled{};
        std::atomic<bool> unexpected{};
        std::thread writer([&] {
            try {
                (void)client.correct(std::wstring(20'000, L'a'), cancellation.get_token());
            } catch (const std::system_error& error) {
                cancelled.store(error.code() == std::make_error_code(std::errc::operation_canceled));
            } catch (...) {
                unexpected.store(true);
            }
            finished.store(true);
        });
        std::this_thread::sleep_for(150ms);
        cancellation.request_stop();
        if (!wait_until([&] { return finished.load(); })) {
            client.close();
            writer.join();
            throw std::runtime_error("stalled child stdin ignored cancellation");
        }
        writer.join();
        require(cancelled.load() && !unexpected.load(), "stalled stdin cancellation result");
        client.close();
    } catch (...) {
        SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
        std::error_code ignored;
        std::filesystem::remove_all(directory_path, ignored);
        throw;
    }
    SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
    std::error_code ignored;
    std::filesystem::remove_all(directory_path, ignored);
}

void test_close_with_pending_turn(const std::wstring& executable) {
    const auto directory = make_test_directory();
    const auto directory_path = std::filesystem::path(directory);
    try {
        clf::CodexClient client(executable, directory);
        client.warm_up();
        std::atomic<bool> finished{};
        std::thread pending([&] {
            try { (void)client.correct(L"wait for cancel"); }
            catch (...) {}
            finished.store(true);
        });
        require(wait_for_stats(directory_path, [](const json& stats) {
            return count(stats, "turn/start") >= 1;
        }), "pending turn before shutdown");
        client.close();
        require(wait_until([&] { return finished.load(); }), "shutdown releases pending turn");
        pending.join();
    } catch (...) {
        std::error_code ignored;
        std::filesystem::remove_all(directory_path, ignored);
        throw;
    }
    std::error_code ignored;
    std::filesystem::remove_all(directory_path, ignored);
}

void test_close_with_pending_warmup(const std::wstring& executable) {
    const auto directory = make_test_directory();
    const auto directory_path = std::filesystem::path(directory);
    require(SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", L"stall_initialize") != 0,
        "set fake-server initialize stall mode");
    try {
        clf::CodexClient client(executable, directory);
        std::atomic<bool> finished{};
        std::exception_ptr failure;
        std::thread startup([&] {
            try { client.warm_up(); }
            catch (...) { failure = std::current_exception(); }
            finished.store(true);
        });
        const bool initialize_pending = wait_for_stats(directory_path, [](const json& stats) {
            return count(stats, "initialize") >= 1;
        });
        client.close();
        const bool shutdown_released = wait_until([&] { return finished.load(); });
        startup.join();
        SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
        require(initialize_pending, "initialize request pending before close");
        require(shutdown_released, "shutdown releases pending initialize request");
        require(failure != nullptr, "closed initialization reports failure");
    } catch (...) {
        SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
        std::error_code ignored;
        std::filesystem::remove_all(directory_path, ignored);
        throw;
    }
    std::error_code ignored;
    std::filesystem::remove_all(directory_path, ignored);
}

void test_parallel_warmup_and_correction(const std::wstring& executable) {
    const auto directory = make_test_directory();
    const auto directory_path = std::filesystem::path(directory);
    require(SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", L"delay_initialize") != 0,
        "set fake-server initialize delay mode");
    try {
        clf::CodexClient client(executable, directory);
        std::atomic<bool> warmup_finished{};
        std::atomic<bool> correction_started{};
        std::atomic<bool> correction_finished{};
        std::exception_ptr warmup_failure;
        std::exception_ptr correction_failure;
        clf::Correction result;
        std::thread startup([&] {
            try { client.warm_up(); }
            catch (...) { warmup_failure = std::current_exception(); }
            warmup_finished.store(true);
        });
        const bool initialize_pending = wait_for_stats(directory_path, [](const json& stats) {
            return count(stats, "initialize") >= 1;
        });
        if (initialize_pending) {
            std::thread correction([&] {
                correction_started.store(true);
                try { result = client.correct(L"Das ist korekt."); }
                catch (...) { correction_failure = std::current_exception(); }
                correction_finished.store(true);
            });
            const bool started_before_response = wait_until([&] {
                return correction_started.load();
            }, 300ms);
            const bool both_finished = wait_until([&] {
                return warmup_finished.load() && correction_finished.load();
            });
            if (!both_finished) client.close();
            startup.join();
            correction.join();
            SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
            require(started_before_response, "correction overlaps pending initialize response");
            require(both_finished, "parallel warm-up and correction complete");
            require(!warmup_failure && !correction_failure, "parallel startup has no failures");
            require(result.text == L"Das ist korrekt.", "parallel correction result");
            const auto stats = read_stats(directory_path);
            require(count(stats, "initialize") == 1, "parallel startup sends one initialize request");
            require(count(stats, "turn/start") == 1, "parallel startup starts one turn");
            client.close();
        } else {
            client.close();
            startup.join();
            SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
            require(false, "initialize request observed for parallel startup");
        }
    } catch (...) {
        SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
        std::error_code ignored;
        std::filesystem::remove_all(directory_path, ignored);
        throw;
    }
    std::error_code ignored;
    std::filesystem::remove_all(directory_path, ignored);
}

void test_child_handle_whitelist(const std::wstring& executable) {
    const auto directory = make_test_directory();
    const auto directory_path = std::filesystem::path(directory);
    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE};
    HANDLE sentinel = CreateEventW(&security, TRUE, FALSE, nullptr);
    require(sentinel != nullptr, "create inheritable sentinel handle");
    const auto sentinel_value = reinterpret_cast<std::uintptr_t>(sentinel);
    const auto sentinel_text = std::to_wstring(static_cast<unsigned long long>(sentinel_value));
    require(SetEnvironmentVariableW(L"CLF_FAKE_SERVER_SENTINEL_HANDLE", sentinel_text.c_str()) != 0,
        "set fake-server sentinel handle");
    try {
        clf::CodexClient client(executable, directory);
        client.warm_up();
        require(wait_for_stats(directory_path, [](const json& stats) {
            return stats.value("sentinelChecked", false) && stats.value("processArgsOk", false);
        }), "fake server reports sentinel-handle visibility");
        const auto stats = read_stats(directory_path);
        require(!stats.value("sentinelInherited", true), "unrelated inheritable handle excluded from child");
        client.close();
    } catch (...) {
        SetEnvironmentVariableW(L"CLF_FAKE_SERVER_SENTINEL_HANDLE", nullptr);
        CloseHandle(sentinel);
        std::error_code ignored;
        std::filesystem::remove_all(directory_path, ignored);
        throw;
    }
    SetEnvironmentVariableW(L"CLF_FAKE_SERVER_SENTINEL_HANDLE", nullptr);
    CloseHandle(sentinel);
    std::error_code ignored;
    std::filesystem::remove_all(directory_path, ignored);
}

void test_parallel_process_pipe_isolation(const std::wstring& executable) {
    const auto directory_a = make_test_directory();
    const auto directory_b = make_test_directory();
    const auto path_a = std::filesystem::path(directory_a);
    const auto path_b = std::filesystem::path(directory_b);
    require(SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", L"delay_initialize") != 0,
        "set fake-server parallel startup delay mode");
    try {
        clf::CodexClient first(executable, directory_a);
        clf::CodexClient second(executable, directory_b);
        std::exception_ptr first_failure, second_failure;
        std::thread start_first([&] { try { first.warm_up(); } catch (...) { first_failure = std::current_exception(); } });
        std::thread start_second([&] { try { second.warm_up(); } catch (...) { second_failure = std::current_exception(); } });
        start_first.join();
        start_second.join();
        require(!first_failure && !second_failure, "parallel fake app-server startup");

        std::atomic<bool> closed{};
        std::thread close_second([&] {
            second.close();
            closed.store(true);
        });
        const bool second_closed = wait_until([&] { return closed.load(); });
        if (!second_closed) first.close();
        close_second.join();
        require(second_closed, "closing one child does not retain another child's pipe handles");

        const auto result = first.correct(L"Das ist korekt.");
        require(result.text == L"Das ist korrekt.", "remaining child still serves correction");
        first.close();
        SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
    } catch (...) {
        SetEnvironmentVariableW(L"CLF_FAKE_SERVER_MODE", nullptr);
        std::error_code ignored;
        std::filesystem::remove_all(path_a, ignored);
        std::filesystem::remove_all(path_b, ignored);
        throw;
    }
    std::error_code ignored;
    std::filesystem::remove_all(path_a, ignored);
    std::filesystem::remove_all(path_b, ignored);
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    std::error_code executable_path_error;
    const auto executable_path = std::filesystem::absolute(utf16_arg(argv[1]), executable_path_error);
    if (executable_path_error || !std::filesystem::is_regular_file(executable_path, executable_path_error)
        || executable_path_error) {
        std::cerr << "fake server executable path is not a regular file\n";
        return 2;
    }
    const auto executable = executable_path.wstring();
    const auto directory = make_test_directory();
    const auto directory_path = std::filesystem::path(directory);
    try {
        clf::set_language("en");
        clf::CodexClient client(executable, directory);

        stage("main.warm_up");
        client.warm_up();
        require(client.profile() == "gpt-6.2-luna/low/priority/full-v1", "model profile");
        require(wait_for_stats(directory_path, [](const json& stats) {
            return count(stats, "thread/start") >= 1 && stats.value("processArgsOk", false);
        }), "warm-up thread and process-local flags");
        require(read_stats(directory_path).value("developerPromptSha256",std::string{}) ==
            "F16FB7F3AE981813763622B049CEA5B02BCDF7B7BFB69C5601540A99E1A5BBEB",
            "developer prompt matches compiled qualified C# reference bytes");
        stage("main.test_connection");
        const auto health = client.test_connection();
        require(health.profile == "gpt-6.2-luna/low/priority/full-v1", "connection profile check");
        stage("main.connect");
        client.connect();

        stage("main.correct");
        const auto corrected = client.correct(L"Das ist korekt.");
        require(corrected.text == L"Das ist korrekt.", "full-v1 corrected result");
        require(corrected.change_count == 1, "full-v1 change count");
        constexpr int turn_count_after_first = 1;
        require(wait_for_stats(directory_path, [](const json& stats) {
            return count(stats, "turn/start") == turn_count_after_first;
        }), "first turn count");

        stage("main.cache_hit");
        const auto cache_hit = client.correct(L"Das ist korekt.");
        require(cache_hit.text == corrected.text, "cache result");
        require(wait_for_stats(directory_path, [&](const json& stats) {
            return count(stats, "turn/start") == turn_count_after_first;
        }), "profile-bound cache hit");

        stage("main.retry");
        const auto repeated = client.correct(L"Gleicher Satz. Gleicher Satz.");
        require(repeated.text == L"Gleicher Satz. Gleicher Satz.", "repeated sentence retry result");
        require(repeated.change_count == 0, "repeated sentence retry count");
        require(wait_for_stats(directory_path, [](const json& stats) {
            return count(stats, "turn/start") >= 3 && count(stats, "thread/unsubscribe") >= 3;
        }), "retry thread cleanup");

        bool rejected_tool = false;
        stage("main.reject_tool");
        try { (void)client.correct(L"tool case"); }
        catch (const clf::AppError& error) { rejected_tool = error.key == "LunaUnexpectedTool"; }
        require(rejected_tool, "tool item rejection");

        bool rejected_server_request = false;
        stage("main.reject_server_request");
        try { (void)client.correct(L"server request case"); }
        catch (const clf::AppError& error) { rejected_server_request = error.key == "LunaUnexpectedTool"; }
        require(rejected_server_request, "server request id collision rejection");

        bool rejected_invalid = false;
        stage("main.reject_invalid");
        try { (void)client.correct(L"invalid case"); }
        catch (const clf::AppError& error) { rejected_invalid = error.key == "LunaInvalidResponse"; }
        require(rejected_invalid, "invalid response rejection");

        stage("main.early_notification");
        const auto early = client.correct(L"early notification case");
        require(early.text == L"early notification case", "early event before turn/start response");

        stage("main.late_previous_completion");
        const auto after_late_event = client.correct(L"late previous completion case");
        require(after_late_event.text == L"late previous completion case",
            "late prior-turn completion does not replace pending-turn result");

        std::stop_source cancellation;
        std::atomic<bool> cancelled{};
        std::atomic<bool> unexpected_failure{};
        stage("main.cancel_turn");
        std::thread pending([&] {
            try {
                (void)client.correct(L"wait for cancel", cancellation.get_token());
            } catch (const std::system_error& error) {
                cancelled.store(error.code() == std::make_error_code(std::errc::operation_canceled));
            } catch (...) {
                unexpected_failure.store(true);
            }
        });
        require(wait_for_stats(directory_path, [](const json& stats) {
            return count(stats, "turn/start") >= 9;
        }), "pending cancellable turn");
        cancellation.request_stop();
        pending.join();
        require(cancelled.load() && !unexpected_failure.load(), "cancellation result");
        require(wait_for_stats(directory_path, [](const json& stats) {
            return count(stats, "turn/interrupt") >= 1;
        }), "turn interrupt after cancellation");

        const auto final_stats = read_stats(directory_path);
        require(final_stats.value("allThreadStartsSafe", false), "all thread restrictions");
        require(final_stats.value("serverRequestsSent", 0) == 1, "one simulated server request");

        client.close();
        std::error_code ignored;
        std::filesystem::remove_all(directory_path, ignored);
        stage("test.stalled_stdin_cancellation");
        test_stalled_stdin_cancellation(executable);
        stage("test.close_pending_turn");
        test_close_with_pending_turn(executable);
        stage("test.close_pending_warmup");
        test_close_with_pending_warmup(executable);
        stage("test.parallel_warmup_correction");
        test_parallel_warmup_and_correction(executable);
        stage("test.handle_whitelist");
        test_child_handle_whitelist(executable);
        stage("test.parallel_process_isolation");
        test_parallel_process_pipe_isolation(executable);
        stage("complete");
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "backend test failed at [stage] " << current_stage
            << ": " << error.what() << '\n';
        const auto fake_stats = read_stats(directory_path);
        std::cerr << "fake-server-stats=" << fake_stats.dump() << '\n';
        std::error_code ignored;
        std::filesystem::remove_all(directory_path, ignored);
        return 1;
    }
}
