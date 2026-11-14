#pragma once
#include "ports.hpp"
#include <memory>
#include <string>

namespace clf {
int run_composer_worker(uint32_t parent_id);
struct ComposerDiagnostics {
  uint64_t capture_count{}, poll_count{}, full_layout_count{},
      geometry_check_count{}, geometry_node_check_count{},
      geometry_walker_step_count{}, geometry_cache_hit_count{},
      retained_tokens{}, retained_identity_data_bytes{}, idle_owned_com_refs{},
      idle_owned_editor_com_refs{};
};
class ComposerClient : public IComposer {
public:
  ComposerClient();
  ~ComposerClient() override;
  ComposerClient(const ComposerClient &) = delete;
  std::optional<Snapshot> capture(bool include_layout = true,
                                  bool require_keyboard_focus = false) override;
  std::optional<Snapshot> poll() override;
  std::optional<Snapshot> refresh(const Snapshot &snapshot) override;
  WriteResult replace(const Snapshot &snapshot, const std::wstring &expected,
                      const std::wstring &replacement) override;
  ComposerDiagnostics diagnostics();
  void invalidate_layout() override;
  std::string status() const override;
  void close() override;

private:
  struct Impl;
  std::unique_ptr<Impl> impl_;
};
} // namespace clf
