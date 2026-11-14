#include "clf/backend.hpp"
#include "clf/localization.hpp"

#include <windows.h>
#include <nlohmann/json.hpp>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <exception>
#include <iostream>
#include <stop_token>
#include <string>
#include <string_view>
#include <system_error>
#include <thread>
#include <vector>

namespace {
using json = nlohmann::json;
using Clock = std::chrono::steady_clock;
using namespace std::chrono_literals;

struct ProbeFailure {
    std::string case_id;
    std::string check;
};

struct ProbeCase {
    std::string id;
    std::string language;
    std::wstring input;
    std::vector<std::wstring> qualifiers;
    std::vector<std::wstring> retained_phrases;
    int expected_newlines{-1};
    bool expected_emoji{};
};

bool contains(const std::wstring& text, const std::wstring& required) {
    return text.find(required) != std::wstring::npos;
}

int newline_count(const std::wstring& text) {
    return static_cast<int>(std::count(text.begin(), text.end(), L'\n'));
}

std::wstring emoji_case_text() {
    std::wstring text = L"This synthetic update is definetely not finished ";
    text.push_back(static_cast<wchar_t>(0xD83D));
    text.push_back(static_cast<wchar_t>(0xDE00));
    text += L". Keep label EMOJI-PROBE-41.";
    return text;
}

bool has_emoji(const std::wstring& text) {
    for (std::size_t index = 0; index + 1 < text.size(); ++index) {
        if (text[index] == static_cast<wchar_t>(0xD83D)
            && text[index + 1] == static_cast<wchar_t>(0xDE00)) return true;
    }
    return false;
}

std::vector<ProbeCase> corpus() {
    const std::wstring de_url = L"https://example.com/manual?lang=de&part=2";
    const std::wstring en_url = L"https://example.com/setup#step-3";
    return {
        {
            "de_qualifier_phrase", "de",
            L"Der Entwurf ist vielleicht noch nich fertig. Die Kennung MUSTERKLAUSEL-17 bleibt bestehen.",
            {L"vielleicht noch nicht"}, {L"MUSTERKLAUSEL-17"}
        },
        {
            "en_qualifier_phrase", "en",
            L"The report is definately not final yet. Keep token SAMPLE-CLAUSE-23 unchanged.",
            {L"definitely not final"}, {L"SAMPLE-CLAUSE-23"}
        },
        {
            "de_markdown_url", "de",
            L"Bitte prüfe [die quelle](" + de_url + L"); die Aussage ist möglicherweise nicht aktuell.",
            {L"möglicherweise nicht aktuell"}, {de_url}
        },
        {
            "en_markdown_url", "en",
            L"Please read [the setup guide](" + en_url + L"), because the procedure is definately not complete.",
            {L"definitely not complete"}, {en_url}
        },
        {
            "de_multiline", "de",
            L"Hinweis: Der Ablauf ist möglicherweise nicht abgeschloßen.\nStatus: nur intern.\nReferenz: NATIVE-PROBE-31.",
            {L"möglicherweise nicht abgeschlossen"}, {L"NATIVE-PROBE-31"}, 2
        },
        {
            "en_emoji", "en", emoji_case_text(),
            {L"definitely not finished"}, {L"EMOJI-PROBE-41"}, -1, true
        }
    };
}

json run_probe() {
    clf::set_language("en");
    std::atomic<int> cache_hits{};
    std::atomic<bool> cancel_turn_started{};
    clf::CodexClient client([&](std::string_view event) {
        if (event == "correction_cache_hit") cache_hits.fetch_add(1, std::memory_order_relaxed);
        if (event == "turn_started") cancel_turn_started.store(true, std::memory_order_release);
    });

    const auto warmup_start = Clock::now();
    client.warm_up();
    const double warmup_ms = std::chrono::duration<double, std::milli>(Clock::now() - warmup_start).count();
    const auto connection = client.test_connection();
    const auto profile = client.profile();
    if (profile.empty() || connection.profile != profile) throw ProbeFailure{"connection", "profile_unresolved"};

    const auto cases = corpus();
    json observations = json::array();
    std::wstring first_result;
    double first_duration_ms{};
    for (std::size_t index = 0; index < cases.size(); ++index) {
        const auto& item = cases[index];
        const auto started = Clock::now();
        const auto result = client.correct(item.input);
        const double duration_ms = std::chrono::duration<double, std::milli>(Clock::now() - started).count();

        for (const auto& qualifier : item.qualifiers) {
            if (!contains(result.text, qualifier)) throw ProbeFailure{item.id, "qualifier_missing"};
        }
        for (const auto& phrase : item.retained_phrases) {
            if (!contains(result.text, phrase)) throw ProbeFailure{item.id, "phrase_not_preserved"};
        }
        const int actual_newlines = newline_count(result.text);
        if (item.expected_newlines >= 0 && actual_newlines != item.expected_newlines)
            throw ProbeFailure{item.id, "line_structure_changed"};
        const bool emoji_preserved = has_emoji(result.text);
        if (item.expected_emoji && !emoji_preserved) throw ProbeFailure{item.id, "emoji_not_preserved"};

        if (index == 0) {
            first_result = result.text;
            first_duration_ms = duration_ms;
        }
        observations.push_back({
            {"caseId", item.id},
            {"language", item.language},
            {"durationMs", duration_ms},
            {"changeCount", result.change_count},
            {"qualifierChecks", item.qualifiers.size()},
            {"retainedPhraseChecks", item.retained_phrases.size()},
            {"newlineCount", actual_newlines},
            {"emojiPreserved", emoji_preserved}
        });
    }

    const int hits_before_repeat = cache_hits.load(std::memory_order_acquire);
    const auto cache_start = Clock::now();
    const auto repeated = client.correct(cases.front().input);
    const double cache_repeat_ms = std::chrono::duration<double, std::milli>(Clock::now() - cache_start).count();
    const bool cache_exact = repeated.text == first_result;
    const int cache_hits_after_repeat = cache_hits.load(std::memory_order_acquire);
    if (!cache_exact || cache_hits_after_repeat <= hits_before_repeat)
        throw ProbeFailure{"cache_repeat", "cache_hit_not_confirmed"};

    // This synthetic prompt is long enough to keep the generation in flight while
    // cancellation is requested, without using any selected or user-authored text.
    std::wstring cancel_input;
    cancel_input.reserve(1600);
    for (int index = 0; index < 32; ++index)
        cancel_input += L"This synthetic sentence is definitely not final; marker CANCEL-PROBE-9. ";
    std::stop_source cancellation;
    cancel_turn_started.store(false, std::memory_order_release);
    std::atomic<bool> cancel_finished{};
    std::atomic<bool> cancelled{};
    std::atomic<bool> cancel_unexpected{};
    const auto cancel_start = Clock::now();
    std::thread cancel_worker([&] {
        try {
            (void)client.correct(cancel_input, cancellation.get_token());
        } catch (const std::system_error& error) {
            cancelled.store(error.code() == std::make_error_code(std::errc::operation_canceled),
                std::memory_order_release);
            cancel_unexpected.store(!cancelled.load(std::memory_order_acquire), std::memory_order_release);
        } catch (...) {
            cancel_unexpected.store(true, std::memory_order_release);
        }
        cancel_finished.store(true, std::memory_order_release);
    });
    const auto turn_start_deadline = Clock::now() + 30s;
    while (!cancel_turn_started.load(std::memory_order_acquire)
        && !cancel_finished.load(std::memory_order_acquire)
        && Clock::now() < turn_start_deadline) {
        std::this_thread::sleep_for(10ms);
    }
    const bool turn_start_observed = cancel_turn_started.load(std::memory_order_acquire);
    if (turn_start_observed) cancellation.request_stop();
    else if (!cancel_finished.load(std::memory_order_acquire)) cancellation.request_stop();
    cancel_worker.join();
    const double cancel_elapsed_ms = std::chrono::duration<double, std::milli>(Clock::now() - cancel_start).count();
    if (!turn_start_observed) throw ProbeFailure{"cancel_turn", "turn_start_not_observed"};
    if (!cancelled.load(std::memory_order_acquire) || cancel_unexpected.load(std::memory_order_acquire))
        throw ProbeFailure{"cancel_turn", "cancellation_not_confirmed"};

    clf::CodexDiagnostics retention;
    const auto retention_deadline = Clock::now() + 10s;
    do {
        retention = client.diagnostics();
        if (retention.pending_rpc_requests == 0
            && retention.turn_states == 0
            && retention.active_turns == 0
            && retention.pending_turn_starts == 0
            && retention.maintenance_queue_entries == 0
            && retention.prepared_threads == 1) break;
        std::this_thread::sleep_for(25ms);
    } while (Clock::now() < retention_deadline);
    if (retention.pending_rpc_requests != 0 || retention.turn_states != 0
        || retention.active_turns != 0 || retention.pending_turn_starts != 0
        || retention.maintenance_queue_entries != 0 || retention.prepared_threads != 1)
        throw ProbeFailure{"cancel_turn", "runtime_cleanup_not_confirmed"};

    const std::wstring after_cancel_input = L"Das ist korekt: POST-CANCEL-7.";
    const auto after_cancel_start = Clock::now();
    const auto after_cancel = client.correct(after_cancel_input);
    const double after_cancel_ms = std::chrono::duration<double, std::milli>(Clock::now() - after_cancel_start).count();
    if (!contains(after_cancel.text, L"POST-CANCEL-7") || !contains(after_cancel.text, L"Das ist korrekt"))
        throw ProbeFailure{"after_cancel", "correction_failed"};

    client.close();
    return json{
        {"status", "passed"},
        {"profile", profile},
        {"warmupMs", warmup_ms},
        {"connectionTestMs", connection.elapsed_ms},
        {"cases", std::move(observations)},
        {"cacheRepeat", {
            {"caseId", cases.front().id},
            {"durationMs", cache_repeat_ms},
            {"firstDurationMs", first_duration_ms},
            {"resultExact", cache_exact},
            {"cacheHitConfirmed", cache_hits_after_repeat > hits_before_repeat}
        }},
        {"cancellation", {
            {"syntheticTurnStartObserved", turn_start_observed},
            {"cancelConfirmed", cancelled.load(std::memory_order_acquire)},
            {"elapsedMs", cancel_elapsed_ms}
        }},
        {"runtimeRetention", {
            {"activeTurns", retention.active_turns},
            {"turnStates", retention.turn_states},
            {"pendingTurnStarts", retention.pending_turn_starts},
            {"pendingRpcRequests", retention.pending_rpc_requests},
            {"maintenanceQueueEntries", retention.maintenance_queue_entries},
            {"preparedThreads", retention.prepared_threads},
            {"cacheEntries", retention.cache_entries},
            {"cacheUtf16Bytes", retention.cache_utf16_bytes},
            {"lineBufferBytes", retention.line_buffer_bytes},
            {"maxLineBufferBytes", retention.max_line_buffer_bytes},
            {"serverLoadedThreadCountObserved", false}
        }},
        {"afterCancel", {
            {"caseId", "post_cancel_synthetic"},
            {"durationMs", after_cancel_ms},
            {"resultChecksPassed", true}
        }}
    };
}

} // namespace

int wmain() {
    wchar_t opt_in[8]{};
    const DWORD opt_in_length = GetEnvironmentVariableW(L"CLF_RUN_LIVE", opt_in, 8);
    if (opt_in_length != 1 || opt_in[0] != L'1') {
        std::cout << json{{"status", "skipped"}, {"optIn", "CLF_RUN_LIVE=1"}}.dump() << '\n';
        return 77;
    }

    try {
        std::cout << run_probe().dump(2) << '\n';
        return 0;
    } catch (const ProbeFailure& failure) {
        std::cout << json{
            {"status", "failed"},
            {"caseId", failure.case_id},
            {"check", failure.check}
        }.dump(2) << '\n';
        return 1;
    } catch (const clf::AppError& error) {
        std::cout << json{{"status", "error"}, {"appErrorKey", error.key}}.dump(2) << '\n';
        return 1;
    } catch (const std::system_error& error) {
        std::cout << json{
            {"status", "error"},
            {"systemErrorCode", error.code().value()}
        }.dump(2) << '\n';
        return 1;
    } catch (...) {
        std::cout << json{{"status", "error"}, {"errorType", "unclassified"}}.dump(2) << '\n';
        return 1;
    }
}
