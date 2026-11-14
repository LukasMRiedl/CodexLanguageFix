#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include "clf/core.hpp"

#include <bcrypt.h>
#include <icu.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <climits>
#include <cstdint>
#include <map>
#include <limits>
#include <new>
#include <set>
#include <stdexcept>
#include <thread>
#include <utility>

#pragma comment(lib, "bcrypt.lib")

namespace clf {
namespace {
static_assert(sizeof(UChar) == sizeof(wchar_t), "ICU and Windows UTF-16 code units must match.");

class Regex {
public:
    Regex(std::wstring_view pattern, uint32_t flags = 0) {
        if (pattern.size() > static_cast<std::size_t>((std::numeric_limits<int32_t>::max)())) return;
        UErrorCode status = U_ZERO_ERROR;
        UParseError parse_error{};
        value_ = uregex_open(reinterpret_cast<const UChar*>(pattern.data()),
            static_cast<int32_t>(pattern.size()), flags, &parse_error, &status);
        if (U_FAILURE(status)) value_ = nullptr;
    }
    Regex(const Regex&) = delete;
    Regex& operator=(const Regex&) = delete;
    ~Regex() { if (value_) uregex_close(value_); }
    URegularExpression* get() const { return value_; }
private:
    URegularExpression* value_{};
};

bool find_matches(std::wstring_view text, std::wstring_view pattern,
    std::vector<Span>& output, uint32_t flags = 0) {
    if (text.size() > static_cast<std::size_t>((std::numeric_limits<int32_t>::max)())) return false;
    Regex regex(pattern, flags);
    if (!regex.get()) return false;
    UErrorCode status = U_ZERO_ERROR;
    uregex_setText(regex.get(), reinterpret_cast<const UChar*>(text.data()),
        static_cast<int32_t>(text.size()), &status);
    if (U_FAILURE(status)) return false;
    while (uregex_findNext(regex.get(), &status)) {
        const int32_t start = uregex_start(regex.get(), 0, &status);
        const int32_t end = uregex_end(regex.get(), 0, &status);
        if (U_FAILURE(status) || start < 0 || end < start) return false;
        if (end > start) output.push_back({static_cast<int>(start), static_cast<int>(end - start)});
    }
    return U_SUCCESS(status);
}

bool is_white_space(wchar_t value) {
    return u_isUWhiteSpace(static_cast<UChar32>(static_cast<uint16_t>(value)));
}

bool is_control(wchar_t value) {
    return u_charType(static_cast<UChar32>(static_cast<uint16_t>(value))) == U_CONTROL_CHAR;
}

std::vector<Span> merge_spans(std::vector<Span> spans, int text_length, bool merge_adjacent) {
    for (auto& span : spans) {
        span.start = std::clamp(span.start, 0, text_length);
        span.length = std::clamp(span.length, 0, text_length - span.start);
    }
    std::erase_if(spans, [](const Span& span) { return span.length <= 0; });
    std::sort(spans.begin(), spans.end(), [](const Span& left, const Span& right) {
        if (left.start != right.start) return left.start < right.start;
        return left.length > right.length;
    });
    if (spans.empty()) return {};

    std::vector<Span> merged;
    Span current = spans.front();
    for (std::size_t i = 1; i < spans.size(); ++i) {
        const auto& next = spans[i];
        const bool joins = merge_adjacent ? next.start <= current.end() : next.start < current.end();
        if (joins) {
            current.length = (std::max)(current.end(), next.end()) - current.start;
        } else {
            merged.push_back(current);
            current = next;
        }
    }
    merged.push_back(current);
    return merged;
}

bool is_covered(int index, const std::vector<Span>& spans) {
    return std::any_of(spans.begin(), spans.end(), [index](const Span& span) {
        return index >= span.start && index < span.end();
    });
}

void add_fenced_code_blocks(const std::wstring& text, std::vector<Span>& spans) {
    std::size_t position = 0;
    int fence_start = -1;
    wchar_t fence_character = L'\0';
    int fence_length = 0;
    while (position < text.size()) {
        const std::size_t line_start = position;
        const auto newline = text.find(L'\n', position);
        const std::size_t line_end = newline == std::wstring::npos ? text.size() : newline + 1;
        const std::size_t content_end = newline != std::wstring::npos && newline > line_start
            && text[newline - 1] == L'\r' ? newline - 1
            : newline != std::wstring::npos ? newline : text.size();
        std::size_t leading = 0;
        while (leading < content_end - line_start && leading < 4
            && (text[line_start + leading] == L' ' || text[line_start + leading] == L'\t')) {
            ++leading;
        }
        if (leading <= 3 && line_start + leading < content_end
            && (text[line_start + leading] == L'`' || text[line_start + leading] == L'~')) {
            const wchar_t current_character = text[line_start + leading];
            std::size_t count = 0;
            while (line_start + leading + count < content_end
                && text[line_start + leading + count] == current_character) {
                ++count;
            }
            if (count >= 3) {
                if (fence_start < 0) {
                    fence_start = static_cast<int>(line_start);
                    fence_character = current_character;
                    fence_length = static_cast<int>(count);
                } else if (current_character == fence_character && count >= static_cast<std::size_t>(fence_length)) {
                    spans.push_back({fence_start, static_cast<int>(line_end) - fence_start});
                    fence_start = -1;
                }
            }
        }
        position = line_end;
    }
    if (fence_start >= 0) spans.push_back({fence_start, static_cast<int>(text.size()) - fence_start});
}

void add_inline_code(const std::wstring& text, std::vector<Span>& spans) {
    for (std::size_t index = 0; index < text.size();) {
        if (text[index] != L'`' || is_covered(static_cast<int>(index), spans)) {
            ++index;
            continue;
        }
        std::size_t tick_count = 1;
        while (index + tick_count < text.size() && text[index + tick_count] == L'`') ++tick_count;
        const std::wstring delimiter(tick_count, L'`');
        const auto closing = text.find(delimiter, index + tick_count);
        if (closing != std::wstring::npos
            && text.find(L'\n', index) >= closing + tick_count) {
            spans.push_back({static_cast<int>(index), static_cast<int>(closing + tick_count - index)});
            index = closing + tick_count;
        } else {
            index += tick_count;
        }
    }
}

struct Line {
    int start{};
    std::wstring content;
    std::wstring ending;
    int prefix_length{};
    int trailing_whitespace_length{};
};

std::vector<Line> get_lines(const std::wstring& text) {
    if (text.size() > static_cast<std::size_t>((std::numeric_limits<int32_t>::max)())) {
        throw std::length_error("Text is too large for ICU line parsing.");
    }
    static constexpr std::wstring_view prefix_pattern =
        LR"((?:[^\S\r\n]+|>[^\S\r\n]?|(?:[-+*•◦▪‣⁃]|[0-9]+[.)])[^\S\r\n]+|#{1,6}[^\S\r\n]+|\[[ xX]\][^\S\r\n]+))";
    Regex prefix_regex(prefix_pattern);
    if (!prefix_regex.get()) throw std::runtime_error("ICU could not compile the text prefix expression.");

    std::vector<Line> lines;
    std::size_t start = 0;
    while (true) {
        std::size_t end = start;
        while (end < text.size() && text[end] != L'\r' && text[end] != L'\n') ++end;
        std::size_t ending_length = 0;
        if (end < text.size()) {
            ending_length = text[end] == L'\r' && end + 1 < text.size() && text[end + 1] == L'\n' ? 2 : 1;
        }
        Line line;
        line.start = static_cast<int>(start);
        line.content = text.substr(start, end - start);
        if (ending_length != 0) line.ending = text.substr(end, ending_length);

        int32_t prefix_length = 0;
        UErrorCode status = U_ZERO_ERROR;
        uregex_setText(prefix_regex.get(), reinterpret_cast<const UChar*>(line.content.data()),
            static_cast<int32_t>(line.content.size()), &status);
        if (U_FAILURE(status)) throw std::runtime_error("ICU could not inspect line prefixes.");
        while (prefix_length < static_cast<int32_t>(line.content.size())) {
            status = U_ZERO_ERROR;
            const UBool found = uregex_find(prefix_regex.get(), prefix_length, &status);
            if (U_FAILURE(status)) throw std::runtime_error("ICU failed while inspecting line prefixes.");
            if (!found) break;
            const int32_t match_start = uregex_start(prefix_regex.get(), 0, &status);
            const int32_t match_end = uregex_end(prefix_regex.get(), 0, &status);
            if (U_FAILURE(status) || match_start != prefix_length || match_end <= match_start) break;
            prefix_length = match_end;
        }
        line.prefix_length = prefix_length;
        int trailing = static_cast<int>(line.content.size());
        while (trailing > line.prefix_length && is_white_space(line.content[static_cast<std::size_t>(trailing - 1)])) {
            --trailing;
        }
        line.trailing_whitespace_length = static_cast<int>(line.content.size()) - trailing;
        lines.push_back(std::move(line));
        if (ending_length == 0) return lines;
        start = end + ending_length;
    }
}

std::wstring_view slice(const std::wstring& text, int start, int length) {
    if (start < 0 || length < 0 || static_cast<std::size_t>(start) > text.size()
        || static_cast<std::size_t>(length) > text.size() - static_cast<std::size_t>(start)) return {};
    return std::wstring_view(text).substr(static_cast<std::size_t>(start), static_cast<std::size_t>(length));
}

bool all_whitespace(std::wstring_view text) {
    return std::all_of(text.begin(), text.end(), is_white_space);
}

bool line_is_nonblank(const Line& line) { return !all_whitespace(line.content); }

bool collect_placeholder_matches(std::wstring_view text, std::vector<Span>& matches) {
    static constexpr std::wstring_view placeholder_pattern = LR"(⟦CLF_PROTECTED_[A-F0-9]+_\d{4}⟧)";
    return find_matches(text, placeholder_pattern, matches);
}

std::vector<int> grapheme_boundary_vector(std::wstring_view text) {
    auto result = grapheme_boundaries(text);
    result.push_back(static_cast<int>(text.size()));
    return result;
}

bool has_boundary(const std::vector<int>& boundaries, int value) {
    return std::binary_search(boundaries.begin(), boundaries.end(), value);
}

void sleep_poll(std::chrono::steady_clock::duration remaining) {
    auto requested = std::chrono::duration_cast<std::chrono::milliseconds>(remaining);
    if (std::chrono::duration_cast<std::chrono::steady_clock::duration>(requested) < remaining) {
        requested += std::chrono::milliseconds(1);
    }
    std::this_thread::sleep_for((std::min)(requested, std::chrono::milliseconds(25)));
}

struct FrozenAnchor { Span before; Span after; };

std::vector<Span> structure_ranges(const std::wstring& text) {
    std::vector<Span> ranges;
    for (const auto& line : get_lines(text)) {
        if (line.prefix_length > 0) ranges.push_back({line.start, line.prefix_length});
        const int suffix_length = line.trailing_whitespace_length + static_cast<int>(line.ending.size());
        if (suffix_length > 0) {
            ranges.push_back({line.start + static_cast<int>(line.content.size()) - line.trailing_whitespace_length,
                suffix_length});
        }
    }
    for (std::size_t i = 0; i < text.size(); ++i) {
        if (is_control(text[i]) || text[i] == L'\ufffc' || text[i] == L'\u2028' || text[i] == L'\u2029') {
            ranges.push_back({static_cast<int>(i), 1});
        }
    }
    std::sort(ranges.begin(), ranges.end(), [](const Span& left, const Span& right) {
        return left.start < right.start;
    });
    std::vector<Span> merged;
    for (const auto& span : ranges) {
        if (merged.empty() || span.start >= merged.back().end()) {
            merged.push_back(span);
        } else {
            auto& previous = merged.back();
            previous.length = (std::max)(previous.end(), span.end()) - previous.start;
        }
    }
    return merged;
}

std::optional<std::vector<FrozenAnchor>> map_frozen_ranges(
    const std::wstring& original, const std::wstring& corrected) {
    const auto before_structure = structure_ranges(original);
    const auto after_structure = structure_ranges(corrected);
    if (before_structure.size() != after_structure.size()) return std::nullopt;
    std::vector<FrozenAnchor> anchors;
    anchors.reserve(before_structure.size());
    for (std::size_t i = 0; i < before_structure.size(); ++i) {
        const auto before = before_structure[i];
        const auto after = after_structure[i];
        if (slice(original, before.start, before.length) != slice(corrected, after.start, after.length)) {
            return std::nullopt;
        }
        anchors.push_back({before, after});
    }

    const auto technical = protected_spans(original);
    std::vector<int> earliest(technical.size());
    std::size_t cursor = 0;
    for (std::size_t i = 0; i < technical.size(); ++i) {
        const auto span = technical[i];
        const auto value = slice(original, span.start, span.length);
        const auto found = corrected.find(value, cursor);
        if (found == std::wstring::npos) return std::nullopt;
        earliest[i] = static_cast<int>(found);
        cursor = found + static_cast<std::size_t>(span.length);
    }
    cursor = corrected.size();
    for (std::size_t offset = technical.size(); offset > 0; --offset) {
        const std::size_t i = offset - 1;
        const auto span = technical[i];
        const auto value = slice(original, span.start, span.length);
        if (cursor < value.size()) return std::nullopt;
        const auto found = corrected.rfind(value, cursor - value.size());
        if (found == std::wstring::npos || static_cast<int>(found) != earliest[i]) return std::nullopt;
        anchors.push_back({span, {static_cast<int>(found), span.length}});
        cursor = found;
    }

    std::sort(anchors.begin(), anchors.end(), [](const FrozenAnchor& left, const FrozenAnchor& right) {
        if (left.before.start != right.before.start) return left.before.start < right.before.start;
        return left.before.end() < right.before.end();
    });
    std::vector<FrozenAnchor> merged;
    for (const auto& anchor : anchors) {
        if (merged.empty()) {
            merged.push_back(anchor);
            continue;
        }
        const auto previous = merged.back();
        if (anchor.before.start >= previous.before.end()) {
            if (anchor.after.start < previous.after.end()) return std::nullopt;
            merged.push_back(anchor);
        } else {
            if (anchor.after.start - previous.after.start != anchor.before.start - previous.before.start) {
                return std::nullopt;
            }
            const int length = (std::max)(previous.before.end(), anchor.before.end()) - previous.before.start;
            merged.back() = {{previous.before.start, length}, {previous.after.start, length}};
        }
    }
    const auto before_boundaries = grapheme_boundary_vector(original);
    const auto after_boundaries = grapheme_boundary_vector(corrected);
    for (const auto& anchor : merged) {
        if (!has_boundary(before_boundaries, anchor.before.start)
            || !has_boundary(before_boundaries, anchor.before.end())
            || !has_boundary(after_boundaries, anchor.after.start)
            || !has_boundary(after_boundaries, anchor.after.end())) {
            return std::nullopt;
        }
    }
    return merged;
}

bool add_edit(std::wstring_view original, std::wstring_view corrected, int start,
    std::vector<Edit>& edits) {
    if (original == corrected) return true;
    int prefix = 0;
    while (prefix < static_cast<int>((std::min)(original.size(), corrected.size()))
        && original[static_cast<std::size_t>(prefix)] == corrected[static_cast<std::size_t>(prefix)]) {
        ++prefix;
    }
    const auto original_boundaries = grapheme_boundary_vector(original);
    const auto corrected_boundaries = grapheme_boundary_vector(corrected);
    while (!has_boundary(original_boundaries, prefix) || !has_boundary(corrected_boundaries, prefix)) --prefix;

    int suffix = 0;
    while (suffix < static_cast<int>((std::min)(original.size(), corrected.size())) - prefix
        && original[original.size() - static_cast<std::size_t>(suffix) - 1]
            == corrected[corrected.size() - static_cast<std::size_t>(suffix) - 1]) {
        ++suffix;
    }
    while (!has_boundary(original_boundaries, static_cast<int>(original.size()) - suffix)
        || !has_boundary(corrected_boundaries, static_cast<int>(corrected.size()) - suffix)) {
        --suffix;
    }

    if (static_cast<int>(original.size()) - prefix - suffix == 0
        || static_cast<int>(corrected.size()) - prefix - suffix == 0) {
        if (prefix > 0) {
            int best = -1;
            for (int boundary : original_boundaries) {
                if (boundary < prefix && has_boundary(corrected_boundaries, boundary)) best = boundary;
            }
            if (best >= 0) prefix = best;
        } else if (suffix > 0) {
            const int original_end = static_cast<int>(original.size()) - suffix;
            const int corrected_end = static_cast<int>(corrected.size()) - suffix;
            int anchor_length = INT_MAX;
            for (int boundary : original_boundaries) {
                const int length = boundary - original_end;
                if (length > 0 && has_boundary(corrected_boundaries, corrected_end + length)) {
                    anchor_length = (std::min)(anchor_length, length);
                }
            }
            if (anchor_length != INT_MAX) suffix -= anchor_length;
        }
    }

    edits.push_back({start + prefix,
        std::wstring(original.substr(static_cast<std::size_t>(prefix), original.size() - static_cast<std::size_t>(prefix + suffix))),
        std::wstring(corrected.substr(static_cast<std::size_t>(prefix), corrected.size() - static_cast<std::size_t>(prefix + suffix)))});
    return true;
}

int occurrence_count(std::wstring_view text, std::wstring_view value) {
    int count = 0;
    for (std::size_t start = 0; (start = text.find(value, start)) != std::wstring_view::npos; start += value.size()) ++count;
    return count;
}

std::string state_status(SelectionCheck check) {
    switch (check) {
    case SelectionCheck::exact: return "ok";
    case SelectionCheck::count_mismatch: return "selection_count_mismatch";
    case SelectionCheck::text_mismatch: return "selection_text_mismatch";
    case SelectionCheck::editor_changed: return "invalid_identity_after_selection";
    case SelectionCheck::source_changed: return "before_mismatch_after_selection";
    }
    throw std::invalid_argument("Unknown selection check.");
}
}

std::vector<Span> protected_spans(const std::wstring& text) {
    if (text.empty()) return {};
    if (text.size() > static_cast<std::size_t>(INT_MAX)) {
        throw std::length_error("Text exceeds the supported UTF-16 offset range.");
    }
    std::vector<Span> spans;
    add_fenced_code_blocks(text, spans);
    add_inline_code(text, spans);
    static constexpr std::array<std::wstring_view, 8> patterns = {
        LR"((?<=\]\()[^)\r\n]+(?=\)))",
        LR"(\b(?:https?://|www\.)[^\s<>()]+)",
        LR"(\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b)",
        LR"((?<!\w)(?:[A-Za-z]:\\|\\\\)[^\s<>"']+)",
        LR"((?<!\w)/(?:[^\s/<>"']+/)+[^\s<>"']*)",
        LR"((?<![\w-])--?[A-Za-z0-9][\w-]*(?:=[^\s]+)?)",
        LR"(\b(?:[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+|[a-z][a-z0-9]+(?:[A-Z][A-Za-z0-9]*)+|[A-Z][a-z0-9]+(?:[A-Z][A-Za-z0-9]*)+)\b)",
        LR"(\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b)"
    };
    for (std::size_t i = 0; i < patterns.size(); ++i) {
        const uint32_t flags = (i == 1 || i == 2) ? UREGEX_CASE_INSENSITIVE : 0;
        if (!find_matches(text, patterns[i], spans, flags)) return {{0, static_cast<int>(text.size())}};
    }
    return merge_spans(std::move(spans), static_cast<int>(text.size()), true);
}

ProtectedText ProtectedText::create(const std::wstring& original) {
    std::vector<Span> spans = protected_spans(original);
    std::vector<Span> placeholder_matches;
    if (!collect_placeholder_matches(original, placeholder_matches)) {
        spans = original.empty() ? std::vector<Span>{} : std::vector<Span>{{0, static_cast<int>(original.size())}};
    } else {
        spans.insert(spans.end(), placeholder_matches.begin(), placeholder_matches.end());
        spans = merge_spans(std::move(spans), static_cast<int>(original.size()), true);
    }
    if (spans.empty()) return {original, {}};

    auto nonce = [] {
        std::array<UCHAR, 8> bytes{};
        const NTSTATUS status = BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(bytes.size()),
            BCRYPT_USE_SYSTEM_PREFERRED_RNG);
        if (status < 0) throw std::runtime_error("BCryptGenRandom failed for protected text.");
        static constexpr wchar_t hex[] = L"0123456789ABCDEF";
        std::wstring value;
        value.reserve(bytes.size() * 2);
        for (const auto byte : bytes) {
            value.push_back(hex[byte >> 4]);
            value.push_back(hex[byte & 0x0F]);
        }
        return value;
    };
    std::wstring nonce_value;
    do { nonce_value = nonce(); }
    while (original.find(L"CLF_PROTECTED_" + nonce_value + L"_") != std::wstring::npos);

    ProtectedText result;
    result.text.reserve(original.size());
    std::size_t cursor = 0;
    result.entries.reserve(spans.size());
    for (std::size_t i = 0; i < spans.size(); ++i) {
        const auto span = spans[i];
        result.text.append(original, cursor, static_cast<std::size_t>(span.start) - cursor);
        std::wstring ordinal = std::to_wstring(i + 1);
        if (ordinal.size() < 4) ordinal.insert(ordinal.begin(), 4 - ordinal.size(), L'0');
        const std::wstring placeholder = L"⟦CLF_PROTECTED_" + nonce_value + L"_" + ordinal + L"⟧";
        result.text.append(placeholder);
        result.entries.emplace_back(placeholder,
            original.substr(static_cast<std::size_t>(span.start), static_cast<std::size_t>(span.length)));
        cursor = static_cast<std::size_t>(span.end());
    }
    result.text.append(original, cursor, original.size() - cursor);
    return result;
}

std::optional<std::wstring> ProtectedText::restore(const std::wstring& corrected) const {
    std::vector<Span> matches;
    if (!collect_placeholder_matches(corrected, matches) || matches.size() != entries.size()) return std::nullopt;
    for (std::size_t i = 0; i < entries.size(); ++i) {
        const auto match = matches[i];
        if (slice(corrected, match.start, match.length) != entries[i].first) return std::nullopt;
    }
    std::wstring value = corrected;
    for (const auto& entry : entries) {
        if (occurrence_count(value, entry.first) != 1) return std::nullopt;
        std::size_t start = 0;
        while ((start = value.find(entry.first, start)) != std::wstring::npos) {
            value.replace(start, entry.first.size(), entry.second);
            start += entry.second.size();
        }
    }
    return value;
}

bool structure_preserved(const std::wstring& original, const std::wstring& corrected) {
    const auto source = get_lines(original);
    const auto target = get_lines(corrected);
    if (source.size() != target.size()) return false;
    for (std::size_t i = 0; i < source.size(); ++i) {
        const auto& before = source[i];
        const auto& after = target[i];
        if (before.ending != after.ending
            || slice(before.content, 0, before.prefix_length) != slice(after.content, 0, after.prefix_length)
            || slice(before.content, static_cast<int>(before.content.size()) - before.trailing_whitespace_length,
                before.trailing_whitespace_length)
                != slice(after.content, static_cast<int>(after.content.size()) - after.trailing_whitespace_length,
                    after.trailing_whitespace_length)) {
            return false;
        }
        const int before_body_length = (std::max)(0,
            static_cast<int>(before.content.size()) - before.prefix_length - before.trailing_whitespace_length);
        const int after_body_length = (std::max)(0,
            static_cast<int>(after.content.size()) - after.prefix_length - after.trailing_whitespace_length);
        const auto before_body = slice(before.content, before.prefix_length, before_body_length);
        const auto after_body = slice(after.content, after.prefix_length, after_body_length);
        if (all_whitespace(before_body) != all_whitespace(after_body)) return false;
    }
    return true;
}

std::optional<std::wstring> restore_layout(const std::wstring& original, const std::wstring& corrected) {
    const auto source = get_lines(original);
    const auto target_all = get_lines(corrected);
    std::vector<const Line*> target;
    for (const auto& line : target_all) if (line_is_nonblank(line)) target.push_back(&line);
    const auto source_count = std::count_if(source.begin(), source.end(), line_is_nonblank);
    if (source_count != static_cast<int>(target.size())) return std::nullopt;

    std::wstring value;
    value.reserve((std::max)(original.size(), corrected.size()));
    std::size_t target_index = 0;
    for (const auto& before : source) {
        if (!line_is_nonblank(before)) {
            value.append(before.content);
            value.append(before.ending);
            continue;
        }
        const Line& after = *target[target_index++];
        std::wstring before_markers;
        std::wstring after_markers;
        for (wchar_t ch : slice(before.content, 0, before.prefix_length)) if (!is_white_space(ch)) before_markers.push_back(ch);
        for (wchar_t ch : slice(after.content, 0, after.prefix_length)) if (!is_white_space(ch)) after_markers.push_back(ch);
        if (before_markers != after_markers) return std::nullopt;

        value.append(slice(before.content, 0, before.prefix_length));
        const int after_body_length = (std::max)(0,
            static_cast<int>(after.content.size()) - after.prefix_length - after.trailing_whitespace_length);
        value.append(slice(after.content, after.prefix_length, after_body_length));
        value.append(slice(before.content, static_cast<int>(before.content.size()) - before.trailing_whitespace_length,
            before.trailing_whitespace_length));
        value.append(before.ending);
    }
    if (!structure_preserved(original, value)) return std::nullopt;
    return value;
}

bool repeated_sentences_preserved(const std::wstring& original, const std::wstring& corrected) {
    static constexpr std::wstring_view boundaries_pattern = LR"((?<=[.!?])\s+)";
    auto counts = [&](const std::wstring& text, std::map<std::wstring, int>& result) {
        std::vector<Span> breaks;
        if (!find_matches(text, boundaries_pattern, breaks)) return false;
        std::size_t start = 0;
        for (const auto& boundary : breaks) {
            auto sentence = std::wstring_view(text).substr(start, static_cast<std::size_t>(boundary.start) - start);
            while (!sentence.empty() && is_white_space(sentence.front())) sentence.remove_prefix(1);
            while (!sentence.empty() && is_white_space(sentence.back())) sentence.remove_suffix(1);
            if (!sentence.empty()) ++result[std::wstring(sentence)];
            start = static_cast<std::size_t>(boundary.end());
        }
        auto sentence = std::wstring_view(text).substr(start);
        while (!sentence.empty() && is_white_space(sentence.front())) sentence.remove_prefix(1);
        while (!sentence.empty() && is_white_space(sentence.back())) sentence.remove_suffix(1);
        if (!sentence.empty()) ++result[std::wstring(sentence)];
        return true;
    };
    std::map<std::wstring, int> before, after;
    if (!counts(original, before) || !counts(corrected, after)) return false;
    for (const auto& [sentence, count] : before) {
        if (count > 1) {
            const auto found = after.find(sentence);
            if (found != after.end() && found->second < count) return false;
        }
    }
    return true;
}

std::optional<EditPlan> make_edit_plan(const std::wstring& original, const std::wstring& corrected) {
    if (!structure_preserved(original, corrected)) return std::nullopt;
    if (original == corrected) return EditPlan{original, corrected, {}};
    const auto anchors = map_frozen_ranges(original, corrected);
    if (!anchors) return std::nullopt;
    std::vector<Edit> edits;
    int before_start = 0;
    int after_start = 0;
    for (std::size_t i = 0; i <= anchors->size(); ++i) {
        const int before_end = i == anchors->size() ? static_cast<int>(original.size()) : (*anchors)[i].before.start;
        const int after_end = i == anchors->size() ? static_cast<int>(corrected.size()) : (*anchors)[i].after.start;
        if (!add_edit(slice(original, before_start, before_end - before_start),
            slice(corrected, after_start, after_end - after_start), before_start, edits)) return std::nullopt;
        if (i == anchors->size()) break;
        const auto& anchor = (*anchors)[i];
        if (slice(original, anchor.before.start, anchor.before.length)
            != slice(corrected, anchor.after.start, anchor.after.length)) return std::nullopt;
        before_start = anchor.before.end();
        after_start = anchor.after.end();
    }
    std::sort(edits.begin(), edits.end(), [](const Edit& left, const Edit& right) { return left.start > right.start; });
    std::wstring reconstructed = original;
    for (const auto& edit : edits) {
        if (edit.start < 0 || static_cast<std::size_t>(edit.start) > reconstructed.size()
            || edit.original.size() > reconstructed.size() - static_cast<std::size_t>(edit.start)) return std::nullopt;
        reconstructed.replace(static_cast<std::size_t>(edit.start), edit.original.size(), edit.replacement);
    }
    if (reconstructed != corrected) return std::nullopt;
    return EditPlan{original, corrected, std::move(edits)};
}

EditPlan reverse_plan(const EditPlan& plan) {
    std::vector<Edit> ascending = plan.edits;
    std::sort(ascending.begin(), ascending.end(), [](const Edit& left, const Edit& right) { return left.start < right.start; });
    std::vector<Edit> reversed;
    reversed.reserve(ascending.size());
    int offset = 0;
    for (const auto& edit : ascending) {
        reversed.push_back({edit.start + offset, edit.replacement, edit.original});
        offset += static_cast<int>(edit.replacement.size()) - static_cast<int>(edit.original.size());
    }
    std::sort(reversed.begin(), reversed.end(), [](const Edit& left, const Edit& right) { return left.start > right.start; });
    return {plan.corrected, plan.original, std::move(reversed)};
}

std::string selection_status(SelectionCheck check) { return state_status(check); }

SelectionCheck wait_for_selection_exact(const std::function<SelectionCheck()>& check) {
    const auto started = std::chrono::steady_clock::now();
    SelectionCheck result = check();
    while (result == SelectionCheck::count_mismatch || result == SelectionCheck::text_mismatch) {
        const auto elapsed = std::chrono::steady_clock::now() - started;
        const auto remaining = std::chrono::milliseconds(500) - elapsed;
        if (remaining <= std::chrono::steady_clock::duration::zero()) return result;
        sleep_poll(remaining);
        if (std::chrono::steady_clock::now() - started >= std::chrono::milliseconds(500)) return result;
        result = check();
    }
    return result;
}

WriteCheck wait_for_exact(const std::wstring& expected,
    const std::function<std::optional<std::wstring>()>& read_text,
    const std::function<bool()>& is_editor_current) {
    const auto started = std::chrono::steady_clock::now();
    while (true) {
        if (!is_editor_current()) return WriteCheck::editor_changed;
        const auto actual = read_text();
        if (!is_editor_current()) return WriteCheck::editor_changed;
        if (actual && *actual == expected) return WriteCheck::exact;
        const auto elapsed = std::chrono::steady_clock::now() - started;
        const auto remaining = std::chrono::seconds(2) - elapsed;
        if (remaining <= std::chrono::steady_clock::duration::zero()) return WriteCheck::text_mismatch;
        sleep_poll(remaining);
        if (std::chrono::steady_clock::now() - started >= std::chrono::seconds(2)) return WriteCheck::text_mismatch;
    }
}

WriteResult apply_edit_plan(const EditPlan& plan, EditTarget& target) {
    std::wstring expected = plan.original;
    std::vector<Edit> applied;
    std::optional<Edit> pending;
    std::optional<std::wstring> pending_after;
    bool attempted_write = false;

    auto recover = [&](std::string status) -> WriteResult {
        try {
            if (pending && target.is_current_editor() && target.read() == pending_after) {
                applied.push_back(*pending);
                expected = *pending_after;
            }
            if (target.is_current_editor() && target.read() == std::optional<std::wstring>(expected)) {
                for (auto it = applied.rbegin(); it != applied.rend(); ++it) {
                    if (!target.is_current_editor() || target.read() != std::optional<std::wstring>(expected)) break;
                    std::wstring previous = expected;
                    if (it->start < 0 || static_cast<std::size_t>(it->start) > previous.size()
                        || it->replacement.size() > previous.size() - static_cast<std::size_t>(it->start)) break;
                    previous.replace(static_cast<std::size_t>(it->start), it->replacement.size(), it->original);
                    (void)target.replace(it->start, it->replacement, it->original);
                    if (target.read() != std::optional<std::wstring>(previous)) break;
                    expected = std::move(previous);
                }
            }
            auto observed = target.read();
            const bool unchanged = !attempted_write || observed == std::optional<std::wstring>(plan.original);
            return {unchanged ? WriteState::unchanged : WriteState::partially_applied,
                plan.original, std::move(observed), static_cast<int>(applied.size()), std::move(status)};
        } catch (const std::bad_alloc&) {
            throw;
        } catch (...) {
            return {attempted_write ? WriteState::partially_applied : WriteState::unchanged,
                plan.original, std::nullopt, static_cast<int>(applied.size()), std::move(status)};
        }
    };

    try {
        if (!target.is_current_editor() || target.read() != std::optional<std::wstring>(expected)
            || std::any_of(plan.edits.begin(), plan.edits.end(), [&](const Edit& edit) {
                return !target.can_replace(edit.start, edit.original);
            })) return recover("selection_preflight_failed");

        for (const auto& edit : plan.edits) {
            if (!target.is_current_editor() || target.read() != std::optional<std::wstring>(expected)) {
                return recover("editor_changed");
            }
            if (edit.start < 0 || static_cast<std::size_t>(edit.start) > expected.size()
                || edit.original.size() > expected.size() - static_cast<std::size_t>(edit.start)) {
                return recover("selection_preflight_failed");
            }
            std::wstring next = expected;
            next.replace(static_cast<std::size_t>(edit.start), edit.original.size(), edit.replacement);
            pending = edit;
            pending_after = next;
            attempted_write = true;
            const bool succeeded = target.replace(edit.start, edit.original, edit.replacement);
            const auto observed = target.read();
            if (observed == std::optional<std::wstring>(next)) {
                applied.push_back(edit);
                expected = std::move(next);
                pending.reset();
                pending_after.reset();
            }
            if (!succeeded || observed != std::optional<std::wstring>(pending_after.value_or(expected))) {
                return recover("write_not_verified");
            }
        }
        if (!target.is_current_editor() || target.read() != std::optional<std::wstring>(plan.corrected)) {
            return recover("final_text_mismatch");
        }
        return {WriteState::applied, plan.original, plan.corrected, static_cast<int>(applied.size()), "ok_verified"};
    } catch (const std::bad_alloc&) {
        throw;
    } catch (...) {
        return recover("write_exception");
    }
}
}
