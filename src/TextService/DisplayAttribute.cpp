#include "DisplayAttribute.h"

namespace Meifeng {

namespace {

// 点状下划线；颜色交给应用默认
TF_DISPLAYATTRIBUTE InputAttribute() {
    TF_DISPLAYATTRIBUTE a = {};
    a.crText.type = TF_CT_NONE;
    a.crBk.type = TF_CT_NONE;
    a.lsStyle = TF_LS_DOT;
    a.fBoldLine = FALSE;
    a.crLine.type = TF_CT_NONE;
    a.bAttr = TF_ATTR_INPUT;
    return a;
}

}  // namespace

DisplayAttributeInfo::DisplayAttributeInfo() { DllAddRef(); }
DisplayAttributeInfo::~DisplayAttributeInfo() { DllRelease(); }

STDMETHODIMP DisplayAttributeInfo::QueryInterface(REFIID riid, void** ppv) {
    if (!ppv) return E_INVALIDARG;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfDisplayAttributeInfo)) {
        *ppv = static_cast<ITfDisplayAttributeInfo*>(this);
        AddRef();
        return S_OK;
    }
    *ppv = nullptr;
    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG) DisplayAttributeInfo::AddRef() { return InterlockedIncrement(&refs_); }

STDMETHODIMP_(ULONG) DisplayAttributeInfo::Release() {
    LONG n = InterlockedDecrement(&refs_);
    if (n == 0) delete this;
    return n;
}

STDMETHODIMP DisplayAttributeInfo::GetGUID(GUID* guid) {
    if (!guid) return E_INVALIDARG;
    *guid = GUID_DisplayAttributeInput;
    return S_OK;
}

STDMETHODIMP DisplayAttributeInfo::GetDescription(BSTR* description) {
    if (!description) return E_INVALIDARG;
    *description = SysAllocString(L"玫枫五笔编码");
    return *description ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP DisplayAttributeInfo::GetAttributeInfo(TF_DISPLAYATTRIBUTE* attribute) {
    if (!attribute) return E_INVALIDARG;
    *attribute = InputAttribute();
    return S_OK;
}

STDMETHODIMP DisplayAttributeInfo::SetAttributeInfo(const TF_DISPLAYATTRIBUTE*) { return E_NOTIMPL; }

STDMETHODIMP DisplayAttributeInfo::Reset() { return S_OK; }

DisplayAttributeEnum::DisplayAttributeEnum() { DllAddRef(); }
DisplayAttributeEnum::~DisplayAttributeEnum() { DllRelease(); }

STDMETHODIMP DisplayAttributeEnum::QueryInterface(REFIID riid, void** ppv) {
    if (!ppv) return E_INVALIDARG;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IEnumTfDisplayAttributeInfo)) {
        *ppv = static_cast<IEnumTfDisplayAttributeInfo*>(this);
        AddRef();
        return S_OK;
    }
    *ppv = nullptr;
    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG) DisplayAttributeEnum::AddRef() { return InterlockedIncrement(&refs_); }

STDMETHODIMP_(ULONG) DisplayAttributeEnum::Release() {
    LONG n = InterlockedDecrement(&refs_);
    if (n == 0) delete this;
    return n;
}

STDMETHODIMP DisplayAttributeEnum::Clone(IEnumTfDisplayAttributeInfo** ppEnum) {
    if (!ppEnum) return E_INVALIDARG;
    auto* clone = new DisplayAttributeEnum();
    clone->index_ = index_;
    *ppEnum = clone;
    return S_OK;
}

STDMETHODIMP DisplayAttributeEnum::Next(ULONG count, ITfDisplayAttributeInfo** info, ULONG* fetched) {
    if (!info) return E_INVALIDARG;
    ULONG n = 0;
    if (count > 0 && index_ == 0) {
        info[0] = new DisplayAttributeInfo();
        index_ = 1;
        n = 1;
    }
    if (fetched) *fetched = n;
    return n == count ? S_OK : S_FALSE;
}

STDMETHODIMP DisplayAttributeEnum::Reset() {
    index_ = 0;
    return S_OK;
}

STDMETHODIMP DisplayAttributeEnum::Skip(ULONG count) {
    if (count > 0 && index_ == 0) {
        index_ = 1;
        return count == 1 ? S_OK : S_FALSE;
    }
    return count == 0 ? S_OK : S_FALSE;
}

}  // namespace Meifeng
