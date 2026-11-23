#include "clf/controller.hpp"
#include "clf/core.hpp"
#include "clf/localization.hpp"
#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <deque>
#include <mutex>
#include <thread>

namespace clf {
namespace {
using Clock = std::chrono::steady_clock;
bool unchanged(const Snapshot &a, const Snapshot &b) {
  return same_editor(a, b) && a.text == b.text;
}
bool whitespace(const std::wstring &text) {
  return text.empty() || std::all_of(text.begin(), text.end(), [](wchar_t c) {
           return c == 0x09 || c == 0x0a || c == 0x0b || c == 0x0c ||
                  c == 0x0d || c == 0x20 || c == 0x85 || c == 0xa0 ||
                  c == 0x1680 || (c >= 0x2000 && c <= 0x200a) || c == 0x2028 ||
                  c == 0x2029 || c == 0x202f || c == 0x205f || c == 0x3000;
         });
}
std::wstring count_message(int count) {
  return translate(count == 1 ? "ChangeCountOne" : "ChangeCountMany",
                   {std::to_wstring(count)}) +
         L" · Codex";
}
} // namespace
struct Controller::Impl {
  IComposer &composer;
  IBackend &backend;
  Settings settings;
  std::function<void()> notify;
  mutable std::mutex view_mutex;
  ViewState state;
  std::mutex queue_mutex;
  std::condition_variable_any wake;
  std::deque<std::function<void()>> queue;
  bool stopping{}, tick_pending{}, focus_dirty{}, layout_dirty{}, in_settings{};
  std::atomic<uint64_t> focus_generation{};
  std::mutex cancel_mutex;
  std::stop_source request_cancel;
  std::jthread actor, warmup, request, auxiliary;
  struct Preparation {
    Snapshot snapshot;
    std::stop_source cancel;
    std::mutex gate;
    std::condition_variable_any done;
    bool finished{}, succeeded{};
    std::jthread thread;
    explicit Preparation(Snapshot value) : snapshot(std::move(value)) {}
  };
  std::shared_ptr<Preparation> preparation;
  std::optional<Snapshot> observed, request_snapshot;
  Clock::time_point observed_at{}, transient_until{};
  bool attempted{}, handled{}, busy{}, auxiliary_busy{};
  std::wstring transient;
  struct Undo {
    Snapshot snapshot;
    std::wstring original, corrected;
    int changes;
  };
  std::optional<Undo> undo_state;

  Impl(IComposer &c, IBackend &b, Settings s, std::function<void()> n)
      : composer(c), backend(b), settings(std::move(s)), notify(std::move(n)) {
    actor = std::jthread([this](std::stop_token stop) {
      while (!stop.stop_requested()) {
        std::function<void()> work;
        {
          std::unique_lock lock(queue_mutex);
          wake.wait(lock, stop, [this] { return !queue.empty(); });
          if (stop.stop_requested())
            break;
          work = std::move(queue.front());
          queue.pop_front();
        }
        try {
          work();
        } catch (...) {
          cancel_request();
          busy = false;
          request_snapshot.reset();
          clear_preparation();
          publish_hidden();
        }
      }
    });
    warmup = std::jthread([this](std::stop_token stop) {
      try {
        backend.warm_up(stop);
      } catch (...) {
      }
    });
    tick();
  }
  ~Impl() {
    {
      std::lock_guard lock(queue_mutex);
      stopping = true;
    }
    cancel_request();
    warmup.request_stop();
    auxiliary.request_stop();
    actor.request_stop();
    wake.notify_all();
    actor.join();
    clear_preparation();
    request.request_stop();
    if (request.joinable())
      request.join();
    if (auxiliary.joinable())
      auxiliary.join();
    if (warmup.joinable())
      warmup.join();
    preparation.reset();
  }
  void post(std::function<void()> fn) {
    {
      std::lock_guard lock(queue_mutex);
      if (stopping)
        return;
      queue.emplace_back(std::move(fn));
    }
    wake.notify_one();
  }
  void cancel_request() {
    std::lock_guard lock(cancel_mutex);
    request_cancel.request_stop();
  }
  void publish() {
    if (notify)
      notify();
  }
  void status(std::wstring message) {
    {
      std::lock_guard lock(view_mutex);
      state.last_status = std::move(message);
    }
    publish();
  }
  void publish_hidden() {
    {
      std::lock_guard lock(view_mutex);
      state.snapshot.reset();
      state.shortcut_eligible = false;
      state.can_undo = false;
      state.busy = busy;
    }
    publish();
  }
  void message(std::wstring text, const Snapshot *origin = nullptr) {
    status(text);
    bool visible = true;
    {
      std::lock_guard lock(view_mutex);
      if (origin)
        visible = state.snapshot && same_editor(*origin, *state.snapshot);
    }
    if (visible) {
      transient = std::move(text);
      transient_until = Clock::now() + std::chrono::seconds(6);
    }
  }
  void clear_preparation() {
    if (preparation)
      preparation->cancel.request_stop();
    observed.reset();
    attempted = false;
    handled = false;
  }
  void suppress(const Snapshot &snapshot) {
    clear_preparation();
    observed = snapshot;
    attempted = handled = true;
  }
  void observe(const Snapshot &snapshot) {
    if (snapshot.host != "Codex" || !snapshot.keyboard_focused || !settings.prepare ||
        snapshot.text.size() > 20000 || whitespace(snapshot.text)) {
      clear_preparation();
      return;
    }
    if (!observed || !unchanged(*observed, snapshot)) {
      clear_preparation();
      observed = snapshot;
      observed_at = Clock::now();
    }
    bool unfinished = false;
    if (preparation) {
      std::lock_guard lock(preparation->gate);
      unfinished = !preparation->finished;
    }
    if (busy || attempted || handled || unfinished ||
        Clock::now() - observed_at < std::chrono::milliseconds(1200))
      return;
    attempted = true;
    auto operation = std::make_shared<Preparation>(snapshot);
    preparation = operation;
    // The actor owns the operation until its worker has finished; the worker
    // does not own its jthread.
    try {
      operation->thread = std::jthread([this, raw = operation.get()] {
        bool succeeded = false;
        try {
          backend.prepare(raw->snapshot.text, raw->cancel.get_token());
          succeeded = !raw->cancel.stop_requested();
        } catch (...) {
        }
        {
          std::lock_guard lock(raw->gate);
          raw->succeeded = succeeded;
          raw->finished = true;
        }
        raw->done.notify_all();
        tick();
      });
    } catch (...) {
      std::lock_guard lock(operation->gate);
      operation->finished = true;
      operation->done.notify_all();
      throw;
    }
  }
  void render(std::optional<Snapshot> snapshot, uint64_t generation) {
    if (generation != focus_generation.load())
      return;
    if (!snapshot || whitespace(snapshot->text)) {
      clear_preparation();
      cancel_request();
      publish_hidden();
      return;
    }
    if (request_snapshot && !unchanged(*request_snapshot, *snapshot))
      cancel_request();
    {
      std::lock_guard lock(view_mutex);
      if (state.snapshot && !same_editor(*state.snapshot, *snapshot))
        transient.clear();
    }
    observe(*snapshot);
    bool ready = false;
    if (!handled && preparation && observed &&
        unchanged(*observed, *snapshot) &&
        unchanged(preparation->snapshot, *snapshot)) {
      std::lock_guard lock(preparation->gate);
      ready = preparation->finished && preparation->succeeded;
    }
    const bool can_undo = undo_state &&
                          same_editor(undo_state->snapshot, *snapshot) &&
                          snapshot->text == undo_state->corrected;
    std::wstring label;
    if (Clock::now() < transient_until)
      label = transient;
    else if (busy)
      label = translate("Checking");
    else if (can_undo)
      label = count_message(undo_state->changes);
    else if (ready)
      label = translate("CorrectionReady");
    {
      std::lock_guard lock(view_mutex);
      if (generation != focus_generation.load())
        return;
      state.snapshot = std::move(snapshot);
      state.busy = busy;
      state.shortcut_eligible = state.snapshot->keyboard_focused;
      state.can_undo = can_undo && !busy;
      state.message = std::move(label);
    }
    publish();
  }
  void refresh() {
    if (!settings.enabled || in_settings) {
      clear_preparation();
      cancel_request();
      composer.invalidate_layout();
      publish_hidden();
      return;
    }
    const auto generation = focus_generation.load();
    auto snapshot = composer.poll();
    if (generation != focus_generation.load())
      return;
    render(std::move(snapshot), generation);
  }
  void tick() {
    {
      std::lock_guard lock(queue_mutex);
      if (stopping || tick_pending)
        return;
      tick_pending = true;
      queue.emplace_back([this] {
        bool layout;
        {
          std::lock_guard inner(queue_mutex);
          tick_pending = false;
          layout = layout_dirty;
          layout_dirty = false;
          focus_dirty = false;
        }
        if (layout)
          composer.invalidate_layout();
        refresh();
      });
    }
    wake.notify_one();
  }
  void changed(bool focus, bool layout) {
    if (focus) {
      {
        std::lock_guard lock(view_mutex);
        focus_generation.fetch_add(1);
        state.snapshot.reset();
        state.shortcut_eligible = false;
        state.can_undo = false;
      }
      cancel_request();
      publish();
    }
    {
      std::lock_guard lock(queue_mutex);
      focus_dirty |= focus;
      layout_dirty |= layout;
    }
    tick();
  }
  void finish_request() {
    busy = false;
    request_snapshot.reset();
    refresh();
  }
  void correct(bool shortcut) {
    post([this, shortcut] {
      if (busy || !settings.enabled || in_settings)
        return;
      busy = true;
      auto generation = focus_generation.load();
      std::optional<Snapshot> snapshot;
      try {
        snapshot = composer.capture(false, shortcut);
      } catch (...) {
        busy = false;
        message(translate("ComposerUnavailable"));
        refresh();
        return;
      }
      if (generation != focus_generation.load() || !snapshot ||
          whitespace(snapshot->text)) {
        busy = false;
        if (!shortcut)
          message(translate("NoPromptDetected"));
        refresh();
        return;
      }
      request_snapshot = snapshot;
      transient.clear();
      {
        std::lock_guard lock(cancel_mutex);
        request_cancel = std::stop_source();
      }
      std::stop_token cancel;
      {
        std::lock_guard lock(cancel_mutex);
        cancel = request_cancel.get_token();
      }
      std::shared_ptr<Preparation> matching;
      if (preparation && !preparation->cancel.stop_requested() &&
          unchanged(preparation->snapshot, *snapshot))
        matching = preparation;
      else if (preparation)
        preparation->cancel.request_stop();
      observed = snapshot;
      handled = attempted = true;
      render(snapshot, generation);
      try {
        request = std::jthread([this, snapshot = *snapshot, generation, cancel,
                                matching](std::stop_token) {
          try {
            if (matching) {
              std::unique_lock lock(matching->gate);
              matching->done.wait(lock, cancel,
                                  [&] { return matching->finished; });
            }
            if (cancel.stop_requested() || generation != focus_generation.load())
              throw AppError("CheckCancelled");
            auto outcome = backend.correct(snapshot.text, cancel);
            post([this, snapshot, generation, cancel,
                  outcome = std::move(outcome)] {
              try {
                if (cancel.stop_requested() ||
                    generation != focus_generation.load() ||
                    !settings.enabled || in_settings)
                  throw AppError("CheckCancelled");
                auto active = composer.capture(false, false);
                if (!active || !unchanged(snapshot, *active))
                  throw AppError("PromptChanged");
                if (outcome.text == snapshot.text) {
                  message(translate("NoChangesFoundProvider", {L"Codex"}),
                          &snapshot);
                  finish_request();
                  return;
                }
                auto current = composer.refresh(snapshot);
                if (!current)
                  throw AppError("ComposerUnavailable");
                if (!unchanged(snapshot, *current))
                  throw AppError("PromptChanged");
                if (cancel.stop_requested() ||
                    generation != focus_generation.load())
                  throw AppError("CheckCancelled");
                auto result =
                    composer.replace(*current, snapshot.text, outcome.text);
                if (result.state != WriteState::applied) {
                  undo_state.reset();
                  throw AppError("WriteRejected");
                }
                auto corrected = *current;
                corrected.text = outcome.text;
                suppress(corrected);
                undo_state = Undo{*current, snapshot.text, outcome.text,
                                  outcome.change_count};
                message(count_message(outcome.change_count), &snapshot);
              } catch (const AppError &e) {
                message(translate(e.key, e.args), &snapshot);
              } catch (...) {
                message(translate("UnexpectedCorrectionFailure"), &snapshot);
              }
              finish_request();
            });
          } catch (const AppError &e) {
            post([this, snapshot, key = e.key, args = e.args, cancel] {
              message(
                  translate(cancel.stop_requested() ? "CheckCancelled" : key,
                            args),
                  &snapshot);
              finish_request();
            });
          } catch (...) {
            post([this, snapshot, cancel] {
              message(translate(cancel.stop_requested()
                                    ? "CheckCancelled"
                                    : "UnexpectedCorrectionFailure"),
                      &snapshot);
              finish_request();
            });
          }
        });
      } catch (...) {
        busy = false;
        request_snapshot.reset();
        message(translate("UnexpectedCorrectionFailure"));
        refresh();
      }
    });
  }
  void undo() {
    post([this] {
      if (busy || !undo_state || !settings.enabled || in_settings)
        return;
      busy = true;
      const auto generation = focus_generation.load();
      try {
        auto current = composer.capture(false, false);
        if (generation != focus_generation.load() || !current ||
            !same_editor(undo_state->snapshot, *current) ||
            current->text != undo_state->corrected)
          throw AppError("UndoUnavailable");
        if (generation != focus_generation.load())
          throw AppError("UndoUnavailable");
        auto result =
            composer.replace(*current, current->text, undo_state->original);
        if (result.state != WriteState::applied)
          throw AppError("UndoFailed");
        current->text = undo_state->original;
        suppress(*current);
        undo_state.reset();
        message(translate("UndoCompleted"));
      } catch (const AppError &e) {
        message(translate(e.key, e.args));
      } catch (...) {
        message(translate("UndoFailed"));
      }
      busy = false;
      refresh();
    });
  }
  void connection(bool login) {
    post([this, login] {
      if (auxiliary_busy)
        return;
      auxiliary_busy = true;
      try {
        auxiliary = std::jthread([this, login](std::stop_token stop) {
          std::wstring result;
          try {
            if (login) {
              backend.connect(stop);
              result = translate("OpenAiConnected");
            } else {
              auto outcome = backend.test_connection(stop);
              result = translate("ProviderAvailable", {L"Codex"}) + L"\r\n" +
                       translate("ResponseTime",
                                 {std::to_wstring(outcome.elapsed_ms)});
            }
          } catch (const AppError &e) {
            result = translate(e.key, e.args);
          } catch (...) {
            result = translate("UnexpectedConnectionFailure");
          }
          post([this, result = std::move(result)] {
            auxiliary_busy = false;
            status(result);
          });
        });
      } catch (...) {
        auxiliary_busy = false;
        status(translate("UnexpectedConnectionFailure"));
      }
    });
  }
};
Controller::Controller(IComposer &c, IBackend &b, Settings s,
                       std::function<void()> n)
    : impl_(std::make_unique<Impl>(c, b, std::move(s), std::move(n))) {}
Controller::~Controller() = default;
ViewState Controller::view() const {
  std::lock_guard lock(impl_->view_mutex);
  return impl_->state;
}
void Controller::tick() { impl_->tick(); }
void Controller::changed(bool f, bool l) { impl_->changed(f, l); }
void Controller::correct(bool s) { impl_->correct(s); }
void Controller::undo() { impl_->undo(); }
void Controller::configure(Settings s) {
  impl_->focus_generation.fetch_add(1);
  impl_->cancel_request();
  impl_->post([impl = impl_.get(), s = std::move(s)] {
    impl->settings = s;
    impl->clear_preparation();
    impl->refresh();
  });
}
void Controller::settings_open(bool open) {
  impl_->focus_generation.fetch_add(1);
  impl_->cancel_request();
  impl_->post([impl = impl_.get(), open] {
    impl->in_settings = open;
    impl->refresh();
  });
}
void Controller::test_connection() { impl_->connection(false); }
void Controller::connect() { impl_->connection(true); }
void Controller::set_status(std::wstring text) {
  impl_->post(
      [impl = impl_.get(), text = std::move(text)] { impl->status(text); });
}
} // namespace clf
