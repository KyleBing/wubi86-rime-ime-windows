#pragma once

#include "Globals.h"

namespace Meifeng {

// 编码区的显示属性：点状下划线，文字颜色跟随应用
class DisplayAttributeInfo final : public ITfDisplayAttributeInfo {
public:
    DisplayAttributeInfo();

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    STDMETHODIMP GetGUID(GUID* guid) override;
    STDMETHODIMP GetDescription(BSTR* description) override;
    STDMETHODIMP GetAttributeInfo(TF_DISPLAYATTRIBUTE* attribute) override;
    STDMETHODIMP SetAttributeInfo(const TF_DISPLAYATTRIBUTE* attribute) override;
    STDMETHODIMP Reset() override;

private:
    ~DisplayAttributeInfo();
    LONG refs_ = 1;
};

// 只有一个显示属性的枚举器
class DisplayAttributeEnum final : public IEnumTfDisplayAttributeInfo {
public:
    DisplayAttributeEnum();

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    STDMETHODIMP Clone(IEnumTfDisplayAttributeInfo** ppEnum) override;
    STDMETHODIMP Next(ULONG count, ITfDisplayAttributeInfo** info, ULONG* fetched) override;
    STDMETHODIMP Reset() override;
    STDMETHODIMP Skip(ULONG count) override;

private:
    ~DisplayAttributeEnum();
    LONG refs_ = 1;
    ULONG index_ = 0;
};

}  // namespace Meifeng
