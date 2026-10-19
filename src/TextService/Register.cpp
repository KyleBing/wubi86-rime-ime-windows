#include "Globals.h"

// 注册 / 注销：COM 组件、TSF 输入配置与类别。由安装程序以管理员身份调用 regsvr32
namespace Meifeng {

namespace {

const GUID* const kCategories[] = {
    &Cat_TipKeyboard,
    &Cat_DisplayAttributeProvider,
    &Cat_ImmersiveSupport,
    &Cat_SystraySupport,
    &Cat_UiElementEnabled,
    &Cat_InputModeCompartment,
    &Cat_SecureMode,
};

std::wstring ClsidString() {
    wchar_t buf[64] = {};
    StringFromGUID2(CLSID_TextService, buf, ARRAYSIZE(buf));
    return buf;
}

std::wstring ModulePath() {
    wchar_t path[MAX_PATH] = {};
    DWORD n = GetModuleFileNameW(g_hInst, path, ARRAYSIZE(path));
    return std::wstring(path, n);
}

// HKCR\CLSID\{clsid}\InprocServer32 = DLL 路径，单元线程
bool RegisterServer() {
    std::wstring key = L"CLSID\\" + ClsidString();
    HKEY clsidKey = nullptr;
    if (RegCreateKeyExW(HKEY_CLASSES_ROOT, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &clsidKey, nullptr) !=
        ERROR_SUCCESS) {
        return false;
    }
    RegSetValueExW(clsidKey, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(kDisplayName), sizeof(kDisplayName));
    HKEY inproc = nullptr;
    bool ok = RegCreateKeyExW(clsidKey, L"InprocServer32", 0, nullptr, 0, KEY_WRITE, nullptr, &inproc, nullptr) ==
              ERROR_SUCCESS;
    if (ok) {
        std::wstring path = ModulePath();
        RegSetValueExW(inproc, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(path.c_str()),
                       static_cast<DWORD>((path.size() + 1) * sizeof(wchar_t)));
        const wchar_t model[] = L"Apartment";
        RegSetValueExW(inproc, L"ThreadingModel", 0, REG_SZ, reinterpret_cast<const BYTE*>(model), sizeof(model));
        RegCloseKey(inproc);
    }
    RegCloseKey(clsidKey);
    return ok;
}

void UnregisterServer() {
    std::wstring key = L"CLSID\\" + ClsidString();
    RegDeleteTreeW(HKEY_CLASSES_ROOT, key.c_str());
}

// 输入配置：简体中文下的「玫枫五笔」，图标取服务进程 exe 的第一个图标
bool RegisterProfile() {
    ComPtr<ITfInputProcessorProfileMgr> mgr;
    if (FAILED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                                IID_ITfInputProcessorProfileMgr, reinterpret_cast<void**>(mgr.Put())))) {
        return false;
    }
    std::wstring icon = ModuleDirectory() + L"MeifengWubi.exe";
    HRESULT hr = mgr->RegisterProfile(CLSID_TextService, kLangId, GUID_Profile, kDisplayName,
                                      static_cast<ULONG>(wcslen(kDisplayName)), icon.c_str(),
                                      static_cast<ULONG>(icon.size()), 0, nullptr, 0, TRUE, 0);
    return SUCCEEDED(hr);
}

void UnregisterProfile() {
    ComPtr<ITfInputProcessorProfileMgr> mgr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                                   IID_ITfInputProcessorProfileMgr, reinterpret_cast<void**>(mgr.Put())))) {
        mgr->UnregisterProfile(CLSID_TextService, kLangId, GUID_Profile, 0);
    }
}

bool RegisterCategories() {
    ComPtr<ITfCategoryMgr> mgr;
    if (FAILED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_ITfCategoryMgr,
                                reinterpret_cast<void**>(mgr.Put())))) {
        return false;
    }
    for (const GUID* cat : kCategories) {
        if (FAILED(mgr->RegisterCategory(CLSID_TextService, *cat, CLSID_TextService))) return false;
    }
    return true;
}

void UnregisterCategories() {
    ComPtr<ITfCategoryMgr> mgr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_ITfCategoryMgr,
                                   reinterpret_cast<void**>(mgr.Put())))) {
        for (const GUID* cat : kCategories) mgr->UnregisterCategory(CLSID_TextService, *cat, CLSID_TextService);
    }
}

// regsvr32 调用时 COM 未必已初始化
struct ComScope {
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ~ComScope() {
        if (SUCCEEDED(hr)) CoUninitialize();
    }
};

}  // namespace

}  // namespace Meifeng

using namespace Meifeng;

STDAPI DllRegisterServer() {
    ComScope com;
    if (!RegisterServer() || !RegisterProfile() || !RegisterCategories()) {
        UnregisterCategories();
        UnregisterProfile();
        UnregisterServer();
        return E_FAIL;
    }
    return S_OK;
}

STDAPI DllUnregisterServer() {
    ComScope com;
    UnregisterCategories();
    UnregisterProfile();
    UnregisterServer();
    return S_OK;
}
