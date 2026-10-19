#include "PipeClient.h"

#include <cstring>

namespace Meifeng {

namespace {

// 与服务端 ImeProtocol.Sanitize 一致：非字母数字换成下划线
std::wstring SanitizedUserName() {
    wchar_t buf[256] = {};
    DWORD size = ARRAYSIZE(buf);
    if (!GetUserNameW(buf, &size)) return L"user";
    std::wstring out;
    for (wchar_t* p = buf; *p; ++p) out += IsCharAlphaNumericW(*p) ? *p : L'_';
    return out;
}

DWORD CurrentSessionId() {
    DWORD id = 0;
    ProcessIdToSessionId(GetCurrentProcessId(), &id);
    return id;
}

}  // namespace

PipeClient::PipeClient() {
    name_ = L"\\\\.\\pipe\\MeifengWubi-" + std::to_wstring(CurrentSessionId()) + L"-" + SanitizedUserName();
    event_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
}

PipeClient::~PipeClient() {
    Close();
    if (event_) CloseHandle(event_);
}

void PipeClient::Close() {
    if (pipe_ != INVALID_HANDLE_VALUE) {
        CancelIo(pipe_);
        CloseHandle(pipe_);
        pipe_ = INVALID_HANDLE_VALUE;
    }
}

// 服务进程没在运行时拉起它（后台模式，不弹设置窗口）。应用容器里的进程拉不起来，忽略失败
void PipeClient::LaunchServer() {
    ULONGLONG now = GetTickCount64();
    if (lastLaunch_ != 0 && now - lastLaunch_ < 10000) return;
    lastLaunch_ = now;
    std::wstring exe = ModuleDirectory() + L"MeifengWubi.exe";
    if (GetFileAttributesW(exe.c_str()) == INVALID_FILE_ATTRIBUTES) return;
    std::wstring cmd = L"\"" + exe + L"\" --background";
    STARTUPINFOW si = {sizeof(si)};
    PROCESS_INFORMATION pi = {};
    if (CreateProcessW(exe.c_str(), cmd.data(), nullptr, nullptr, FALSE, DETACHED_PROCESS, nullptr,
                       ModuleDirectory().c_str(), &si, &pi)) {
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

// 连接管道；失败后 1.5 秒内不再重试，避免每次按键都卡在连接上
bool PipeClient::EnsureConnected() {
    if (pipe_ != INVALID_HANDLE_VALUE) return true;
    ULONGLONG now = GetTickCount64();
    if (lastConnectAttempt_ != 0 && now - lastConnectAttempt_ < 1500) return false;
    lastConnectAttempt_ = now;

    for (int attempt = 0; attempt < 2; ++attempt) {
        HANDLE h = CreateFileW(name_.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
                               FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
        if (h != INVALID_HANDLE_VALUE) {
            pipe_ = h;
            serverPid_ = 0;
            GetNamedPipeServerProcessId(h, &serverPid_);
            return true;
        }
        DWORD err = GetLastError();
        if (err == ERROR_PIPE_BUSY) {
            if (!WaitNamedPipeW(name_.c_str(), 200)) return false;
            continue;
        }
        if (err == ERROR_FILE_NOT_FOUND) LaunchServer();
        return false;
    }
    return false;
}

// 重叠写；超时则取消，由调用方断开
bool PipeClient::WriteAll(const void* data, DWORD size, DWORD timeoutMs) {
    const char* p = static_cast<const char*>(data);
    while (size > 0) {
        OVERLAPPED ov = {};
        ov.hEvent = event_;
        ResetEvent(event_);
        DWORD n = 0;
        BOOL ok = WriteFile(pipe_, p, size, nullptr, &ov);
        if (!ok && GetLastError() != ERROR_IO_PENDING) return false;
        if (WaitForSingleObject(event_, timeoutMs) != WAIT_OBJECT_0) {
            CancelIo(pipe_);
            GetOverlappedResult(pipe_, &ov, &n, TRUE);
            return false;
        }
        if (!GetOverlappedResult(pipe_, &ov, &n, FALSE) || n == 0) return false;
        p += n;
        size -= n;
    }
    return true;
}

// 重叠读满 size 字节；超时同上
bool PipeClient::ReadAll(void* data, DWORD size, DWORD timeoutMs) {
    char* p = static_cast<char*>(data);
    while (size > 0) {
        OVERLAPPED ov = {};
        ov.hEvent = event_;
        ResetEvent(event_);
        DWORD n = 0;
        BOOL ok = ReadFile(pipe_, p, size, nullptr, &ov);
        if (!ok && GetLastError() != ERROR_IO_PENDING) return false;
        if (WaitForSingleObject(event_, timeoutMs) != WAIT_OBJECT_0) {
            CancelIo(pipe_);
            GetOverlappedResult(pipe_, &ov, &n, TRUE);
            return false;
        }
        if (!GetOverlappedResult(pipe_, &ov, &n, FALSE) || n == 0) return false;
        p += n;
        size -= n;
    }
    return true;
}

bool PipeClient::Request(const Protocol::Fields& request, Protocol::Fields& response, DWORD timeoutMs) {
    response.clear();
    if (!event_ || !EnsureConnected()) return false;
    std::string body = Protocol::Encode(request);
    std::string packet(4 + body.size(), '\0');
    UINT32 len = static_cast<UINT32>(body.size());
    std::memcpy(packet.data(), &len, 4);
    std::memcpy(packet.data() + 4, body.data(), body.size());

    UINT32 replyLen = 0;
    if (!WriteAll(packet.data(), static_cast<DWORD>(packet.size()), timeoutMs) ||
        !ReadAll(&replyLen, 4, timeoutMs) || replyLen > 4 * 1024 * 1024) {
        Close();
        return false;
    }
    std::string reply(replyLen, '\0');
    if (replyLen > 0 && !ReadAll(reply.data(), replyLen, timeoutMs)) {
        Close();
        return false;
    }
    response = Protocol::Decode(reply);
    return true;
}

}  // namespace Meifeng
