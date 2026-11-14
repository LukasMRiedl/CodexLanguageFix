#pragma once
#include "ports.hpp"
#include <functional>
#include <memory>

namespace clf {
struct ViewState {
  std::optional<Snapshot> snapshot;
  bool busy{}, can_undo{}, shortcut_eligible{};
  std::wstring message, last_status;
};
class Controller {
public:
  Controller(IComposer &, IBackend &, Settings, std::function<void()> changed);
  ~Controller();
  Controller(const Controller &) = delete;
  ViewState view() const;
  void tick();
  void changed(bool focus_changed, bool layout_changed);
  void correct(bool from_shortcut = false);
  void undo();
  void configure(Settings);
  void settings_open(bool);
  void test_connection();
  void connect();
  void set_status(std::wstring);

private:
  struct Impl;
  std::unique_ptr<Impl> impl_;
};
} // namespace clf
