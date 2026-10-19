#include "TextService.h"

#include "DisplayAttribute.h"
#include "LangBarButton.h"

#include <shellapi.h>

namespace Meifeng {

namespace {

constexpr wchar_t kWindowClass[] = L"MeifengWubiTextServiceWindow";
LONG g_instanceCounter = 0;

// 编辑会话：把一次输出写进文档，并按需回报光标
class ApplySession final : public ITfEditSession {
public:
    ApplySession(TextService* service, ITfContext* context, ImeOutput output, const char* caretReason)
        : service_(service), context_(context), output_(std::move(output)), caretReason_(caretReason) {
        service_->AddRef();
    }

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_INVALIDARG;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfEditSession)) {
            *ppv = static_cast<ITfEditSession*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        LONG n = InterlockedDecrement(&refs_);
        if (n == 0) delete this;
        return n;
    }

    STDMETHODIMP DoEditSession(TfEditCookie ec) override {
        service_->ApplyOutput(ec, context_.Get(), output_);
        if (caretReason_ && (output_.wantCaret || !output_.preedit.empty())) {
            service_->ReportCaret(ec, context_.Get(), caretReason_);
        }
        return S_OK;
    }

private:
    ~ApplySession() { service_->Release(); }

    LONG refs_ = 1;
    TextService* service_;
    ComPtr<ITfContext> context_;
    ImeOutput output_;
    const char* caretReason_;
};

// 只读编辑会话：取光标矩形并回报
class CaretSession final : public ITfEditSession {
public:
    CaretSession(TextService* service, ITfContext* context, const char* reason)
        : service_(service), context_(context), reason_(reason) {
        service_->AddRef();
    }

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_INVALIDARG;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfEditSession)) {
            *ppv = static_cast<ITfEditSession*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        LONG n = InterlockedDecrement(&refs_);
        if (n == 0) delete this;
        return n;
    }

    STDMETHODIMP DoEditSession(TfEditCookie ec) override {
        service_->ReportCaret(ec, context_.Get(), reason_);
        return S_OK;
    }

private:
    ~CaretSession() { service_->Release(); }

    LONG refs_ = 1;
    TextService* service_;
    ComPtr<ITfContext> context_;
    const char* reason_;
};

// 读线程级 / 上下文级开关（键盘关闭、只读上下文等）
bool CompartmentValue(IUnknown* owner, REFGUID guid, LONG& value) {
    ComPtr<ITfCompartmentMgr> mgr;
    if (!owner || FAILED(owner->QueryInterface(IID_ITfCompartmentMgr, reinterpret_cast<void**>(mgr.Put())))) return false;
    ComPtr<ITfCompartment> compartment;
    if (FAILED(mgr->GetCompartment(guid, compartment.Put()))) return false;
    VARIANT v;
    VariantInit(&v);
    if (compartment->GetValue(&v) != S_OK) return false;
    bool ok = v.vt == VT_I4;
    if (ok) value = v.lVal;
    VariantClear(&v);
    return ok;
}

// 当前修饰键状态，位定义同 Meifeng.Core.Input.KeyMods
int CurrentMods() {
    int mods = 0;
    if (GetKeyState(VK_SHIFT) & 0x8000) mods |= 1;
    if (GetKeyState(VK_CONTROL) & 0x8000) mods |= 2;
    if (GetKeyState(VK_MENU) & 0x8000) mods |= 4;
    if ((GetKeyState(VK_LWIN) & 0x8000) || (GetKeyState(VK_RWIN) & 0x8000)) mods |= 8;
    if (GetKeyState(VK_CAPITAL) & 1) mods |= 16;
    return mods;
}

// 按键对应的字符：只考虑 Shift 与大写锁定，忽略 Ctrl / Alt；不改动死键状态
std::wstring KeyChar(WPARAM vk, LPARAM lParam, int mods) {
    BYTE state[256] = {};
    if (mods & 1) state[VK_SHIFT] = 0x80;
    if (mods & 16) state[VK_CAPITAL] = 0x01;
    wchar_t buf[8] = {};
    UINT scan = (lParam >> 16) & 0xFF;
    int n = ToUnicodeEx(static_cast<UINT>(vk), scan, state, buf, ARRAYSIZE(buf), 0x4, GetKeyboardLayout(0));
    if (n <= 0) return {};
    std::wstring s(buf, n);
    // 控制字符（如 Ctrl 组合产生的）不当作字符
    if (s.size() == 1 && s[0] < 0x20) return {};
    return s;
}

// 逻辑坐标转物理像素：不感知 DPI 的程序返回的是虚拟化后的坐标，服务进程按物理像素摆候选窗
void ToPhysical(HWND hwnd, RECT& rc) {
    if (!hwnd) return;
    using Fn = BOOL(WINAPI*)(HWND, LPPOINT);
    static Fn fn = reinterpret_cast<Fn>(
        reinterpret_cast<void*>(GetProcAddress(GetModuleHandleW(L"user32.dll"), "LogicalToPhysicalPointForPerMonitorDPI")));
    if (!fn) return;
    POINT tl = {rc.left, rc.top};
    POINT br = {rc.right, rc.bottom};
    if (fn(hwnd, &tl) && fn(hwnd, &br)) rc = {tl.x, tl.y, br.x, br.y};
}

}  // namespace

TextService::TextService() { DllAddRef(); }

TextService::~TextService() { DllRelease(); }

STDMETHODIMP TextService::QueryInterface(REFIID riid, void** ppv) {
    if (!ppv) return E_INVALIDARG;
    *ppv = nullptr;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfTextInputProcessor) ||
        IsEqualIID(riid, IID_ITfTextInputProcessorEx)) {
        *ppv = static_cast<ITfTextInputProcessorEx*>(this);
    } else if (IsEqualIID(riid, IID_ITfThreadMgrEventSink)) {
        *ppv = static_cast<ITfThreadMgrEventSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfKeyEventSink)) {
        *ppv = static_cast<ITfKeyEventSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfCompositionSink)) {
        *ppv = static_cast<ITfCompositionSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfDisplayAttributeProvider)) {
        *ppv = static_cast<ITfDisplayAttributeProvider*>(this);
    }
    if (!*ppv) return E_NOINTERFACE;
    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) TextService::AddRef() { return InterlockedIncrement(&refs_); }

STDMETHODIMP_(ULONG) TextService::Release() {
    LONG n = InterlockedDecrement(&refs_);
    if (n == 0) delete this;
    return n;
}

// ---- 激活 / 停用 ----

STDMETHODIMP TextService::Activate(ITfThreadMgr* threadMgr, TfClientId clientId) {
    return ActivateEx(threadMgr, clientId, 0);
}

STDMETHODIMP TextService::ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD) {
    threadMgr_ = threadMgr;
    clientId_ = clientId;
    // 客户端号：进程号 + 进程内序号，服务端据此区分各线程的文本服务
    clientKey_ = (static_cast<long long>(GetCurrentProcessId()) << 24) |
                 (InterlockedIncrement(&g_instanceCounter) & 0xFFFFFF);

    if (!AdviseSinks()) {
        Deactivate();
        return E_FAIL;
    }
    ComPtr<ITfCategoryMgr> categories;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_ITfCategoryMgr,
                                   reinterpret_cast<void**>(categories.Put())))) {
        categories->RegisterGUID(GUID_DisplayAttributeInput, &inputAttributeAtom_);
    }
    CreateMessageWindow();

    langBar_ = new LangBarButton(this);
    ComPtr<ITfLangBarItemMgr> items;
    if (SUCCEEDED(threadMgr_->QueryInterface(IID_ITfLangBarItemMgr, reinterpret_cast<void**>(items.Put())))) {
        items->AddItem(langBar_);
    }
    SetKeyboardOpen(true);

    Protocol::Fields resp;
    Request({{"cmd", "hello"}}, resp, 1500);

    ComPtr<ITfDocumentMgr> doc;
    if (SUCCEEDED(threadMgr_->GetFocus(doc.Put())) && doc) FocusDocument(doc.Get());
    return S_OK;
}

STDMETHODIMP TextService::Deactivate() {
    BlurDocument();
    EndCompositionNow();
    if (langBar_) {
        ComPtr<ITfLangBarItemMgr> items;
        if (threadMgr_ &&
            SUCCEEDED(threadMgr_->QueryInterface(IID_ITfLangBarItemMgr, reinterpret_cast<void**>(items.Put())))) {
            items->RemoveItem(langBar_);
        }
        langBar_->Detach();
        langBar_->Release();
        langBar_ = nullptr;
    }
    UnadviseSinks();
    DestroyMessageWindow();
    pipe_.Close();
    composition_.Reset();
    compositionContext_.Reset();
    threadMgr_.Reset();
    clientId_ = TF_CLIENTID_NULL;
    return S_OK;
}

bool TextService::AdviseSinks() {
    ComPtr<ITfSource> source;
    if (FAILED(threadMgr_->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(source.Put())))) return false;
    if (FAILED(source->AdviseSink(IID_ITfThreadMgrEventSink, static_cast<ITfThreadMgrEventSink*>(this),
                                  &threadMgrCookie_))) {
        threadMgrCookie_ = TF_INVALID_COOKIE;
        return false;
    }
    ComPtr<ITfKeystrokeMgr> keystrokes;
    if (FAILED(threadMgr_->QueryInterface(IID_ITfKeystrokeMgr, reinterpret_cast<void**>(keystrokes.Put())))) {
        return false;
    }
    if (FAILED(keystrokes->AdviseKeyEventSink(clientId_, static_cast<ITfKeyEventSink*>(this), TRUE))) return false;
    keySinkAdvised_ = true;
    return true;
}

void TextService::UnadviseSinks() {
    if (!threadMgr_) return;
    if (keySinkAdvised_) {
        ComPtr<ITfKeystrokeMgr> keystrokes;
        if (SUCCEEDED(threadMgr_->QueryInterface(IID_ITfKeystrokeMgr, reinterpret_cast<void**>(keystrokes.Put())))) {
            keystrokes->UnadviseKeyEventSink(clientId_);
        }
        keySinkAdvised_ = false;
    }
    if (threadMgrCookie_ != TF_INVALID_COOKIE) {
        ComPtr<ITfSource> source;
        if (SUCCEEDED(threadMgr_->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(source.Put())))) {
            source->UnadviseSink(threadMgrCookie_);
        }
        threadMgrCookie_ = TF_INVALID_COOKIE;
    }
}

// 打开键盘开关：部分程序据此判断输入法是否启用
void TextService::SetKeyboardOpen(bool open) {
    ComPtr<ITfCompartmentMgr> mgr;
    if (FAILED(threadMgr_->QueryInterface(IID_ITfCompartmentMgr, reinterpret_cast<void**>(mgr.Put())))) return;
    ComPtr<ITfCompartment> compartment;
    if (FAILED(mgr->GetCompartment(Compartment_KeyboardOpenClose, compartment.Put()))) return;
    VARIANT v;
    VariantInit(&v);
    v.vt = VT_I4;
    v.lVal = open ? 1 : 0;
    compartment->SetValue(clientId_, &v);
}

// 停用时编码区还在：同步清掉，避免把半截编码留在文档里
void TextService::EndCompositionNow() {
    if (!composition_ || !compositionContext_) return;
    ImeOutput clear;
    clear.hasPreedit = true;
    RequestApply(compositionContext_.Get(), clear, true, nullptr);
}

// ---- 与服务进程通信 ----

bool TextService::Request(Protocol::Fields request, Protocol::Fields& response, DWORD timeoutMs) {
    request.emplace_back("client", std::to_string(clientKey_));
    request.emplace_back("hwnd", std::to_string(reinterpret_cast<long long>(window_)));
    if (!pipe_.Request(request, response, timeoutMs)) return false;
    UpdateAscii(response);
    return true;
}

void TextService::UpdateAscii(const Protocol::Fields& response) {
    std::string ascii = Protocol::Get(response, "ascii");
    if (ascii.empty()) return;
    bool mode = ascii == "1";
    if (mode == asciiMode_) return;
    asciiMode_ = mode;
    if (langBar_) langBar_->Refresh();
}

ImeOutput TextService::ParseOutput(const Protocol::Fields& response) {
    ImeOutput out;
    int count = Protocol::GetInt(response, "commits");
    for (int i = 0; i < count; ++i) {
        ImeOutput::Commit c;
        c.text = Protocol::FromUtf8(Protocol::Get(response, "commit" + std::to_string(i)));
        c.replace = Protocol::GetInt(response, "replace" + std::to_string(i));
        out.commits.push_back(std::move(c));
    }
    for (const auto& [k, v] : response) {
        if (k == "preedit") {
            out.hasPreedit = true;
            out.preedit = Protocol::FromUtf8(v);
        }
    }
    out.wantCaret = Protocol::Get(response, "caret") == "1";
    return out;
}

void TextService::ToggleAscii() {
    Protocol::Fields resp;
    Request({{"cmd", "toggle"}}, resp);
}

// 语言栏菜单：交给服务进程打开窗口；服务没运行时直接启动它
void TextService::OpenServerFeature(const char* what) {
    Protocol::Fields resp;
    if (pipe_.Connected() || Request({{"cmd", "hello"}}, resp, 1500)) {
        if (pipe_.ServerPid()) AllowSetForegroundWindow(pipe_.ServerPid());
        if (Request({{"cmd", "open"}, {"what", what}}, resp)) return;
    }
    std::wstring exe = ModuleDirectory() + L"MeifengWubi.exe";
    ShellExecuteW(nullptr, L"open", exe.c_str(), nullptr, ModuleDirectory().c_str(), SW_SHOWNORMAL);
}

// ---- 按键 ----

bool TextService::KeyboardEnabled(ITfContext* context) {
    LONG value = 0;
    if (CompartmentValue(threadMgr_.Get(), Compartment_KeyboardOpenClose, value) && value == 0) return false;
    if (context) {
        if (CompartmentValue(context, Compartment_KeyboardDisabled, value) && value != 0) return false;
        if (CompartmentValue(context, Compartment_EmptyContext, value) && value != 0) return false;
    }
    return true;
}

bool TextService::SendKey(WPARAM vk, LPARAM lParam, bool up, KeyResult& result) {
    // 排序 / 删除窗口在服务进程里弹出。前台是别的程序时，先允许服务进程抢到前台
    if (!up && pipe_.ServerPid()) AllowSetForegroundWindow(pipe_.ServerPid());
    int mods = CurrentMods();
    Protocol::Fields req = {
        {"cmd", "key"},
        {"vk", std::to_string(static_cast<int>(vk))},
        {"ch", Protocol::ToUtf8(up ? std::wstring() : KeyChar(vk, lParam, mods))},
        {"mods", std::to_string(mods)},
        {"up", up ? "1" : "0"},
    };
    Protocol::Fields resp;
    result = KeyResult();
    if (!Request(req, resp, 800)) return false;
    result.vk = vk;
    result.up = up;
    result.valid = true;
    result.eaten = Protocol::Get(resp, "eaten") == "1";
    result.output = ParseOutput(resp);
    return true;
}

// 输出是否需要动文档：有上屏或编码变化
static bool ChangesDocument(const ImeOutput& output, bool composing, const std::wstring& current) {
    if (!output.commits.empty()) return true;
    if (!output.hasPreedit) return false;
    return output.preedit != current || (output.preedit.empty() && composing);
}

// OnTest* 阶段：把按键交给服务进程（有副作用，只发一次），结果留给随后的 On*
bool TextService::TestKey(ITfContext* context, WPARAM vk, LPARAM lParam, bool up, BOOL* eaten) {
    *eaten = FALSE;
    pendingKey_.valid = false;
    if (!KeyboardEnabled(context)) return true;
    KeyResult r;
    if (!SendKey(vk, lParam, up, r)) return true;
    bool changes = ChangesDocument(r.output, composition_.Get() != nullptr, currentPreedit_);
    // 不吃键也没输出：TSF 不会再调 On*，这里补上光标回报
    if (!r.eaten && !changes) {
        if (r.output.wantCaret && context) RequestCaret(context, "update");
        return true;
    }
    pendingKey_ = std::move(r);
    *eaten = TRUE;
    return true;
}

// On* 阶段：取用 OnTest* 的结果（应用直接调 On* 时现发），写入文档。
// 服务端不吃的键返回 FALSE，按键照常交给应用
bool TextService::HandleKey(ITfContext* context, WPARAM vk, LPARAM lParam, bool up, BOOL* eaten) {
    *eaten = FALSE;
    KeyResult r;
    if (pendingKey_.valid && pendingKey_.vk == vk && pendingKey_.up == up) {
        r = std::move(pendingKey_);
    } else {
        if (!KeyboardEnabled(context)) return true;
        if (!SendKey(vk, lParam, up, r)) return true;
    }
    pendingKey_.valid = false;
    bool changes = ChangesDocument(r.output, composition_.Get() != nullptr, currentPreedit_);
    if (changes && context) {
        RequestApply(context, r.output, true, "update");
    } else if (r.output.wantCaret && context) {
        RequestCaret(context, "update");
    }
    *eaten = r.eaten ? TRUE : FALSE;
    return true;
}

STDMETHODIMP TextService::OnTestKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    TestKey(context, wParam, lParam, false, eaten);
    return S_OK;
}

STDMETHODIMP TextService::OnKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    HandleKey(context, wParam, lParam, false, eaten);
    return S_OK;
}

STDMETHODIMP TextService::OnTestKeyUp(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    TestKey(context, wParam, lParam, true, eaten);
    return S_OK;
}

STDMETHODIMP TextService::OnKeyUp(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    HandleKey(context, wParam, lParam, true, eaten);
    return S_OK;
}

// ---- 编辑会话 ----

ITfContext* TextService::FocusedContext(ComPtr<ITfContext>& holder) {
    if (composition_ && compositionContext_) {
        holder = compositionContext_.Get();
        return holder.Get();
    }
    ComPtr<ITfDocumentMgr> doc;
    if (!threadMgr_ || FAILED(threadMgr_->GetFocus(doc.Put())) || !doc) return nullptr;
    if (FAILED(doc->GetTop(holder.Put()))) return nullptr;
    return holder.Get();
}

void TextService::RequestApply(ITfContext* context, const ImeOutput& output, bool sync, const char* caretReason) {
    // 编码区属于原来的上下文：输出写回那里
    ITfContext* target = (composition_ && compositionContext_) ? compositionContext_.Get() : context;
    if (!target) return;
    auto* session = new ApplySession(this, target, output, caretReason);
    HRESULT hr = E_FAIL;
    DWORD flags = TF_ES_READWRITE | (sync ? TF_ES_SYNC : TF_ES_ASYNCDONTCARE);
    if (FAILED(target->RequestEditSession(clientId_, session, flags, &hr)) || hr == TF_E_SYNCHRONOUS) {
        target->RequestEditSession(clientId_, session, TF_ES_READWRITE | TF_ES_ASYNCDONTCARE, &hr);
    }
    session->Release();
}

void TextService::RequestCaret(ITfContext* context, const char* reason) {
    if (!context) return;
    auto* session = new CaretSession(this, context, reason);
    HRESULT hr = E_FAIL;
    context->RequestEditSession(clientId_, session, TF_ES_READ | TF_ES_ASYNCDONTCARE, &hr);
    session->Release();
}

void TextService::ApplyOutput(TfEditCookie ec, ITfContext* context, const ImeOutput& output) {
    for (const auto& commit : output.commits) InsertCommit(ec, context, commit.text, commit.replace);
    if (output.hasPreedit) SetPreedit(ec, context, output.preedit);
}

// 在光标处开一个编码区
bool TextService::StartComposition(TfEditCookie ec, ITfContext* context) {
    ComPtr<ITfInsertAtSelection> insert;
    if (FAILED(context->QueryInterface(IID_ITfInsertAtSelection, reinterpret_cast<void**>(insert.Put())))) return false;
    ComPtr<ITfRange> range;
    if (FAILED(insert->InsertTextAtSelection(ec, TF_IAS_QUERYONLY, nullptr, 0, range.Put())) || !range) return false;
    ComPtr<ITfContextComposition> contextComposition;
    if (FAILED(context->QueryInterface(IID_ITfContextComposition,
                                       reinterpret_cast<void**>(contextComposition.Put())))) {
        return false;
    }
    ComPtr<ITfComposition> composition;
    if (FAILED(contextComposition->StartComposition(ec, range.Get(), static_cast<ITfCompositionSink*>(this),
                                                    composition.Put())) ||
        !composition) {
        return false;
    }
    composition_ = composition;
    compositionContext_ = context;
    return true;
}

void TextService::SetSelectionTo(TfEditCookie ec, ITfContext* context, ITfRange* range) {
    TF_SELECTION sel = {};
    sel.range = range;
    sel.style.ase = TF_AE_NONE;
    sel.style.fInterimChar = FALSE;
    context->SetSelection(ec, 1, &sel);
}

// 结束编码区（文字留在文档里）
void TextService::FinishComposition(TfEditCookie ec) {
    if (!composition_) return;
    ComPtr<ITfRange> range;
    if (SUCCEEDED(composition_->GetRange(range.Put())) && range) {
        if (compositionContext_) ClearDisplayAttribute(ec, compositionContext_.Get(), range.Get());
        range->Collapse(ec, TF_ANCHOR_END);
        composition_->ShiftStart(ec, range.Get());
    }
    composition_->EndComposition(ec);
    composition_.Reset();
    compositionContext_.Reset();
}

// 上屏一段文字：replace 为要先删掉的光标前字数（标点替换用）
void TextService::InsertCommit(TfEditCookie ec, ITfContext* context, const std::wstring& text, int replace) {
    if (!composition_ && !StartComposition(ec, context)) {
        // 不支持编码区的程序：直接插在光标处
        ComPtr<ITfInsertAtSelection> insert;
        ComPtr<ITfRange> range;
        if (SUCCEEDED(context->QueryInterface(IID_ITfInsertAtSelection, reinterpret_cast<void**>(insert.Put())))) {
            insert->InsertTextAtSelection(ec, 0, text.c_str(), static_cast<LONG>(text.size()), range.Put());
        }
        return;
    }
    ComPtr<ITfRange> range;
    if (FAILED(composition_->GetRange(range.Put())) || !range) return;
    if (replace > 0) {
        ComPtr<ITfRange> extended;
        if (SUCCEEDED(range->Clone(extended.Put()))) {
            LONG moved = 0;
            extended->ShiftStart(ec, -replace, &moved, nullptr);
            composition_->ShiftStart(ec, extended.Get());
            range.Reset();
            composition_->GetRange(range.Put());
        }
    }
    if (!range) return;
    range->SetText(ec, 0, text.c_str(), static_cast<LONG>(text.size()));
    ClearDisplayAttribute(ec, context, range.Get());
    range->Collapse(ec, TF_ANCHOR_END);
    SetSelectionTo(ec, context, range.Get());
    FinishComposition(ec);
    currentPreedit_.clear();
}

// 把编码区设成 preedit；空串则清掉编码区
void TextService::SetPreedit(TfEditCookie ec, ITfContext* context, const std::wstring& preedit) {
    if (preedit.empty()) {
        if (composition_) {
            ComPtr<ITfRange> range;
            if (SUCCEEDED(composition_->GetRange(range.Put())) && range) range->SetText(ec, 0, L"", 0);
            FinishComposition(ec);
        }
        currentPreedit_.clear();
        return;
    }
    if (!composition_ && !StartComposition(ec, context)) return;
    ComPtr<ITfRange> range;
    if (FAILED(composition_->GetRange(range.Put())) || !range) return;
    range->SetText(ec, 0, preedit.c_str(), static_cast<LONG>(preedit.size()));
    range.Reset();
    if (FAILED(composition_->GetRange(range.Put())) || !range) return;
    ApplyDisplayAttribute(ec, context, range.Get());
    ComPtr<ITfRange> caret;
    if (SUCCEEDED(range->Clone(caret.Put()))) {
        caret->Collapse(ec, TF_ANCHOR_END);
        SetSelectionTo(ec, context, caret.Get());
    }
    currentPreedit_ = preedit;
}

void TextService::ApplyDisplayAttribute(TfEditCookie ec, ITfContext* context, ITfRange* range) {
    if (inputAttributeAtom_ == TF_INVALID_GUIDATOM) return;
    ComPtr<ITfProperty> prop;
    if (FAILED(context->GetProperty(Prop_Attribute, prop.Put()))) return;
    VARIANT v;
    VariantInit(&v);
    v.vt = VT_I4;
    v.lVal = static_cast<LONG>(inputAttributeAtom_);
    prop->SetValue(ec, range, &v);
}

void TextService::ClearDisplayAttribute(TfEditCookie ec, ITfContext* context, ITfRange* range) {
    ComPtr<ITfProperty> prop;
    if (SUCCEEDED(context->GetProperty(Prop_Attribute, prop.Put()))) prop->Clear(ec, range);
}

// 取编码区（没有编码时取光标）的屏幕矩形，回报给服务进程定位候选窗 / 模式提示
void TextService::ReportCaret(TfEditCookie ec, ITfContext* context, const char* reason) {
    ComPtr<ITfRange> range;
    if (composition_) {
        composition_->GetRange(range.Put());
    } else {
        TF_SELECTION sel = {};
        ULONG fetched = 0;
        if (SUCCEEDED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &sel, &fetched)) && fetched == 1) {
            range = sel.range;
            sel.range->Release();
        }
    }
    RECT rc = {};
    bool ok = false;
    ComPtr<ITfContextView> view;
    if (range && SUCCEEDED(context->GetActiveView(view.Put())) && view) {
        BOOL clipped = FALSE;
        if (SUCCEEDED(view->GetTextExt(ec, range.Get(), &rc, &clipped)) && (rc.bottom > rc.top || rc.right > rc.left)) {
            HWND hwnd = nullptr;
            view->GetWnd(&hwnd);
            ToPhysical(hwnd, rc);
            ok = true;
        }
    }
    if (!ok) {
        // 退而求其次：系统插入符位置
        GUITHREADINFO info = {sizeof(info)};
        if (GetGUIThreadInfo(GetCurrentThreadId(), &info) && info.hwndCaret) {
            POINT tl = {info.rcCaret.left, info.rcCaret.top};
            POINT br = {info.rcCaret.right, info.rcCaret.bottom};
            ClientToScreen(info.hwndCaret, &tl);
            ClientToScreen(info.hwndCaret, &br);
            rc = {tl.x, tl.y, br.x, br.y};
            ToPhysical(info.hwndCaret, rc);
            ok = true;
        }
    }
    if (!ok) return;
    Protocol::Fields resp;
    Request({{"cmd", "caret"},
             {"reason", reason ? reason : "update"},
             {"cx", std::to_string(rc.left)},
             {"cy", std::to_string(rc.top)},
             {"cw", std::to_string(rc.right - rc.left)},
             {"cht", std::to_string(rc.bottom - rc.top)}},
            resp, 300);
}

STDMETHODIMP TextService::OnCompositionTerminated(TfEditCookie ec, ITfComposition* composition) {
    // 应用结束了编码区（点到别处等）：文字已留在文档，通知服务端清掉 Rime 编码
    if (compositionContext_) {
        ComPtr<ITfRange> range;
        if (composition && SUCCEEDED(composition->GetRange(range.Put())) && range) {
            ClearDisplayAttribute(ec, compositionContext_.Get(), range.Get());
        }
    }
    composition_.Reset();
    compositionContext_.Reset();
    currentPreedit_.clear();
    Protocol::Fields resp;
    Request({{"cmd", "terminated"}}, resp, 300);
    return S_OK;
}

// ---- 焦点 ----

STDMETHODIMP TextService::OnSetFocus(ITfDocumentMgr* focus, ITfDocumentMgr*) {
    BlurDocument();
    if (focus) FocusDocument(focus);
    return S_OK;
}

void TextService::FocusDocument(ITfDocumentMgr* doc) {
    ComPtr<ITfContext> context;
    if (FAILED(doc->GetTop(context.Put())) || !context) return;
    focused_ = true;
    Protocol::Fields resp;
    if (!Request({{"cmd", "focus"}, {"on", "1"}}, resp)) return;
    ImeOutput output = ParseOutput(resp);
    if (output.wantCaret) RequestCaret(context.Get(), "focus");
}

void TextService::BlurDocument() {
    if (!focused_) return;
    focused_ = false;
    Protocol::Fields resp;
    if (!Request({{"cmd", "focus"}, {"on", "0"}}, resp, 500)) return;
    ImeOutput output = ParseOutput(resp);
    if (compositionContext_ && ChangesDocument(output, composition_.Get() != nullptr, currentPreedit_)) {
        RequestApply(compositionContext_.Get(), output, false, nullptr);
    }
}

// ---- 隐藏消息窗口 ----

bool TextService::CreateMessageWindow() {
    static ATOM atom = 0;
    if (!atom) {
        WNDCLASSEXW wc = {sizeof(wc)};
        wc.lpfnWndProc = WindowProc;
        wc.hInstance = g_hInst;
        wc.lpszClassName = kWindowClass;
        atom = RegisterClassExW(&wc);
        if (!atom && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return false;
    }
    window_ = CreateWindowExW(0, kWindowClass, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, g_hInst, nullptr);
    if (!window_) return false;
    SetWindowLongPtrW(window_, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
    // 宿主以管理员运行时也要收得到服务进程的消息
    ChangeWindowMessageFilterEx(window_, WM_MEIFENG_UPDATE, MSGFLT_ALLOW, nullptr);
    return true;
}

void TextService::DestroyMessageWindow() {
    if (window_) {
        SetWindowLongPtrW(window_, GWLP_USERDATA, 0);
        DestroyWindow(window_);
        window_ = nullptr;
    }
}

LRESULT CALLBACK TextService::WindowProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (msg == WM_MEIFENG_UPDATE) {
        auto* self = reinterpret_cast<TextService*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (self) self->OnServerUpdate();
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

// 服务进程有待取输出（点选候选、面板关闭恢复编码、中英切换等）
void TextService::OnServerUpdate() {
    Protocol::Fields resp;
    if (!Request({{"cmd", "fetch"}}, resp)) return;
    ImeOutput output = ParseOutput(resp);
    ComPtr<ITfContext> holder;
    ITfContext* context = FocusedContext(holder);
    if (!context) return;
    if (ChangesDocument(output, composition_.Get() != nullptr, currentPreedit_)) {
        RequestApply(context, output, false, "update");
    } else if (output.wantCaret) {
        RequestCaret(context, "update");
    }
}

// ---- 显示属性 ----

STDMETHODIMP TextService::EnumDisplayAttributeInfo(IEnumTfDisplayAttributeInfo** ppEnum) {
    if (!ppEnum) return E_INVALIDARG;
    *ppEnum = new DisplayAttributeEnum();
    return S_OK;
}

STDMETHODIMP TextService::GetDisplayAttributeInfo(REFGUID guid, ITfDisplayAttributeInfo** ppInfo) {
    if (!ppInfo) return E_INVALIDARG;
    *ppInfo = nullptr;
    if (!IsEqualGUID(guid, GUID_DisplayAttributeInput)) return E_INVALIDARG;
    *ppInfo = new DisplayAttributeInfo();
    return S_OK;
}

}  // namespace Meifeng
