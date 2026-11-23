#include "clf/composer.hpp"
#include "clf/core.hpp"
#include <Windows.h>

#include <Objbase.h>
#include <ShlObj.h>

#include <UIAutomation.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <deque>
#include <future>
#include <map>
#include <memory>
#include <mutex>
#include <nlohmann/json.hpp>
#include <stdexcept>
#include <thread>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
using json = nlohmann::json;
namespace clf {
namespace {
void check(HRESULT h) {
  if (FAILED(h))
    throw std::runtime_error("uia_failed");
}
struct Handle {
  HANDLE h{};
  Handle() = default;
  explicit Handle(HANDLE v) : h(v) {}
  ~Handle() {
    if (h && h != INVALID_HANDLE_VALUE)
      CloseHandle(h);
  }
  Handle(const Handle &) = delete;
  Handle &operator=(const Handle &) = delete;
  void reset(HANDLE v = nullptr) {
    if (h && h != INVALID_HANDLE_VALUE)
      CloseHandle(h);
    h = v;
  }
};
std::wstring take(BSTR b) {
  std::wstring s = b ? std::wstring(b, SysStringLen(b)) : L"";
  SysFreeString(b);
  return s;
}
std::wstring text(IUIAutomationTextRange *r) {
  BSTR b{};
  check(r->GetText(20001, &b));
  auto s = take(b);
  if (s.size() > 20000)
    throw std::runtime_error("text_limit");
  return s;
}
std::vector<int> rid(IUIAutomationElement *e) {
  SAFEARRAY *a{};
  const auto result = e->GetRuntimeId(&a);
  std::unique_ptr<SAFEARRAY, decltype(&SafeArrayDestroy)> owner(a,
                                                            SafeArrayDestroy);
  check(result);
  if (!a || SafeArrayGetDim(a) != 1)
    throw std::runtime_error("identity");
  VARTYPE type{};
  check(SafeArrayGetVartype(a, &type));
  if (type != VT_I4 || SafeArrayGetElemsize(a) != sizeof(int))
    throw std::runtime_error("identity");
  LONG l{}, u{};
  check(SafeArrayGetLBound(a, 1, &l));
  check(SafeArrayGetUBound(a, 1, &u));
  if (u < l)
    throw std::runtime_error("identity");
  std::vector<int> v;
  for (LONG i = l;; ++i) {
    int n{};
    check(SafeArrayGetElement(a, &i, &n));
    v.push_back(n);
    if (i == u)
      break;
  }
  return v;
}
Rect rect(RECT r) {
  return {double(r.left), double(r.top), double(r.right - r.left),
          double(r.bottom - r.top), r.right <= r.left || r.bottom <= r.top};
}
enum class Host { other, codex, learn_harness };

Host editor_host(DWORD pid) {
  Handle p(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid));
  if (!p.h)
    return Host::other;
  std::wstring s(32768, L'\0');
  DWORD n = DWORD(s.size());
  if (!QueryFullProcessImageNameW(p.h, 0, s.data(), &n))
    return Host::other;
  s.resize(n);
  std::transform(s.begin(), s.end(), s.begin(),
                 [](wchar_t c) { return wchar_t(towlower(c)); });
  auto k = s.find_last_of(L"\\/");
  const auto name = std::wstring_view(s).substr(k + 1);
  if (name == L"chatgpt.exe" &&
      (s.find(L"\\windowsapps\\openai.codex_") != s.npos ||
       s.find(L"\\openai\\codex\\") != s.npos))
    return Host::codex;
  if (name != L"learnharness.exe")
    return Host::other;
  static const auto installed_path = [] {
    PWSTR raw{};
    const auto result = SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &raw);
    std::unique_ptr<wchar_t, decltype(&CoTaskMemFree)> folder(raw, CoTaskMemFree);
    if (FAILED(result) || !folder)
      return std::wstring{};
    auto path = std::wstring(folder.get()) + L"\\Programs\\learnharness\\LearnHarness.exe";
    std::transform(path.begin(), path.end(), path.begin(),
                   [](wchar_t c) { return wchar_t(towlower(c)); });
    return path;
  }();
  return s == installed_path ? Host::learn_harness : Host::other;
}
json jr(Rect r) { return {r.x, r.y, r.width, r.height, r.empty}; }
Rect rj(const json &j) { return {j.at(0), j.at(1), j.at(2), j.at(3), j.at(4)}; }
json wire(const Snapshot &s) {
  json j = {{"token", s.token},
            {"runtime", s.runtime_id},
            {"text", utf8(s.text)},
            {"bounds", jr(s.bounds)},
            {"pid", s.process_id},
            {"hwnd", s.host_window},
            {"host", s.host},
            {"focus", s.keyboard_focused},
            {"category", s.field_category},
            {"method", s.read_method},
            {"occupied", nullptr},
            {"textBounds", nullptr}};
  if (s.editor_bounds)
    j["editor"] = jr(*s.editor_bounds);
  if (s.right_control)
    j["right"] = jr(*s.right_control);
  if (s.placement_bounds)
    j["placement"] = jr(*s.placement_bounds);
  if (s.occupied) {
    j["occupied"] = json::array();
    for (auto r : *s.occupied)
      j["occupied"].push_back(jr(r));
  }
  if (s.editor_text_bounds) {
    j["textBounds"] = json::array();
    for (auto r : *s.editor_text_bounds)
      j["textBounds"].push_back(jr(r));
  }
  return j;
}
Snapshot unwire(const json &j) {
  Snapshot s;
  s.token = j.at("token");
  s.runtime_id = j.at("runtime").get<std::vector<int>>();
  s.text = utf16(j.at("text").get<std::string>());
  s.bounds = rj(j.at("bounds"));
  s.process_id = j.at("pid");
  s.host_window = j.at("hwnd");
  s.host = j.at("host");
  s.keyboard_focused = j.at("focus");
  s.field_category = j.at("category");
  s.read_method = j.at("method");
  if (j.contains("editor"))
    s.editor_bounds = rj(j["editor"]);
  if (j.contains("right"))
    s.right_control = rj(j["right"]);
  if (j.contains("placement"))
    s.placement_bounds = rj(j["placement"]);
  if (!j.at("occupied").is_null()) {
    s.occupied.emplace();
    for (auto &r : j.at("occupied"))
      s.occupied->push_back(rj(r));
  }
  if (!j.at("textBounds").is_null()) {
    s.editor_text_bounds.emplace();
    for (auto &r : j.at("textBounds"))
      s.editor_text_bounds->push_back(rj(r));
  }
  return s;
}
bool readall(HANDLE h, void *p, DWORD n) {
  auto b = (BYTE *)p;
  while (n) {
    DWORD k{};
    if (!ReadFile(h, b, n, &k, nullptr) || !k)
      return false;
    b += k;
    n -= k;
  }
  return true;
}
bool writeall(HANDLE h, const void *p, DWORD n) {
  auto b = (const BYTE *)p;
  while (n) {
    DWORD k{};
    if (!WriteFile(h, b, n, &k, nullptr) || !k)
      return false;
    b += k;
    n -= k;
  }
  return true;
}
std::optional<json> receive(HANDLE h) {
  BYTE b[4];
  if (!readall(h, b, 4))
    return {};
  uint32_t n = uint32_t(b[0]) | (uint32_t(b[1]) << 8) | (uint32_t(b[2]) << 16) |
               (uint32_t(b[3]) << 24);
  if (!n || n > 262144)
    throw std::runtime_error("frame_limit");
  std::string s(n, '\0');
  if (!readall(h, s.data(), n))
    return {};
  return json::parse(s);
}
void transmit(HANDLE h, const json &j) {
  auto s = j.dump();
  uint32_t n = uint32_t(s.size());
  if (!n || n > 262144)
    throw std::runtime_error("frame_limit");
  BYTE b[4] = {BYTE(n), BYTE(n >> 8), BYTE(n >> 16), BYTE(n >> 24)};
  if (!writeall(h, b, 4) || !writeall(h, s.data(), n))
    throw std::runtime_error("pipe_failed");
}
struct Dirty final : IUIAutomationFocusChangedEventHandler,
                     IUIAutomationEventHandler,
                     IUIAutomationPropertyChangedEventHandler {
  std::atomic<ULONG> refs{1};
  std::atomic_uint flags{7};
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void **p) override {
    if (!p)
      return E_POINTER;
    *p = nullptr;
    if (id == __uuidof(IUnknown) ||
        id == __uuidof(IUIAutomationFocusChangedEventHandler))
      *p = static_cast<IUIAutomationFocusChangedEventHandler *>(this);
    else if (id == __uuidof(IUIAutomationEventHandler))
      *p = static_cast<IUIAutomationEventHandler *>(this);
    else if (id == __uuidof(IUIAutomationPropertyChangedEventHandler))
      *p = static_cast<IUIAutomationPropertyChangedEventHandler *>(this);
    else
      return E_NOINTERFACE;
    AddRef();
    return S_OK;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
  ULONG STDMETHODCALLTYPE Release() override {
    auto n = --refs;
    if (!n)
      delete this;
    return n;
  }
  HRESULT STDMETHODCALLTYPE
  HandleFocusChangedEvent(IUIAutomationElement *) override {
    flags.fetch_or(1);
    return S_OK;
  }
  HRESULT STDMETHODCALLTYPE HandleAutomationEvent(IUIAutomationElement *,
                                                  EVENTID id) override {
    flags.fetch_or(id == UIA_Text_TextChangedEventId ? 2u : 4u);
    return S_OK;
  }
  HRESULT STDMETHODCALLTYPE HandlePropertyChangedEvent(IUIAutomationElement *,
                                                       PROPERTYID,
                                                       VARIANT) override {
    flags.fetch_or(4);
    return S_OK;
  }
};
enum class CaptureReason {
  not_attempted,
  captured,
  foreground_missing,
  other_host,
  focused_other_host,
  focused_non_composer,
  no_keyboard_focused_composer,
  no_matching_editor,
  ambiguous_editors,
  keyboard_focus_mismatch,
  invalid_editor_bounds,
  ambiguous_placeholder,
  uia_operation_failed,
  snapshot_expired,
  refresh_identity_unavailable
};
const char *capture_reason(CaptureReason reason) {
  switch (reason) {
  case CaptureReason::not_attempted:
    return "not_attempted";
  case CaptureReason::captured:
    return "captured";
  case CaptureReason::foreground_missing:
    return "foreground_missing";
  case CaptureReason::other_host:
    return "other_host";
  case CaptureReason::focused_other_host:
    return "focused_other_host";
  case CaptureReason::focused_non_composer:
    return "focused_non_composer";
  case CaptureReason::no_keyboard_focused_composer:
    return "no_keyboard_focused_composer";
  case CaptureReason::no_matching_editor:
    return "no_matching_editor";
  case CaptureReason::ambiguous_editors:
    return "ambiguous_editors";
  case CaptureReason::keyboard_focus_mismatch:
    return "keyboard_focus_mismatch";
  case CaptureReason::invalid_editor_bounds:
    return "invalid_editor_bounds";
  case CaptureReason::ambiguous_placeholder:
    return "ambiguous_placeholder";
  case CaptureReason::uia_operation_failed:
    return "uia_operation_failed";
  case CaptureReason::snapshot_expired:
    return "snapshot_expired";
  case CaptureReason::refresh_identity_unavailable:
    return "refresh_identity_unavailable";
  }
  return "uia_operation_failed";
}
class Accessor {
  static Snapshot identity_only(const Snapshot &source) {
    Snapshot identity;
    identity.token = source.token;
    identity.runtime_id = source.runtime_id;
    identity.process_id = source.process_id;
    identity.host_window = source.host_window;
    identity.host = source.host;
    identity.field_category = source.field_category;
    identity.read_method = source.read_method;
    return identity;
  }
  ComPtr<IUIAutomation> u;
  ComPtr<IUIAutomationTreeWalker> w, geometryWalker;
  ComPtr<Dirty> d;
  ComPtr<IUIAutomationElement> eventRoot;
  std::deque<Snapshot> ids;
  uint64_t next{};
  CaptureReason captureReason{CaptureReason::not_attempted};
  DWORD owner{};
  std::optional<EditPlan> lastPlan;
  std::optional<Snapshot> lastEditor;
  std::optional<Snapshot> cache;
  struct GeometryNode {
    std::vector<int> path, id;
    Rect box;
    int children{};
    bool offscreen{};
    std::optional<std::vector<Rect>> text_bounds;
  };
  std::vector<GeometryNode> geometryNodes;
  IUIAutomationElement *recordingRoot{};
  bool layoutValid{};
  bool broadGeometry{};
  Rect geometryEditor;
  uint64_t captures{}, polls{}, fullLayouts{}, cacheHits{}, geometryChecks{},
      geometryNodeChecks{}, geometrySteps{};
  UINT cacheDpi{};
  static std::wstring lower(std::wstring x) {
    std::transform(x.begin(), x.end(), x.begin(),
                   [](wchar_t c) { return wchar_t(towlower(c)); });
    return x;
  }
  std::wstring property(IUIAutomationElement *e, PROPERTYID id) {
    VARIANT v;
    VariantInit(&v);
    check(e->GetCurrentPropertyValue(id, &v));
    std::wstring s;
    if (v.vt == VT_BSTR && v.bstrVal)
      s.assign(v.bstrVal, SysStringLen(v.bstrVal));
    VariantClear(&v);
    return s;
  }
  static std::wstring normalized(std::wstring s) {
    int n =
        NormalizeString(NormalizationKC, s.data(), int(s.size()), nullptr, 0);
    if (n > 0) {
      std::wstring out(n, L'\0');
      n = NormalizeString(NormalizationKC, s.data(), int(s.size()), out.data(),
                          n);
      if (n > 0) {
        out.resize(n);
        s = out;
      }
    }
    std::wstring out;
    bool space = false;
    for (auto c : s) {
      if (iswspace(c)) {
        space = !out.empty();
        continue;
      }
      if (space)
        out += L' ';
      space = false;
      out += wchar_t(towlower(c));
    }
    return out;
  }
  static bool has_class(std::wstring_view classes, std::wstring_view wanted) {
    std::wstring token;
    auto match = [&] { return lower(token) == lower(std::wstring(wanted)); };
    for (auto c : classes) {
      if (iswspace(c)) {
        if (!token.empty() && match())
          return true;
        token.clear();
      } else
        token += c;
    }
    return !token.empty() && match();
  }
  bool shape(IUIAutomationElement *e) {
    int t = type(e);
    return (t == UIA_EditControlTypeId || t == UIA_GroupControlTypeId ||
            t == UIA_DocumentControlTypeId) &&
           has_class(property(e, UIA_ClassNamePropertyId), L"ProseMirror");
  }
  bool learn_harness_editor(IUIAutomationElement *e) {
    if (type(e) != UIA_EditControlTypeId ||
        !has_class(property(e, UIA_ClassNamePropertyId), L"lh-input"))
      return false;
    const auto name = property(e, UIA_NamePropertyId);
    for (std::wstring_view label : {
             L"What do you want to practice?", L"What do you want to learn?",
             L"Ask, answer, or steer the lesson…",
             L"Ask anything, or press Enter for the next one.",
             L"Type your answer…", L"Type your own answer…",
             L"Why this answer? Whatever comes to mind.",
             L"Work here, think out loud.", L"Whatever comes to mind as you read."})
      if (name == label)
        return true;
    return false;
  }
  bool side_question_editor(IUIAutomationElement *e) {
    return type(e) == UIA_EditControlTypeId &&
           property(e, UIA_NamePropertyId) == L"Side question";
  }
  bool structured(IUIAutomationElement *e) {
    int t = type(e);
    auto c = property(e, UIA_ClassNamePropertyId);
    return shape(e) || learn_harness_editor(e) ||
           (t == UIA_ComboBoxControlTypeId && has_class(c, L"cursor-text") &&
            has_class(c, L"overflow-y-auto")) ||
           (t == UIA_EditControlTypeId && has_class(c, L"overflow-y-auto") &&
            (has_class(c, L"cursor-text") ||
              has_class(c, L"ui-prompt-input-editor__input")));
  }
  bool editable(IUIAutomationElement *e) {
    BOOL enabled{}, focusable{};
    check(e->get_CurrentIsEnabled(&enabled));
    check(e->get_CurrentIsKeyboardFocusable(&focusable));
    if (!enabled || !focusable)
      return false;
    int t = type(e);
    auto c = lower(property(e, UIA_ClassNamePropertyId));
    if (t == UIA_DocumentControlTypeId &&
        lower(property(e, UIA_AutomationIdPropertyId)) == L"rootwebarea")
      return false;
    ComPtr<IUIAutomationValuePattern> v;
    bool hasValue = SUCCEEDED(e->GetCurrentPatternAs(UIA_ValuePatternId,
                                                     IID_PPV_ARGS(&v))) &&
                    v;
    BOOL ro = TRUE;
    if (hasValue)
      check(v->get_CurrentIsReadOnly(&ro));
    bool native = t == UIA_DocumentControlTypeId &&
                  (c.starts_with(L"richedit") || c == L"richtextbox");
    bool tokenized =
        (t == UIA_ComboBoxControlTypeId || t == UIA_EditControlTypeId) &&
        has_class(c, L"cursor-text") && has_class(c, L"overflow-y-auto");
    if (!(t == UIA_EditControlTypeId || native || (hasValue && !ro) ||
          shape(e) || tokenized))
      return false;
    if (t == UIA_EditControlTypeId || native || hasValue)
      return true;
    ComPtr<IUIAutomationTextPattern> p;
    return SUCCEEDED(
               e->GetCurrentPatternAs(UIA_TextPatternId, IID_PPV_ARGS(&p))) &&
           p;
  }
  bool natural(IUIAutomationElement *e, const std::wstring &caption = L"") {
    auto id = normalized(property(e, UIA_AutomationIdPropertyId));
    auto name = normalized(property(e, UIA_NamePropertyId));
    auto label = normalized(caption);
    if (label.empty()) {
      ComPtr<IUIAutomationElement> l;
      if (SUCCEEDED(e->get_CurrentLabeledBy(&l)) && l)
        label = normalized(property(l.Get(), UIA_NamePropertyId));
    }
    static const std::vector<std::wstring> excluded = {L"Search",
                                                       L"Search…",
                                                       L"Name",
                                                       L"Title",
                                                       L"Nickname",
                                                       L"Occupation",
                                                       L"Project name",
                                                       L"Path",
                                                       L"File path",
                                                       L"Folder path",
                                                       L"URL",
                                                       L"URI",
                                                       L"API key",
                                                       L"Access token",
                                                       L"Token",
                                                       L"Password",
                                                       L"Authentication",
                                                       L"Auth",
                                                       L"Code",
                                                       L"JSON",
                                                       L"JSON Schema",
                                                       L"Settings",
                                                       L"Configuration",
                                                       L"Config",
                                                       L"Endpoint",
                                                       L"Command",
                                                       L"Shell",
                                                       L"Terminal",
                                                       L"Model",
                                                       L"Workspace",
                                                       L"Directory",
                                                       L"File name",
                                                       L"Suchen",
                                                       L"Titel",
                                                       L"Spitzname",
                                                       L"Beruf",
                                                       L"Projektname",
                                                       L"Pfad",
                                                       L"Dateipfad",
                                                       L"Ordnerpfad",
                                                       L"API-Schlüssel",
                                                       L"Zugriffstoken",
                                                       L"Passwort",
                                                       L"Authentifizierung",
                                                       L"Einstellungen",
                                                       L"Konfiguration",
                                                       L"Befehl",
                                                       L"Endpunkt",
                                                       L"Modell",
                                                       L"Arbeitsbereich",
                                                       L"Verzeichnis",
                                                       L"Dateiname",
                                                       L"PIN",
                                                       L"OTP",
                                                       L"CVV",
                                                       L"CVC",
                                                       L"IBAN",
                                                       L"Credit card",
                                                       L"Card number",
                                                       L"Kreditkarte",
                                                       L"Kartennummer",
                                                       L"Benutzername",
                                                       L"Username",
                                                       L"Verification code",
                                                       L"Bestätigungscode"};
    for (auto &x : excluded)
      if (normalized(x) == name || normalized(x) == label)
        return false;
    auto cls = property(e, UIA_ClassNamePropertyId);
    for (auto c : {L"CodeMirror", L"cm-editor", L"Monaco", L"monaco-editor",
                   L"ace_editor", L"jsoneditor"})
      if (has_class(cls, c))
        return false;
    auto raw = property(e, UIA_AutomationIdPropertyId);
    std::wstring separated;
    for (size_t i = 0; i < raw.size(); ++i) {
      if (i && iswupper(raw[i]) &&
          (iswlower(raw[i - 1]) || iswdigit(raw[i - 1])))
        separated += L' ';
      separated += wchar_t(towlower(raw[i]));
    }
    if (id.find(L"apikey") != id.npos || id.find(L"json") != id.npos)
      return false;
    static const std::wstring technical =
        L" search name title path url uri auth password secret token key code "
        L"json setting settings config configuration endpoint command shell "
        L"terminal model workspace directory filename query sql regex "
        L"expression nickname occupation otp pin cvv cvc iban creditcard "
        L"cardnumber payment username login ";
    std::wstring token;
    for (size_t i = 0; i <= separated.size(); ++i) {
      if (i < separated.size() && iswalnum(separated[i]))
        token += separated[i];
      else if (!token.empty()) {
        if (technical.find(L" " + token + L" ") != technical.npos)
          return false;
        token.clear();
      }
    }
    if (id == L"artifact-feedback-details" ||
        id == L"chatgpt-custom-instructions" ||
        id == L"chatgpt-personalization-more-about-you")
      return true;
    for (auto x : {L"How else could we improve? (optional)",
                   L"Custom instructions", L"More about you",
                   L"Was könnten wir sonst noch verbessern? (optional)",
                   L"Benutzerdefinierte Anweisungen", L"Mehr über dich",
                   L"Edit message", L"Nachricht bearbeiten"})
      if (normalized(x) == name || normalized(x) == label)
        return true;
    return false;
  }
  bool eligible(IUIAutomationElement *e) {
    int pid{}, control_type{};
    BOOL enabled{}, off{}, pass{};
    check(e->get_CurrentProcessId(&pid));
    const auto host = editor_host(pid);
    if (host == Host::other)
      return false;
    check(e->get_CurrentControlType(&control_type));
    check(e->get_CurrentIsEnabled(&enabled));
    check(e->get_CurrentIsOffscreen(&off));
    check(e->get_CurrentIsPassword(&pass));
    if (!enabled || off || pass)
      return false;
    const bool side_question = host == Host::learn_harness && side_question_editor(e);
    ComPtr<IUIAutomationValuePattern> v;
    if (SUCCEEDED(
            e->GetCurrentPatternAs(UIA_ValuePatternId, IID_PPV_ARGS(&v))) &&
        v) {
      BOOL ro{};
      check(v->get_CurrentIsReadOnly(&ro));
      if (ro)
        return false;
      // Chromium dispatches input and change events for a textarea ValuePattern write.
      if (side_question)
        return true;
    }
    if (side_question)
      return false;
    ComPtr<IUIAutomationTextPattern> t;
    if (SUCCEEDED(
            e->GetCurrentPatternAs(UIA_TextPatternId, IID_PPV_ARGS(&t))) &&
        t) {
      ComPtr<IUIAutomationTextRange> r;
      check(t->get_DocumentRange(&r));
      VARIANT ro;
      VariantInit(&ro);
      check(r->GetAttributeValue(UIA_IsReadOnlyAttributeId, &ro));
      bool readonly = ro.vt == VT_BOOL && ro.boolVal == VARIANT_TRUE;
      VariantClear(&ro);
      if (readonly)
        return false;
      if (host == Host::learn_harness) {
        if (!learn_harness_editor(e))
          return false;
        // LearnHarness keeps the original file paths and citations outside the
        // visible chip text. Only edit a draft whose text range is plain text.
        ComPtr<IUIAutomationElementArray> children;
        check(r->GetChildren(&children));
        int count{};
        if (children)
          check(children->get_Length(&count));
        for (int i = 0; i < count; ++i) {
          ComPtr<IUIAutomationElement> child;
          check(children->GetElement(i, &child));
          if (type(child.Get()) != UIA_TextControlTypeId)
            return false;
        }
        return true;
      }
    }
    if (host == Host::learn_harness)
      return false;
    if (shape(e))
      return true;
    if (control_type != UIA_EditControlTypeId)
      return false;
    if (natural(e))
      return true;
    RECT eb{};
    check(e->get_CurrentBoundingRectangle(&eb));
    ComPtr<IUIAutomationElement> parent;
    check(w->GetParentElement(e, &parent));
    for (int depth = 0; parent && depth < 3; ++depth) {
      int pt{};
      check(parent->get_CurrentControlType(&pt));
      if (pt == UIA_DocumentControlTypeId || pt == UIA_WindowControlTypeId)
        break;
      VARIANT vv;
      VariantInit(&vv);
      vv.vt = VT_I4;
      vv.lVal = UIA_EditControlTypeId;
      ComPtr<IUIAutomationCondition> ec;
      check(u->CreatePropertyCondition(UIA_ControlTypePropertyId, vv, &ec));
      ComPtr<IUIAutomationElementArray> edits;
      check(parent->FindAll(TreeScope_Descendants, ec.Get(), &edits));
      int count{};
      check(edits->get_Length(&count));
      if (count > 1)
        break;
      vv.lVal = UIA_TextControlTypeId;
      ComPtr<IUIAutomationCondition> tc;
      check(u->CreatePropertyCondition(UIA_ControlTypePropertyId, vv, &tc));
      ComPtr<IUIAutomationElementArray> captions;
      check(parent->FindAll(TreeScope_Descendants, tc.Get(), &captions));
      check(captions->get_Length(&count));
      double scale = GetDpiForWindow(GetForegroundWindow()) / 96.;
      for (int i = 0; i < count; ++i) {
        ComPtr<IUIAutomationElement> caption;
        check(captions->GetElement(i, &caption));
        BOOL hidden{};
        check(caption->get_CurrentIsOffscreen(&hidden));
        RECT cb{};
        check(caption->get_CurrentBoundingRectangle(&cb));
        if (!hidden && cb.bottom <= eb.top &&
            eb.top - cb.bottom <= 24 * scale && cb.right > eb.left &&
            cb.left < eb.right &&
            natural(e, property(caption.Get(), UIA_NamePropertyId)))
          return true;
      }
      ComPtr<IUIAutomationElement> nextParent;
      check(w->GetParentElement(parent.Get(), &nextParent));
      parent = nextParent;
    }
    return false;
  }
  ComPtr<IUIAutomationTextPattern> pattern(IUIAutomationElement *e) {
    ComPtr<IUIAutomationTextPattern> p;
    check(e->GetCurrentPatternAs(UIA_TextPatternId, IID_PPV_ARGS(&p)));
    return p;
  }
  std::wstring read(IUIAutomationElement *e) {
    if (!structured(e)) {
      ComPtr<IUIAutomationValuePattern> v;
      if (SUCCEEDED(
              e->GetCurrentPatternAs(UIA_ValuePatternId, IID_PPV_ARGS(&v))) &&
          v) {
        BSTR b{};
        check(v->get_CurrentValue(&b));
        auto t = take(b);
        if (t.size() > 20000)
          throw std::runtime_error("text_limit");
        return t;
      }
    }
    auto p = pattern(e);
    ComPtr<IUIAutomationTextRange> r;
    check(p->get_DocumentRange(&r));
    return text(r.Get());
  }
  bool focused(IUIAutomationElement *e) {
    ComPtr<IUIAutomationElement> f;
    check(u->GetFocusedElement(&f));
    auto id = rid(e);
    for (int n = 0; f && n < 12; ++n) {
      int pid{};
      check(f->get_CurrentProcessId(&pid));
      if (editor_host(pid) == Host::other)
        return false;
      if (rid(f.Get()) == id)
        return true;
      ComPtr<IUIAutomationElement> p;
      check(w->GetParentElement(f.Get(), &p));
      f = p;
    }
    return false;
  }
  bool current(IUIAutomationElement *e, const Snapshot &s) {
    int pid{};
    check(e->get_CurrentProcessId(&pid));
    return pid == int(s.process_id) &&
           GetForegroundWindow() == reinterpret_cast<HWND>(s.host_window) &&
           eligible(e) && rid(e) == s.runtime_id && focused(e);
  }
  void events(IUIAutomationElement *root) {
    if (root && eventRoot) {
      BOOL same{};
      if (SUCCEEDED(u->CompareElements(eventRoot.Get(), root, &same)) && same)
        return;
    }
    if (eventRoot) {
      u->RemoveAutomationEventHandler(UIA_Text_TextChangedEventId,
                                      eventRoot.Get(), d.Get());
      u->RemoveAutomationEventHandler(UIA_LayoutInvalidatedEventId,
                                      eventRoot.Get(), d.Get());
      u->RemovePropertyChangedEventHandler(eventRoot.Get(), d.Get());
    }
    eventRoot = root;
    if (root) {
      u->AddAutomationEventHandler(UIA_Text_TextChangedEventId, root,
                                   TreeScope_Subtree, nullptr, d.Get());
      u->AddAutomationEventHandler(UIA_LayoutInvalidatedEventId, root,
                                   TreeScope_Subtree, nullptr, d.Get());
      PROPERTYID props[] = {UIA_BoundingRectanglePropertyId,
                            UIA_IsOffscreenPropertyId};
      u->AddPropertyChangedEventHandlerNativeArray(root, TreeScope_Subtree,
                                                   nullptr, d.Get(), props, 2);
    }
  }
  bool own_overlay(HWND window) {
    if (!window)
      return false;
    DWORD pid{};
    GetWindowThreadProcessId(window, &pid);
    if (pid != owner)
      return false;
    wchar_t name[128]{};
    int count = GetClassNameW(window, name, 128);
    return count > 0 &&
           std::wstring_view(name, count) == L"CodexLanguageFix.Overlay";
  }
  ComPtr<IUIAutomationElement> resolve(const Snapshot &s, bool exact) {
    HWND foreground = GetForegroundWindow();
    auto host = reinterpret_cast<HWND>(s.host_window);
    if (!host || (foreground != host && !own_overlay(foreground)) ||
        editor_host(s.process_id) == Host::other)
      return {};
    DWORD hostPid{};
    GetWindowThreadProcessId(host, &hostPid);
    const auto kind = editor_host(hostPid);
    if (kind == Host::other || kind != editor_host(s.process_id))
      return {};
    ComPtr<IUIAutomationElement> root;
    check(u->ElementFromHandle(host, &root));
    ComPtr<IUIAutomationElement> match;
    auto consider = [&](IUIAutomationElement *e) {
      int pid{};
      check(e->get_CurrentProcessId(&pid));
      if (pid != int(s.process_id))
        return;
      if (rid(e) != s.runtime_id || !eligible(e) ||
          (exact && read(e) != s.text))
        return;
      if (match)
        throw std::runtime_error("ambiguous_identity");
      match = e;
    };
    if (foreground == host) {
      ComPtr<IUIAutomationElement> f;
      check(u->GetFocusedElement(&f));
      for (int n = 0; f && n < 12; ++n) {
        int pid{};
        check(f->get_CurrentProcessId(&pid));
        if (pid != int(s.process_id))
          break;
        if (rid(f.Get()) == s.runtime_id) {
          consider(f.Get());
          return match;
        }
        if (editable(f.Get()))
          return {};
        f = parent(f.Get());
      }
    }
    if (kind == Host::learn_harness)
      return {};
    // A noneditable Codex toolbar control or the trusted overlay may have
    // displaced focus. A different editable field was rejected above; resolve
    // only the original HWND and exact identity, never a historical COM object.
    consider(root.Get());
    for (auto &e : controls(root.Get()))
      consider(e.Get());
    return match;
  }
  ComPtr<IUIAutomationTextRange> clone(IUIAutomationTextRange *r) {
    ComPtr<IUIAutomationTextRange> x;
    check(r->Clone(&x));
    return x;
  }
  ComPtr<IUIAutomationTextRange> range(IUIAutomationElement *e, int start,
                                       const std::wstring &expected) {
    auto p = pattern(e);
    ComPtr<IUIAutomationTextRange> doc;
    check(p->get_DocumentRange(&doc));
    auto full = text(doc.Get());
    if (start < 0 || size_t(start) > full.size() ||
        full.substr(start, expected.size()) != expected)
      return {};
    if (expected.empty()) {
      if (full.empty())
        return clone(doc.Get());
      auto boundaries = grapheme_boundaries(full);
      auto at = std::lower_bound(boundaries.begin(), boundaries.end(), start);
      if (start != int(full.size()) && (at == boundaries.end() || *at != start))
        return {};
      int anchorStart{}, anchorLength{};
      if (start == 0) {
        anchorLength = boundaries.size() > 1 ? boundaries[1] : int(full.size());
      } else {
        if (at == boundaries.begin())
          return {};
        anchorStart = *(at - 1);
        anchorLength = start - anchorStart;
      }
      auto caret =
          range(e, anchorStart, full.substr(anchorStart, anchorLength));
      if (!caret)
        return {};
      check(caret->MoveEndpointByRange(
          start == 0 ? TextPatternRangeEndpoint_End
                     : TextPatternRangeEndpoint_Start,
          caret.Get(),
          start == 0 ? TextPatternRangeEndpoint_Start
                     : TextPatternRangeEndpoint_End));
      auto prefix = clone(doc.Get());
      check(prefix->MoveEndpointByRange(TextPatternRangeEndpoint_End,
                                        caret.Get(),
                                        TextPatternRangeEndpoint_Start));
      int compare{};
      check(caret->CompareEndpoints(TextPatternRangeEndpoint_Start, caret.Get(),
                                    TextPatternRangeEndpoint_End, &compare));
      return compare == 0 && text(prefix.Get()) == full.substr(0, start) &&
                     text(caret.Get()).empty()
                 ? caret
                 : ComPtr<IUIAutomationTextRange>{};
    }
    auto remaining = clone(doc.Get());
    bool startAnchored = false, endAnchored = false;
    VARIANT v;
    VariantInit(&v);
    v.vt = VT_I4;
    v.lVal = UIA_TextControlTypeId;
    ComPtr<IUIAutomationCondition> tc;
    check(u->CreatePropertyCondition(UIA_ControlTypePropertyId, v, &tc));
    ComPtr<IUIAutomationElementArray> children;
    check(e->FindAll(TreeScope_Descendants, tc.Get(), &children));
    int childCount{};
    check(children->get_Length(&childCount));
    for (int i = 0; i < childCount; ++i) {
      ComPtr<IUIAutomationElement> child;
      check(children->GetElement(i, &child));
      ComPtr<IUIAutomationTextRange> childRange;
      check(p->RangeFromChild(child.Get(), &childRange));
      auto prefix = clone(doc.Get());
      check(prefix->MoveEndpointByRange(TextPatternRangeEndpoint_End,
                                        childRange.Get(),
                                        TextPatternRangeEndpoint_Start));
      auto pt = text(prefix.Get()), rt = text(childRange.Get());
      auto anchor = [&](int offset) {
        return pt.size() <= full.size() &&
               rt.size() <= full.size() - pt.size() &&
               offset >= int(pt.size()) &&
               offset - int(pt.size()) < int(rt.size()) &&
               pt == full.substr(0, pt.size()) &&
               rt == full.substr(pt.size(), rt.size()) &&
               rt.substr(0, offset - pt.size()).find_first_of(L"\r\n") ==
                   std::wstring::npos;
      };
      if (!startAnchored && anchor(start)) {
        check(remaining->MoveEndpointByRange(TextPatternRangeEndpoint_Start,
                                             childRange.Get(),
                                             TextPatternRangeEndpoint_Start));
        startAnchored = true;
      }
      if (!endAnchored && anchor(start + int(expected.size()) - 1)) {
        check(remaining->MoveEndpointByRange(TextPatternRangeEndpoint_End,
                                             childRange.Get(),
                                             TextPatternRangeEndpoint_End));
        endAnchored = true;
      }
      if (startAnchored && endAnchored)
        break;
    }
    int previous = -1;
    for (size_t n = 0; n <= full.size(); ++n) {
      BSTR bs = SysAllocStringLen(expected.data(), UINT(expected.size()));
      ComPtr<IUIAutomationTextRange> f;
      auto hr = remaining->FindText(bs, FALSE, FALSE, &f);
      SysFreeString(bs);
      check(hr);
      if (!f)
        return {};
      int lower{}, upper{}, length{};
      check(f->CompareEndpoints(TextPatternRangeEndpoint_Start, remaining.Get(),
                                TextPatternRangeEndpoint_Start, &lower));
      check(f->CompareEndpoints(TextPatternRangeEndpoint_End, doc.Get(),
                                TextPatternRangeEndpoint_End, &upper));
      check(f->CompareEndpoints(TextPatternRangeEndpoint_Start, f.Get(),
                                TextPatternRangeEndpoint_End, &length));
      if (lower < 0 || upper > 0 || length >= 0)
        return {};
      auto pre = clone(doc.Get());
      check(pre->MoveEndpointByRange(TextPatternRangeEndpoint_End, f.Get(),
                                     TextPatternRangeEndpoint_Start));
      auto prefix = text(pre.Get());
      if (int(prefix.size()) <= previous || prefix.size() > size_t(start) ||
          prefix != full.substr(0, prefix.size()) || text(f.Get()) != expected)
        return {};
      if (prefix.size() == size_t(start)) {
        check(pre->MoveEndpointByRange(TextPatternRangeEndpoint_End, f.Get(),
                                       TextPatternRangeEndpoint_End));
        return text(pre.Get()) == full.substr(0, start + expected.size())
                   ? f
                   : ComPtr<IUIAutomationTextRange>{};
      }
      previous = int(prefix.size());
      auto one = clone(f.Get());
      check(one->MoveEndpointByRange(TextPatternRangeEndpoint_End, f.Get(),
                                     TextPatternRangeEndpoint_Start));
      int moved{};
      check(one->MoveEndpointByUnit(TextPatternRangeEndpoint_End,
                                    TextUnit_Character, 1, &moved));
      int fromFound{}, fromRemaining{}, fromDocument{};
      check(one->CompareEndpoints(TextPatternRangeEndpoint_End, f.Get(),
                                  TextPatternRangeEndpoint_Start, &fromFound));
      check(one->CompareEndpoints(TextPatternRangeEndpoint_End, remaining.Get(),
                                  TextPatternRangeEndpoint_Start,
                                  &fromRemaining));
      check(one->CompareEndpoints(TextPatternRangeEndpoint_End, doc.Get(),
                                  TextPatternRangeEndpoint_End, &fromDocument));
      if (moved != 1 || fromFound <= 0 || fromRemaining <= 0 ||
          fromDocument > 0)
        return {};
      check(remaining->MoveEndpointByRange(TextPatternRangeEndpoint_Start,
                                           one.Get(),
                                           TextPatternRangeEndpoint_End));
    }
    return {};
  }
  SelectionCheck selection(IUIAutomationElement *e, const Snapshot &s,
                           const std::wstring &before, int start,
                           const std::wstring &expected) {
    if (!current(e, s))
      return SelectionCheck::editor_changed;
    if (read(e) != before)
      return SelectionCheck::source_changed;
    auto p = pattern(e);
    ComPtr<IUIAutomationTextRangeArray> a;
    check(p->GetSelection(&a));
    int n{};
    check(a->get_Length(&n));
    if (n != 1)
      return SelectionCheck::count_mismatch;
    ComPtr<IUIAutomationTextRange> sel, doc;
    check(a->GetElement(0, &sel));
    check(p->get_DocumentRange(&doc));
    auto pre = clone(doc.Get()), through = clone(doc.Get()),
         suf = clone(doc.Get()), chosen = clone(doc.Get());
    check(pre->MoveEndpointByRange(TextPatternRangeEndpoint_End, sel.Get(),
                                   TextPatternRangeEndpoint_Start));
    check(through->MoveEndpointByRange(TextPatternRangeEndpoint_End, sel.Get(),
                                       TextPatternRangeEndpoint_End));
    check(suf->MoveEndpointByRange(TextPatternRangeEndpoint_Start, sel.Get(),
                                   TextPatternRangeEndpoint_End));
    check(chosen->MoveEndpointByRange(TextPatternRangeEndpoint_End, sel.Get(),
                                      TextPatternRangeEndpoint_End));
    check(chosen->MoveEndpointByRange(TextPatternRangeEndpoint_Start, sel.Get(),
                                      TextPatternRangeEndpoint_Start));
    auto phase = [](const char *p) {
      DWORD n{};
      WriteFile(GetStdHandle(STD_ERROR_HANDLE), p, DWORD(strlen(p)), &n,
                nullptr);
    };
    phase("uia-phase:selection_document\n");
    if (text(doc.Get()) != before)
      return SelectionCheck::text_mismatch;
    phase("uia-phase:selection_prefix\n");
    if (text(pre.Get()) != before.substr(0, start))
      return SelectionCheck::text_mismatch;
    phase("uia-phase:selection_through\n");
    if (text(through.Get()) != before.substr(0, start + expected.size()))
      return SelectionCheck::text_mismatch;
    phase("uia-phase:selection_selected\n");
    if (text(chosen.Get()) != expected)
      return SelectionCheck::text_mismatch;
    phase("uia-phase:selection_suffix\n");
    if (text(suf.Get()) != before.substr(start + expected.size()))
      return SelectionCheck::text_mismatch;
    if (!current(e, s))
      return SelectionCheck::editor_changed;
    if (read(e) != before)
      return SelectionCheck::source_changed;
    return SelectionCheck::exact;
  }
  static bool modifiers() {
    for (int k : {VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN})
      if (GetAsyncKeyState(k) < 0)
        return false;
    return true;
  }
  static INPUT key(WORD k, DWORD flags = 0) {
    INPUT x{};
    x.type = INPUT_KEYBOARD;
    x.ki.wVk = k;
    x.ki.dwFlags = flags;
    return x;
  }
  static bool simple(const std::wstring &s) {
    return valid_utf16(s) && std::none_of(s.begin(), s.end(), [](wchar_t c) {
             return c < 32 || (c >= 127 && c <= 159) || c == 0xfffc ||
                    c == 0x2028 || c == 0x2029;
           });
  }
  static bool selectkeys(const std::wstring &s, bool extend = true) {
    if (!simple(s) || s.empty() || !modifiers())
      return false;
    std::vector<INPUT> v;
    if (extend)
      v.push_back(key(VK_SHIFT));
    for (auto offset : grapheme_boundaries(s)) {
      (void)offset;
      v.push_back(key(VK_RIGHT, KEYEVENTF_EXTENDEDKEY));
      v.push_back(key(VK_RIGHT, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP));
    }
    if (extend)
      v.push_back(key(VK_SHIFT, KEYEVENTF_KEYUP));
    auto sent = SendInput(UINT(v.size()), v.data(), sizeof(INPUT));
    if (sent == v.size())
      return true;
    if (sent) {
      std::vector<INPUT> release;
      if (extend) {
        if (sent > 1 && sent % 2 == 0)
          release.push_back(
              key(VK_RIGHT, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP));
        release.push_back(key(VK_SHIFT, KEYEVENTF_KEYUP));
      } else if (sent % 2)
        release.push_back(
            key(VK_RIGHT, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP));
      if (!release.empty())
        SendInput(UINT(release.size()), release.data(), sizeof(INPUT));
    }
    return false;
  }
  static bool input(const std::wstring &s) {
    if (!simple(s) || !modifiers())
      return false;
    std::vector<INPUT> v;
    if (s.empty())
      v = {key(VK_BACK), key(VK_BACK, KEYEVENTF_KEYUP)};
    else
      for (auto c : s) {
        auto x = key(0, KEYEVENTF_UNICODE);
        x.ki.wScan = c;
        v.push_back(x);
        x.ki.dwFlags |= KEYEVENTF_KEYUP;
        v.push_back(x);
      }
    return SendInput(UINT(v.size()), v.data(), sizeof(INPUT)) == v.size();
  }
  template <class F> bool wait(F f) {
    for (int n = 0; n < 25; ++n) {
      if (f())
        return true;
      Sleep(20);
    }
    return false;
  }
  bool patch(IUIAutomationElement *e, const Snapshot &s, int start,
             const std::wstring &old, const std::wstring &replacement) {
    if (!current(e, s) || !simple(old) || !simple(replacement))
      return false;
    auto before = read(e);
    if (start < 0 || size_t(start) > before.size() ||
        before.substr(start, old.size()) != old)
      return false;
    int selectionStart = start;
    std::wstring anchorText = old;
    if (old.empty() && !before.empty()) {
      auto boundaries = grapheme_boundaries(before);
      auto at = std::lower_bound(boundaries.begin(), boundaries.end(), start);
      if (start != int(before.size()) &&
          (at == boundaries.end() || *at != start))
        return false;
      if (start == 0) {
        anchorText = before.substr(
            0, boundaries.size() > 1 ? boundaries[1] : int(before.size()));
      } else {
        if (at == boundaries.begin())
          return false;
        selectionStart = *(at - 1);
        anchorText = before.substr(selectionStart, start - selectionStart);
      }
    }
    auto r = range(e, selectionStart, anchorText);
    if (!r)
      return false;
    auto caret = clone(r.Get());
    check(caret->MoveEndpointByRange(TextPatternRangeEndpoint_End, caret.Get(),
                                     TextPatternRangeEndpoint_Start));
    if (!current(e, s) || read(e) != before)
      return false;
    check(caret->Select());
    if (wait_for_selection_exact([&] {
          return selection(e, s, before, selectionStart, L"");
        }) != SelectionCheck::exact)
      return false;
    if (!current(e, s) || read(e) != before)
      return false;
    if (old.empty()) {
      if (selectionStart < start && !selectkeys(anchorText, false))
        return false;
    } else if (!selectkeys(old))
      return false;
    if (wait_for_selection_exact([&] {
          return selection(e, s, before, start, old);
        }) != SelectionCheck::exact)
      return false;
    if (!current(e, s) || read(e) != before)
      return false;
    if (!input(replacement))
      return false;
    auto after = before;
    after.replace(start, old.size(), replacement);
    return wait_for_exact(
               after, [&]() -> std::optional<std::wstring> { return read(e); },
               [&] { return current(e, s); }) == WriteCheck::exact;
  }
  Rect bounds(IUIAutomationElement *e) {
    RECT r{};
    check(e->get_CurrentBoundingRectangle(&r));
    return rect(r);
  }
  int type(IUIAutomationElement *e) {
    int t{};
    check(e->get_CurrentControlType(&t));
    return t;
  }
  bool contains(Rect a, Rect b) {
    return !a.empty && !b.empty && a.x <= b.x && a.y <= b.y &&
           a.x + a.width >= b.x + b.width && a.y + a.height >= b.y + b.height;
  }
  bool intersects(Rect a, Rect b) {
    return !a.empty && !b.empty && a.x < b.x + b.width && b.x < a.x + a.width &&
           a.y < b.y + b.height && b.y < a.y + a.height;
  }
  Rect intersection(Rect a, Rect b) {
    double x = std::max(a.x, b.x), y = std::max(a.y, b.y),
           r = std::min(a.x + a.width, b.x + b.width),
           bt = std::min(a.y + a.height, b.y + b.height);
    return {x, y, std::max(0., r - x), std::max(0., bt - y), r <= x || bt <= y};
  }
  ComPtr<IUIAutomationElement> parent(IUIAutomationElement *e) {
    ComPtr<IUIAutomationElement> p;
    check(w->GetParentElement(e, &p));
    return p;
  }
  ComPtr<IUIAutomationElement> document(IUIAutomationElement *e) {
    ComPtr<IUIAutomationElement> p = e;
    for (int n = 0; p && n < 20; ++n) {
      if (type(p.Get()) == UIA_DocumentControlTypeId &&
          lower(property(p.Get(), UIA_AutomationIdPropertyId)) ==
              L"rootwebarea")
        return p;
      p = parent(p.Get());
    }
    return {};
  }
  int child_count(IUIAutomationElement *e) {
    ComPtr<IUIAutomationElement> child;
    check(geometryWalker->GetFirstChildElement(e, &child));
    int count = 0;
    while (child) {
      ++geometrySteps;
      ++count;
      ComPtr<IUIAutomationElement> nextChild;
      check(geometryWalker->GetNextSiblingElement(child.Get(), &nextChild));
      child = nextChild;
    }
    return count;
  }
  void record_geometry(IUIAutomationElement *e) {
    if (!recordingRoot)
      return;
    auto id = rid(e);
    if (std::any_of(geometryNodes.begin(), geometryNodes.end(),
                    [&](auto &n) { return n.id == id; }))
      return;
    GeometryNode node;
    node.id = id;
    node.box = bounds(e);
    BOOL off{};
    check(e->get_CurrentIsOffscreen(&off));
    node.offscreen = off != FALSE;
    node.children = child_count(e);
    ComPtr<IUIAutomationElement> cur = e;
    auto rootId = rid(recordingRoot);
    for (int depth = 0; rid(cur.Get()) != rootId; ++depth) {
      if (depth >= 40)
        throw std::runtime_error("geometry_scope");
      int index = 0;
      ComPtr<IUIAutomationElement> previous;
      check(geometryWalker->GetPreviousSiblingElement(cur.Get(), &previous));
      while (previous) {
        ++index;
        ComPtr<IUIAutomationElement> before;
        check(
            geometryWalker->GetPreviousSiblingElement(previous.Get(), &before));
        previous = before;
      }
      node.path.push_back(index);
      ComPtr<IUIAutomationElement> parentNode;
      check(geometryWalker->GetParentElement(cur.Get(), &parentNode));
      if (!parentNode)
        throw std::runtime_error("geometry_scope");
      cur = parentNode;
    }
    std::reverse(node.path.begin(), node.path.end());
    geometryNodes.push_back(std::move(node));
    ComPtr<IUIAutomationElement> ancestor;
    check(geometryWalker->GetParentElement(e, &ancestor));
    if (ancestor && rid(e) != rootId)
      record_geometry(ancestor.Get());
  }
  bool geometry_matches(IUIAutomationElement *root) {
    if (!layoutValid || geometryNodes.empty())
      return false;
    try {
      ++geometryChecks;
      // These references exist only during this read. The retained fingerprint
      // is plain identity/geometry data; shared prefixes are traversed once per
      // poll.
      std::map<std::vector<int>, ComPtr<IUIAutomationElement>> resolved;
      std::map<std::vector<int>, std::vector<ComPtr<IUIAutomationElement>>>
          children;
      resolved[{}] = root;
      auto getChildren = [&](const std::vector<int> &path,
                             IUIAutomationElement *element)
          -> const std::vector<ComPtr<IUIAutomationElement>> & {
        auto [at, inserted] = children.try_emplace(path);
        if (inserted) {
          ComPtr<IUIAutomationElement> child;
          check(geometryWalker->GetFirstChildElement(element, &child));
          while (child) {
            ++geometrySteps;
            at->second.push_back(child);
            ComPtr<IUIAutomationElement> nextChild;
            check(
                geometryWalker->GetNextSiblingElement(child.Get(), &nextChild));
            child = nextChild;
          }
        }
        return at->second;
      };
      for (auto &node : geometryNodes) {
        ++geometryNodeChecks;
        std::vector<int> prefix;
        ComPtr<IUIAutomationElement> element = root;
        for (int index : node.path) {
          auto &siblings = getChildren(prefix, element.Get());
          if (index < 0 || size_t(index) >= siblings.size())
            return false;
          prefix.push_back(index);
          auto [at, inserted] = resolved.try_emplace(prefix, siblings[index]);
          element = at->second;
        }
        BOOL off{};
        check(element->get_CurrentIsOffscreen(&off));
        if (rid(element.Get()) != node.id ||
            bounds(element.Get()) != node.box ||
            (off != FALSE) != node.offscreen ||
            getChildren(node.path, element.Get()).size() !=
                size_t(node.children))
          return false;
        if (node.text_bounds) {
          auto doc = document(element.Get());
          if (visible(element.Get(), doc.Get()) != node.text_bounds)
            return false;
        }
      }
      return true;
    } catch (...) {
      return false;
    }
  }
  void record_text_geometry(IUIAutomationElement *element,
                            const std::vector<Rect> &boxes) {
    if (broadGeometry)
      return;
    record_geometry(element);
    const auto id = rid(element);
    auto node = std::find_if(geometryNodes.begin(), geometryNodes.end(),
                             [&](const auto &value) { return value.id == id; });
    if (node == geometryNodes.end())
      throw std::runtime_error("geometry_text_scope");
    node->text_bounds = boxes;
  }
  std::vector<ComPtr<IUIAutomationElement>>
  controls(IUIAutomationElement *scope) {
    if (recordingRoot && (type(scope) == UIA_DocumentControlTypeId ||
                          type(scope) == UIA_WindowControlTypeId ||
                          !surface(geometryEditor, bounds(scope)))) {
      broadGeometry = true;
    }
    ComPtr<IUIAutomationCondition> condition;
    check(u->CreateTrueCondition(&condition));
    ComPtr<IUIAutomationElementArray> a;
    check(scope->FindAll(TreeScope_Descendants, condition.Get(), &a));
    int n{};
    check(a->get_Length(&n));
    std::vector<ComPtr<IUIAutomationElement>> result;
    for (int i = 0; i < n; ++i) {
      ComPtr<IUIAutomationElement> e;
      check(a->GetElement(i, &e));
      int pid{};
      check(e->get_CurrentProcessId(&pid));
      if (editor_host(pid) == Host::other)
        continue;
      BOOL off{};
      check(e->get_CurrentIsOffscreen(&off));
      if (recordingRoot && !broadGeometry)
        record_geometry(e.Get());
      if (!off)
        result.push_back(e);
    }
    return result;
  }
  std::optional<std::vector<Rect>>
  visible(IUIAutomationElement *e, IUIAutomationElement *doc = nullptr) {
    std::vector<Rect> out;
    try {
      ComPtr<IUIAutomationTextPattern> p;
      ComPtr<IUIAutomationTextRange> range;
      if (SUCCEEDED(
              e->GetCurrentPatternAs(UIA_TextPatternId, IID_PPV_ARGS(&p))) &&
          p)
        check(p->get_DocumentRange(&range));
      else if (doc) {
        p = pattern(doc);
        check(p->RangeFromChild(e, &range));
      }
      if (!range)
        return {};
      int rangeLength{};
      check(range->CompareEndpoints(TextPatternRangeEndpoint_Start, range.Get(),
                                    TextPatternRangeEndpoint_End,
                                    &rangeLength));
      if (rangeLength == 0)
        return {};
      SAFEARRAY *a{};
      check(range->GetBoundingRectangles(&a));
      if (!a)
        return {};
      LONG lo{}, hi{};
      SafeArrayGetLBound(a, 1, &lo);
      SafeArrayGetUBound(a, 1, &hi);
      for (LONG k = lo; k + 3 <= hi; k += 4) {
        double v[4]{};
        for (LONG i = 0; i < 4; ++i) {
          LONG at = k + i;
          check(SafeArrayGetElement(a, &at, &v[i]));
        }
        Rect r{v[0], v[1], v[2], v[3], v[2] <= 0 || v[3] <= 0};
        if (r.empty || !std::isfinite(v[0]) || !std::isfinite(v[1]) ||
            !std::isfinite(v[2]) || !std::isfinite(v[3])) {
          SafeArrayDestroy(a);
          return {};
        }
        out.push_back(r);
      }
      SafeArrayDestroy(a);
    } catch (...) {
      return {};
    }
    return out;
  }
  bool surface(Rect editor, Rect candidate) {
    return !editor.empty && !candidate.empty &&
           candidate.width >= editor.width &&
           (candidate.width >= editor.width + 24 ||
            candidate.height >= editor.height + 24) &&
           candidate.height >= editor.height &&
           candidate.height <= std::max(240., editor.height + 160) &&
           contains(candidate, editor);
  }
  bool associated(IUIAutomationElement *scope, Rect box, Rect editor) {
    for (auto &e : controls(scope)) {
      auto b = bounds(e.Get());
      if (type(e.Get()) == UIA_ButtonControlTypeId && contains(box, b) &&
          !intersects(editor, b))
        return true;
    }
    return false;
  }
  bool obstacle(IUIAutomationElement *e,
                const std::vector<std::vector<int>> &ancestors) {
    int t = type(e);
    if (shape(e))
      return true;
    if (t == UIA_DocumentControlTypeId)
      return false;
    bool ancestor = std::find(ancestors.begin(), ancestors.end(), rid(e)) !=
                    ancestors.end();
    return !ancestor ||
           (t != UIA_GroupControlTypeId && t != UIA_PaneControlTypeId &&
            t != UIA_WindowControlTypeId && t != UIA_CustomControlTypeId);
  }
  void layout(Snapshot &s, IUIAutomationElement *e,
              IUIAutomationElement *windowRoot) {
    ++fullLayouts;
    geometryNodes.clear();
    broadGeometry = false;
    geometryEditor = *s.editor_bounds;
    layoutValid = false;
    recordingRoot = windowRoot;
    struct RecordingCleanup {
      IUIAutomationElement *&root;
      ~RecordingCleanup() { root = nullptr; }
    } cleanup{recordingRoot};
    record_geometry(e);
    auto editor = *s.editor_bounds;
    auto doc = document(e);
    if (!doc)
      doc = windowRoot;
    std::vector<std::vector<int>> ancestors;
    for (auto p = parent(e); p && ancestors.size() < 30; p = parent(p.Get()))
      ancestors.push_back(rid(p.Get()));
    ComPtr<IUIAutomationElement> scope = e;
    bool found = s.field_category != "chat";
    for (int n = 0; !found && n < 6; ++n) {
      auto p = parent(scope.Get());
      if (!p || type(p.Get()) == UIA_DocumentControlTypeId)
        break;
      scope = p;
      auto candidate = bounds(scope.Get());
      if (surface(editor, candidate) &&
          associated(scope.Get(), candidate, editor)) {
        s.bounds = candidate;
        found = true;
        break;
      }
    }
    s.occupied = std::vector<Rect>{editor};
    auto anchor = [&](Rect r) {
      if (r.height >= 24 && r.width > r.height * 1.5 &&
          (!s.right_control || r.x > s.right_control->x))
        s.right_control = r;
    };
    if (found)
      layoutValid = true;
    if (found && s.field_category == "chat") {
      for (auto &c : controls(scope.Get())) {
        BOOL focusable{};
        check(c->get_CurrentIsKeyboardFocusable(&focusable));
        int t = type(c.Get());
        if (!focusable && t != UIA_TextControlTypeId)
          continue;
        if (!obstacle(c.Get(), ancestors))
          continue;
        auto b = bounds(c.Get());
        if (intersects(b, s.bounds)) {
          auto texts = t == UIA_TextControlTypeId && !contains(editor, b)
                           ? visible(c.Get(), doc.Get())
                           : std::optional<std::vector<Rect>>{};
          if (!texts || texts->empty())
            s.occupied->push_back(b);
          else {
            record_text_geometry(c.Get(), *texts);
            s.occupied->insert(s.occupied->end(), texts->begin(), texts->end());
          }
        }
        if (t == UIA_ButtonControlTypeId && !b.empty && b.width >= 24 &&
            b.x >= s.bounds.x + s.bounds.width * .35 &&
            b.x < s.bounds.x + s.bounds.width &&
            b.y + b.height >= s.bounds.y - 12 &&
            b.y <=
                s.bounds.y + s.bounds.height + std::max(48., s.bounds.height))
          anchor(b);
      }
    } else if (!found) {
      scope = doc;
      for (auto p = parent(e); p; p = parent(p.Get())) {
        if (rid(p.Get()) == rid(doc.Get()))
          break;
        auto b = bounds(p.Get());
        if ((surface(editor, b) && associated(p.Get(), b, editor)) ||
            (type(p.Get()) == UIA_WindowControlTypeId && contains(b, editor))) {
          scope = p;
          break;
        }
      }
      std::vector<ComposerControlBounds> controlBounds;
      double scale =
          GetDpiForWindow(reinterpret_cast<HWND>(s.host_window)) / 96.;
      Rect nearby{editor.x - 200 * scale, editor.y - 100 * scale,
                  editor.width + 400 * scale, editor.height + 200 * scale,
                  false};
      for (auto &c : controls(scope.Get())) {
        int t = type(c.Get());
        BOOL focusable{};
        check(c->get_CurrentIsKeyboardFocusable(&focusable));
        if (!focusable && t != UIA_ButtonControlTypeId &&
            t != UIA_TextControlTypeId && t != UIA_EditControlTypeId)
          continue;
        if (!obstacle(c.Get(), ancestors))
          continue;
        auto b = bounds(c.Get());
        auto texts = t == UIA_TextControlTypeId && !contains(editor, b) &&
                             intersects(b, nearby)
                         ? visible(c.Get(), doc.Get())
                         : std::optional<std::vector<Rect>>{};
        if (!texts || texts->empty())
          texts = std::vector<Rect>{b};
        for (auto r : *texts)
          controlBounds.push_back(
              {r, t == UIA_ButtonControlTypeId,
               t == UIA_EditControlTypeId || shape(c.Get())});
      }
      if (auto a = associate_toolbar(
              editor, bounds(scope.Get()), controlBounds,
              GetDpiForWindow(reinterpret_cast<HWND>(s.host_window)) / 96.)) {
        layoutValid = true;
        s.bounds = a->surface_bounds;
        s.occupied = a->occupied_bounds;
        for (auto r : a->row_buttons)
          anchor(r);
      }
    }
    auto available = intersection(bounds(doc.Get()), bounds(windowRoot));
    scope = doc;
    auto p = parent(e);
    if (p && contains(bounds(p.Get()), editor)) {
      scope = p;
      available = intersection(available, bounds(p.Get()));
    }
    for (int n = 0; p && n < 20; ++n) {
      if (rid(p.Get()) == rid(doc.Get()))
        break;
      if (type(p.Get()) == UIA_WindowControlTypeId) {
        available = intersection(available, bounds(p.Get()));
        scope = p;
        break;
      }
      p = parent(p.Get());
    }
    if (contains(available, editor)) {
      s.placement_bounds = available;
      double pad =
          36 * GetDpiForWindow(reinterpret_cast<HWND>(s.host_window)) / 96.;
      Rect nearby =
          intersection({editor.x - pad, editor.y - pad, editor.width + 2 * pad,
                        editor.height + 2 * pad, false},
                       available);
      for (auto &c : controls(scope.Get())) {
        int t = type(c.Get());
        BOOL focusable{};
        check(c->get_CurrentIsKeyboardFocusable(&focusable));
        if (!focusable && t != UIA_ButtonControlTypeId &&
            t != UIA_TextControlTypeId && t != UIA_EditControlTypeId &&
            t != UIA_ImageControlTypeId)
          continue;
        auto b = bounds(c.Get());
        if (!intersects(nearby, b) || !obstacle(c.Get(), ancestors))
          continue;
        auto texts = t == UIA_TextControlTypeId
                         ? visible(c.Get(), doc.Get())
                         : std::optional<std::vector<Rect>>{};
        if (!texts || texts->empty())
          s.occupied->push_back(b);
        else {
          record_text_geometry(c.Get(), *texts);
          s.occupied->insert(s.occupied->end(), texts->begin(), texts->end());
        }
      }
    }
    s.editor_text_bounds = visible(e);
    // A document-wide fallback is re-evaluated as an unresolved local scope.
    // It must never masquerade as a cheap geometry-cache hit or retain the chat
    // tree.
    if (broadGeometry) {
      layoutValid = false;
      geometryNodes.clear();
    }
  }

public:
  Accessor(DWORD parentId) : owner(parentId) {
    check(CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                           IID_PPV_ARGS(&u)));
    check(u->get_ControlViewWalker(&w));
    check(u->get_RawViewWalker(&geometryWalker));
    d.Attach(new Dirty);
    check(u->AddFocusChangedEventHandler(nullptr, d.Get()));
  }
  ~Accessor() {
    u->RemoveAllEventHandlers();
    eventRoot.Reset();
    d.Reset();
  }
  void invalidate() {
    d->flags.fetch_or(4);
    cache.reset();
    layoutValid = false;
  }
  std::optional<Snapshot> capture(bool includeLayout, bool require) {
    ++captures;
    captureReason = CaptureReason::uia_operation_failed;
    try {
      auto dirty = d->flags.exchange(0);
      if (!includeLayout && (dirty & 4u))
        d->flags.fetch_or(4);
      HWND hwnd = GetForegroundWindow();
      DWORD pid{};
      GetWindowThreadProcessId(hwnd, &pid);
      const auto host = editor_host(pid);
      if (!hwnd || host == Host::other) {
        captureReason = hwnd ? CaptureReason::other_host
                             : CaptureReason::foreground_missing;
        events(nullptr);
        cache.reset();
        return {};
      }
      ComPtr<IUIAutomationElement> root;
      check(u->ElementFromHandle(hwnd, &root));
      events(root.Get());
      ComPtr<IUIAutomationElement> e, f;
      check(u->GetFocusedElement(&f));
      auto originalFocus = f;
      bool foreignFocusedHost = false;
      for (int n = 0; f && n < 16; ++n) {
        int p{};
        check(f->get_CurrentProcessId(&p));
        if (editor_host(p) != host) {
          foreignFocusedHost = n == 0;
          break;
        }
        bool isEditable = editable(f.Get());
        if (isEditable) {
          if (eligible(f.Get()))
            e = f;
          else {
            captureReason = CaptureReason::focused_non_composer;
            return {};
          }
          break;
        }
        f = parent(f.Get());
      }
      if (!e && !require && host == Host::codex) {
        auto searchRoot = root;
        if (originalFocus) {
          int focusedPid{};
          check(originalFocus->get_CurrentProcessId(&focusedPid));
          if (editor_host(focusedPid) == host) {
            auto focusedDocument = document(originalFocus.Get());
            if (focusedDocument)
              searchRoot = focusedDocument;
          }
        }
        for (auto &x : controls(searchRoot.Get())) {
          BOOL focusable{}, hasFocus{};
          check(x->get_CurrentIsKeyboardFocusable(&focusable));
          check(x->get_CurrentHasKeyboardFocus(&hasFocus));
          auto b = bounds(x.Get());
          if (focusable && eligible(x.Get()) && (shape(x.Get()) || hasFocus) &&
              !b.empty && b.width >= 40 && b.height >= 20) {
            if (e) {
              captureReason = CaptureReason::ambiguous_editors;
              return {};
            }
            e = x;
          }
        }
      }
      if (!e) {
        captureReason = foreignFocusedHost ? CaptureReason::focused_other_host
                        : require ? CaptureReason::no_keyboard_focused_composer
                                  : CaptureReason::no_matching_editor;
        return {};
      }
      Snapshot s;
      int ep{};
      check(e->get_CurrentProcessId(&ep));
      s.process_id = ep;
      s.host_window = reinterpret_cast<uint64_t>(hwnd);
      if (host == Host::learn_harness)
        s.host = "LearnHarness";
      s.runtime_id = rid(e.Get());
      s.text = read(e.Get());
      s.keyboard_focused = focused(e.Get());
      if (require && !s.keyboard_focused) {
        captureReason = CaptureReason::keyboard_focus_mismatch;
        return {};
      }
      s.bounds = bounds(e.Get());
      if (s.bounds.empty || s.bounds.width < 40 || s.bounds.height < 20) {
        captureReason = CaptureReason::invalid_editor_bounds;
        return {};
      }
      s.read_method = "text_pattern";
      s.field_category = shape(e.Get()) ? "chat" : "prose";
      if (!structured(e.Get())) {
        ComPtr<IUIAutomationValuePattern> v;
        if (SUCCEEDED(
                e->GetCurrentPatternAs(UIA_ValuePatternId, IID_PPV_ARGS(&v))) &&
            v)
          s.read_method = "value_pattern";
      }
      if (s.field_category == "chat" ||
          (host == Host::learn_harness && s.read_method == "text_pattern")) {
        auto name = property(e.Get(), UIA_NamePropertyId);
        auto trimmed = s.text;
        while (!trimmed.empty() &&
               (trimmed.back() == L'\r' || trimmed.back() == L'\n'))
          trimmed.pop_back();
        if ((!name.empty() && name == trimmed) ||
            (host == Host::learn_harness && s.text.find(L'\uFFFC') != s.text.npos)) {
          captureReason = CaptureReason::ambiguous_placeholder;
          return {};
        }
      }
      s.editor_bounds = s.bounds;
      bool reuse = includeLayout && cache && same_editor(*cache, s) &&
                   cache->editor_bounds == s.editor_bounds &&
                   cacheDpi == GetDpiForWindow(hwnd) && !(dirty & 4u) &&
                   geometry_matches(root.Get());
      if (reuse) {
        ++cacheHits;
        s.bounds = cache->bounds;
        s.occupied = cache->occupied;
        s.right_control = cache->right_control;
        s.placement_bounds = cache->placement_bounds;
        s.editor_text_bounds = visible(e.Get());
      } else if (includeLayout) {
        try {
          layout(s, e.Get(), root.Get());
        } catch (...) {
          layoutValid = false;
          s.bounds = *s.editor_bounds;
          s.occupied = std::vector<Rect>{*s.editor_bounds};
          s.right_control.reset();
          s.placement_bounds.reset();
        }
        cacheDpi = GetDpiForWindow(hwnd);
      }
      for (auto &x : ids)
        if (same_editor(x, s)) {
          s.token = x.token;
          x = identity_only(s);
          if (includeLayout)
            cache = s;
          captureReason = CaptureReason::captured;
          return s;
        }
      s.token = ++next;
      if (ids.size() == 16)
        ids.pop_front();
      ids.push_back(identity_only(s));
      if (includeLayout)
        cache = s;
      captureReason = CaptureReason::captured;
      return s;
    } catch (...) {
      captureReason = CaptureReason::uia_operation_failed;
      invalidate();
      return {};
    }
  }
  const char *capture_status() const { return capture_reason(captureReason); }
  std::optional<Snapshot> poll() {
    ++polls;
    return capture(true, false);
  }
  std::optional<Snapshot> refresh(uint64_t token) {
    captureReason = CaptureReason::uia_operation_failed;
    try {
      auto i = std::find_if(ids.begin(), ids.end(),
                            [&](auto &s) { return s.token == token; });
      if (i == ids.end()) {
        captureReason = CaptureReason::snapshot_expired;
        return {};
      }
      auto saved = *i;
      auto e = resolve(saved, false);
      if (!e) {
        captureReason = CaptureReason::refresh_identity_unavailable;
        return {};
      }
      saved.text = read(e.Get());
      saved.keyboard_focused = focused(e.Get());
      saved.editor_bounds = bounds(e.Get());
      if (saved.editor_bounds->empty) {
        captureReason = CaptureReason::invalid_editor_bounds;
        return {};
      }
      saved.bounds = *saved.editor_bounds;
      *i = identity_only(saved);
      captureReason = CaptureReason::captured;
      return saved;
    } catch (...) {
      captureReason = CaptureReason::uia_operation_failed;
      return {};
    }
  }
  ComposerDiagnostics diagnostics() const {
    ComposerDiagnostics result;
    result.capture_count = captures;
    result.poll_count = polls;
    result.full_layout_count = fullLayouts;
    result.geometry_cache_hit_count = cacheHits;
    result.geometry_check_count = geometryChecks;
    result.geometry_node_check_count = geometryNodeChecks;
    result.geometry_walker_step_count = geometrySteps;
    result.retained_tokens = ids.size();
    for (auto &s : ids)
      result.retained_identity_data_bytes +=
          sizeof(Snapshot) + s.runtime_id.size() * sizeof(int) +
          s.text.size() * sizeof(wchar_t) +
          (s.occupied ? s.occupied->size() * sizeof(Rect) : 0) +
          (s.editor_text_bounds ? s.editor_text_bounds->size() * sizeof(Rect)
                                : 0);
    for (auto &node : geometryNodes)
      result.retained_identity_data_bytes +=
          sizeof(GeometryNode) +
          (node.path.size() + node.id.size()) * sizeof(int) +
          (node.text_bounds ? node.text_bounds->size() * sizeof(Rect) : 0);
    result.idle_owned_com_refs = uint64_t(bool(u)) + uint64_t(bool(w)) +
                                 uint64_t(bool(geometryWalker)) +
                                 uint64_t(bool(d)) + uint64_t(bool(eventRoot));
    result.idle_owned_editor_com_refs = 0;
    return result;
  }
  WriteResult replace(uint64_t token, const std::wstring &expected,
                      const std::wstring &replacement) {
    WriteResult out;
    out.original = expected;
    out.status = "invalid_identity";
    auto i = std::find_if(ids.begin(), ids.end(),
                          [&](auto &s) { return s.token == token; });
    if (i == ids.end())
      return out;
    auto s = *i;
    s.text = expected;
    auto e = resolve(s, true);
    if (!e)
      return out;
    if (!focused(e.Get())) {
      if (GetForegroundWindow() != reinterpret_cast<HWND>(s.host_window) &&
          !own_overlay(GetForegroundWindow()))
        return out;
      check(e->SetFocus());
      if (!focused(e.Get()))
        return out;
    }
    auto plan = lastPlan && lastEditor && same_editor(*lastEditor, s) &&
                        lastPlan->corrected == expected &&
                        lastPlan->original == replacement
                    ? std::optional<EditPlan>(reverse_plan(*lastPlan))
                    : make_edit_plan(expected, replacement);
    if (!plan) {
      out.status = "unsafe_edit_plan";
      return out;
    }
    constexpr size_t side_question_max_chars = 8000;
    if (s.host == "LearnHarness" && s.read_method == "value_pattern" &&
        replacement.size() > side_question_max_chars) {
      out.status = "text_limit";
      return out;
    }
    struct Target : EditTarget {
      Accessor &a;
      IUIAutomationElement *e;
      Snapshot s;
      Target(Accessor &aa, IUIAutomationElement *ee, Snapshot ss)
          : a(aa), e(ee), s(std::move(ss)) {}
      bool is_current_editor() override { return a.current(e, s); }
      std::optional<std::wstring> read() override { return a.read(e); }
      bool can_replace(int start, std::wstring_view old) override {
        if (s.read_method == "value_pattern") {
          ComPtr<IUIAutomationValuePattern> v;
          if (FAILED(e->GetCurrentPatternAs(UIA_ValuePatternId,
                                            IID_PPV_ARGS(&v))) ||
              !v)
            return false;
          BOOL ro{};
          check(v->get_CurrentIsReadOnly(&ro));
          return !ro && start == 0 && a.read(e) == old;
        }
        return bool(a.range(e, start, std::wstring(old)));
      }
      bool replace(int start, std::wstring_view old,
                   std::wstring_view replacement) override {
        if (s.read_method == "value_pattern") {
          if (!can_replace(start, old) || !is_current_editor() ||
              a.read(e) != old)
            return false;
          ComPtr<IUIAutomationValuePattern> v;
          check(e->GetCurrentPatternAs(UIA_ValuePatternId, IID_PPV_ARGS(&v)));
          BSTR b =
              SysAllocStringLen(replacement.data(), UINT(replacement.size()));
          auto hr = v->SetValue(b);
          SysFreeString(b);
          check(hr);
          return wait_for_exact(
                     std::wstring(replacement), [&] { return read(); },
                     [&] { return is_current_editor(); }) == WriteCheck::exact;
        }
        return a.patch(e, s, start, std::wstring(old),
                       std::wstring(replacement));
      }
    } target(*this, e.Get(), s);
    if (s.read_method == "value_pattern") {
      plan->edits.clear();
      if (expected != replacement)
        plan->edits.push_back({0, expected, replacement});
    }
    out = apply_edit_plan(*plan, target);
    if (out.state == WriteState::applied) {
      lastPlan = *plan;
      lastEditor = identity_only(s);
    }
    invalidate();
    return out;
  }
};
} // namespace
int run_composer_worker(uint32_t parent_id) {
  Handle parent(OpenProcess(SYNCHRONIZE, FALSE, parent_id));
  if (!parent.h)
    return 2;
  std::atomic_bool done{};
  std::thread watch([&] {
    while (!done) {
      if (WaitForSingleObject(parent.h, 100) == WAIT_OBJECT_0)
        ExitProcess(1);
    }
  });
  int result = 1;
  HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  if (SUCCEEDED(hr)) {
    try {
      Accessor a(parent_id);
      while (auto req = receive(GetStdHandle(STD_INPUT_HANDLE))) {
        auto &j = *req;
        if (j.at("version") != 2)
          break;
        auto op = j.at("operation").get<std::string>();
        json response = {{"version", 2}, {"status", "ok"}};
        std::optional<Snapshot> s;
        if (op == "capture")
          s = a.capture(j.value("layout", true),
                        j.value("requireFocus", false));
        else if (op == "poll")
          s = a.poll();
        else if (op == "refresh")
          s = a.refresh(j.at("token"));
        else if (op == "invalidate")
          a.invalidate();
        else if (op == "diagnostics") {
          auto value = a.diagnostics();
          response["diagnostics"] = {
              {"capture_count", value.capture_count},
              {"poll_count", value.poll_count},
              {"full_layout_count", value.full_layout_count},
              {"geometry_cache_hit_count", value.geometry_cache_hit_count},
              {"geometry_check_count", value.geometry_check_count},
              {"geometry_node_check_count", value.geometry_node_check_count},
              {"geometry_walker_step_count", value.geometry_walker_step_count},
              {"retained_tokens", value.retained_tokens},
              {"retained_identity_data_bytes",
               value.retained_identity_data_bytes},
              {"idle_owned_com_refs", value.idle_owned_com_refs},
              {"idle_owned_editor_com_refs", value.idle_owned_editor_com_refs}};
        } else if (op == "replace") {
          auto old = utf16(j.at("expected").get<std::string>()),
               replacement = utf16(j.at("replacement").get<std::string>());
          if (old.size() > 20000 || replacement.size() > 20000 ||
              !valid_utf16(old) || !valid_utf16(replacement))
            break;
          auto r = a.replace(j.at("token"), old, replacement);
          response["write"] = {{"state", int(r.state)},
                               {"status", r.status},
                               {"count", r.applied_changes}};
          if (r.observed)
            response["write"]["observed"] = utf8(*r.observed);
        } else if (op != "ping")
          break;
        response["capture_status"] = a.capture_status();
        if (s)
          response["snapshot"] = wire(*s);
        transmit(GetStdHandle(STD_OUTPUT_HANDLE), response);
      }
      result = 0;
    } catch (...) {
      result = 1;
    }
    CoUninitialize();
  }
  done = true;
  watch.join();
  return result;
}
struct ComposerClient::Impl {
  std::mutex mutex;
  Handle process, input, output, error;
  std::string status = "not_attempted", phase, line;
  bool overlong{};
  std::atomic_bool closed{};
  uint64_t generation{};
  std::chrono::steady_clock::time_point retry{};
  void stop() {
    if (process.h) {
      TerminateProcess(process.h, 1);
      WaitForSingleObject(process.h, 1000);
    }
    process.reset();
    input.reset();
    output.reset();
    error.reset();
    ++generation;
  }
  bool start() {
    if (closed || std::chrono::steady_clock::now() < retry)
      return false;
    SECURITY_ATTRIBUTES sa{sizeof(sa), nullptr, TRUE};
    HANDLE inR{}, inW{}, outR{}, outW{}, errR{}, errW{};
    if (!CreatePipe(&inR, &inW, &sa, 0))
      return false;
    Handle ir(inR), iw(inW);
    if (!CreatePipe(&outR, &outW, &sa, 0))
      return false;
    Handle orr(outR), ow(outW);
    if (!CreatePipe(&errR, &errW, &sa, 0))
      return false;
    Handle er(errR), ew(errW);
    SetHandleInformation(iw.h, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(orr.h, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(er.h, HANDLE_FLAG_INHERIT, 0);
    wchar_t path[32768];
    DWORD n = GetModuleFileNameW(nullptr, path, 32768);
    if (!n || n == 32768)
      return false;
    std::wstring cmd = L"\"" + std::wstring(path, n) +
                       L"\" --composer-worker " +
                       std::to_wstring(GetCurrentProcessId());
    SIZE_T bytes{};
    InitializeProcThreadAttributeList(nullptr, 1, 0, &bytes);
    std::vector<BYTE> storage(bytes);
    auto attributes =
        reinterpret_cast<PPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
    if (!InitializeProcThreadAttributeList(attributes, 1, 0, &bytes))
      return false;
    struct AttributeCleanup {
      PPROC_THREAD_ATTRIBUTE_LIST p;
      ~AttributeCleanup() { DeleteProcThreadAttributeList(p); }
    } cleanup{attributes};
    HANDLE inherited[] = {ir.h, ow.h, ew.h};
    if (!UpdateProcThreadAttribute(attributes, 0,
                                   PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited,
                                   sizeof(inherited), nullptr, nullptr))
      return false;
    STARTUPINFOEXW si{};
    si.StartupInfo.cb = sizeof(si);
    si.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
    si.StartupInfo.hStdInput = ir.h;
    si.StartupInfo.hStdOutput = ow.h;
    si.StartupInfo.hStdError = ew.h;
    si.lpAttributeList = attributes;
    PROCESS_INFORMATION pi{};
    if (!CreateProcessW(path, cmd.data(), nullptr, nullptr, TRUE,
                        CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT,
                        nullptr, nullptr, &si.StartupInfo, &pi))
      return false;
    CloseHandle(pi.hThread);
    process.reset(pi.hProcess);
    input.reset(iw.h);
    iw.h = nullptr;
    output.reset(orr.h);
    orr.h = nullptr;
    error.reset(er.h);
    er.h = nullptr;
    return true;
  }
  std::optional<json> exchange(json request, bool allow) {
    if (!process.h && (!allow || !start()))
      return {};
    request["version"] = 2;
    phase.clear();
    try {
      auto future = std::async(std::launch::async, [&] {
        transmit(input.h, request);
        return receive(output.h);
      });
      auto deadline =
          std::chrono::steady_clock::now() + std::chrono::seconds(10);
      bool timedOut = false;
      while (future.wait_for(std::chrono::milliseconds(10)) !=
             std::future_status::ready) {
        DWORD avail{};
        if (PeekNamedPipe(error.h, nullptr, 0, nullptr, &avail, nullptr) &&
            avail) {
          char b[256];
          DWORD read{};
          ReadFile(error.h, b, std::min<DWORD>(avail, sizeof(b)), &read,
                   nullptr);
          for (DWORD i = 0; i < read; ++i) {
            char c = b[i];
            if (c == '\n') {
              if (!overlong) {
                if (!line.empty() && line.back() == '\r')
                  line.pop_back();
                for (auto allowed : {"uia-phase:selection_document",
                                     "uia-phase:selection_prefix",
                                     "uia-phase:selection_through",
                                     "uia-phase:selection_selected",
                                     "uia-phase:selection_suffix"})
                  if (line == allowed)
                    phase = line.substr(10);
              }
              line.clear();
              overlong = false;
            } else if (line.size() < 160)
              line += c;
            else
              overlong = true;
          }
        }
        if (closed || std::chrono::steady_clock::now() >= deadline) {
          timedOut = true;
          TerminateProcess(process.h, 1);
          break;
        }
      }
      auto response = future.get();
      if (timedOut)
        throw std::runtime_error("worker_timeout");
      if (!response || response->at("version") != 2)
        throw std::runtime_error("protocol");
      const auto operation = request.value("operation", std::string{});
      if (operation == "capture" || operation == "poll" ||
          operation == "refresh")
        status = response->value("capture_status", "uia_operation_failed");
      return response;
    } catch (...) {
      status =
          phase.empty() ? "worker_unavailable" : "worker_unavailable_" + phase;
      retry = std::chrono::steady_clock::now() + std::chrono::seconds(5);
      stop();
      return {};
    }
  }
};
ComposerClient::ComposerClient() : impl_(std::make_unique<Impl>()) {}
ComposerClient::~ComposerClient() { close(); }
std::optional<Snapshot> ComposerClient::capture(bool layout, bool focus) {
  std::lock_guard lock(impl_->mutex);
  auto r = impl_->exchange(
      {{"operation", "capture"}, {"layout", layout}, {"requireFocus", focus}},
      true);
  if (r && r->contains("snapshot")) {
    auto s = unwire((*r)["snapshot"]);
    s.token |= impl_->generation << 32;
    return s;
  }
  return {};
}
std::optional<Snapshot> ComposerClient::poll() {
  std::lock_guard lock(impl_->mutex);
  auto r = impl_->exchange({{"operation", "poll"}}, true);
  if (r && r->contains("snapshot")) {
    auto s = unwire((*r)["snapshot"]);
    s.token |= impl_->generation << 32;
    return s;
  }
  return {};
}
std::optional<Snapshot> ComposerClient::refresh(const Snapshot &s) {
  std::lock_guard lock(impl_->mutex);
  if ((s.token >> 32) != impl_->generation)
    return {};
  auto r = impl_->exchange(
      {{"operation", "refresh"}, {"token", s.token & 0xffffffff}}, false);
  if (r && r->contains("snapshot")) {
    auto x = unwire((*r)["snapshot"]);
    x.token |= impl_->generation << 32;
    return x;
  }
  return {};
}
WriteResult ComposerClient::replace(const Snapshot &s,
                                    const std::wstring &expected,
                                    const std::wstring &replacement) {
  std::lock_guard lock(impl_->mutex);
  WriteResult w;
  w.original = expected;
  if ((s.token >> 32) != impl_->generation) {
    w.status = "worker_snapshot_expired";
    return w;
  }
  auto r = impl_->exchange({{"operation", "replace"},
                            {"token", s.token & 0xffffffff},
                            {"expected", utf8(expected)},
                            {"replacement", utf8(replacement)}},
                           false);
  if (!r || !r->contains("write")) {
    w.state = WriteState::partially_applied;
    w.status = impl_->status;
    return w;
  }
  auto j = (*r)["write"];
  w.state = WriteState(j.at("state").get<int>());
  w.status = j.at("status");
  w.applied_changes = j.at("count");
  if (j.contains("observed"))
    w.observed = utf16(j["observed"].get<std::string>());
  return w;
}
ComposerDiagnostics ComposerClient::diagnostics() {
  std::lock_guard lock(impl_->mutex);
  ComposerDiagnostics value;
  auto response = impl_->exchange({{"operation", "diagnostics"}}, false);
  if (!response || !response->contains("diagnostics"))
    return value;
  auto &j = (*response)["diagnostics"];
  value.capture_count = j.at("capture_count");
  value.poll_count = j.at("poll_count");
  value.full_layout_count = j.at("full_layout_count");
  value.geometry_cache_hit_count = j.at("geometry_cache_hit_count");
  value.geometry_check_count = j.at("geometry_check_count");
  value.geometry_node_check_count = j.at("geometry_node_check_count");
  value.geometry_walker_step_count = j.at("geometry_walker_step_count");
  value.retained_tokens = j.at("retained_tokens");
  value.retained_identity_data_bytes = j.at("retained_identity_data_bytes");
  value.idle_owned_com_refs = j.at("idle_owned_com_refs");
  value.idle_owned_editor_com_refs = j.at("idle_owned_editor_com_refs");
  return value;
}
void ComposerClient::invalidate_layout() {
  std::lock_guard lock(impl_->mutex);
  if (impl_->process.h)
    impl_->exchange({{"operation", "invalidate"}}, false);
}
std::string ComposerClient::status() const {
  std::lock_guard lock(impl_->mutex);
  return impl_->status;
}
void ComposerClient::close() {
  impl_->closed = true;
  std::lock_guard lock(impl_->mutex);
  impl_->stop();
}
} // namespace clf
