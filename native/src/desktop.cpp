#include "clf/desktop.hpp"
#include "clf/backend.hpp"
#include "clf/composer.hpp"
#include "clf/controller.hpp"
#include "clf/core.hpp"
#include "clf/localization.hpp"
#include <Windows.h>

#include <Objbase.h>

#include <CommCtrl.h>
#include <Shellapi.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <tuple>
#include <vector>
namespace clf {
namespace {
constexpr UINT changed_message = WM_APP + 1, tray_message = WM_APP + 2,
               event_message = WM_APP + 3;
constexpr UINT_PTR poll_timer = 1, event_timer = 2, animation_timer = 3;
constexpr int correct_id = 100, undo_id = 101, enabled_id = 110,
              autostart_id = 111, prepare_id = 112, connect_id = 113,
              test_id = 114, settings_id = 115, exit_id = 116, auto_id = 120,
              german_id = 121, english_id = 122, record_id = 130,
              restore_id = 131;
constexpr wchar_t main_class[] = L"CodexLanguageFix.Native",
                  overlay_class[] = L"CodexLanguageFix.Overlay",
                  settings_class[] = L"CodexLanguageFix.Settings";
bool intersects(Rect a, Rect b) {
  return !a.empty && !b.empty && a.x < b.x + b.width && b.x < a.x + a.width &&
         a.y < b.y + b.height && b.y < a.y + a.height;
}
bool contains(Rect a, Rect b) {
  return !a.empty && !b.empty && a.x <= b.x && a.y <= b.y &&
         a.x + a.width >= b.x + b.width && a.y + a.height >= b.y + b.height;
}
bool valid_shortcut(UINT m, UINT k) {
  bool supported =
      k == 8 || k == 9 || k == 12 || k == 13 || (k >= 0x13 && k <= 0x2f) ||
      (k >= 0x30 && k <= 0x39) || (k >= 0x41 && k <= 0x5a) || k == 0x5d ||
      k == 0x5f || (k >= 0x60 && k <= 0x87) || k == 0x90 || k == 0x91 ||
      (k >= 0xa6 && k <= 0xb7) || (k >= 0xba && k <= 0xc0) ||
      (k >= 0xdb && k <= 0xe4) || k == 0xe6 || (k >= 0xe9 && k <= 0xfe);
  return !(m & ~7) && supported && k != VK_F12 && k != VK_SHIFT &&
         k != VK_CONTROL && k != VK_MENU && k != VK_LWIN && k != VK_RWIN &&
         !(k >= VK_LSHIFT && k <= VK_RMENU) && k != VK_PROCESSKEY &&
         k != VK_PACKET && ((m & 3) || (k >= VK_F1 && k <= VK_F24));
}
std::wstring shortcut(UINT m, UINT k) {
  std::wstring s;
  if (m & MOD_CONTROL)
    s += is_german() ? L"Strg+" : L"Ctrl+";
  if (m & MOD_ALT)
    s += L"Alt+";
  if (m & MOD_SHIFT)
    s += is_german() ? L"Umschalt+" : L"Shift+";
  if (k >= VK_F1 && k <= VK_F24)
    return s + L"F" + std::to_wstring(k - VK_F1 + 1);
  if ((k >= L'0' && k <= L'9') || (k >= L'A' && k <= L'Z'))
    return s + wchar_t(k);
  UINT scan = MapVirtualKeyExW(k, MAPVK_VK_TO_VSC_EX, GetKeyboardLayout(0));
  wchar_t name[64]{};
  LONG param = LONG((scan & 255) << 16);
  if (scan & 0xff00)
    param |= 1 << 24;
  if (scan && GetKeyNameTextW(param, name, 64) > 0)
    return s + name;
  wchar_t fallback[20];
  swprintf_s(fallback, L"VK 0x%02X", k);
  return s + fallback;
}
class Desktop {
  HINSTANCE instance = GetModuleHandleW(nullptr);
  HWND main{}, overlay{}, status{}, correct{}, undo{}, statusLabel{},
      settingsWindow{}, shortcutBox{}, settingsStatus{}, inlineLabel{},
      record{}, restore{}, save{}, cancel{};
  HFONT font{}, smallFont{}, statusFont{};
  UINT fontDpi{}, themeDpi{};
  HWND tooltip{};
  std::wstring correctTip, undoTip;
  bool shortcutFailureReported{};
  HBRUSH surface{}, hoverSurface{}, pressedSurface{}, borderSurface{};
  HPEN primaryPen{}, secondaryPen{}, accentPen{};
  int overlayRegionWidth{}, overlayRegionHeight{}, overlayRegionRadius{};
  int statusRegionWidth{}, statusRegionHeight{}, statusRegionRadius{};
  bool dark = true, hotkeyRegistered{}, capturing{}, focusPending{},
       layoutPending{};
  std::atomic_bool shuttingDown{};
  UINT taskbarCreated{}, animation{}, draftModifiers{}, draftKey{};
  std::vector<HWINEVENTHOOK> hooks;
  Settings settings;
  std::unique_ptr<ComposerClient> composer;
  std::unique_ptr<CodexClient> backend;
  std::unique_ptr<Controller> controller;
  ViewState state;
  NOTIFYICONDATAW tray{sizeof(tray)};
  std::wstring currentInline;
  std::vector<HWND> settingsLabels;
  static Desktop *active;
  int px(int n, UINT dpi = 0) const {
    return MulDiv(
        n,
        int(dpi ? dpi
                : GetDpiForWindow(settingsWindow ? settingsWindow : overlay)),
        96);
  }
  static LRESULT CALLBACK procedure(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    Desktop *self =
        reinterpret_cast<Desktop *>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (msg == WM_NCCREATE) {
      self = static_cast<Desktop *>(
          reinterpret_cast<CREATESTRUCTW *>(lp)->lpCreateParams);
      SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
    }
    if (!self)
      return DefWindowProcW(hwnd, msg, wp, lp);
    try {
      return self->message(hwnd, msg, wp, lp);
    } catch (const AppError &e) {
      if (self->controller)
        self->controller->set_status(translate(e.key, e.args));
    } catch (...) {
      if (self->controller)
        self->controller->set_status(translate("UnexpectedCorrectionFailure"));
    }
    return 0;
  }
  static LRESULT CALLBACK no_focus_button(HWND hwnd, UINT msg, WPARAM wp,
                                          LPARAM lp, UINT_PTR, DWORD_PTR) {
    if (msg == WM_MOUSEACTIVATE)
      return MA_NOACTIVATE;
    if (msg == WM_LBUTTONDOWN) {
      if (IsWindowEnabled(hwnd)) {
        SetCapture(hwnd);
        SendMessageW(hwnd, BM_SETSTATE, TRUE, 0);
      }
      return 0;
    }
    if (msg == WM_LBUTTONUP) {
      bool pressed = GetCapture() == hwnd;
      if (pressed)
        ReleaseCapture();
      SendMessageW(hwnd, BM_SETSTATE, FALSE, 0);
      RECT client{};
      GetClientRect(hwnd, &client);
      POINT point{static_cast<short>(LOWORD(lp)),
                  static_cast<short>(HIWORD(lp))};
      if (pressed && IsWindowEnabled(hwnd) && PtInRect(&client, point))
        SendMessageW(GetParent(hwnd), WM_COMMAND,
                     MAKEWPARAM(GetDlgCtrlID(hwnd), BN_CLICKED),
                     reinterpret_cast<LPARAM>(hwnd));
      return 0;
    }
    if (msg == WM_CAPTURECHANGED) {
      SendMessageW(hwnd, BM_SETSTATE, FALSE, 0);
      return 0;
    }
    if (msg == WM_NCDESTROY)
      RemoveWindowSubclass(hwnd, no_focus_button, 1);
    return DefSubclassProc(hwnd, msg, wp, lp);
  }
  static void CALLBACK event(HWINEVENTHOOK, DWORD id, HWND hwnd, LONG object,
                             LONG, DWORD, DWORD) {
    auto self = active;
    if (!self || self->shuttingDown || object == OBJID_CARET ||
        object == OBJID_CURSOR)
      return;
    if (id != EVENT_SYSTEM_FOREGROUND && hwnd) {
      DWORD pid{};
      GetWindowThreadProcessId(hwnd, &pid);
      if (pid == GetCurrentProcessId())
        return;
      HWND er = GetAncestor(hwnd, GA_ROOT),
           fr = GetAncestor(GetForegroundWindow(), GA_ROOT);
      if (er && fr && er != fr)
        return;
    }
    bool focus = id == EVENT_SYSTEM_FOREGROUND || id == EVENT_OBJECT_FOCUS ||
                 id == EVENT_SYSTEM_MINIMIZESTART ||
                 id == EVENT_SYSTEM_MINIMIZEEND;
    bool layout = id == EVENT_OBJECT_DESTROY || id == EVENT_OBJECT_SHOW ||
                  id == EVENT_OBJECT_HIDE || id == EVENT_OBJECT_REORDER ||
                  id == EVENT_OBJECT_LOCATIONCHANGE ||
                  id == EVENT_OBJECT_CONTENTSCROLLED ||
                  id == EVENT_SYSTEM_MINIMIZESTART ||
                  id == EVENT_SYSTEM_MINIMIZEEND;
    PostMessageW(self->main, event_message, focus, layout);
  }
  void register_class(const wchar_t *name) {
    WNDCLASSEXW c{sizeof(c)};
    c.hInstance = instance;
    c.lpfnWndProc = procedure;
    c.lpszClassName = name;
    c.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    c.hIcon = LoadIconW(nullptr, IDI_INFORMATION);
    c.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
    if (!RegisterClassExW(&c) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
      throw std::runtime_error("window_class_failed");
  }
  HWND child(HWND parent, const wchar_t *kind, const std::wstring &text,
             DWORD style, int id, int x, int y, int width, int height) {
    HWND h = CreateWindowExW(
        kind == std::wstring(L"EDIT") ? WS_EX_CLIENTEDGE : 0, kind,
        text.c_str(), WS_CHILD | WS_VISIBLE | style, x, y, width, height,
        parent, reinterpret_cast<HMENU>(INT_PTR(id)), instance, nullptr);
    if (!h)
      throw std::runtime_error("control_failed");
    SendMessageW(h, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
    return h;
  }
  void fonts(UINT dpi) {
    if (fontDpi == dpi && font && smallFont && statusFont)
      return;
    auto nextFont = CreateFontW(-MulDiv(14, int(dpi), 96), 0, 0, 0, FW_NORMAL,
                                FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                                OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                                CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    auto nextSmall = CreateFontW(
        -MulDiv(11, int(dpi), 96), 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    auto nextStatus = CreateFontW(
        -MulDiv(10, int(dpi), 96), 0, 0, 0, FW_MEDIUM, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    if (!nextFont || !nextSmall || !nextStatus) {
      if (nextFont)
        DeleteObject(nextFont);
      if (nextSmall)
        DeleteObject(nextSmall);
      if (nextStatus)
        DeleteObject(nextStatus);
      return;
    }
    auto previous = font, previousSmall = smallFont,
         previousStatus = statusFont;
    font = nextFont;
    smallFont = nextSmall;
    statusFont = nextStatus;
    fontDpi = dpi;
    for (auto h : {correct, undo, shortcutBox, settingsStatus, inlineLabel,
                   record, restore, save, cancel})
      if (h)
        SendMessageW(h, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
    for (auto h : settingsLabels)
      SendMessageW(h, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
    if (correct)
      SendMessageW(correct, WM_SETFONT, reinterpret_cast<WPARAM>(smallFont),
                   TRUE);
    if (undo)
      SendMessageW(undo, WM_SETFONT, reinterpret_cast<WPARAM>(statusFont),
                   TRUE);
    if (statusLabel)
      SendMessageW(statusLabel, WM_SETFONT,
                   reinterpret_cast<WPARAM>(statusFont), TRUE);
    if (previous)
      DeleteObject(previous);
    if (previousSmall)
      DeleteObject(previousSmall);
    if (previousStatus)
      DeleteObject(previousStatus);
  }
  void round_window(HWND window, int width, int height, int radius,
                    int &previousWidth, int &previousHeight,
                    int &previousRadius) {
    if (!window || width <= 0 || height <= 0 || radius <= 0 ||
        (width == previousWidth && height == previousHeight &&
         radius == previousRadius))
      return;
    HRGN region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2,
                                    radius * 2);
    if (!region)
      return;
    if (!SetWindowRgn(window, region, TRUE)) {
      DeleteObject(region);
      return;
    }
    previousWidth = width;
    previousHeight = height;
    previousRadius = radius;
  }
  void rounded_surface(HDC dc, const RECT &bounds, int radius, HBRUSH fill) {
    int width = bounds.right - bounds.left;
    int height = bounds.bottom - bounds.top;
    if (width <= 0 || height <= 0)
      return;
    auto edge = borderSurface ? borderSurface : surface;
    auto background = fill ? fill : surface;
    if (!edge)
      edge = GetSysColorBrush(COLOR_WINDOW);
    if (!background)
      background = GetSysColorBrush(COLOR_WINDOW);
    int border = std::min({std::max(1, px(1)), width / 2, height / 2});
    auto oldBrush = SelectObject(dc, edge);
    auto oldPen = SelectObject(dc, GetStockObject(NULL_PEN));
    RoundRect(dc, bounds.left, bounds.top, bounds.right, bounds.bottom,
              radius * 2, radius * 2);
    RECT inner{bounds.left + border, bounds.top + border,
               bounds.right - border, bounds.bottom - border};
    if (inner.right > inner.left && inner.bottom > inner.top) {
      SelectObject(dc, background);
      int innerRadius = std::max(0, radius - border);
      RoundRect(dc, inner.left, inner.top, inner.right, inner.bottom,
                innerRadius * 2, innerRadius * 2);
    }
    if (oldBrush && oldBrush != HGDI_ERROR)
      SelectObject(dc, oldBrush);
    if (oldPen && oldPen != HGDI_ERROR)
      SelectObject(dc, oldPen);
  }
  void rounded_fill(HDC dc, const RECT &bounds, int radius, HBRUSH fill) {
    if (!fill || bounds.right <= bounds.left || bounds.bottom <= bounds.top)
      return;
    auto oldBrush = SelectObject(dc, fill);
    auto oldPen = SelectObject(dc, GetStockObject(NULL_PEN));
    RoundRect(dc, bounds.left, bounds.top, bounds.right, bounds.bottom,
              radius * 2, radius * 2);
    if (oldBrush && oldBrush != HGDI_ERROR)
      SelectObject(dc, oldBrush);
    if (oldPen && oldPen != HGDI_ERROR)
      SelectObject(dc, oldPen);
  }
  int text_width(const std::wstring &text, HFONT typeface) {
    if (text.empty())
      return 0;
    HWND owner = overlay ? overlay : status;
    HDC dc = GetDC(owner);
    if (!dc)
      return 0;
    auto previous = SelectObject(dc, typeface ? typeface : font);
    SIZE extent{};
    auto count = static_cast<int>(std::min(
        text.size(), static_cast<size_t>(std::numeric_limits<int>::max())));
    bool measured = count > 0 &&
                    GetTextExtentPoint32W(dc, text.c_str(), count, &extent);
    if (previous && previous != HGDI_ERROR)
      SelectObject(dc, previous);
    ReleaseDC(owner, dc);
    return measured ? std::max(0, static_cast<int>(extent.cx)) : 0;
  }
  void status_decorations(HDC dc, const RECT &bounds) {
    const UINT dpi = GetDpiForWindow(status);
    const int unit = std::max(1, MulDiv(1, int(dpi), 96));
    const int border = unit;
    const int glyphLeft = border + MulDiv(3, int(dpi), 96);
    const int centerY = (bounds.top + bounds.bottom) / 2;
    if (state.can_undo) {
      if (accentPen) {
        auto previous = SelectObject(dc, accentPen);
        MoveToEx(dc, glyphLeft + MulDiv(2, int(dpi), 96),
                 centerY + MulDiv(1, int(dpi), 96), nullptr);
        LineTo(dc, glyphLeft + MulDiv(5, int(dpi), 96),
               centerY + MulDiv(5, int(dpi), 96));
        LineTo(dc, glyphLeft + MulDiv(11, int(dpi), 96),
               centerY - MulDiv(3, int(dpi), 96));
        if (previous && previous != HGDI_ERROR)
          SelectObject(dc, previous);
      }
      RECT undoBounds{};
      GetClientRect(undo, &undoBounds);
      int undoX = bounds.right - border - MulDiv(3, int(dpi), 96) -
                  (undoBounds.right - undoBounds.left);
      int separatorX = undoX - MulDiv(2, int(dpi), 96);
      int separatorHeight = MulDiv(9, int(dpi), 96);
      RECT separator{separatorX, centerY - separatorHeight / 2,
                     separatorX + unit, centerY + (separatorHeight + 1) / 2};
      if (borderSurface)
        FillRect(dc, &separator, borderSurface);
    } else {
      int diameter = MulDiv(4, int(dpi), 96);
      int centerX = glyphLeft + MulDiv(6, int(dpi), 96);
      auto oldBrush = SelectObject(dc, GetStockObject(DC_BRUSH));
      auto oldPen = SelectObject(dc, GetStockObject(NULL_PEN));
      COLORREF oldColor = SetDCBrushColor(
          dc, dark ? RGB(184, 185, 190) : RGB(107, 108, 114));
      Ellipse(dc, centerX - diameter / 2, centerY - diameter / 2,
              centerX + (diameter + 1) / 2, centerY + (diameter + 1) / 2);
      SetDCBrushColor(dc, oldColor);
      if (oldBrush && oldBrush != HGDI_ERROR)
        SelectObject(dc, oldBrush);
      if (oldPen && oldPen != HGDI_ERROR)
        SelectObject(dc, oldPen);
    }
  }
  void tooltips() {
    if (!tooltip) {
      tooltip = CreateWindowExW(
          WS_EX_TOPMOST | WS_EX_NOACTIVATE, TOOLTIPS_CLASSW, nullptr,
          WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX, CW_USEDEFAULT, CW_USEDEFAULT,
          CW_USEDEFAULT, CW_USEDEFAULT, main, nullptr, instance, nullptr);
      if (!tooltip)
        return;
      TOOLINFOW info{sizeof(info)};
      info.uFlags = TTF_IDISHWND | TTF_SUBCLASS;
      info.hwnd = overlay;
      info.uId = reinterpret_cast<UINT_PTR>(correct);
      info.lpszText = const_cast<wchar_t *>(L"");
      SendMessageW(tooltip, TTM_ADDTOOLW, 0, reinterpret_cast<LPARAM>(&info));
      info.hwnd = status;
      info.uId = reinterpret_cast<UINT_PTR>(undo);
      SendMessageW(tooltip, TTM_ADDTOOLW, 0, reinterpret_cast<LPARAM>(&info));
    }
    correctTip = translate(
        "CorrectTooltip",
        {shortcut(settings.shortcut_modifiers, settings.shortcut_vk)});
    undoTip = translate("UndoTooltip");
    TOOLINFOW info{sizeof(info)};
    info.hwnd = overlay;
    info.uId = reinterpret_cast<UINT_PTR>(correct);
    info.lpszText = correctTip.data();
    SendMessageW(tooltip, TTM_UPDATETIPTEXTW, 0,
                 reinterpret_cast<LPARAM>(&info));
    info.hwnd = status;
    info.uId = reinterpret_cast<UINT_PTR>(undo);
    info.lpszText = undoTip.data();
    SendMessageW(tooltip, TTM_UPDATETIPTEXTW, 0,
                 reinterpret_cast<LPARAM>(&info));
  }
  void settings_layout() {
    if (!settingsWindow || settingsLabels.size() != 3)
      return;
    UINT dpi = GetDpiForWindow(settingsWindow);
    fonts(dpi);
    auto move = [&](HWND h, int x, int y, int w, int hgt) {
      MoveWindow(h, MulDiv(x, int(dpi), 96), MulDiv(y, int(dpi), 96),
                 MulDiv(w, int(dpi), 96), MulDiv(hgt, int(dpi), 96), TRUE);
    };
    move(settingsLabels[0], 24, 20, 464, 24);
    move(settingsLabels[1], 24, 50, 464, 44);
    move(shortcutBox, 24, 98, 464, 36);
    move(record, 24, 146, 214, 34);
    move(restore, 248, 146, 240, 34);
    move(inlineLabel, 24, 188, 464, 42);
    move(settingsLabels[2], 24, 238, 464, 24);
    move(settingsStatus, 24, 266, 464, 100);
    move(save, 260, 384, 108, 34);
    move(cancel, 380, 384, 108, 34);
  }
  void tray_add() {
    tray.hWnd = main;
    tray.uID = 1;
    tray.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    tray.uCallbackMessage = tray_message;
    tray.hIcon = LoadIconW(nullptr, IDI_INFORMATION);
    wcscpy_s(tray.szTip, L"Codex Language Fix");
    Shell_NotifyIconW(NIM_ADD, &tray);
    tray.uVersion = NOTIFYICON_VERSION_4;
    Shell_NotifyIconW(NIM_SETVERSION, &tray);
  }
  void set_hotkey(bool enabled) {
    if (enabled == hotkeyRegistered)
      return;
    if (enabled) {
      hotkeyRegistered =
          RegisterHotKey(main, 1, settings.shortcut_modifiers | MOD_NOREPEAT,
                         settings.shortcut_vk);
      if (hotkeyRegistered)
        shortcutFailureReported = false;
      if (!hotkeyRegistered && controller && !shortcutFailureReported) {
        shortcutFailureReported = true;
        controller->set_status(translate(
            "ShortcutUnavailable",
            {shortcut(settings.shortcut_modifiers, settings.shortcut_vk)}));
      }
    } else {
      UnregisterHotKey(main, 1);
      hotkeyRegistered = false;
    }
  }
  void persist(Settings candidate) {
    save_settings(candidate);
    settings = candidate;
    set_language(settings.language);
    controller->configure(settings);
    if (settingsWindow)
      settings_language();
    render();
  }
  void menu() {
    HMENU menu = CreatePopupMenu(), language = CreatePopupMenu();
    auto item = [&](UINT id, const char *key, bool checked = false) {
      AppendMenuW(menu, MF_STRING | (checked ? MF_CHECKED : 0), id,
                  translate(key).c_str());
    };
    item(enabled_id, "Enabled", settings.enabled);
    item(autostart_id, "StartWithWindows", settings.start_with_windows);
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    item(connect_id, "ConnectOpenAi");
    item(prepare_id, "PrepareLunaCorrections", settings.prepare);
    item(test_id, "TestConnection");
    for (auto [id, key, value] :
         std::array<std::tuple<UINT, const char *, const char *>, 3>{
             {{auto_id, "LanguageAutomatic", "auto"},
              {german_id, "LanguageGerman", "de"},
              {english_id, "LanguageEnglish", "en"}}})
      AppendMenuW(language,
                  MF_STRING | (normalize_language(settings.language) == value ? MF_CHECKED : 0), id,
                  translate(key).c_str());
    AppendMenuW(menu, MF_POPUP, reinterpret_cast<UINT_PTR>(language),
                translate("Language").c_str());
    item(settings_id, "Settings");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    item(exit_id, "Exit");
    POINT p{};
    GetCursorPos(&p);
    SetForegroundWindow(main);
    UINT id = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.x, p.y, 0,
                             main, nullptr);
    PostMessageW(main, WM_NULL, 0, 0);
    DestroyMenu(menu);
    if (id)
      command(id);
  }
  void command(UINT id) {
    if (id == correct_id) {
      controller->correct();
      return;
    }
    if (id == undo_id) {
      controller->undo();
      return;
    }
    if (id == connect_id) {
      controller->connect();
      return;
    }
    if (id == test_id) {
      controller->test_connection();
      return;
    }
    if (id == settings_id) {
      open_settings();
      return;
    }
    if (id == exit_id) {
      DestroyWindow(main);
      return;
    }
    auto candidate = settings;
    if (id == enabled_id)
      candidate.enabled = !candidate.enabled;
    else if (id == prepare_id)
      candidate.prepare = !candidate.prepare;
    else if (id == autostart_id) {
      try {
        set_autostart(!candidate.start_with_windows);
        candidate.start_with_windows = !candidate.start_with_windows;
      } catch (...) {
        controller->set_status(translate("AutostartChangeFailed"));
        return;
      }
    } else if (id == auto_id || id == german_id || id == english_id)
      candidate.language = id == auto_id     ? "auto"
                           : id == german_id ? "de"
                                             : "en";
    else
      return;
    persist(candidate);
  }
  void theme(Rect r) {
    HDC dc = GetDC(nullptr);
    double luminance{};
    int count{};
    if (dc) {
      for (double x : {r.x + 20, r.x + r.width * .5, r.x + r.width - 90}) {
        auto pixel = GetPixel(dc, int(std::round(x)),
                              int(std::round(r.y + r.height - 14)));
        if (pixel == CLR_INVALID)
          continue;
        luminance += (.2126 * GetRValue(pixel) + .7152 * GetGValue(pixel) +
                      .0722 * GetBValue(pixel)) /
                     255.;
        ++count;
      }
      ReleaseDC(nullptr, dc);
    }
    bool value = !count || luminance / count < .52;
    if (surface && value == dark && themeDpi == fontDpi)
      return;

    COLORREF background = value ? RGB(43, 43, 45) : RGB(255, 255, 255);
    COLORREF hover = value ? RGB(58, 58, 60) : RGB(243, 243, 243);
    COLORREF pressed = value ? RGB(68, 68, 70) : RGB(232, 233, 235);
    COLORREF border = value ? RGB(75, 75, 77) : RGB(231, 232, 235);
    COLORREF primary = value ? RGB(233, 233, 238) : RGB(48, 49, 54);
    COLORREF secondary = value ? RGB(184, 185, 190) : RGB(107, 108, 114);
    COLORREF accent = value ? RGB(120, 191, 169) : RGB(24, 120, 95);
    int stroke = std::max(1, MulDiv(1, int(fontDpi), 96));
    HBRUSH nextSurface = CreateSolidBrush(background);
    HBRUSH nextHover = CreateSolidBrush(hover);
    HBRUSH nextPressed = CreateSolidBrush(pressed);
    HBRUSH nextBorder = CreateSolidBrush(border);
    HPEN nextPrimary = CreatePen(PS_SOLID, stroke, primary);
    HPEN nextSecondary = CreatePen(PS_SOLID, stroke, secondary);
    HPEN nextAccent = CreatePen(PS_SOLID, stroke, accent);
    if (!nextSurface || !nextHover || !nextPressed || !nextBorder ||
        !nextPrimary || !nextSecondary || !nextAccent) {
      for (HGDIOBJ object : std::array<HGDIOBJ, 7>{
               nextSurface, nextHover, nextPressed, nextBorder, nextPrimary,
               nextSecondary, nextAccent})
        if (object)
          DeleteObject(object);
      return;
    }

    auto previousSurface = surface;
    auto previousHover = hoverSurface;
    auto previousPressed = pressedSurface;
    auto previousBorder = borderSurface;
    auto previousPrimary = primaryPen;
    auto previousSecondary = secondaryPen;
    auto previousAccent = accentPen;
    surface = nextSurface;
    hoverSurface = nextHover;
    pressedSurface = nextPressed;
    borderSurface = nextBorder;
    primaryPen = nextPrimary;
    secondaryPen = nextSecondary;
    accentPen = nextAccent;
    dark = value;
    themeDpi = fontDpi;
    for (HGDIOBJ object : std::array<HGDIOBJ, 7>{
             previousSurface, previousHover, previousPressed, previousBorder,
             previousPrimary, previousSecondary, previousAccent})
      if (object)
        DeleteObject(object);
    InvalidateRect(overlay, nullptr, TRUE);
    InvalidateRect(status, nullptr, TRUE);
  }
  bool status_safe(Rect candidate, const Snapshot &s, Rect screen) {
    if (!contains(screen, candidate))
      return false;
    Rect region = s.placement_bounds.value_or(s.bounds);
    if (!contains(region, candidate))
      return false;
    auto editor = s.editor_bounds.value_or(s.bounds);
    if (s.editor_text_bounds) {
      for (auto r : *s.editor_text_bounds)
        if (intersects(candidate, r))
          return false;
    } else if (intersects(candidate, editor))
      return false;
    if (s.occupied)
      for (auto r : *s.occupied) {
        if (r == editor && s.editor_text_bounds)
          continue;
        if (intersects(candidate, r))
          return false;
      }
    else if (intersects(candidate, s.bounds))
      return false;
    if (s.right_control && intersects(candidate, *s.right_control))
      return false;
    return true;
  }
  void render() {
    if (!controller)
      return;
    state = controller->view();
    set_hotkey(settings.enabled && state.shortcut_eligible && !state.busy &&
               !settingsWindow);
    if (settingsStatus)
      SetWindowTextW(settingsStatus, state.last_status.empty()
                                         ? translate("NoStatus").c_str()
                                         : state.last_status.c_str());
    if (settingsWindow || !settings.enabled || !state.snapshot) {
      ShowWindow(overlay, SW_HIDE);
      ShowWindow(status, SW_HIDE);
      return;
    }
    auto &s = *state.snapshot;
    POINT center{LONG(s.bounds.x + s.bounds.width / 2),
                 LONG(s.bounds.y + s.bounds.height / 2)};
    HMONITOR monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
    MONITORINFO info{sizeof(info)};
    if (!GetMonitorInfoW(monitor, &info)) {
      ShowWindow(overlay, SW_HIDE);
      ShowWindow(status, SW_HIDE);
      return;
    }
    if (MonitorFromWindow(overlay, MONITOR_DEFAULTTONEAREST) != monitor) {
      ShowWindow(overlay, SW_HIDE);
      SetWindowPos(overlay, nullptr, center.x, center.y, 0, 0,
                   SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOZORDER);
    }
    UINT dpi = GetDpiForWindow(overlay);
    fonts(dpi);
    tooltips();
    double scale = dpi / 96.;
    Rect screen{double(info.rcWork.left), double(info.rcWork.top),
                double(info.rcWork.right - info.rcWork.left),
                double(info.rcWork.bottom - info.rcWork.top), false};
    auto placement = overlay_placement(s, screen, scale);
    if (!placement) {
      ShowWindow(overlay, SW_HIDE);
      ShowWindow(status, SW_HIDE);
      return;
    }
    theme(s.bounds);
    auto r = *placement;
    SetWindowPos(overlay, HWND_TOPMOST, int(std::round(r.x)),
                 int(std::round(r.y)), int(std::round(r.width)),
                 int(std::round(r.height)), SWP_NOACTIVATE | SWP_SHOWWINDOW);
    int overlayWidth = int(std::round(r.width));
    int overlayHeight = int(std::round(r.height));
    round_window(overlay, overlayWidth, overlayHeight, px(8, dpi),
                 overlayRegionWidth, overlayRegionHeight,
                 overlayRegionRadius);
    MoveWindow(correct, 0, 0, int(std::round(r.width)),
               int(std::round(r.height)), TRUE);
    EnableWindow(correct, !state.busy);
    EnableWindow(undo, !state.busy);
    InvalidateRect(correct, nullptr, TRUE);
    if (state.busy)
      SetTimer(main, animation_timer, 40, nullptr);
    else
      KillTimer(main, animation_timer);
    SetWindowTextW(correct, translate("CorrectAutomationName").c_str());
    const std::wstring undoLabel = translate("Undo");
    SetWindowTextW(undo, undoLabel.c_str());
    SetWindowTextW(statusLabel, state.message.c_str());
    bool visible = false;
    if (!state.message.empty()) {
      const int border = px(1, dpi);
      const int statusHeight = px(20, dpi);
      const int left = border + px(3, dpi) + px(12, dpi) + px(2, dpi);
      const int textRight = px(5, dpi);
      const int maxTextWidth = px(184, dpi);
      int messageWidth = std::min(text_width(state.message, statusFont),
                                  maxTextWidth);
      if (messageWidth == 0)
        messageWidth = std::min(px(64, dpi), maxTextWidth);
      const int undoTextWidth = state.can_undo
                                    ? text_width(undoLabel, statusFont)
                                    : 0;
      const int undoWidth = state.can_undo
                                ? px(10 + 12 + 3, dpi) + undoTextWidth
                                : 0;
      const int right = px(3, dpi) + border;
      const int separatorSpace = state.can_undo ? px(2, dpi) : 0;
      const int maxStatusWidth = px(state.can_undo ? 280 : 220, dpi);
      int width = left + messageWidth + textRight + separatorSpace + undoWidth +
                  right;
      if (width > maxStatusWidth) {
        messageWidth = std::max(
            0, messageWidth - (width - maxStatusWidth));
        width = left + messageWidth + textRight + separatorSpace + undoWidth +
                right;
      }
      int labelWidth = messageWidth + textRight;
      int gap = px(4, dpi);
      for (Rect box : std::array<Rect, 4>{
               {{r.x + r.width - width, r.y + r.height + gap, double(width),
                 double(statusHeight),
                 false},
                {r.x + r.width - width, r.y - gap - statusHeight,
                 double(width), double(statusHeight),
                 false},
                {r.x + r.width + gap, r.y, double(width),
                 double(statusHeight), false},
                {r.x - gap - width, r.y, double(width),
                 double(statusHeight), false}}}) {
        if (!status_safe(box, s, screen) || intersects(box, r))
          continue;
        SetWindowPos(status, HWND_TOPMOST, int(std::round(box.x)),
                     int(std::round(box.y)), width, statusHeight,
                     SWP_NOACTIVATE | SWP_SHOWWINDOW);
        round_window(status, width, statusHeight, px(6, dpi),
                     statusRegionWidth, statusRegionHeight,
                     statusRegionRadius);
        const int innerHeight = std::max(1, statusHeight - 2 * border);
        MoveWindow(statusLabel, left, border, labelWidth, innerHeight, TRUE);
        int undoX = width - border - px(3, dpi) - undoWidth;
        MoveWindow(undo, undoX, border, undoWidth, innerHeight, TRUE);
        ShowWindow(undo, state.can_undo ? SW_SHOWNA : SW_HIDE);
        visible = true;
        break;
      }
    }
    if (!visible)
      ShowWindow(status, SW_HIDE);
  }
  void draw_button(DRAWITEMSTRUCT *d) {
    bool pressed = (d->itemState & ODS_SELECTED) != 0;
    bool hot = (d->itemState & ODS_HOTLIGHT) != 0;
    auto base = surface ? surface : GetSysColorBrush(COLOR_WINDOW);
    auto fill = pressed && pressedSurface ? pressedSurface
                : hot && hoverSurface  ? hoverSurface
                                       : base;
    COLORREF primary = dark ? RGB(233, 233, 238) : RGB(48, 49, 54);
    if (d->itemState & ODS_DISABLED)
      primary = dark ? RGB(153, 154, 158) : RGB(135, 136, 141);
    SetBkMode(d->hDC, TRANSPARENT);
    SetTextColor(d->hDC, primary);
    HFONT face = d->CtlID == correct_id ? smallFont : statusFont;
    auto previous = SelectObject(d->hDC, face ? face : font);
    RECT r = d->rcItem;
    if (d->CtlID == correct_id) {
      if ((hot || pressed) && fill != base)
        rounded_fill(d->hDC, r, px(8), fill);
      else
        FillRect(d->hDC, &r, base);
      if (state.busy) {
        int size = std::min(r.right - r.left, r.bottom - r.top) * 2 / 3,
            x = (r.right - size) / 2, y = (r.bottom - size) / 2;
        HPEN pen = CreatePen(
            PS_SOLID, std::max(1, px(2)), primary);
        if (pen) {
          auto old = SelectObject(d->hDC, pen);
          double angle = animation * .22;
          Arc(d->hDC, x, y, x + size, y + size,
              x + size / 2 + int(std::cos(angle) * size),
              y + size / 2 + int(std::sin(angle) * size),
              x + size / 2 + int(std::cos(angle + 4.4) * size),
              y + size / 2 + int(std::sin(angle + 4.4) * size));
          if (old && old != HGDI_ERROR)
            SelectObject(d->hDC, old);
          DeleteObject(pen);
        }
      } else {
        UINT dpi = GetDpiForWindow(overlay);
        r.bottom -= px(4, dpi);
        DrawTextW(d->hDC, L"Aa", 2, &r,
                  DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
        if (secondaryPen) {
          auto old = SelectObject(d->hDC, secondaryPen);
          int middle = (r.left + r.right) / 2;
          int y = r.bottom - px(4, dpi);
          std::array<POINT, 15> wave{};
          for (int i = 0; i < int(wave.size()); ++i) {
            int offset = i - int(wave.size() / 2);
            wave[i] = {middle + MulDiv(offset, int(dpi), 96),
                       y + int(std::round(std::sin(offset * .75) *
                                          MulDiv(1, int(dpi), 96)))};
          }
          Polyline(d->hDC, wave.data(), int(wave.size()));
          if (old && old != HGDI_ERROR)
            SelectObject(d->hDC, old);
        }
      }
    } else {
      FillRect(d->hDC, &r, base);
      if ((hot || pressed) && fill != base) {
        int inset = std::max(1, px(1));
        RECT hoverBounds{r.left + inset, r.top + inset, r.right - inset,
                         r.bottom - inset};
        rounded_fill(d->hDC, hoverBounds, px(4), fill);
      }
      auto label = translate("Undo");
      UINT dpi = GetDpiForWindow(status);
      int iconWidth = px(12, dpi), iconGap = px(3, dpi);
      int labelWidth = text_width(label, statusFont);
      int contentWidth = iconWidth + iconGap + labelWidth;
      int iconX = r.left + std::max(0, (int(r.right - r.left) - contentWidth) / 2);
      int iconY = (r.top + r.bottom) / 2 - px(6, dpi);
      if (primaryPen) {
        auto old = SelectObject(d->hDC, primaryPen);
        POINT arrow[]{
            {iconX + MulDiv(5, int(dpi), 96), iconY + MulDiv(2, int(dpi), 96)},
            {iconX + MulDiv(2, int(dpi), 96), iconY + MulDiv(5, int(dpi), 96)},
            {iconX + MulDiv(5, int(dpi), 96), iconY + MulDiv(8, int(dpi), 96)}};
        Polyline(d->hDC, arrow, 3);
        POINT returnArrow[]{
            {iconX + MulDiv(2, int(dpi), 96), iconY + MulDiv(5, int(dpi), 96)},
            {iconX + MulDiv(7, int(dpi), 96), iconY + MulDiv(5, int(dpi), 96)},
            {iconX + MulDiv(9, int(dpi), 96), iconY + MulDiv(6, int(dpi), 96)},
            {iconX + MulDiv(10, int(dpi), 96), iconY + MulDiv(8, int(dpi), 96)},
            {iconX + MulDiv(9, int(dpi), 96), iconY + MulDiv(10, int(dpi), 96)},
            {iconX + MulDiv(6, int(dpi), 96), iconY + MulDiv(10, int(dpi), 96)}};
        Polyline(d->hDC, returnArrow, 6);
        if (old && old != HGDI_ERROR)
          SelectObject(d->hDC, old);
      }
      RECT labelBounds{iconX + iconWidth + iconGap, r.top,
                       r.right - px(4, dpi), r.bottom};
      DrawTextW(d->hDC, label.c_str(), -1, &labelBounds,
                DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS |
                    DT_NOPREFIX);
    }
    if (previous && previous != HGDI_ERROR)
      SelectObject(d->hDC, previous);
    if (d->itemState & ODS_FOCUS)
      DrawFocusRect(d->hDC, &d->rcItem);
  }
  void settings_inline(const std::wstring &text) {
    currentInline = text;
    SetWindowTextW(inlineLabel, text.c_str());
  }
  void recording(bool value) {
    capturing = value;
    EnableWindow(restore, !value);
    EnableWindow(save, !value);
    SetWindowTextW(record,
                   translate(value ? "Cancel" : "RecordShortcut").c_str());
    if (value) {
      settings_inline(translate("RecordShortcutPrompt"));
      SetFocus(settingsWindow);
    } else
      settings_inline(L"");
  }
  void settings_language() {
    SetWindowTextW(settingsWindow, translate("Settings").c_str());
    std::array<const char *, 3> keys{"RecordShortcut", "ShortcutHint",
                                     "LastStatus"};
    for (size_t i = 0; i < settingsLabels.size(); ++i)
      SetWindowTextW(settingsLabels[i], translate(keys[i]).c_str());
    SetWindowTextW(shortcutBox, shortcut(draftModifiers, draftKey).c_str());
    SetWindowTextW(record,
                   translate(capturing ? "Cancel" : "RecordShortcut").c_str());
    SetWindowTextW(restore, translate("RestoreDefaultShortcut").c_str());
    SetWindowTextW(save, translate("Save").c_str());
    SetWindowTextW(cancel, translate("Cancel").c_str());
  }
  void open_settings() {
    if (settingsWindow) {
      SetForegroundWindow(settingsWindow);
      return;
    }
    controller->settings_open(true);
    set_hotkey(false);
    ShowWindow(overlay, SW_HIDE);
    ShowWindow(status, SW_HIDE);
    draftModifiers = settings.shortcut_modifiers;
    draftKey = settings.shortcut_vk;
    UINT dpi = GetDpiForSystem();
    int width = MulDiv(520, dpi, 96), height = MulDiv(480, dpi, 96);
    settingsWindow = CreateWindowExW(
        WS_EX_CONTROLPARENT, settings_class, translate("Settings").c_str(),
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU, width / 2, height / 2, width,
        height, main, nullptr, instance, this);
    if (!settingsWindow) {
      controller->settings_open(false);
      return;
    }
    dpi = GetDpiForWindow(settingsWindow);
    auto p = [&](int n) { return MulDiv(n, int(dpi), 96); };
    auto label = [&](const char *key, int y, int h) {
      auto c = child(settingsWindow, L"STATIC", translate(key), 0, 0, p(24),
                     p(y), p(464), p(h));
      settingsLabels.push_back(c);
      return c;
    };
    label("RecordShortcut", 20, 24);
    label("ShortcutHint", 50, 44);
    shortcutBox =
        child(settingsWindow, L"EDIT", shortcut(draftModifiers, draftKey),
              WS_TABSTOP | ES_READONLY | ES_AUTOHSCROLL, 140, p(24), p(98),
              p(464), p(36));
    record = child(settingsWindow, L"BUTTON", translate("RecordShortcut"),
                   WS_TABSTOP, record_id, p(24), p(146), p(214), p(34));
    restore =
        child(settingsWindow, L"BUTTON", translate("RestoreDefaultShortcut"),
              WS_TABSTOP, restore_id, p(248), p(146), p(240), p(34));
    inlineLabel = child(settingsWindow, L"STATIC", L"", 0, 0, p(24), p(188),
                        p(464), p(42));
    label("LastStatus", 238, 24);
    settingsStatus = child(
        settingsWindow, L"EDIT",
        state.last_status.empty() ? translate("NoStatus") : state.last_status,
        WS_TABSTOP | ES_READONLY | ES_MULTILINE | ES_AUTOVSCROLL | WS_VSCROLL,
        141, p(24), p(266), p(464), p(100));
    save = child(settingsWindow, L"BUTTON", translate("Save"),
                 WS_TABSTOP | BS_DEFPUSHBUTTON, IDOK, p(260), p(384), p(108),
                 p(34));
    cancel = child(settingsWindow, L"BUTTON", translate("Cancel"), WS_TABSTOP,
                   IDCANCEL, p(380), p(384), p(108), p(34));
    MONITORINFO mi{sizeof(mi)};
    GetMonitorInfoW(
        MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTONEAREST),
        &mi);
    SetWindowPos(settingsWindow, HWND_TOP,
                 (mi.rcWork.left + mi.rcWork.right - width) / 2,
                 (mi.rcWork.top + mi.rcWork.bottom - height) / 2, 0, 0,
                 SWP_NOSIZE);
    settings_layout();
    ShowWindow(settingsWindow, SW_SHOW);
    SetForegroundWindow(settingsWindow);
    SetFocus(record);
  }
  void save_shortcut() {
    if (capturing)
      return;
    if (!valid_shortcut(draftModifiers, draftKey)) {
      settings_inline(translate("InvalidShortcut"));
      return;
    }
    HWND probe = CreateWindowExW(0, main_class, L"ShortcutProbe", 0, 0, 0, 0, 0,
                                 HWND_MESSAGE, nullptr, instance, this);
    if (!probe) {
      settings_inline(translate("ShortcutSaveFailed"));
      return;
    }
    BOOL registered =
        RegisterHotKey(probe, 2, draftModifiers | MOD_NOREPEAT, draftKey);
    DWORD error = GetLastError();
    if (registered)
      UnregisterHotKey(probe, 2);
    DestroyWindow(probe);
    if (!registered) {
      settings_inline(translate(error == ERROR_HOTKEY_ALREADY_REGISTERED
                                    ? "ShortcutConflict"
                                    : "ShortcutUnavailable",
                                {shortcut(draftModifiers, draftKey)}));
      return;
    }
    auto candidate = settings;
    candidate.shortcut_modifiers = draftModifiers;
    candidate.shortcut_vk = draftKey;
    try {
      save_settings(candidate);
      settings = candidate;
      controller->configure(settings);
      controller->set_status(
          translate("ShortcutSaved", {shortcut(draftModifiers, draftKey)}));
      DestroyWindow(settingsWindow);
    } catch (...) {
      settings_inline(translate("ShortcutSaveFailed"));
    }
  }
  void capture_key(WPARAM key, LPARAM flags) {
    if (flags & (1LL << 30))
      return;
    if (key == VK_ESCAPE) {
      recording(false);
      return;
    }
    if (key == VK_SHIFT || key == VK_CONTROL || key == VK_MENU ||
        key == VK_LWIN || key == VK_RWIN ||
        (key >= VK_LSHIFT && key <= VK_RMENU))
      return;
    UINT modifiers =
        (GetKeyState(VK_CONTROL) < 0 ? MOD_CONTROL : 0) |
        (GetKeyState(VK_MENU) < 0 ? MOD_ALT : 0) |
        (GetKeyState(VK_SHIFT) < 0 ? MOD_SHIFT : 0) |
        (GetKeyState(VK_LWIN) < 0 || GetKeyState(VK_RWIN) < 0 ? MOD_WIN : 0);
    recording(false);
    if (!valid_shortcut(modifiers, UINT(key))) {
      settings_inline(translate("InvalidShortcut"));
      return;
    }
    draftModifiers = modifiers;
    draftKey = UINT(key);
    SetWindowTextW(shortcutBox, shortcut(draftModifiers, draftKey).c_str());
  }
  LRESULT message(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_MOUSEACTIVATE && (hwnd == overlay || hwnd == status))
      return MA_NOACTIVATE;
    if (msg == WM_DRAWITEM) {
      draw_button(reinterpret_cast<DRAWITEMSTRUCT *>(lp));
      return TRUE;
    }
    if (msg == WM_CTLCOLORSTATIC && (hwnd == status)) {
      auto dc = reinterpret_cast<HDC>(wp);
      SetTextColor(dc, dark ? RGB(184, 185, 190) : RGB(107, 108, 114));
      SetBkColor(dc, dark ? RGB(43, 43, 45) : RGB(255, 255, 255));
      return reinterpret_cast<LRESULT>(surface);
    }
    if (msg == WM_ERASEBKGND && (hwnd == overlay || hwnd == status)) {
      RECT r{};
      GetClientRect(hwnd, &r);
      HDC dc = reinterpret_cast<HDC>(wp);
      if (hwnd == status) {
        rounded_surface(dc, r, std::max(1, statusRegionRadius), surface);
        status_decorations(dc, r);
      } else {
        FillRect(dc, &r,
                 surface ? surface : GetSysColorBrush(COLOR_WINDOW));
      }
      return 1;
    }
    if (msg == WM_DPICHANGED) {
      if (hwnd == settingsWindow) {
        auto r = reinterpret_cast<RECT *>(lp);
        SetWindowPos(hwnd, nullptr, r->left, r->top, r->right - r->left,
                     r->bottom - r->top, SWP_NOACTIVATE | SWP_NOZORDER);
        settings_layout();
      }
      if (controller)
        controller->changed(false, true);
      return 0;
    }
    if (hwnd == settingsWindow) {
      if (msg == WM_COMMAND) {
        switch (LOWORD(wp)) {
        case record_id:
          recording(!capturing);
          return 0;
        case restore_id:
          recording(false);
          draftModifiers = 3;
          draftKey = 0xbc;
          SetWindowTextW(shortcutBox, shortcut(3, 0xbc).c_str());
          settings_inline(translate("ShortcutReset"));
          return 0;
        case IDOK:
          save_shortcut();
          return 0;
        case IDCANCEL:
          DestroyWindow(hwnd);
          return 0;
        }
      }
      if (msg == WM_CLOSE) {
        DestroyWindow(hwnd);
        return 0;
      }
      if (msg == WM_DESTROY) {
        settingsWindow = nullptr;
        shortcutBox = settingsStatus = inlineLabel = record = restore = save =
            cancel = nullptr;
        settingsLabels.clear();
        capturing = false;
        if (controller)
          controller->settings_open(false);
        render();
        return 0;
      }
    }
    if (hwnd == main) {
      if (msg == taskbarCreated) {
        tray_add();
        return 0;
      }
      switch (msg) {
      case changed_message:
        render();
        return 0;
      case event_message:
        if (!layoutPending && !focusPending)
          SetTimer(main, event_timer, 50, nullptr);
        focusPending |= wp != 0;
        layoutPending |= lp != 0;
        return 0;
      case tray_message:
        if (LOWORD(lp) == WM_CONTEXTMENU || LOWORD(lp) == WM_RBUTTONUP)
          menu();
        else if (LOWORD(lp) == WM_LBUTTONDBLCLK)
          open_settings();
        return 0;
      case WM_TIMER:
        if (wp == poll_timer)
          controller->tick();
        else if (wp == event_timer) {
          KillTimer(main, event_timer);
          controller->changed(focusPending, layoutPending);
          focusPending = layoutPending = false;
        } else if (wp == animation_timer) {
          ++animation;
          InvalidateRect(correct, nullptr, TRUE);
        }
        return 0;
      case WM_HOTKEY:
        if (wp == 1 && LOWORD(lp) == settings.shortcut_modifiers &&
            HIWORD(lp) == settings.shortcut_vk)
          controller->correct(true);
        return 0;
      case WM_COMMAND:
        command(LOWORD(wp));
        return 0;
      case WM_QUERYENDSESSION:
        return TRUE;
      case WM_ENDSESSION:
        if (wp)
          DestroyWindow(main);
        return 0;
      case WM_CLOSE:
        DestroyWindow(main);
        return 0;
      case WM_DESTROY:
        shuttingDown = true;
        PostQuitMessage(0);
        return 0;
      }
    }
    if ((hwnd == overlay || hwnd == status) && msg == WM_COMMAND) {
      command(LOWORD(wp));
      return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
  }

public:
  ~Desktop() {
    shuttingDown = true;
    active = nullptr;
    for (auto h : hooks)
      UnhookWinEvent(h);
    if (main) {
      KillTimer(main, poll_timer);
      KillTimer(main, event_timer);
      KillTimer(main, animation_timer);
      UnregisterHotKey(main, 1);
    }
    controller.reset();
    if (backend)
      backend->close();
    if (composer)
      composer->close();
    Shell_NotifyIconW(NIM_DELETE, &tray);
    if (settingsWindow)
      DestroyWindow(settingsWindow);
    if (tooltip)
      DestroyWindow(tooltip);
    if (status)
      DestroyWindow(status);
    if (overlay)
      DestroyWindow(overlay);
    if (main && IsWindow(main))
      DestroyWindow(main);
    if (font)
      DeleteObject(font);
    if (smallFont)
      DeleteObject(smallFont);
    if (statusFont)
      DeleteObject(statusFont);
    for (HGDIOBJ object : std::array<HGDIOBJ, 7>{
             surface, hoverSurface, pressedSurface, borderSurface, primaryPen,
             secondaryPen, accentPen})
      if (object)
        DeleteObject(object);
  }
  int run() {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    INITCOMMONCONTROLSEX common{sizeof(common), ICC_STANDARD_CLASSES};
    InitCommonControlsEx(&common);
    settings = load_settings();
    set_language(settings.language);
    if (!settings.notice_shown) {
      TASKDIALOG_BUTTON accept{IDOK, nullptr};
      auto caption = translate("Acknowledge"),
           heading = translate("FirstRunHeading"),
           body = translate("FirstRunExplanation",
                            {shortcut(settings.shortcut_modifiers,
                                      settings.shortcut_vk)}) +
                  L"\n\n" + translate("FirstRunPrivacy");
      accept.pszButtonText = caption.c_str();
      TASKDIALOGCONFIG dialog{sizeof(dialog)};
      dialog.hInstance = instance;
      dialog.dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_SIZE_TO_CONTENT;
      dialog.pszWindowTitle = L"Codex Language Fix";
      dialog.pszMainInstruction = heading.c_str();
      dialog.pszContent = body.c_str();
      dialog.cButtons = 1;
      dialog.pButtons = &accept;
      dialog.nDefaultButton = IDOK;
      int clicked{};
      if (FAILED(TaskDialogIndirect(&dialog, &clicked, nullptr, nullptr)) ||
          clicked != IDOK)
        return 0;
      settings.notice_shown = true;
      save_settings(settings);
    }
    if (settings.start_with_windows && !autostart_enabled()) {
      try {
        set_autostart(true);
      } catch (...) {
        settings.start_with_windows = false;
        save_settings(settings);
      }
    }
    register_class(main_class);
    register_class(overlay_class);
    register_class(settings_class);
    main = CreateWindowExW(0, main_class, L"Codex Language Fix", WS_OVERLAPPED,
                           0, 0, 0, 0, nullptr, nullptr, instance, this);
    if (!main)
      throw std::runtime_error("window_failed");
    overlay =
        CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                        overlay_class, L"Codex Language Fix", WS_POPUP, 0, 0,
                        30, 30, main, nullptr, instance, this);
    status =
        CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                        overlay_class, L"Codex Language Fix status", WS_POPUP,
                        0, 0, 260, 26, main, nullptr, instance, this);
    if (!overlay || !status)
      throw std::runtime_error("window_failed");
    fonts(GetDpiForWindow(overlay));
    correct = child(overlay, L"BUTTON", translate("CorrectAutomationName"),
                    BS_OWNERDRAW, correct_id, 0, 0, 30, 30);
    statusLabel =
        child(status, L"STATIC", L"", SS_LEFT | SS_CENTERIMAGE | SS_ENDELLIPSIS,
              0, 6, 0, 180, 26);
    undo = child(status, L"BUTTON", translate("Undo"), BS_OWNERDRAW, undo_id,
                 190, 0, 70, 26);
    SetWindowSubclass(correct, no_focus_button, 1, 0);
    SetWindowSubclass(undo, no_focus_button, 1, 0);
    SendMessageW(correct, WM_SETFONT, reinterpret_cast<WPARAM>(smallFont),
                 TRUE);
    SendMessageW(undo, WM_SETFONT, reinterpret_cast<WPARAM>(statusFont), TRUE);
    SendMessageW(statusLabel, WM_SETFONT, reinterpret_cast<WPARAM>(statusFont),
                 TRUE);
    composer = std::make_unique<ComposerClient>();
    backend = std::make_unique<CodexClient>();
    controller =
        std::make_unique<Controller>(*composer, *backend, settings, [this] {
          if (!shuttingDown)
            PostMessageW(main, changed_message, 0, 0);
        });
    active = this;
    taskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");
    tray_add();
    for (DWORD id :
         {EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_DESTROY, EVENT_OBJECT_SHOW,
          EVENT_OBJECT_HIDE, EVENT_OBJECT_REORDER, EVENT_OBJECT_FOCUS,
          EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_CONTENTSCROLLED,
          EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND}) {
      auto hook =
          SetWinEventHook(id, id, nullptr, event, 0, 0, WINEVENT_OUTOFCONTEXT);
      if (hook)
        hooks.push_back(hook);
    }
    SetTimer(main, poll_timer, 750, nullptr);
    controller->tick();
    MSG msg{};
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
      if (settingsWindow && capturing &&
          (msg.message == WM_KEYDOWN || msg.message == WM_SYSKEYDOWN)) {
        capture_key(msg.wParam, msg.lParam);
        continue;
      }
      if (settingsWindow &&
          (msg.message == WM_KEYDOWN || msg.message == WM_SYSKEYDOWN) &&
          msg.wParam == VK_ESCAPE) {
        DestroyWindow(settingsWindow);
        continue;
      }
      if (settingsWindow && IsDialogMessageW(settingsWindow, &msg))
        continue;
      TranslateMessage(&msg);
      DispatchMessageW(&msg);
    }
    return int(msg.wParam);
  }
};
Desktop *Desktop::active{};
} // namespace
int run_desktop() {
  HANDLE mutex =
      CreateMutexW(nullptr, TRUE, L"Local\\CodexLanguageFix.SingleInstance");
  if (!mutex)
    return 1;
  if (GetLastError() == ERROR_ALREADY_EXISTS) {
    CloseHandle(mutex);
    return 0;
  }
  HRESULT apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
  if (FAILED(apartment)) {
    ReleaseMutex(mutex);
    CloseHandle(mutex);
    return 1;
  }
  int code = 1;
  try {
    Desktop app;
    code = app.run();
  } catch (const AppError &e) {
    MessageBoxW(nullptr, translate(e.key, e.args).c_str(),
                L"Codex Language Fix", MB_ICONERROR);
  } catch (...) {
    MessageBoxW(nullptr, translate("UnexpectedCorrectionFailure").c_str(),
                L"Codex Language Fix", MB_ICONERROR);
  }
  CoUninitialize();
  ReleaseMutex(mutex);
  CloseHandle(mutex);
  return code;
}
} // namespace clf
