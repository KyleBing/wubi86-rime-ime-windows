#include "Protocol.h"

#include "Globals.h"

#include <cstdlib>

namespace Meifeng::Protocol {

std::string ToUtf8(const std::wstring& s) {
    if (s.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0, nullptr, nullptr);
    std::string out(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), out.data(), n, nullptr, nullptr);
    return out;
}

std::wstring FromUtf8(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
    std::wstring out(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), out.data(), n);
    return out;
}

std::string Escape(const std::string& v) {
    std::string out;
    out.reserve(v.size());
    for (char c : v) {
        switch (c) {
            case '%': out += "%25"; break;
            case '\n': out += "%0A"; break;
            case '\r': out += "%0D"; break;
            case '=': out += "%3D"; break;
            default: out += c; break;
        }
    }
    return out;
}

// 十六进制字符转数值，非法返回 -1
static int HexValue(char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

std::string Unescape(const std::string& v) {
    if (v.find('%') == std::string::npos) return v;
    std::string out;
    out.reserve(v.size());
    for (size_t i = 0; i < v.size(); ++i) {
        if (v[i] == '%' && i + 2 < v.size()) {
            int hi = HexValue(v[i + 1]);
            int lo = HexValue(v[i + 2]);
            if (hi >= 0 && lo >= 0) {
                out += static_cast<char>(hi * 16 + lo);
                i += 2;
                continue;
            }
        }
        out += v[i];
    }
    return out;
}

std::string Encode(const Fields& fields) {
    std::string body;
    for (const auto& [k, v] : fields) {
        body += k;
        body += '=';
        body += Escape(v);
        body += '\n';
    }
    return body;
}

Fields Decode(const std::string& body) {
    Fields fields;
    size_t start = 0;
    while (start < body.size()) {
        size_t end = body.find('\n', start);
        if (end == std::string::npos) end = body.size();
        std::string line = body.substr(start, end - start);
        size_t eq = line.find('=');
        if (eq != std::string::npos && eq > 0) fields.emplace_back(line.substr(0, eq), Unescape(line.substr(eq + 1)));
        start = end + 1;
    }
    return fields;
}

std::string Get(const Fields& fields, const std::string& key, const std::string& fallback) {
    for (const auto& [k, v] : fields) {
        if (k == key) return v;
    }
    return fallback;
}

int GetInt(const Fields& fields, const std::string& key, int fallback) {
    std::string v = Get(fields, key);
    if (v.empty()) return fallback;
    return std::atoi(v.c_str());
}

}  // namespace Meifeng::Protocol
