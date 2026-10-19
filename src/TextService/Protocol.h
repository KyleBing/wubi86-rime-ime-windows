#pragma once

#include <string>
#include <utility>
#include <vector>

// 与服务进程通信的消息格式（与 Meifeng.Core.Ipc.ImeProtocol 一致）：
// 4 字节小端长度 + UTF-8 正文；正文为若干行 key=value，值里 % 换行 = 转义成 %25 %0A %0D %3D
namespace Meifeng::Protocol {

using Fields = std::vector<std::pair<std::string, std::string>>;

std::string ToUtf8(const std::wstring& s);
std::wstring FromUtf8(const std::string& s);

std::string Escape(const std::string& v);
std::string Unescape(const std::string& v);

// 字段表编成正文
std::string Encode(const Fields& fields);
// 正文拆成字段表
Fields Decode(const std::string& body);

// 按键名取值；不存在时返回 fallback
std::string Get(const Fields& fields, const std::string& key, const std::string& fallback = "");
int GetInt(const Fields& fields, const std::string& key, int fallback = 0);

}  // namespace Meifeng::Protocol
