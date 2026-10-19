#include "LangBarButton.h"

#include "TextService.h"

namespace Meifeng {

namespace {

enum MenuId : UINT {
    kMenuToggle = 1,
    kMenuSettings,
    kMenuQuickAdd,
    kMenuDeploy,
};

constexpr DWORD kSinkCookie = 0x4D46;

// 中 / 英图标从安装目录读取；调用方负责 DestroyIcon
HICON LoadModeIcon(bool ascii) {
    std::wstring path = ModuleDirectory() + (ascii ? L"Resources\\Brand\\ModeEnglish.ico" : L"Resources\\Brand\\ModeChinese.ico");
    int size = GetSystemMetrics(SM_CXSMICON);
    return static_cast<HICON>(LoadImageW(nullptr, path.c_str(), IMAGE_ICON, size, size, LR_LOADFROMFILE));
}

}  // namespace

LangBarButton::LangBarButton(TextService* service) : service_(service) { DllAddRef(); }

LangBarButton::~LangBarButton() {
    if (sink_) sink_->Release();
    DllRelease();
}

STDMETHODIMP LangBarButton::QueryInterface(REFIID riid, void** ppv) {
    if (!ppv) return E_INVALIDARG;
    *ppv = nullptr;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfLangBarItem) ||
        IsEqualIID(riid, IID_ITfLangBarItemButton)) {
        *ppv = static_cast<ITfLangBarItemButton*>(this);
    } else if (IsEqualIID(riid, IID_ITfSource)) {
        *ppv = static_cast<ITfSource*>(this);
    }
    if (!*ppv) return E_NOINTERFACE;
    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) LangBarButton::AddRef() { return InterlockedIncrement(&refs_); }

STDMETHODIMP_(ULONG) LangBarButton::Release() {
    LONG n = InterlockedDecrement(&refs_);
    if (n == 0) delete this;
    return n;
}

STDMETHODIMP LangBarButton::GetInfo(TF_LANGBARITEMINFO* info) {
    if (!info) return E_INVALIDARG;
    info->clsidService = CLSID_TextService;
    // 用系统约定的输入模式 GUID，Windows 8 起显示在任务栏输入指示器里
    info->guidItem = GUID_LbiInputMode;
    info->dwStyle = TF_LBI_STYLE_BTN_BUTTON | TF_LBI_STYLE_SHOWNINTRAY;
    info->ulSort = 0;
    lstrcpynW(info->szDescription, kDisplayName, ARRAYSIZE(info->szDescription));
    return S_OK;
}

STDMETHODIMP LangBarButton::GetStatus(DWORD* status) {
    if (!status) return E_INVALIDARG;
    *status = 0;
    return S_OK;
}

STDMETHODIMP LangBarButton::Show(BOOL) { return E_NOTIMPL; }

STDMETHODIMP LangBarButton::GetTooltipString(BSTR* tooltip) {
    if (!tooltip) return E_INVALIDARG;
    bool ascii = service_ && service_->AsciiMode();
    *tooltip = SysAllocString(ascii ? L"玫枫五笔：英文（单击切换）" : L"玫枫五笔：中文（单击切换）");
    return *tooltip ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP LangBarButton::OnClick(TfLBIClick click, POINT pt, const RECT*) {
    if (!service_) return S_OK;
    if (click == TF_LBI_CLK_LEFT) {
        service_->ToggleAscii();
    } else {
        ShowMenu(pt);
    }
    return S_OK;
}

// 按钮样式不走 InitMenu；保留实现以防某些宿主仍会调用
STDMETHODIMP LangBarButton::InitMenu(ITfMenu* menu) {
    if (!menu) return E_INVALIDARG;
    bool ascii = service_ && service_->AsciiMode();
    const wchar_t* toggle = ascii ? L"切换到中文" : L"切换到英文";
    menu->AddMenuItem(kMenuToggle, 0, nullptr, nullptr, toggle, lstrlenW(toggle), nullptr);
    menu->AddMenuItem(0, TF_LBMENUF_SEPARATOR, nullptr, nullptr, L"", 0, nullptr);
    menu->AddMenuItem(kMenuSettings, 0, nullptr, nullptr, L"玫枫五笔设置…", 7, nullptr);
    menu->AddMenuItem(kMenuQuickAdd, 0, nullptr, nullptr, L"添加自造词…", 6, nullptr);
    menu->AddMenuItem(kMenuDeploy, 0, nullptr, nullptr, L"重新部署", 4, nullptr);
    return S_OK;
}

STDMETHODIMP LangBarButton::OnMenuSelect(UINT id) {
    RunCommand(id);
    return S_OK;
}

void LangBarButton::RunCommand(UINT id) {
    if (!service_) return;
    switch (id) {
        case kMenuToggle: service_->ToggleAscii(); break;
        case kMenuSettings: service_->OpenServerFeature("settings"); break;
        case kMenuQuickAdd: service_->OpenServerFeature("quickadd"); break;
        case kMenuDeploy: service_->OpenServerFeature("deploy"); break;
        default: break;
    }
}

// 右键弹出菜单：借一个临时的不可见窗口承载 TrackPopupMenu
void LangBarButton::ShowMenu(POINT pt) {
    HMENU menu = CreatePopupMenu();
    if (!menu) return;
    bool ascii = service_ && service_->AsciiMode();
    AppendMenuW(menu, MF_STRING, kMenuToggle, ascii ? L"切换到中文" : L"切换到英文");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, kMenuSettings, L"玫枫五笔设置…");
    AppendMenuW(menu, MF_STRING, kMenuQuickAdd, L"添加自造词…");
    AppendMenuW(menu, MF_STRING, kMenuDeploy, L"重新部署");

    HWND owner = CreateWindowExW(WS_EX_TOOLWINDOW, L"STATIC", L"", WS_POPUP, pt.x, pt.y, 0, 0, nullptr, nullptr,
                                 g_hInst, nullptr);
    if (owner) SetForegroundWindow(owner);
    UINT id = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.x, pt.y,
                               owner, nullptr);
    if (owner) DestroyWindow(owner);
    DestroyMenu(menu);
    if (id) RunCommand(id);
}

STDMETHODIMP LangBarButton::GetIcon(HICON* icon) {
    if (!icon) return E_INVALIDARG;
    *icon = LoadModeIcon(service_ && service_->AsciiMode());
    return *icon ? S_OK : E_FAIL;
}

STDMETHODIMP LangBarButton::GetText(BSTR* text) {
    if (!text) return E_INVALIDARG;
    *text = SysAllocString(service_ && service_->AsciiMode() ? L"英" : L"中");
    return *text ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP LangBarButton::AdviseSink(REFIID riid, IUnknown* sink, DWORD* cookie) {
    if (!sink || !cookie) return E_INVALIDARG;
    if (!IsEqualIID(riid, IID_ITfLangBarItemSink)) return CONNECT_E_CANNOTCONNECT;
    if (sink_) return CONNECT_E_ADVISELIMIT;
    if (FAILED(sink->QueryInterface(IID_ITfLangBarItemSink, reinterpret_cast<void**>(&sink_)))) {
        sink_ = nullptr;
        return E_NOINTERFACE;
    }
    *cookie = kSinkCookie;
    sinkCookie_ = kSinkCookie;
    return S_OK;
}

STDMETHODIMP LangBarButton::UnadviseSink(DWORD cookie) {
    if (cookie != sinkCookie_ || !sink_) return CONNECT_E_NOCONNECTION;
    sink_->Release();
    sink_ = nullptr;
    sinkCookie_ = 0;
    return S_OK;
}

void LangBarButton::Refresh() {
    if (sink_) sink_->OnUpdate(TF_LBI_ICON | TF_LBI_TEXT | TF_LBI_TOOLTIP);
}

}  // namespace Meifeng
