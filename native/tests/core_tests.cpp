#include "clf/core.hpp"
#include "clf/localization.hpp"

#include <algorithm>
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
void check(bool value, const char* name) {
    if (!value) throw std::runtime_error(std::string("core test failed: ") + name);
}

struct MemoryTarget final : clf::EditTarget {
    explicit MemoryTarget(std::wstring value) : text(std::move(value)) {}
    bool is_current_editor() override { return current; }
    std::optional<std::wstring> read() override { return readable ? std::optional<std::wstring>(text) : std::nullopt; }
    std::optional<std::wstring> read_original_editor() override { return same_editor ? read() : std::nullopt; }
    bool can_replace(int start, std::wstring_view expected) override {
        return start >= 0 && static_cast<std::size_t>(start) <= text.size()
            && expected.size() <= text.size() - static_cast<std::size_t>(start)
            && std::wstring_view(text).substr(static_cast<std::size_t>(start), expected.size()) == expected;
    }
    bool replace(int start, std::wstring_view expected, std::wstring_view replacement) override {
        ++write_calls;
        if (!current || !can_replace(start, expected)) return false;
        text.replace(static_cast<std::size_t>(start), expected.size(), replacement);
        if (lose_focus_on_write == write_calls) current = false;
        if (fail_on_write == write_calls) return false;
        return true;
    }
    std::wstring text;
    int write_calls{};
    int fail_on_write{};
    int lose_focus_on_write{};
    bool current{true};
    bool readable{true};
    bool same_editor{true};
};

clf::Rect rect(double x, double y, double width, double height) { return {x, y, width, height}; }
double top(const clf::Rect& value) { return value.y; }
double bottom(const clf::Rect& value) { return value.y + value.height; }
clf::ComposerControlBounds button(clf::Rect bounds) { return {bounds, true, false}; }
}

int main() {
    using namespace clf;
    try {
        check(utf16(utf8(L"Grüße 🙂")) == L"Grüße 🙂", "strict UTF-8 roundtrip");
        check(valid_utf16(L"🙂"), "valid surrogate pair");
        check(!valid_utf16(std::wstring(1, static_cast<wchar_t>(0xD800))), "reject lone high surrogate");
        check(sha256("abc") == "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", "BCrypt SHA-256");
        check(grapheme_boundaries(L"a\u0308🙂👩‍💻") == std::vector<int>({0, 2, 4}), "ICU grapheme start offsets");
        check(grapheme_boundaries(L"क्‍ष") == std::vector<int>({0, 3}), ".NET 8 UAX 29 revision 35 Indic boundaries");

        const std::wstring protected_input = L"Bitte prüfe `var eror = 1;`, https://example.test/falsch und C:\\Temp\\eror.txt."
            L" Nutze --output=result.txt und foo_bar.";
        const auto spans = protected_spans(protected_input);
        check(!spans.empty(), "protected span detection");
        for (const auto& span : spans) check(span.length > 0 && span.start >= 0 && span.end() <= static_cast<int>(protected_input.size()), "protected span bounds");
        const auto protected_text = ProtectedText::create(protected_input);
        check(!protected_text.entries.empty(), "Luna protected placeholders created");
        check(protected_text.text.find(L"var eror") == std::wstring::npos, "inline code replaced");
        std::wstring protected_correction = protected_text.text;
        const auto restored = protected_text.restore(protected_correction);
        check(restored && *restored == protected_input, "protected placeholders restore exactly");
        check(!protected_text.restore(protected_correction + L" ⟦CLF_PROTECTED_DEADBEEF_9999⟧"), "unknown placeholder rejected");

        const std::wstring original_layout = L"\t- korekt  \r\n\r\n  - Zweiter\t\r\n";
        const auto layout = restore_layout(original_layout, L"- korrekt\n\n- Zweiter");
        check(layout && *layout == L"\t- korrekt  \r\n\r\n  - Zweiter\t\r\n", "restore exact Markdown and CRLF layout");
        check(structure_preserved(original_layout, *layout), "restored layout passes structure guard");
        check(!structure_preserved(L"Text\r\nAbsatz", L"Text\nAbsatz"), "line ending changes rejected");
        check(!restore_layout(L"Erster\nZweiter", L"Erster Zweiter"), "missing line rejected");

        const auto plan = make_edit_plan(L"🙂 korekt `x`\r\nZwei Worte", L"🙃 korrekt. `x`\r\nZwei sehr kurze Worte");
        check(plan.has_value(), "safe plan includes grapheme and protected anchors");
        check(std::is_sorted(plan->edits.begin(), plan->edits.end(), [](const Edit& a, const Edit& b) { return a.start > b.start; }), "edits sorted descending");
        auto reconstructed = plan->original;
        for (const auto& edit : plan->edits) reconstructed.replace(static_cast<std::size_t>(edit.start), edit.original.size(), edit.replacement);
        check(reconstructed == plan->corrected, "edit plan reconstructs exact corrected text");
        const auto reverse = reverse_plan(*plan);
        check(reverse.original == plan->corrected && reverse.corrected == plan->original, "reverse plan endpoints");
        auto reversed_text = reverse.original;
        for (const auto& edit : reverse.edits) reversed_text.replace(static_cast<std::size_t>(edit.start), edit.original.size(), edit.replacement);
        check(reversed_text == reverse.corrected, "reverse edits reconstruct exact original");
        check(!make_edit_plan(L"Nutze `x` und `x`", L"Nutze `x`"), "ambiguous protected span removal rejected");
        const auto emoji_plan = make_edit_plan(L"👩‍💻", L"👩‍💻!");
        check(emoji_plan && emoji_plan->edits.size() == 1 && emoji_plan->edits[0].original == L"👩‍💻", "ZWJ grapheme remains whole");
        const auto indic_plan = make_edit_plan(L"korekt क्‍ष [Titel](https://a.test/x)", L"korrekt क्‍ष [Titel](https://a.test/x)");
        check(indic_plan && indic_plan->edits == std::vector<Edit>({{2, L"r", L"rr"}}), "ASCII correction before frozen Indic text");
        std::wstring long_text;
        for (int i = 0; i < 2000; ++i) long_text += L"- korekt\r\n";
        auto long_corrected = long_text;
        std::size_t find = 0;
        while ((find = long_corrected.find(L"korekt", find)) != std::wstring::npos) {
            long_corrected.replace(find, 6, L"korrekt");
            find += 7;
        }
        const auto long_plan = make_edit_plan(long_text, long_corrected);
        check(long_plan && long_plan->edits.size() == 2000, "20k source preserves per-line edits");

        check(repeated_sentences_preserved(L"Noch da. Noch da.", L"Noch da. Noch da."), "unchanged repeated sentences");
        check(!repeated_sentences_preserved(L"Noch da. Noch da.", L"Noch da."), "repetition loss rejected");
        check(repeated_sentences_preserved(L"Noch da. Noch da.", L"Noch korrekt. Noch korrekt."), "corrected repeated sentences accepted");

        int selection_calls = 0;
        const auto selection = wait_for_selection_exact([&] {
            return ++selection_calls == 1 ? SelectionCheck::text_mismatch : SelectionCheck::exact;
        });
        check(selection == SelectionCheck::exact && selection_calls == 2, "selection verification retries transient mismatch");
        check(selection_status(SelectionCheck::source_changed) == "before_mismatch_after_selection", "selection status mapping");
        check(wait_for_exact(L"expected", [] { return std::optional<std::wstring>(L"expected"); }, [] { return true; }) == WriteCheck::exact, "write verification exact readback");
        check(wait_for_exact(L"expected", [] { return std::optional<std::wstring>(L"expected"); }, [] { return false; }) == WriteCheck::editor_changed, "write verification guards editor identity");

        const auto transaction_plan = make_edit_plan(L"- korekt\n- korekt", L"- korrekt\n- korrekt");
        check(transaction_plan.has_value(), "transaction plan available");
        MemoryTarget target(transaction_plan->original);
        const auto write_result = apply_edit_plan(*transaction_plan, target);
        check(write_result.state == WriteState::applied && target.text == transaction_plan->corrected && write_result.status == "ok_verified", "write transaction applies and verifies");
        MemoryTarget failing(transaction_plan->original);
        failing.fail_on_write = 1;
        const auto rollback = apply_edit_plan(*transaction_plan, failing);
        check(rollback.state == WriteState::unchanged && failing.text == transaction_plan->original && rollback.status == "write_not_verified", "failed write rolls back verified own text");
        MemoryTarget completed(transaction_plan->original);
        completed.fail_on_write = static_cast<int>(transaction_plan->edits.size());
        const auto completion = apply_edit_plan(*transaction_plan, completed);
        check(completion.state == WriteState::applied && completed.text == transaction_plan->corrected
            && completed.write_calls == completed.fail_on_write, "exact complete correction survives a late write failure");
        MemoryTarget lost_focus(transaction_plan->original);
        lost_focus.lose_focus_on_write = static_cast<int>(transaction_plan->edits.size());
        const auto committed = apply_edit_plan(*transaction_plan, lost_focus);
        check(committed.state == WriteState::applied && committed.observed == transaction_plan->corrected
            && lost_focus.write_calls == lost_focus.lose_focus_on_write, "exact original editor readback survives focus loss");
        MemoryTarget lost_identity(transaction_plan->original);
        lost_identity.lose_focus_on_write = lost_focus.lose_focus_on_write;
        lost_identity.same_editor = false;
        const auto unverified = apply_edit_plan(*transaction_plan, lost_identity);
        check(unverified.state == WriteState::partially_applied, "unidentified readback cannot confirm an edit");

        const Rect editor = rect(100, 100, 600, 80);
        const Rect document = rect(0, 0, 1000, 1000);
        const std::vector<ComposerControlBounds> controls = {
            {editor, false, true}, button(rect(100, 186, 32, 32)), button(rect(654, 186, 32, 32)),
            {rect(240, 190, 150, 24), false, false}
        };
        const auto toolbar = associate_toolbar(editor, document, controls, 1);
        check(toolbar && toolbar->row_buttons.size() == 2 && toolbar->occupied_bounds.size() == 4, "toolbar association preserves occupied controls");
        check(!associate_toolbar(editor, document, {button(rect(650, 186, 32, 32))}, 1), "single button is not a toolbar");
        Snapshot snapshot;
        snapshot.bounds = rect(833, 1400, 728, 232);
        snapshot.editor_bounds = rect(849, 1408, 696, 152);
        snapshot.occupied = std::vector<Rect>{*snapshot.editor_bounds, rect(1136, 1568, 289, 56)};
        snapshot.right_control = rect(1136, 1568, 289, 56);
        snapshot.placement_bounds = rect(240, 32, 2400, 1640);
        const auto placement = overlay_placement(snapshot, *snapshot.placement_bounds, 2);
        check(placement && placement->width == 56 && top(*placement) > bottom(*snapshot.editor_bounds), "overlay placed clear of composer controls");
        auto unknown_occupied = snapshot;
        unknown_occupied.occupied.reset();
        check(!overlay_placement(unknown_occupied, *snapshot.placement_bounds, 2), "unknown occupied bounds reject placement");
        Snapshot inside_only;
        inside_only.bounds = rect(0, 0, 120, 60);
        inside_only.editor_bounds = inside_only.bounds;
        inside_only.occupied = std::vector<Rect>{};
        inside_only.placement_bounds = inside_only.bounds;
        check(!overlay_placement(inside_only, inside_only.bounds, 1), "unknown editor text bounds reject in-editor placement");
        inside_only.editor_text_bounds = std::vector<Rect>{};
        check(overlay_placement(inside_only, inside_only.bounds, 1).has_value(), "verified empty editor text bounds allow safe in-editor placement");
        snapshot.runtime_id = {1, 2};
        check(same_editor(snapshot, snapshot), "same editor identity");
        auto other = snapshot;
        other.runtime_id = {1};
        check(!same_editor(snapshot, other), "different runtime identity rejected");

        check(normalize_language("DE")=="de" && normalize_language("EN")=="en" && normalize_language("unsupported")=="auto", "reference language mode normalization");
        set_language("EN");
        check(!is_german() && translate("ChangeCountMany",{L"1234"})==L"1,234 changes", "English grouped count formatting");
        set_language("de");
        check(is_german() && translate("ChangeCountMany",{L"1234"})==L"1.234 Änderungen", "German grouped count formatting");
        check(translate("ResponseTime",{L"1234.5"})==L"Antwort in 1.234 ms.", "reference midpoint number formatting");
        std::cout << "core_tests: all cases passed\n";
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
