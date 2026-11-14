#pragma once
#include "ports.hpp"
#include <memory>
#include <stop_token>
#include <functional>
#include <string_view>

namespace clf {
using Diagnostic = std::function<void(std::string_view)>;
struct CodexDiagnostics {
    std::uint64_t pending_rpc_requests{}, turn_states{}, active_turns{}, pending_turn_starts{};
    std::uint64_t maintenance_queue_entries{}, prepared_threads{};
    std::uint64_t cache_entries{}, cache_utf16_bytes{};
    std::uint64_t line_buffer_bytes{}, max_line_buffer_bytes{};
};
class CodexClient final : public IBackend {
public:
    explicit CodexClient(Diagnostic diagnostic = {});
#ifdef CLF_TESTING
    CodexClient(std::wstring executable_override, std::wstring application_directory_override,
        Diagnostic diagnostic = {});
#endif
    ~CodexClient();
    CodexClient(const CodexClient&) = delete;
    void warm_up(std::stop_token stop = {}) override;
    Correction correct(const std::wstring& text, std::stop_token stop = {}) override;
    Correction prepare(const std::wstring& text, std::stop_token stop = {}) override;
    void connect(std::stop_token stop = {}) override;
    Correction test_connection(std::stop_token stop = {}) override;
    std::string profile() const;
    CodexDiagnostics diagnostics() const;
    void close();
private:
    struct Impl; std::unique_ptr<Impl> impl_;
};
std::wstring application_directory();
Settings parse_settings_json(std::string_view json_bytes);
Settings load_settings();
void save_settings(const Settings& settings);
void set_autostart(bool enabled);
bool autostart_enabled();
}
