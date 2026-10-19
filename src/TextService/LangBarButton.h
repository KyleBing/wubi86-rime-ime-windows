#pragma once

#include "Globals.h"

namespace Meifeng {

class TextService;

// 语言栏 / 任务栏输入指示器上的中 / 英按钮。左键切换，右键菜单：设置、造词、部署
class LangBarButton final : public ITfLangBarItemButton, public ITfSource {
public:
    explicit LangBarButton(TextService* service);

    // 文本服务停用前断开回指
    void Detach() { service_ = nullptr; }
    // 中英状态变化后刷新图标与文字
    void Refresh();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ITfLangBarItem
    STDMETHODIMP GetInfo(TF_LANGBARITEMINFO* info) override;
    STDMETHODIMP GetStatus(DWORD* status) override;
    STDMETHODIMP Show(BOOL show) override;
    STDMETHODIMP GetTooltipString(BSTR* tooltip) override;

    // ITfLangBarItemButton
    STDMETHODIMP OnClick(TfLBIClick click, POINT pt, const RECT* area) override;
    STDMETHODIMP InitMenu(ITfMenu* menu) override;
    STDMETHODIMP OnMenuSelect(UINT id) override;
    STDMETHODIMP GetIcon(HICON* icon) override;
    STDMETHODIMP GetText(BSTR* text) override;

    // ITfSource
    STDMETHODIMP AdviseSink(REFIID riid, IUnknown* sink, DWORD* cookie) override;
    STDMETHODIMP UnadviseSink(DWORD cookie) override;

private:
    ~LangBarButton();
    void ShowMenu(POINT pt);
    void RunCommand(UINT id);

    LONG refs_ = 1;
    TextService* service_;
    ITfLangBarItemSink* sink_ = nullptr;
    DWORD sinkCookie_ = 0;
};

}  // namespace Meifeng
