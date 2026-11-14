#include "clf/core.hpp"

#include <algorithm>
#include <cmath>
#include <limits>
#include <optional>
#include <utility>

namespace clf {
namespace {
double left(const Rect& rect) { return rect.x; }
double top(const Rect& rect) { return rect.y; }
double right(const Rect& rect) { return rect.x + rect.width; }
double bottom(const Rect& rect) { return rect.y + rect.height; }

bool valid(const Rect& rect) {
    return !rect.empty && rect.width > 0 && rect.height > 0
        && std::isfinite(left(rect)) && std::isfinite(top(rect))
        && std::isfinite(right(rect)) && std::isfinite(bottom(rect))
        && std::isfinite(rect.width) && std::isfinite(rect.height);
}

bool contains(const Rect& outer, const Rect& inner) {
    return !outer.empty && !inner.empty && left(inner) >= left(outer) && top(inner) >= top(outer)
        && right(inner) <= right(outer) && bottom(inner) <= bottom(outer);
}

bool intersects(const Rect& first, const Rect& second) {
    return !first.empty && !second.empty && left(first) < right(second) && right(first) > left(second)
        && top(first) < bottom(second) && bottom(first) > top(second);
}

Rect intersection(Rect first, Rect second) {
    if (first.empty || second.empty) return {};
    const double x = (std::max)(left(first), left(second));
    const double y = (std::max)(top(first), top(second));
    const double r = (std::min)(right(first), right(second));
    const double b = (std::min)(bottom(first), bottom(second));
    if (r < x || b < y) return {};
    return {x, y, r - x, b - y};
}

Rect united(Rect first, Rect second) {
    if (first.empty) return second;
    if (second.empty) return first;
    const double x = (std::min)(left(first), left(second));
    const double y = (std::min)(top(first), top(second));
    const double r = (std::max)(right(first), right(second));
    const double b = (std::max)(bottom(first), bottom(second));
    return {x, y, r - x, b - y};
}

bool inflated_intersection(const Rect& candidate, const Rect& obstacle, double gap) {
    const double l = left(obstacle) - gap;
    const double t = top(obstacle) - gap;
    const double r = right(obstacle) + gap;
    const double b = bottom(obstacle) + gap;
    if (!std::isfinite(l) || !std::isfinite(t) || !std::isfinite(r) || !std::isfinite(b)) return true;
    return left(candidate) < r && right(candidate) > l && top(candidate) < b && bottom(candidate) > t;
}

bool is_clear(const Rect& candidate, const std::vector<Rect>& obstacles, double gap) {
    return std::none_of(obstacles.begin(), obstacles.end(), [&](const Rect& obstacle) {
        return inflated_intersection(candidate, obstacle, gap);
    });
}

bool try_candidate(double x, double y, double size, Rect& candidate) {
    candidate = {};
    if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(size) || size <= 0
        || !std::isfinite(x + size) || !std::isfinite(y + size)) return false;
    candidate = {x, y, size, size};
    return true;
}

std::vector<double> candidate_positions(double minimum, double maximum,
    const std::vector<Rect>& obstacles, double size, double gap, bool horizontal, double preferred) {
    if (!std::isfinite(minimum) || !std::isfinite(maximum) || maximum < minimum) return {};
    std::vector<double> positions;
    auto add = [&](double value) {
        if (std::isfinite(value) && value >= minimum && value <= maximum
            && std::find(positions.begin(), positions.end(), value) == positions.end()) positions.push_back(value);
    };
    auto add_clamped = [&](double value) {
        if (std::isfinite(value)) add(std::clamp(value, minimum, maximum));
    };
    add_clamped(preferred);
    add(minimum);
    add(maximum);
    add_clamped(minimum + gap);
    add_clamped(maximum - gap);
    for (const auto& obstacle : obstacles) {
        if (horizontal) {
            add(left(obstacle) - size - gap);
            add(right(obstacle) + gap);
        } else {
            add(top(obstacle) - size - gap);
            add(bottom(obstacle) + gap);
        }
    }
    if (std::isfinite(preferred)) {
        std::sort(positions.begin(), positions.end(), [preferred](double a, double b) {
            const double da = std::abs(a - preferred), db = std::abs(b - preferred);
            return da == db ? a < b : da < db;
        });
    } else {
        std::sort(positions.begin(), positions.end());
    }
    return positions;
}

std::vector<double> candidate_positions(const Rect& area, const std::vector<Rect>& obstacles,
    double size, double gap, bool horizontal, double preferred) {
    const double minimum = horizontal ? left(area) : top(area);
    const double maximum = horizontal ? right(area) - size : bottom(area) - size;
    return candidate_positions(minimum, maximum, obstacles, size, gap, horizontal, preferred);
}

bool field_axis_range(double area_start, double area_end, double field_start, double field_end,
    double size, double preferred, double& minimum, double& maximum, double& aligned) {
    minimum = maximum = aligned = 0;
    if (!std::isfinite(area_start) || !std::isfinite(area_end)
        || !std::isfinite(field_start) || !std::isfinite(field_end)
        || !std::isfinite(size) || !std::isfinite(preferred) || area_end - area_start < size) return false;
    const double field_length = field_end - field_start;
    if (field_length >= size) {
        minimum = (std::max)(area_start, field_start);
        maximum = (std::min)(area_end - size, field_end - size);
        aligned = preferred;
        return maximum >= minimum;
    }
    aligned = field_start + (field_length - size) / 2;
    if (!std::isfinite(aligned) || aligned < area_start || aligned > area_end - size) return false;
    minimum = maximum = aligned;
    return true;
}

std::optional<Rect> find_on_fixed_x(const Rect& area, const std::vector<Rect>& obstacles,
    double size, double gap, double x, double field_top, double field_bottom, double preferred_y) {
    double minimum_y{}, maximum_y{}, preferred{};
    if (!std::isfinite(x) || !field_axis_range(top(area), bottom(area), field_top, field_bottom,
            size, preferred_y, minimum_y, maximum_y, preferred)) return std::nullopt;
    for (double y : candidate_positions(minimum_y, maximum_y, obstacles, size, gap, false, preferred)) {
        Rect candidate;
        if (try_candidate(x, y, size, candidate) && contains(area, candidate) && is_clear(candidate, obstacles, gap)) return candidate;
    }
    return std::nullopt;
}

std::optional<Rect> find_on_fixed_y(const Rect& area, const std::vector<Rect>& obstacles,
    double size, double gap, double preferred_x, double y) {
    if (!std::isfinite(y)) return std::nullopt;
    for (double x : candidate_positions(area, obstacles, size, gap, true, preferred_x)) {
        Rect candidate;
        if (try_candidate(x, y, size, candidate) && contains(area, candidate) && is_clear(candidate, obstacles, gap)) return candidate;
    }
    return std::nullopt;
}

std::optional<Rect> find_on_fixed_y(const Rect& area, const std::vector<Rect>& obstacles,
    double size, double gap, double y, double field_left, double field_right, double preferred_x) {
    double minimum_x{}, maximum_x{}, preferred{};
    if (!std::isfinite(y) || !field_axis_range(left(area), right(area), field_left, field_right,
            size, preferred_x, minimum_x, maximum_x, preferred)) return std::nullopt;
    for (double x : candidate_positions(minimum_x, maximum_x, obstacles, size, gap, true, preferred)) {
        Rect candidate;
        if (try_candidate(x, y, size, candidate) && contains(area, candidate) && is_clear(candidate, obstacles, gap)) return candidate;
    }
    return std::nullopt;
}

std::optional<Rect> find_in_composer(Rect composer, Rect editor, const std::vector<Rect>& obstacles,
    Rect screen, double size, double gap, const std::optional<Rect>& placement_bounds,
    const std::optional<std::vector<Rect>>& editor_text_bounds) {
    if (!placement_bounds || !editor_text_bounds || std::any_of(editor_text_bounds->begin(), editor_text_bounds->end(),
            [](const Rect& bounds) { return !valid(bounds); })) return std::nullopt;
    const Rect area = intersection(intersection(composer, *placement_bounds), screen);
    if (!valid(area)) return std::nullopt;
    std::vector<Rect> inner_obstacles;
    for (const auto& bounds : obstacles) if (!(bounds == editor)) inner_obstacles.push_back(bounds);
    inner_obstacles.insert(inner_obstacles.end(), editor_text_bounds->begin(), editor_text_bounds->end());
    const double x = right(composer) - size - gap;
    const double y = top(composer) + gap;
    Rect candidate;
    if (!try_candidate(x, y, size, candidate) || !contains(area, candidate) || !is_clear(candidate, inner_obstacles, gap)) return std::nullopt;
    return candidate;
}

std::optional<Rect> find_overlay(Rect composer, Rect editor, const std::vector<Rect>& occupied,
    Rect screen, double scale, std::optional<Rect> preferred_control,
    std::optional<Rect> placement_bounds, const std::optional<std::vector<Rect>>& editor_text_bounds) {
    if (!valid(composer) || !valid(editor) || !valid(screen) || !std::isfinite(scale) || scale <= 0
        || (placement_bounds && !valid(*placement_bounds))
        || (preferred_control && !preferred_control->empty && !valid(*preferred_control))) return std::nullopt;

    std::vector<Rect> obstacles;
    for (const auto& bounds : occupied) {
        if (bounds.empty) continue;
        if (!valid(bounds)) return std::nullopt;
        obstacles.push_back(bounds);
    }
    obstacles.push_back(editor);
    const double size = 28 * scale;
    const double gap = 4 * scale;
    if (!std::isfinite(size) || !std::isfinite(gap) || size <= 0) return std::nullopt;

    Rect toolbar_bounds = intersection(composer, screen);
    if (placement_bounds) toolbar_bounds = intersection(toolbar_bounds, *placement_bounds);
    if (valid(toolbar_bounds)) {
        const bool has_preferred = preferred_control && !preferred_control->empty;
        const double preferred_x = has_preferred ? left(*preferred_control) - size - gap : right(toolbar_bounds) - size - gap;
        const double preferred_y = has_preferred
            ? top(*preferred_control) + (preferred_control->height - size) / 2
            : bottom(editor) + gap;
        if (auto row = find_on_fixed_y(toolbar_bounds, obstacles, size, gap, preferred_x, preferred_y)) return row;

        const auto x_positions = candidate_positions(toolbar_bounds, obstacles, size, gap, true, preferred_x);
        const auto y_positions = candidate_positions(toolbar_bounds, obstacles, size, gap, false, preferred_y);
        for (double y : y_positions) {
            for (double x : x_positions) {
                Rect candidate;
                if (try_candidate(x, y, size, candidate) && contains(toolbar_bounds, candidate)
                    && is_clear(candidate, obstacles, gap)) return candidate;
            }
        }
    }

    if (auto in_editor = find_in_composer(composer, editor, obstacles, screen, size, gap,
            placement_bounds, editor_text_bounds)) return in_editor;
    if (!placement_bounds) return std::nullopt;
    const Rect sidecar_area = intersection(*placement_bounds, screen);
    if (!valid(sidecar_area)) return std::nullopt;
    if (auto candidate = find_on_fixed_x(sidecar_area, obstacles, size, gap,
            right(editor) + gap, top(editor), bottom(editor), bottom(editor) - size)) return candidate;
    if (auto candidate = find_on_fixed_x(sidecar_area, obstacles, size, gap,
            left(editor) - size - gap, top(editor), bottom(editor), bottom(editor) - size)) return candidate;
    if (auto candidate = find_on_fixed_y(sidecar_area, obstacles, size, gap,
            bottom(editor) + gap, left(editor), right(editor), right(editor) - size)) return candidate;
    return find_on_fixed_y(sidecar_area, obstacles, size, gap,
        top(editor) - size - gap, left(editor), right(editor), right(editor) - size);
}

std::vector<Rect> distinct_rects(const std::vector<Rect>& values) {
    std::vector<Rect> result;
    for (const auto& value : values) {
        if (std::find(result.begin(), result.end(), value) == result.end()) result.push_back(value);
    }
    return result;
}
}

std::optional<ToolbarAssociation> associate_toolbar(Rect editor, Rect document,
    const std::vector<ComposerControlBounds>& controls, double dpi_scale) {
    if (!valid(editor) || !valid(document) || !std::isfinite(dpi_scale) || dpi_scale <= 0
        || !contains(document, editor)) return std::nullopt;

    std::vector<Rect> buttons;
    for (const auto& control : controls) {
        if (!control.is_button || !valid(control.bounds)) continue;
        const auto& bounds = control.bounds;
        if (left(bounds) >= left(editor) - 12 * dpi_scale
            && right(bounds) <= right(editor) + 12 * dpi_scale
            && top(bounds) >= bottom(editor) - 2 * dpi_scale
            && top(bounds) <= bottom(editor) + 24 * dpi_scale
            && bounds.height >= 18 * dpi_scale && bounds.height <= 64 * dpi_scale
            && bounds.width >= 18 * dpi_scale) buttons.push_back(bounds);
    }
    buttons = distinct_rects(buttons);

    std::vector<std::vector<Rect>> rows;
    for (const auto& seed : buttons) {
        const double center = top(seed) + seed.height / 2;
        std::vector<Rect> row;
        for (const auto& button : buttons) {
            const double button_center = top(button) + button.height / 2;
            const double overlap = (std::min)(bottom(seed), bottom(button)) - (std::max)(top(seed), top(button));
            if (std::abs(button_center - center) <= 8 * dpi_scale
                && overlap >= (std::min)(seed.height, button.height) / 2) row.push_back(button);
        }
        std::sort(row.begin(), row.end(), [](const Rect& a, const Rect& b) { return left(a) < left(b); });
        if (row.size() < 2) continue;
        bool spans_editor = false;
        for (const auto& left_button : row) {
            if (right(left_button) <= left(editor) + editor.width * 0.45) {
                const bool has_right = std::any_of(row.begin(), row.end(), [&](const Rect& right_button) {
                    return left(right_button) >= left(editor) + editor.width * 0.55
                        && left(right_button) - right(left_button) >= 8 * dpi_scale;
                });
                if (has_right) { spans_editor = true; break; }
            }
        }
        if (spans_editor && std::find(rows.begin(), rows.end(), row) == rows.end()) rows.push_back(std::move(row));
    }

    auto complete = [&](Rect candidate, const std::vector<Rect>& row_buttons) -> std::optional<ToolbarAssociation> {
        candidate = intersection(candidate, document);
        for (const auto& control : controls) {
            if (control.is_editor && !(control.bounds == editor) && valid(control.bounds) && intersects(control.bounds, candidate)) {
                return std::nullopt;
            }
        }
        std::vector<Rect> occupied;
        for (const auto& control : controls) {
            if (valid(control.bounds) && intersects(control.bounds, candidate)) occupied.push_back(control.bounds);
        }
        occupied.push_back(editor);
        return ToolbarAssociation{candidate, distinct_rects(occupied), row_buttons};
    };

    if (rows.empty()) {
        std::vector<Rect> inline_buttons;
        for (const auto& control : controls) {
            const auto& button = control.bounds;
            if (!control.is_button || !valid(button) || button.height < 18 * dpi_scale
                || button.height > 64 * dpi_scale || button.width < 18 * dpi_scale) continue;
            const double vertical_overlap = (std::min)(bottom(button), bottom(editor)) - (std::max)(top(button), top(editor));
            if (vertical_overlap >= (std::min)(button.height, editor.height) / 2) inline_buttons.push_back(button);
        }
        inline_buttons = distinct_rects(inline_buttons);
        std::vector<std::pair<Rect, Rect>> pairs;
        for (const auto& left_button : inline_buttons) {
            if (right(left_button) > left(editor) || left(editor) - right(left_button) > 24 * dpi_scale) continue;
            for (const auto& right_button : inline_buttons) {
                if (left(right_button) < right(editor) || left(right_button) - right(editor) > 24 * dpi_scale) continue;
                if (std::abs(top(left_button) + left_button.height / 2 - top(right_button) - right_button.height / 2)
                    <= 8 * dpi_scale) pairs.emplace_back(left_button, right_button);
            }
        }
        if (pairs.size() != 1) return std::nullopt;
        Rect surface = united(united(editor, pairs[0].first), pairs[0].second);
        surface = {left(surface) - 36 * dpi_scale, top(surface) - 4 * dpi_scale,
            surface.width + 40 * dpi_scale, surface.height + 8 * dpi_scale};
        return complete(surface, {pairs[0].first, pairs[0].second});
    }

    if (rows.size() != 1) return std::nullopt;
    Rect surface = editor;
    for (const auto& button : rows[0]) surface = united(surface, button);
    surface = {left(surface) - 4 * dpi_scale, top(surface) - 4 * dpi_scale,
        surface.width + 8 * dpi_scale, surface.height + 8 * dpi_scale};
    return complete(surface, rows[0]);
}

std::optional<Rect> overlay_placement(const Snapshot& snapshot, Rect screen, double scale) {
    if (!snapshot.occupied) return std::nullopt;
    const Rect editor = snapshot.editor_bounds.value_or(snapshot.bounds);
    return find_overlay(snapshot.bounds, editor, *snapshot.occupied, screen, scale,
        snapshot.right_control, snapshot.placement_bounds, snapshot.editor_text_bounds);
}

bool same_editor(const Snapshot& a, const Snapshot& b) {
    return a.host == b.host && a.process_id == b.process_id && a.host_window == b.host_window
        && !a.runtime_id.empty() && a.runtime_id == b.runtime_id;
}
}
