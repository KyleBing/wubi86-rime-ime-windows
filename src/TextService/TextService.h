#pragma once

#include "Globals.h"
#include "PipeClient.h"

#include <string>
#include <vector>

namespace Meifeng {

class LangBarButton;

// 服务进程一次处理的输出：依次上屏（可先删掉光标前若干字），再把编码区设为 preedit
struct ImeOutput {
    struct Commit {
        std::wstring text;
        int replace = 0;
    };
    std::vector<Commit> commits;
    std::wstring preedit;
    bool hasPreedit = false;
    bool wantCaret = false;

    bool Empty() const { return commits.empty() && !hasPreedit && !wantCaret; }
};

// 文本服务主体：每个使用输入法的界面线程一个实例
class TextService final : public ITfTextInputProcessorEx,
                    public ITfThreadMgrEventSink,
                    public ITfKeyEventSink,
                    public ITfCompositionSink,
                    public ITfDisplayAttributeProvider {
public:
    TextService();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ITfTextInputProcessor(Ex)
    STDMETHODIMP Activate(ITfThreadMgr* threadMgr, TfClientId clientId) override;
    STDMETHODIMP Deactivate() override;
    STDMETHODIMP ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD flags) override;

    // ITfThreadMgrEventSink
    STDMETHODIMP OnInitDocumentMgr(ITfDocumentMgr*) override { return S_OK; }
    STDMETHODIMP OnUninitDocumentMgr(ITfDocumentMgr*) override { return S_OK; }
    STDMETHODIMP OnSetFocus(ITfDocumentMgr* focus, ITfDocumentMgr* previous) override;
    STDMETHODIMP OnPushContext(ITfContext*) override { return S_OK; }
    STDMETHODIMP OnPopContext(ITfContext*) override { return S_OK; }

    // ITfKeyEventSink
    STDMETHODIMP OnSetFocus(BOOL) override { return S_OK; }
    STDMETHODIMP OnTestKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    STDMETHODIMP OnKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    STDMETHODIMP OnTestKeyUp(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    STDMETHODIMP OnKeyUp(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    STDMETHODIMP OnPreservedKey(ITfContext*, REFGUID, BOOL* eaten) override {
        *eaten = FALSE;
        return S_OK;
    }

    // ITfCompositionSink
    STDMETHODIMP OnCompositionTerminated(TfEditCookie ec, ITfComposition* composition) override;

    // ITfDisplayAttributeProvider
    STDMETHODIMP EnumDisplayAttributeInfo(IEnumTfDisplayAttributeInfo** ppEnum) override;
    STDMETHODIMP GetDisplayAttributeInfo(REFGUID guid, ITfDisplayAttributeInfo** ppInfo) override;

    // ---- 供编辑会话 / 语言栏使用 ----

    // 在编辑会话里把输出写进文档
    void ApplyOutput(TfEditCookie ec, ITfContext* context, const ImeOutput& output);
    // 在编辑会话里取编码区（或光标）的屏幕矩形，并回报给服务进程
    void ReportCaret(TfEditCookie ec, ITfContext* context, const char* reason);

    bool AsciiMode() const { return asciiMode_; }
    // 语言栏点击：切换中英
    void ToggleAscii();
    // 语言栏菜单：settings / quickadd / deploy
    void OpenServerFeature(const char* what);

private:
    ~TextService();

    // 按键转给服务进程；结果按键码缓存，OnTest* 与 On* 只发一次
    struct KeyResult {
        WPARAM vk = 0;
        bool up = false;
        bool valid = false;
        bool eaten = false;
        ImeOutput output;
    };
    bool SendKey(WPARAM vk, LPARAM lParam, bool up, KeyResult& result);
    bool TestKey(ITfContext* context, WPARAM vk, LPARAM lParam, bool up, BOOL* eaten);
    bool HandleKey(ITfContext* context, WPARAM vk, LPARAM lParam, bool up, BOOL* eaten);
    bool KeyboardEnabled(ITfContext* context);

    // 请求编辑会话写入输出；sync 为 true 时尽量同步（按键处理里用）
    void RequestApply(ITfContext* context, const ImeOutput& output, bool sync, const char* caretReason);
    void RequestCaret(ITfContext* context, const char* reason);
    ITfContext* FocusedContext(ComPtr<ITfContext>& holder);

    // 通用请求：附带客户端号与消息窗口；应答中的 ascii 同步到语言栏
    bool Request(Protocol::Fields request, Protocol::Fields& response, DWORD timeoutMs = 800);
    static ImeOutput ParseOutput(const Protocol::Fields& response);
    void UpdateAscii(const Protocol::Fields& response);

    // 文档焦点变化
    void FocusDocument(ITfDocumentMgr* doc);
    void BlurDocument();

    // 隐藏消息窗口：服务进程投递 WM_MEIFENG_UPDATE 后来取输出
    bool CreateMessageWindow();
    void DestroyMessageWindow();
    static LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
    void OnServerUpdate();

    // 编码区操作（都在读写编辑会话里调用）
    bool StartComposition(TfEditCookie ec, ITfContext* context);
    void FinishComposition(TfEditCookie ec);
    void InsertCommit(TfEditCookie ec, ITfContext* context, const std::wstring& text, int replace);
    void SetPreedit(TfEditCookie ec, ITfContext* context, const std::wstring& preedit);
    static void SetSelectionTo(TfEditCookie ec, ITfContext* context, ITfRange* range);
    void ApplyDisplayAttribute(TfEditCookie ec, ITfContext* context, ITfRange* range);
    static void ClearDisplayAttribute(TfEditCookie ec, ITfContext* context, ITfRange* range);

    bool AdviseSinks();
    void UnadviseSinks();
    void SetKeyboardOpen(bool open);
    void EndCompositionNow();

    LONG refs_ = 1;
    ComPtr<ITfThreadMgr> threadMgr_;
    TfClientId clientId_ = TF_CLIENTID_NULL;
    DWORD threadMgrCookie_ = TF_INVALID_COOKIE;
    bool keySinkAdvised_ = false;
    ComPtr<ITfComposition> composition_;
    ComPtr<ITfContext> compositionContext_;
    TfGuidAtom inputAttributeAtom_ = TF_INVALID_GUIDATOM;
    LangBarButton* langBar_ = nullptr;
    HWND window_ = nullptr;
    PipeClient pipe_;
    long long clientKey_ = 0;
    bool focused_ = false;
    bool asciiMode_ = false;
    // 文档里当前编码区的文字，用来判断服务端输出是否需要改文档
    std::wstring currentPreedit_;
    KeyResult pendingKey_;
};

}  // namespace Meifeng
