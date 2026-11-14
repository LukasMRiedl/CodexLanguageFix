#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <bcrypt.h>
#include <icu.h>

#include "clf/core.hpp"

#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

#pragma comment(lib, "bcrypt.lib")

namespace clf {
namespace {
static_assert(sizeof(wchar_t) == 2, "The native text core requires Windows UTF-16 wchar_t.");

int checked_int_size(std::size_t size) {
    if (size > static_cast<std::size_t>((std::numeric_limits<int>::max)())) {
        throw std::length_error("Text is too large for the Windows conversion API.");
    }
    return static_cast<int>(size);
}

void check_ntstatus(NTSTATUS status, const char* operation) {
    if (status < 0) {
        throw std::runtime_error(operation);
    }
}

struct AlgorithmHandle {
    BCRYPT_ALG_HANDLE value{};
    ~AlgorithmHandle() { if (value) BCryptCloseAlgorithmProvider(value, 0); }
};

struct HashHandle {
    BCRYPT_HASH_HANDLE value{};
    ~HashHandle() { if (value) BCryptDestroyHash(value); }
};

enum class GraphemeType {
    other, cr, lf, control, extend, zwj, regional_indicator, prepend, spacing_mark,
    l, v, t, lv, lvt, extended_pictograph
};

GraphemeType grapheme_type(UChar32 code_point) {
    switch (u_getIntPropertyValue(code_point, UCHAR_GRAPHEME_CLUSTER_BREAK)) {
    case U_GCB_CONTROL: return GraphemeType::control;
    case U_GCB_CR: return GraphemeType::cr;
    case U_GCB_EXTEND:
    case U_GCB_E_MODIFIER: return GraphemeType::extend;
    case U_GCB_L: return GraphemeType::l;
    case U_GCB_LF: return GraphemeType::lf;
    case U_GCB_LV: return GraphemeType::lv;
    case U_GCB_LVT: return GraphemeType::lvt;
    case U_GCB_T: return GraphemeType::t;
    case U_GCB_V: return GraphemeType::v;
    case U_GCB_SPACING_MARK: return GraphemeType::spacing_mark;
    case U_GCB_PREPEND: return GraphemeType::prepend;
    case U_GCB_REGIONAL_INDICATOR: return GraphemeType::regional_indicator;
    case U_GCB_E_BASE:
    case U_GCB_E_BASE_GAZ:
    case U_GCB_GLUE_AFTER_ZWJ: return GraphemeType::extended_pictograph;
    case U_GCB_ZWJ: return GraphemeType::zwj;
    default:
        return u_hasBinaryProperty(code_point, UCHAR_EXTENDED_PICTOGRAPHIC)
            ? GraphemeType::extended_pictograph : GraphemeType::other;
    }
}

struct GraphemeProcessor {
    std::wstring_view input;
    int offset{};
    int current_length{};
    GraphemeType current_type{GraphemeType::other};

    void move_next() {
        offset += current_length;
        current_length = 0;
        if (offset >= static_cast<int>(input.size())) {
            current_type = GraphemeType::other;
            return;
        }

        const auto first = static_cast<std::uint16_t>(input[static_cast<std::size_t>(offset)]);
        UChar32 code_point = first;
        if (first >= 0xD800 && first <= 0xDBFF && offset + 1 < static_cast<int>(input.size())) {
            const auto second = static_cast<std::uint16_t>(input[static_cast<std::size_t>(offset + 1)]);
            if (second >= 0xDC00 && second <= 0xDFFF) {
                code_point = 0x10000 + ((first - 0xD800) << 10) + (second - 0xDC00);
                current_length = 2;
            }
        }
        if (current_length == 0) {
            current_length = 1;
            if (first >= 0xD800 && first <= 0xDFFF) code_point = 0xFFFD;
        }
        current_type = grapheme_type(code_point);
    }
};

int next_grapheme_length(std::wstring_view input) {
    if (input.empty()) return 0;
    GraphemeProcessor processor{input};
    processor.move_next();

    // .NET 8's StringInfo follows UAX #29 revision 35. Preserve its exact
    // boundary rules while taking the character properties from Windows ICU.
    while (processor.current_type == GraphemeType::prepend) processor.move_next();
    if (processor.offset > 0 && (processor.current_type == GraphemeType::control
            || processor.current_type == GraphemeType::cr || processor.current_type == GraphemeType::lf)) {
        return processor.offset;
    }

    GraphemeType previous = processor.current_type;
    processor.move_next();
    switch (previous) {
    case GraphemeType::cr:
        if (processor.current_type != GraphemeType::lf) return processor.offset;
        processor.move_next();
        return processor.offset;
    case GraphemeType::control:
    case GraphemeType::lf:
        return processor.offset;
    default:
        break;
    }

    // Hangul syllable composition, rules GB6-GB8.
    while (true) {
        if (previous == GraphemeType::l) {
            while (processor.current_type == GraphemeType::l) processor.move_next();
            if (processor.current_type == GraphemeType::v) {
                processor.move_next();
                previous = GraphemeType::v;
                continue;
            }
            if (processor.current_type == GraphemeType::lv) {
                processor.move_next();
                previous = GraphemeType::lv;
                continue;
            }
            if (processor.current_type == GraphemeType::lvt) {
                processor.move_next();
                previous = GraphemeType::lvt;
                continue;
            }
            break;
        }
        if (previous == GraphemeType::lv || previous == GraphemeType::v) {
            if (processor.current_type == GraphemeType::v) {
                processor.move_next();
                previous = GraphemeType::v;
                continue;
            }
            if (processor.current_type == GraphemeType::t) {
                processor.move_next();
                previous = GraphemeType::t;
                continue;
            }
            break;
        }
        if (previous == GraphemeType::lvt || previous == GraphemeType::t) {
            if (processor.current_type == GraphemeType::t) {
                processor.move_next();
                previous = GraphemeType::t;
                continue;
            }
        }
        break;
    }

    // Extended pictographs, rules GB11 and GB9.
    while (previous == GraphemeType::extended_pictograph) {
        while (processor.current_type == GraphemeType::extend) processor.move_next();
        if (processor.current_type != GraphemeType::zwj) break;
        processor.move_next();
        if (processor.current_type != GraphemeType::extended_pictograph) break;
        processor.move_next();
    }

    if (previous == GraphemeType::regional_indicator
        && processor.current_type == GraphemeType::regional_indicator) {
        processor.move_next();
    }

    // Rules GB9 and GB9a, including modifiers classified as Extend.
    while (processor.current_type == GraphemeType::extend || processor.current_type == GraphemeType::zwj
        || processor.current_type == GraphemeType::spacing_mark) {
        processor.move_next();
    }
    return processor.offset;
}
}

bool valid_utf16(std::wstring_view text) {
    for (std::size_t i = 0; i < text.size(); ++i) {
        const auto unit = static_cast<std::uint16_t>(text[i]);
        if (unit >= 0xD800 && unit <= 0xDBFF) {
            if (i + 1 >= text.size()) return false;
            const auto next = static_cast<std::uint16_t>(text[++i]);
            if (next < 0xDC00 || next > 0xDFFF) return false;
        } else if (unit >= 0xDC00 && unit <= 0xDFFF) {
            return false;
        }
    }
    return true;
}

std::string utf8(std::wstring_view text) {
    if (!valid_utf16(text)) throw std::invalid_argument("Input contains ill-formed UTF-16.");
    if (text.empty()) return {};
    const int source_length = checked_int_size(text.size());
    const int required = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), source_length,
        nullptr, 0, nullptr, nullptr);
    if (required <= 0) throw std::runtime_error("WideCharToMultiByte failed for UTF-16 input.");
    std::string output(static_cast<std::size_t>(required), '\0');
    if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), source_length,
            output.data(), required, nullptr, nullptr) != required) {
        throw std::runtime_error("WideCharToMultiByte failed while encoding UTF-8.");
    }
    return output;
}

std::wstring utf16(std::string_view text) {
    if (text.empty()) return {};
    const int source_length = checked_int_size(text.size());
    const int required = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), source_length,
        nullptr, 0);
    if (required <= 0) throw std::invalid_argument("Input is not well-formed UTF-8.");
    std::wstring output(static_cast<std::size_t>(required), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), source_length,
            output.data(), required) != required) {
        throw std::invalid_argument("Input is not well-formed UTF-8.");
    }
    if (!valid_utf16(output)) throw std::invalid_argument("Input is not well-formed UTF-8.");
    return output;
}

std::string sha256(std::string_view bytes) {
    AlgorithmHandle algorithm;
    check_ntstatus(BCryptOpenAlgorithmProvider(&algorithm.value, BCRYPT_SHA256_ALGORITHM,
        nullptr, 0), "BCryptOpenAlgorithmProvider failed for SHA-256.");

    DWORD object_size = 0;
    DWORD returned = 0;
    check_ntstatus(BCryptGetProperty(algorithm.value, BCRYPT_OBJECT_LENGTH,
        reinterpret_cast<PUCHAR>(&object_size), sizeof(object_size), &returned, 0),
        "BCryptGetProperty failed for SHA-256.");
    HashHandle hash;
    std::vector<UCHAR> object(object_size);
    check_ntstatus(BCryptCreateHash(algorithm.value, &hash.value, object.data(), object_size,
        nullptr, 0, 0), "BCryptCreateHash failed for SHA-256.");
    if (!bytes.empty()) {
        check_ntstatus(BCryptHashData(hash.value,
            reinterpret_cast<PUCHAR>(const_cast<char*>(bytes.data())),
            checked_int_size(bytes.size()), 0), "BCryptHashData failed for SHA-256.");
    }

    std::vector<UCHAR> digest(32);
    check_ntstatus(BCryptFinishHash(hash.value, digest.data(), static_cast<ULONG>(digest.size()), 0),
        "BCryptFinishHash failed for SHA-256.");
    static constexpr char hex[] = "0123456789ABCDEF";
    std::string result;
    result.reserve(digest.size() * 2);
    for (UCHAR byte : digest) {
        result.push_back(hex[byte >> 4]);
        result.push_back(hex[byte & 0x0F]);
    }
    return result;
}

std::vector<int> grapheme_boundaries(std::wstring_view text) {
    if (text.size() > static_cast<std::size_t>((std::numeric_limits<int>::max)())) {
        throw std::length_error("Text is too large for ICU grapheme segmentation.");
    }
    std::vector<int> result;
    result.reserve(text.size());
    std::size_t offset = 0;
    while (offset < text.size()) {
        result.push_back(static_cast<int>(offset));
        const int length = next_grapheme_length(text.substr(offset));
        if (length <= 0 || static_cast<std::size_t>(length) > text.size() - offset) {
            throw std::runtime_error("ICU grapheme properties produced an invalid UTF-16 boundary.");
        }
        offset += static_cast<std::size_t>(length);
    }
    return result;
}
}
