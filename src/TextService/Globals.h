#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <msctf.h>
#include <ctfutb.h>
#include <olectl.h>
#include <string>

#include "MingwCompat.h"

// 玫枫五笔 TSF 文本服务：只负责把按键转给服务进程（MeifengWubi.exe），再把上屏 / 编码写回文档
namespace Meifeng {

// 文本服务 CLSID 与输入配置（简体中文）
extern const CLSID CLSID_TextService;
extern const GUID GUID_Profile;
// 编码下划线的显示属性
extern const GUID GUID_DisplayAttributeInput;
// 语言栏中 / 英按钮（Windows 8 起任务栏输入指示器使用 GUID_LBI_INPUTMODE）
extern const GUID GUID_LangBarButton;
extern const GUID GUID_LbiInputMode;

// TSF 类别（自己定义一份，避免依赖各 SDK 是否导出）
extern const GUID Cat_TipKeyboard;
extern const GUID Cat_DisplayAttributeProvider;
extern const GUID Cat_ImmersiveSupport;
extern const GUID Cat_SystraySupport;
extern const GUID Cat_UiElementEnabled;
extern const GUID Cat_InputModeCompartment;
extern const GUID Cat_SecureMode;
extern const GUID Prop_Attribute;
extern const GUID Compartment_KeyboardOpenClose;
extern const GUID Compartment_KeyboardDisabled;
extern const GUID Compartment_EmptyContext;

constexpr LANGID kLangId = MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED);
constexpr wchar_t kDisplayName[] = L"玫枫五笔";
// 与 Meifeng.Core.Ipc.ImeProtocol.WM_MEIFENG_UPDATE 一致
constexpr UINT WM_MEIFENG_UPDATE = WM_APP + 0x4D46;

extern HINSTANCE g_hInst;
extern LONG g_dllRefs;

// DLL 所在目录（末尾带反斜杠）；服务进程与 DLL 装在同一目录
std::wstring ModuleDirectory();

inline void DllAddRef() { InterlockedIncrement(&g_dllRefs); }
inline void DllRelease() { InterlockedDecrement(&g_dllRefs); }

// 简单的 COM 智能指针，避免依赖 ATL / WRL
template <class T>
class ComPtr {
public:
    ComPtr() = default;
    ComPtr(T* p) : p_(p) { if (p_) p_->AddRef(); }
    ComPtr(const ComPtr& o) : p_(o.p_) { if (p_) p_->AddRef(); }
    ~ComPtr() { Reset(); }
    ComPtr& operator=(const ComPtr& o) {
        if (o.p_) o.p_->AddRef();
        Reset();
        p_ = o.p_;
        return *this;
    }
    ComPtr& operator=(T* p) {
        if (p) p->AddRef();
        Reset();
        p_ = p;
        return *this;
    }
    void Reset() {
        if (p_) { T* t = p_; p_ = nullptr; t->Release(); }
    }
    T** Put() { Reset(); return &p_; }
    T* Get() const { return p_; }
    T* operator->() const { return p_; }
    explicit operator bool() const { return p_ != nullptr; }
    template <class U>
    HRESULT As(ComPtr<U>& out) const {
        return p_ ? p_->QueryInterface(__uuidof(U), reinterpret_cast<void**>(out.Put())) : E_POINTER;
    }

private:
    T* p_ = nullptr;
};

}  // namespace Meifeng
