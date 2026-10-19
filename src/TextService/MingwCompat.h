#pragma once

// mingw-w64 的 msctf.h / ctfutb.h 缺少部分 TSF 声明。只在 mingw 下做语法检查时使用，
// 正式构建走 MSVC + Windows SDK，不会包含本文件。声明与 SDK 保持一致
#ifdef __MINGW32__

#include <ctfutb.h>

#ifndef TF_CLIENTID_NULL
#define TF_CLIENTID_NULL 0
#endif
#ifndef TF_INVALID_GUIDATOM
#define TF_INVALID_GUIDATOM 0
#endif

#ifndef TF_LBI_ICON
#define TF_LBI_ICON 0x00000001
#define TF_LBI_TEXT 0x00000002
#define TF_LBI_TOOLTIP 0x00000004
#define TF_LBI_BITMAP 0x00000008
#define TF_LBI_BALLOON 0x00000010
#endif

#ifndef TF_LBI_STYLE_SHOWNINTRAY
#define TF_LBI_STYLE_HIDDENSTATUSCONTROL 0x00000001
#define TF_LBI_STYLE_SHOWNINTRAY 0x00000002
#define TF_LBI_STYLE_BTN_BUTTON 0x00010000
#define TF_LBI_STYLE_BTN_MENU 0x00020000
#define TF_LBI_STYLE_BTN_TOGGLE 0x00040000
#endif

#ifndef TF_LBMENUF_SEPARATOR
#define TF_LBMENUF_CHECKED 0x00000001
#define TF_LBMENUF_SUBMENU 0x00000002
#define TF_LBMENUF_SEPARATOR 0x00000004
#define TF_LBMENUF_RADIOCHECKED 0x00000008
#define TF_LBMENUF_GRAYED 0x00000010
#endif

typedef enum { TF_LBI_CLK_RIGHT = 1, TF_LBI_CLK_LEFT = 2 } TfLBIClick;

// {6E4E2102-F9CD-433D-B496-303CE03A6507}
static const IID IID_ITfTextInputProcessorEx = {0x6e4e2102, 0xf9cd, 0x433d, {0xb4, 0x96, 0x30, 0x3c, 0xe0, 0x3a, 0x65, 0x07}};
// {FEE47777-163C-4769-996A-6E9C50AD8F54}
static const IID IID_ITfDisplayAttributeProvider = {0xfee47777, 0x163c, 0x4769, {0x99, 0x6a, 0x6e, 0x9c, 0x50, 0xad, 0x8f, 0x54}};
// {28C7F1D0-DE25-11D2-AFDD-00105A2799B5}
static const IID IID_ITfLangBarItemButton = {0x28c7f1d0, 0xde25, 0x11d2, {0xaf, 0xdd, 0x00, 0x10, 0x5a, 0x27, 0x99, 0xb5}};
// {6F8A98E4-AAA0-4F15-8C5B-07E0DF0A3DD8}
static const IID IID_ITfMenu = {0x6f8a98e4, 0xaaa0, 0x4f15, {0x8c, 0x5b, 0x07, 0xe0, 0xdf, 0x0a, 0x3d, 0xd8}};

struct ITfTextInputProcessorEx : public ITfTextInputProcessor {
    virtual HRESULT STDMETHODCALLTYPE ActivateEx(ITfThreadMgr* ptim, TfClientId tid, DWORD dwFlags) = 0;
};

struct ITfDisplayAttributeProvider : public IUnknown {
    virtual HRESULT STDMETHODCALLTYPE EnumDisplayAttributeInfo(IEnumTfDisplayAttributeInfo** ppEnum) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetDisplayAttributeInfo(REFGUID guid, ITfDisplayAttributeInfo** ppInfo) = 0;
};

struct ITfMenu : public IUnknown {
    virtual HRESULT STDMETHODCALLTYPE AddMenuItem(UINT uId, DWORD dwFlags, HBITMAP hbmp, HBITMAP hbmpMask,
                                                  const WCHAR* pch, ULONG cch, ITfMenu** ppMenu) = 0;
};

struct ITfLangBarItemButton : public ITfLangBarItem {
    virtual HRESULT STDMETHODCALLTYPE OnClick(TfLBIClick click, POINT pt, const RECT* prcArea) = 0;
    virtual HRESULT STDMETHODCALLTYPE InitMenu(ITfMenu* pMenu) = 0;
    virtual HRESULT STDMETHODCALLTYPE OnMenuSelect(UINT wID) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetIcon(HICON* phIcon) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetText(BSTR* pbstrText) = 0;
};

#endif  // __MINGW32__
