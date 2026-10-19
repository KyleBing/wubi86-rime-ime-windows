#pragma once

#include "Globals.h"
#include "Protocol.h"

namespace Meifeng {

// 到服务进程的命名管道连接。所有调用都在文本服务所在的 STA 线程上，单线程使用
class PipeClient {
public:
    PipeClient();
    ~PipeClient();

    // 发一条请求并等应答；超时或断开返回 false（随后自动重连）
    bool Request(const Protocol::Fields& request, Protocol::Fields& response, DWORD timeoutMs = 800);

    void Close();
    bool Connected() const { return pipe_ != INVALID_HANDLE_VALUE; }
    // 服务进程号，用于 AllowSetForegroundWindow
    DWORD ServerPid() const { return serverPid_; }

private:
    bool EnsureConnected();
    bool WriteAll(const void* data, DWORD size, DWORD timeoutMs);
    bool ReadAll(void* data, DWORD size, DWORD timeoutMs);
    void LaunchServer();

    HANDLE pipe_ = INVALID_HANDLE_VALUE;
    HANDLE event_ = nullptr;
    std::wstring name_;
    DWORD serverPid_ = 0;
    ULONGLONG lastConnectAttempt_ = 0;
    ULONGLONG lastLaunch_ = 0;
};

}  // namespace Meifeng
