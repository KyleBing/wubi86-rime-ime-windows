#include "Globals.h"

#include "TextService.h"

#include <new>

namespace Meifeng {

// {55C8F83B-3F8E-46AF-BFED-5B224BB9381A}
const CLSID CLSID_TextService = {0x55c8f83b, 0x3f8e, 0x46af, {0xbf, 0xed, 0x5b, 0x22, 0x4b, 0xb9, 0x38, 0x1a}};
// {7FFFE32B-55CA-488B-AFF4-6AD3EB0C3A8C}
const GUID GUID_Profile = {0x7fffe32b, 0x55ca, 0x488b, {0xaf, 0xf4, 0x6a, 0xd3, 0xeb, 0x0c, 0x3a, 0x8c}};
// {9F8E36D8-7E62-4C87-B6EE-3315A92E9C8B}
const GUID GUID_DisplayAttributeInput = {0x9f8e36d8, 0x7e62, 0x4c87, {0xb6, 0xee, 0x33, 0x15, 0xa9, 0x2e, 0x9c, 0x8b}};
// {51AEE202-7D1F-4DB3-91B0-CC6EA316168F}
const GUID GUID_LangBarButton = {0x51aee202, 0x7d1f, 0x4db3, {0x91, 0xb0, 0xcc, 0x6e, 0xa3, 0x16, 0x16, 0x8f}};

// 以下为系统定义的 GUID（值与 Windows SDK 相同）
const GUID GUID_LbiInputMode = {0x2c77a81e, 0x41cc, 0x4178, {0xa3, 0xa7, 0x5f, 0x8a, 0x98, 0x75, 0x68, 0xe6}};
const GUID Cat_TipKeyboard = {0x34745c63, 0xb2f0, 0x4784, {0x8b, 0x67, 0x5e, 0x12, 0xc8, 0x70, 0x1a, 0x31}};
const GUID Cat_DisplayAttributeProvider = {0x046b8c80, 0x1647, 0x40f7, {0x9b, 0x21, 0xb9, 0x3b, 0x81, 0xaa, 0xbc, 0x1b}};
const GUID Cat_ImmersiveSupport = {0x13a016df, 0x560b, 0x46cd, {0x94, 0x7a, 0x4c, 0x3a, 0xf1, 0xe0, 0xe3, 0x5d}};
const GUID Cat_SystraySupport = {0x25504fb4, 0x7bab, 0x4bc1, {0x9c, 0x69, 0xcf, 0x81, 0x89, 0x0f, 0x0e, 0xf5}};
const GUID Cat_UiElementEnabled = {0x49d2f9cf, 0x1f5e, 0x11d7, {0xa6, 0xd3, 0x00, 0x06, 0x5b, 0x84, 0x43, 0x5c}};
const GUID Cat_InputModeCompartment = {0xccf05dd7, 0x4a87, 0x11d7, {0xa6, 0xe2, 0x00, 0x06, 0x5b, 0x84, 0x43, 0x5c}};
const GUID Cat_SecureMode = {0x49d2f9ce, 0x1f5e, 0x11d7, {0xa6, 0xd3, 0x00, 0x06, 0x5b, 0x84, 0x43, 0x5c}};
const GUID Prop_Attribute = {0x34b45670, 0x7526, 0x11d2, {0xa1, 0x47, 0x00, 0x10, 0x5a, 0x27, 0x99, 0xb5}};
const GUID Compartment_KeyboardOpenClose = {0x58273aad, 0x01bb, 0x4164, {0x95, 0xc6, 0x75, 0x5b, 0xa0, 0xb5, 0x16, 0x2d}};
const GUID Compartment_KeyboardDisabled = {0x71a5b253, 0x1951, 0x466b, {0x9f, 0xbc, 0x9c, 0x88, 0x08, 0xfa, 0x84, 0xf2}};
const GUID Compartment_EmptyContext = {0xd7487dbf, 0x804e, 0x41c5, {0x89, 0x4d, 0xad, 0x96, 0xfd, 0x4e, 0xea, 0x13}};

HINSTANCE g_hInst = nullptr;
LONG g_dllRefs = 0;

std::wstring ModuleDirectory() {
    wchar_t path[MAX_PATH] = {};
    DWORD n = GetModuleFileNameW(g_hInst, path, ARRAYSIZE(path));
    std::wstring s(path, n);
    size_t slash = s.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring() : s.substr(0, slash + 1);
}

namespace {

// 文本服务的类工厂
class ClassFactory : public IClassFactory {
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_INVALIDARG;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory)) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    // 静态单例，引用计数只影响 DLL 是否可卸载
    STDMETHODIMP_(ULONG) AddRef() override {
        DllAddRef();
        return 2;
    }
    STDMETHODIMP_(ULONG) Release() override {
        DllRelease();
        return 1;
    }

    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override {
        if (!ppv) return E_INVALIDARG;
        *ppv = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* service = new (std::nothrow) TextService();
        if (!service) return E_OUTOFMEMORY;
        HRESULT hr = service->QueryInterface(riid, ppv);
        service->Release();
        return hr;
    }

    STDMETHODIMP LockServer(BOOL lock) override {
        if (lock) DllAddRef();
        else DllRelease();
        return S_OK;
    }
};

ClassFactory g_factory;

}  // namespace

}  // namespace Meifeng

using namespace Meifeng;

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_hInst = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv) {
    if (!ppv) return E_INVALIDARG;
    *ppv = nullptr;
    if (!IsEqualCLSID(clsid, CLSID_TextService)) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow() { return g_dllRefs <= 0 ? S_OK : S_FALSE; }
