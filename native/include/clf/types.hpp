#pragma once
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace clf {
struct Rect {
    double x{}, y{}, width{}, height{}; bool empty{true};
    Rect() = default;
    Rect(double left, double top, double w, double h, bool is_empty = false)
        : x(left), y(top), width(w), height(h), empty(is_empty) {}
    bool operator==(const Rect&) const = default;
};
struct Span { int start{}, length{}; int end() const { return start + length; } bool operator==(const Span&) const = default; };
struct Edit { int start{}; std::wstring original, replacement; bool operator==(const Edit&) const = default; };
struct EditPlan { std::wstring original, corrected; std::vector<Edit> edits; };
enum class WriteState { unchanged, applied, partially_applied };
struct WriteResult { WriteState state{WriteState::unchanged}; std::wstring original; std::optional<std::wstring> observed; int applied_changes{}; std::string status{"not_attempted"}; };
struct Snapshot {
    uint64_t token{}; std::vector<int> runtime_id; std::wstring text; Rect bounds;
    uint32_t process_id{}; uint64_t host_window{};
    std::string host{"Codex"};
    std::optional<Rect> right_control, editor_bounds, placement_bounds;
    std::optional<std::vector<Rect>> occupied, editor_text_bounds;
    std::string field_category{"chat"}, read_method{"unknown"};
    bool keyboard_focused{};
};
struct Correction { std::wstring text; int change_count{}; double elapsed_ms{}; std::string profile; };
struct Settings {
    int version{2}; bool enabled{true}, start_with_windows{true}, prepare{}, notice_shown{};
    bool shortcut_reset_on_load{};
    std::string language{"auto"}; uint32_t shortcut_modifiers{3}, shortcut_vk{0xBC};
};
}
