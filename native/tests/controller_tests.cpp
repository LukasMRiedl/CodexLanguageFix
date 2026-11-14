#include "clf/controller.hpp"
#include "clf/core.hpp"
#include "clf/localization.hpp"
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <iostream>
#include <mutex>
#include <stdexcept>
#include <thread>
using namespace clf;
using namespace std::chrono_literals;
namespace {
void require(bool value, const char *reason) {
  if (!value)
    throw std::runtime_error(reason);
}
template <class F> void until(F predicate) {
  auto end = std::chrono::steady_clock::now() + 4s;
  while (!predicate()) {
    if (std::chrono::steady_clock::now() > end)
      throw std::runtime_error("test_timeout");
    std::this_thread::sleep_for(5ms);
  }
}
class Composer final : public IComposer {
public:
  std::mutex gate;
  std::condition_variable capture_released;
  std::optional<Snapshot> current;
  std::atomic<int> writes{}, captures{}, polls{};
  bool partial{}, throw_capture{}, hold_capture{};
  Composer() {
    Snapshot s;
    s.token = 1;
    s.runtime_id = {42};
    s.process_id = 7;
    s.host_window = 9;
    s.text = L"Der Satz ist korekt.";
    s.keyboard_focused = true;
    current = s;
  }
  std::optional<Snapshot> capture(bool = true,
                                  bool require_focus = false) override {
    std::unique_lock lock(gate);
    auto captured = current;
    ++captures;
    capture_released.wait(lock, [&] { return !hold_capture; });
    if (throw_capture) {
      throw_capture = false;
      throw std::runtime_error("worker_fault");
    }
    if (require_focus && captured && !captured->keyboard_focused)
      return {};
    return captured;
  }
  std::optional<Snapshot> poll() override {
    ++polls;
    return capture();
  }
  std::optional<Snapshot> refresh(const Snapshot &) override {
    return capture();
  }
  WriteResult replace(const Snapshot &snapshot, const std::wstring &expected,
                      const std::wstring &replacement) override {
    std::lock_guard lock(gate);
    ++writes;
    WriteResult result;
    if (!current || !same_editor(*current, snapshot) ||
        current->text != expected)
      return result;
    if (partial) {
      current->text = L"uncertain partial";
      result.state = WriteState::partially_applied;
      return result;
    }
    current->text = replacement;
    result.state = WriteState::applied;
    result.observed = replacement;
    return result;
  }
  void invalidate_layout() override {}
  std::string status() const override { return "test"; }
  void close() override {}
  void text(std::wstring value) {
    std::lock_guard lock(gate);
    current->text = std::move(value);
  }
  std::wstring text() {
    std::lock_guard lock(gate);
    return current ? current->text : L"";
  }
};
class Backend final : public IBackend {
public:
  std::mutex gate;
  std::condition_variable_any wake;
  bool hold = true;
  std::atomic<int> calls{}, prepares{}, cancelled{}, auxiliary_calls{};
  bool hold_auxiliary{};
  void warm_up(std::stop_token = {}) override {}
  Correction correct(const std::wstring &text,
                     std::stop_token stop = {}) override {
    ++calls;
    std::unique_lock lock(gate);
    wake.wait(lock, stop, [&] { return !hold; });
    if (stop.stop_requested()) {
      ++cancelled;
      throw AppError("CheckCancelled");
    }
    auto value = text;
    auto offset = value.find(L"korekt");
    if (offset != std::wstring::npos)
      value.replace(offset, 6, L"korrekt");
    return {value, 1, 1.0, "test"};
  }
  Correction prepare(const std::wstring &text,
                     std::stop_token stop = {}) override {
    ++prepares;
    return correct(text, stop);
  }
  void connect(std::stop_token stop = {}) override {
    (void)test_connection(stop);
  }
  Correction test_connection(std::stop_token stop = {}) override {
    ++auxiliary_calls;
    if (hold_auxiliary) {
      std::unique_lock lock(gate);
      wake.wait(lock, stop, [&] { return !hold; });
    }
    return {};
  }
  void release() {
    std::lock_guard lock(gate);
    hold = false;
    wake.notify_all();
  }
};
void visible(Controller &controller) {
  until([&] { return controller.view().snapshot.has_value(); });
}
void test_exact_undo() {
  Composer c;
  Backend b;
  Controller controller(c, b, Settings{}, [] {});
  visible(controller);
  controller.correct(true);
  until([&] { return b.calls == 1; });
  b.release();
  until([&] { return controller.view().can_undo; });
  require(c.text() == L"Der Satz ist korrekt.", "correction_applied");
  controller.undo();
  until([&] { return c.writes == 2 && !controller.view().busy; });
  require(c.text() == L"Der Satz ist korekt.", "undo_exact");
}
void test_changed_text() {
  Composer c;
  Backend b;
  Controller controller(c, b, Settings{}, [] {});
  visible(controller);
  controller.correct();
  until([&] { return b.calls == 1; });
  c.text(L"Neue Benutzereingabe");
  controller.tick();
  until([&] { return b.cancelled == 1; });
  until([&] { return !controller.view().busy; });
  require(c.writes == 0 && c.text() == L"Neue Benutzereingabe",
          "typed_input_preserved");
}
void test_focus_changed() {
  Composer c;
  Backend b;
  Controller controller(c, b, Settings{}, [] {});
  visible(controller);
  controller.correct();
  until([&] { return b.calls == 1; });
  controller.changed(true, true);
  b.release();
  until([&] { return !controller.view().busy; });
  require(c.writes == 0, "focus_cancelled_without_write");
}
void test_focus_hides_while_capture_is_blocked() {
  Composer c;
  Backend b;
  Controller controller(c, b, Settings{}, [] {});
  visible(controller);
  struct Release {
    Composer &composer;
    ~Release() {
      {
        std::lock_guard lock(composer.gate);
        composer.hold_capture = false;
      }
      composer.capture_released.notify_all();
    }
  } release{c};
  const auto previous = c.captures.load();
  {
    std::lock_guard lock(c.gate);
    c.hold_capture = true;
  }
  controller.tick();
  until([&] { return c.captures > previous; });
  {
    std::lock_guard lock(c.gate);
    c.current.reset();
  }
  controller.changed(true, false);
  const auto hidden = controller.view();
  require(!hidden.snapshot && !hidden.shortcut_eligible && !hidden.can_undo,
          "focus_change_hides_before_blocked_worker_returns");
}
void test_partial_not_retried() {
  Composer c;
  Backend b;
  c.partial = true;
  Controller controller(c, b, Settings{}, [] {});
  visible(controller);
  controller.correct();
  until([&] { return b.calls == 1; });
  b.release();
  until([&] { return c.writes == 1 && !controller.view().busy; });
  controller.undo();
  std::this_thread::sleep_for(30ms);
  require(c.writes == 1 && c.text() == L"uncertain partial" &&
              !controller.view().can_undo,
          "partial_not_replayed_or_undone");
}
void test_stale_undo() {
  Composer c;
  Backend b;
  b.release();
  Controller controller(c, b, Settings{}, [] {});
  visible(controller);
  controller.correct();
  until([&] { return controller.view().can_undo; });
  c.text(L"Neue Eingabe");
  controller.undo();
  until([&] {
    return !controller.view().busy &&
           controller.view().last_status == translate("UndoUnavailable");
  });
  require(c.writes == 1 && c.text() == L"Neue Eingabe", "stale_undo_rejected");
}
void test_worker_failure_recovers() {
  Composer c;
  Backend b;
  Controller controller(c, b, Settings{}, [] {});
  visible(controller);
  {
    std::lock_guard lock(c.gate);
    c.throw_capture = true;
  }
  controller.correct();
  until([&] {
    return controller.view().last_status == translate("ComposerUnavailable");
  });
  require(!controller.view().busy, "capture_failure_busy_cleared");
  b.release();
  controller.correct();
  until([&] { return controller.view().can_undo; });
}
void test_preparation_adopted_once() {
  Composer c;
  Backend b;
  Settings settings;
  settings.prepare = true;
  Controller controller(c, b, settings, [] {});
  visible(controller);
  std::this_thread::sleep_for(1250ms);
  controller.tick();
  until([&] { return b.prepares == 1; });
  controller.correct();
  until([&] { return controller.view().busy; });
  require(b.calls == 1, "manual_waits_matching_preparation");
  b.release();
  until([&] { return controller.view().can_undo; });
  require(b.calls == 2 && b.prepares == 1 && c.writes == 1,
          "manual_adopts_preparation_then_cache_path");
  std::this_thread::sleep_for(1250ms);
  controller.tick();
  std::this_thread::sleep_for(30ms);
  require(b.prepares == 1, "applied_result_suppresses_preparation");
}
void test_disabled_and_settings_cancel() {
  for (bool settings_open : {false, true}) {
    Composer c;
    Backend b;
    Controller controller(c, b, Settings{}, [] {});
    visible(controller);
    controller.correct();
    until([&] { return b.calls == 1; });
    if (settings_open)
      controller.settings_open(true);
    else {
      Settings settings;
      settings.enabled = false;
      controller.configure(settings);
    }
    until([&] { return !controller.view().snapshot; });
    b.release();
    until([&] { return !controller.view().busy; });
    require(c.writes == 0, "disabled_settings_cancelled");
  }
}
void test_identity_rejected_without_poll() {
  Composer c;
  Backend b;
  Controller controller(c, b, Settings{}, [] {});
  visible(controller);
  controller.correct();
  until([&] { return b.calls == 1; });
  {
    std::lock_guard lock(c.gate);
    c.current->runtime_id = {99};
  }
  b.release();
  until([&] { return !controller.view().busy; });
  require(c.writes == 0, "fresh_identity_rejected");
}
void test_shutdown_cancels_all_work() {
  for (int phase = 0; phase < 4; ++phase) {
    Composer c;
    Backend b;
    Settings settings;
    settings.prepare = phase == 1 || phase == 2;
    b.hold_auxiliary = phase == 3;
    auto controller = std::make_unique<Controller>(c, b, settings, [] {});
    visible(*controller);
    if (phase == 0) {
      controller->correct();
      until([&] { return b.calls == 1; });
    }
    if (phase == 1 || phase == 2) {
      std::this_thread::sleep_for(1250ms);
      controller->tick();
      until([&] { return b.prepares == 1; });
      if (phase == 2) {
        controller->correct();
        until([&] { return controller->view().busy; });
      }
    }
    if (phase == 3) {
      controller->test_connection();
      until([&] { return b.auxiliary_calls == 1; });
    }
    const auto started = std::chrono::steady_clock::now();
    controller.reset();
    require(std::chrono::steady_clock::now() - started < 1s,
            "shutdown_bounded");
    require(c.writes == 0, "shutdown_never_writes");
  }
}
} // namespace
int main() {
  try {
    set_language("de");
    test_exact_undo();
    test_changed_text();
    test_focus_changed();
    test_focus_hides_while_capture_is_blocked();
    test_partial_not_retried();
    test_stale_undo();
    test_worker_failure_recovers();
    test_preparation_adopted_once();
    test_disabled_and_settings_cancel();
    test_identity_rejected_without_poll();
    test_shutdown_cancels_all_work();
    std::cout << "15 controller scenarios passed\n";
    return 0;
  } catch (const std::exception &e) {
    std::cerr << e.what() << '\n';
    return 1;
  }
}
