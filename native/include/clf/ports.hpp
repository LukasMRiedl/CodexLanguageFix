#pragma once
#include "types.hpp"
#include <stop_token>

namespace clf {
class IBackend {
public:
  virtual ~IBackend() = default;
  virtual void warm_up(std::stop_token stop = {}) = 0;
  virtual Correction correct(const std::wstring &,
                             std::stop_token stop = {}) = 0;
  virtual Correction prepare(const std::wstring &,
                             std::stop_token stop = {}) = 0;
  virtual void connect(std::stop_token stop = {}) = 0;
  virtual Correction test_connection(std::stop_token stop = {}) = 0;
};
class IComposer {
public:
  virtual ~IComposer() = default;
  virtual std::optional<Snapshot>
  capture(bool include_layout = true, bool require_keyboard_focus = false) = 0;
  virtual std::optional<Snapshot> poll() = 0;
  virtual std::optional<Snapshot> refresh(const Snapshot &) = 0;
  virtual WriteResult replace(const Snapshot &, const std::wstring &,
                              const std::wstring &) = 0;
  virtual void invalidate_layout() = 0;
  virtual std::string status() const = 0;
  virtual void close() = 0;
};
} // namespace clf
