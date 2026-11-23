#pragma once
#include "types.hpp"
#include <chrono>
#include <functional>
#include <string_view>
#include <utility>

namespace clf {
std::string utf8(std::wstring_view text);
std::wstring utf16(std::string_view text);
std::string sha256(std::string_view bytes);
bool valid_utf16(std::wstring_view text);
// Returns UTF-16 grapheme start offsets, matching ParseCombiningCharacters (no terminal offset).
std::vector<int> grapheme_boundaries(std::wstring_view text);
std::vector<Span> protected_spans(const std::wstring& text);
struct ProtectedText {
    std::wstring text;
    std::vector<std::pair<std::wstring,std::wstring>> entries;
    static ProtectedText create(const std::wstring& original);
    std::optional<std::wstring> restore(const std::wstring& corrected) const;
};
bool structure_preserved(const std::wstring& original, const std::wstring& corrected);
std::optional<std::wstring> restore_layout(const std::wstring& original, const std::wstring& corrected);
std::optional<EditPlan> make_edit_plan(const std::wstring& original, const std::wstring& corrected);
EditPlan reverse_plan(const EditPlan& plan);
bool repeated_sentences_preserved(const std::wstring& original, const std::wstring& corrected);

enum class SelectionCheck { exact, count_mismatch, text_mismatch, editor_changed, source_changed };
std::string selection_status(SelectionCheck check);
SelectionCheck wait_for_selection_exact(const std::function<SelectionCheck()>& check);

enum class WriteCheck { exact, text_mismatch, editor_changed };
WriteCheck wait_for_exact(const std::wstring& expected,
    const std::function<std::optional<std::wstring>()>& read_text,
    const std::function<bool()>& is_editor_current);

class EditTarget {
public:
    virtual ~EditTarget() = default;
    virtual bool is_current_editor() = 0;
    virtual std::optional<std::wstring> read() = 0;
    // Verify and read the original editor even if it has lost keyboard focus.
    virtual std::optional<std::wstring> read_original_editor() = 0;
    virtual bool can_replace(int start, std::wstring_view expected) = 0;
    virtual bool replace(int start, std::wstring_view expected, std::wstring_view replacement) = 0;
};
WriteResult apply_edit_plan(const EditPlan& plan, EditTarget& target);

struct ComposerControlBounds { Rect bounds; bool is_button{}; bool is_editor{}; };
struct ToolbarAssociation { Rect surface_bounds; std::vector<Rect> occupied_bounds; std::vector<Rect> row_buttons; };
std::optional<ToolbarAssociation> associate_toolbar(Rect editor, Rect document,
    const std::vector<ComposerControlBounds>& controls, double dpi_scale);
std::optional<Rect> overlay_placement(const Snapshot& snapshot, Rect screen, double scale);
bool same_editor(const Snapshot& a, const Snapshot& b);
}
