#pragma once

#include "clf/core.hpp"
#include <nlohmann/json.hpp>

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace clf::test {
namespace detail {

inline constexpr int warmup_iterations = 64;
inline constexpr int measured_iterations = 30;
inline constexpr std::uint64_t checksum_offset = 14695981039346656037ULL;
inline constexpr std::uint64_t checksum_prime = 1099511628211ULL;

struct Consumption {
    std::uint64_t checksum{};
    std::uint64_t result_count{};
};

inline std::uint64_t mix(std::uint64_t checksum, std::uint64_t value) {
    return (checksum ^ value) * checksum_prime;
}

template <class Operation, class Consume>
nlohmann::json measure(std::string_view id, Operation&& operation, Consume&& consume) {
    using Clock = std::chrono::steady_clock;
    std::vector<double> samples;
    samples.reserve(measured_iterations);
    auto checksum = checksum_offset;
    std::uint64_t result_count = 0;

    for (int iteration = 0; iteration < warmup_iterations + measured_iterations; ++iteration) {
        const auto started = Clock::now();
        auto result = operation();
        const auto stopped = Clock::now();

        const auto consumed = consume(result);
        checksum = mix(checksum, consumed.checksum);
        result_count = consumed.result_count;
        if (iteration >= warmup_iterations) {
            samples.push_back(std::chrono::duration<double, std::micro>(stopped - started).count());
        }
    }

    std::sort(samples.begin(), samples.end());
    const auto middle = samples.size() / 2;
    const double median_us = (samples[middle - 1] + samples[middle]) / 2.0;
    const auto p95_index = (samples.size() * 95 + 99) / 100 - 1;

    nlohmann::json result;
    result["id"] = std::string(id);
    result["medianUs"] = median_us;
    result["p95Us"] = samples[p95_index];
    result["maxUs"] = samples.back();
    result["resultCount"] = result_count;
    result["checksum"] = checksum;
    return result;
}

template <class Edits>
inline Consumption consume_edits(const Edits& edits, std::uint64_t checksum) {
    checksum = mix(checksum, edits.size());
    for (const auto& edit : edits) {
        checksum = mix(checksum, static_cast<std::uint64_t>(edit.start));
        checksum = mix(checksum, edit.original.size());
        checksum = mix(checksum, edit.replacement.size());
    }
    return {checksum, edits.size()};
}

struct ProtectedResult {
    ProtectedText protected_text;
    std::optional<std::wstring> restored;
};

struct LayoutResult {
    bool structure_preserved{};
    std::optional<std::wstring> restored;
};

struct EditPlanResult {
    std::optional<EditPlan> plan;
    std::optional<EditPlan> reverse;
};

} // namespace detail

inline nlohmann::json Run(const std::wstring& original, const std::wstring& corrected) {
    using detail::Consumption;
    using detail::measure;
    using detail::mix;

    nlohmann::json operations = nlohmann::json::array();
    operations.push_back(measure("protected_spans",
        [&] { return protected_spans(original); },
        [](const auto& spans) {
            auto checksum = mix(detail::checksum_offset, spans.size());
            for (const auto& span : spans) {
                checksum = mix(checksum, static_cast<std::uint64_t>(span.start));
                checksum = mix(checksum, static_cast<std::uint64_t>(span.length));
            }
            return Consumption{checksum, spans.size()};
        }));

    operations.push_back(measure("protected_text_self_restore",
        [&] {
            auto protected_text = ProtectedText::create(original);
            auto restored = protected_text.restore(protected_text.text);
            return detail::ProtectedResult{std::move(protected_text), std::move(restored)};
        },
        [&](const detail::ProtectedResult& result) {
            const bool self_restore = result.restored && *result.restored == original;
            auto checksum = mix(detail::checksum_offset, result.protected_text.text.size());
            checksum = mix(checksum, result.restored ? result.restored->size() : 0);
            checksum = mix(checksum, self_restore ? 1 : 0);
            return Consumption{checksum, self_restore ? 1U : 0U};
        }));

    operations.push_back(measure("structure_and_layout",
        [&] {
            const bool preserved = structure_preserved(original, corrected);
            auto restored = restore_layout(original, corrected);
            return detail::LayoutResult{preserved, std::move(restored)};
        },
        [](const detail::LayoutResult& result) {
            auto checksum = mix(detail::checksum_offset, result.structure_preserved ? 1 : 0);
            checksum = mix(checksum, result.restored ? 1 : 0);
            checksum = mix(checksum, result.restored ? result.restored->size() : 0);
            return Consumption{checksum, result.restored ? 1U : 0U};
        }));

    operations.push_back(measure("edit_and_reverse_plan",
        [&] {
            auto plan = make_edit_plan(original, corrected);
            std::optional<EditPlan> reverse;
            if (plan) reverse = reverse_plan(*plan);
            return detail::EditPlanResult{std::move(plan), std::move(reverse)};
        },
        [](const detail::EditPlanResult& result) {
            auto checksum = mix(detail::checksum_offset, result.plan ? 1 : 0);
            std::uint64_t count = 0;
            if (result.plan) {
                const auto consumed = detail::consume_edits(result.plan->edits, checksum);
                checksum = consumed.checksum;
                count += consumed.result_count;
            }
            checksum = mix(checksum, result.reverse ? 1 : 0);
            if (result.reverse) {
                const auto consumed = detail::consume_edits(result.reverse->edits, checksum);
                checksum = consumed.checksum;
                count += consumed.result_count;
            }
            return Consumption{checksum, count};
        }));

    operations.push_back(measure("repeated_sentence_check",
        [&] { return repeated_sentences_preserved(original, corrected); },
        [](bool preserved) {
            return Consumption{mix(detail::checksum_offset, preserved ? 1 : 0), 1};
        }));

    operations.push_back(measure("grapheme_boundaries",
        [&] { return grapheme_boundaries(original); },
        [](const auto& boundaries) {
            auto checksum = mix(detail::checksum_offset, boundaries.size());
            for (const int boundary : boundaries) {
                checksum = mix(checksum, static_cast<std::uint64_t>(boundary));
            }
            return Consumption{checksum, boundaries.size()};
        }));

    std::string profile = "model";
    profile.push_back('\0');
    profile += "low";
    profile.push_back('\0');
    profile += "priority";
    profile.push_back('\0');
    profile += "test";
    profile.push_back('\0');
    operations.push_back(measure("cache_key",
        [&] { return sha256(profile + utf8(original)); },
        [](const std::string& key) {
            auto checksum = mix(detail::checksum_offset, key.size());
            if (!key.empty()) {
                checksum = mix(checksum, static_cast<unsigned char>(key.front()));
                checksum = mix(checksum, static_cast<unsigned char>(key.back()));
            }
            return Consumption{checksum, key.size()};
        }));

    return {
        {"schema", 1},
        {"scope", "pure_core_operations_only"},
        {"releasePass", false},
        {"originalUtf16Units", original.size()},
        {"correctedUtf16Units", corrected.size()},
        {"warmupIterations", detail::warmup_iterations},
        {"measuredIterations", detail::measured_iterations},
        {"operations", std::move(operations)}
    };
}

} // namespace clf::test
