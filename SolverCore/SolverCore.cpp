#define SOLVER_CORE_EXPORTS
#include "SolverCore.h"

#include <windows.h>
#include <winhttp.h>

#include <algorithm>
#include <cctype>
#include <cstring>
#include <string>
#include <vector>

namespace {

std::wstring utf8ToWide(const std::string& input) {
    if (input.empty()) {
        return L"";
    }
    int len = MultiByteToWideChar(CP_UTF8, 0, input.data(), static_cast<int>(input.size()), nullptr, 0);
    if (len <= 0) {
        return L"";
    }
    std::wstring out(static_cast<size_t>(len), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, input.data(), static_cast<int>(input.size()), &out[0], len);
    return out;
}

std::string wideToUtf8(const std::wstring& input) {
    if (input.empty()) {
        return "";
    }
    int len = WideCharToMultiByte(CP_UTF8, 0, input.data(), static_cast<int>(input.size()), nullptr, 0, nullptr, nullptr);
    if (len <= 0) {
        return "";
    }
    std::string out(static_cast<size_t>(len), '\0');
    WideCharToMultiByte(CP_UTF8, 0, input.data(), static_cast<int>(input.size()), &out[0], len, nullptr, nullptr);
    return out;
}

int writeOutput(const std::string& text, char* outBuf, int outBufSize) {
    if (!outBuf || outBufSize <= 0) {
        return 0;
    }
    size_t copyLen = std::min(text.size(), static_cast<size_t>(outBufSize - 1));
    if (copyLen > 0) {
        std::memcpy(outBuf, text.data(), copyLen);
    }
    outBuf[copyLen] = '\0';
    return static_cast<int>(copyLen);
}

std::string escapeJsonString(const std::string& s) {
    std::string out;
    out.reserve(s.size() + 32);
    for (unsigned char c : s) {
        switch (c) {
            case '"':  out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\b': out += "\\b";  break;
            case '\f': out += "\\f";  break;
            case '\n': out += "\\n";  break;
            case '\r': out += "\\r";  break;
            case '\t': out += "\\t";  break;
            default:
                if (c < 0x20) {
                    char buf[7];
                    snprintf(buf, sizeof(buf), "\\u%04x", c);
                    out += buf;
                } else {
                    out.push_back(static_cast<char>(c));
                }
                break;
        }
    }
    return out;
}

void appendUtf8Codepoint(std::string& out, unsigned int cp) {
    if (cp <= 0x7F) {
        out.push_back(static_cast<char>(cp));
    } else if (cp <= 0x7FF) {
        out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    } else if (cp <= 0xFFFF) {
        out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    } else {
        out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    }
}

bool parseHex4(const std::string& s, size_t pos, unsigned int& outVal) {
    if (pos + 4 > s.size()) {
        return false;
    }
    unsigned int v = 0;
    for (size_t i = 0; i < 4; ++i) {
        char c = s[pos + i];
        v <<= 4;
        if (c >= '0' && c <= '9') v |= static_cast<unsigned int>(c - '0');
        else if (c >= 'a' && c <= 'f') v |= static_cast<unsigned int>(c - 'a' + 10);
        else if (c >= 'A' && c <= 'F') v |= static_cast<unsigned int>(c - 'A' + 10);
        else return false;
    }
    outVal = v;
    return true;
}

bool extractJsonStringAt(const std::string& json, size_t quotePos, std::string& outValue, size_t& endPos) {
    if (quotePos >= json.size() || json[quotePos] != '"') {
        return false;
    }
    std::string out;
    size_t i = quotePos + 1;
    while (i < json.size()) {
        char c = json[i];
        if (c == '"') {
            outValue = std::move(out);
            endPos = i + 1;
            return true;
        }
        if (c == '\\' && i + 1 < json.size()) {
            char next = json[i + 1];
            switch (next) {
                case '"':  out.push_back('"');  i += 2; break;
                case '\\': out.push_back('\\'); i += 2; break;
                case '/':  out.push_back('/');  i += 2; break;
                case 'b':  out.push_back('\b'); i += 2; break;
                case 'f':  out.push_back('\f'); i += 2; break;
                case 'n':  out.push_back('\n'); i += 2; break;
                case 'r':  out.push_back('\r'); i += 2; break;
                case 't':  out.push_back('\t'); i += 2; break;
                case 'u': {
                    unsigned int cp = 0;
                    if (parseHex4(json, i + 2, cp)) {
                        if (cp >= 0xD800 && cp <= 0xDBFF && i + 12 <= json.size() &&
                            json[i + 6] == '\\' && json[i + 7] == 'u') {
                            unsigned int low = 0;
                            if (parseHex4(json, i + 8, low) && low >= 0xDC00 && low <= 0xDFFF) {
                                cp = 0x10000 + (((cp - 0xD800) << 10) | (low - 0xDC00));
                                i += 12;
                                appendUtf8Codepoint(out, cp);
                                break;
                            }
                        }
                        appendUtf8Codepoint(out, cp);
                        i += 6;
                    } else {
                        out.push_back(next);
                        i += 2;
                    }
                    break;
                }
                default:
                    out.push_back(next);
                    i += 2;
                    break;
            }
        } else {
            out.push_back(c);
            ++i;
        }
    }
    return false;
}

bool findJsonStringFieldAfter(const std::string& json, const std::string& anchor, const std::string& fieldName, std::string& outValue) {
    size_t startPos = 0;
    if (!anchor.empty()) {
        size_t anchorPos = json.find(anchor);
        if (anchorPos == std::string::npos) {
            return false;
        }
        startPos = anchorPos + anchor.size();
    }

    std::string pattern = "\"" + fieldName + "\"";
    size_t pos = startPos;
    while ((pos = json.find(pattern, pos)) != std::string::npos) {
        size_t colon = pos + pattern.size();
        while (colon < json.size() && std::isspace(static_cast<unsigned char>(json[colon]))) {
            ++colon;
        }
        if (colon < json.size() && json[colon] == ':') {
            size_t valStart = colon + 1;
            while (valStart < json.size() && std::isspace(static_cast<unsigned char>(json[valStart]))) {
                ++valStart;
            }
            if (valStart < json.size() && json[valStart] == '"') {
                size_t endPos = 0;
                if (extractJsonStringAt(json, valStart, outValue, endPos)) {
                    return true;
                }
            }
        }
        pos += pattern.size();
    }
    return false;
}

std::string stripMarkdownFences(std::string text) {
    auto trim = [](std::string& s) {
        while (!s.empty() && std::isspace(static_cast<unsigned char>(s.front()))) s.erase(s.begin());
        while (!s.empty() && std::isspace(static_cast<unsigned char>(s.back()))) s.pop_back();
    };
    trim(text);
    if (text.rfind("```", 0) == 0) {
        size_t firstNl = text.find('\n');
        if (firstNl != std::string::npos) {
            text = text.substr(firstNl + 1);
            size_t lastFence = text.rfind("```");
            if (lastFence != std::string::npos) {
                text = text.substr(0, lastFence);
            }
            trim(text);
        }
    }
    return text;
}

struct HttpResponse {
    int statusCode = 0;
    std::string body;
    std::string error;
};

HttpResponse executeHttp(
    const std::string& method,
    const std::string& url,
    const std::vector<std::wstring>& headers,
    const std::string& body,
    int receiveTimeoutMs = 120000) {
    HttpResponse result;
    std::wstring wUrl = utf8ToWide(url);

    URL_COMPONENTS uc{};
    uc.dwStructSize = sizeof(uc);
    wchar_t hostBuf[256]{};
    wchar_t pathBuf[2048]{};
    uc.lpszHostName = hostBuf;
    uc.dwHostNameLength = sizeof(hostBuf) / sizeof(wchar_t);
    uc.lpszUrlPath = pathBuf;
    uc.dwUrlPathLength = sizeof(pathBuf) / sizeof(wchar_t);

    if (!WinHttpCrackUrl(wUrl.c_str(), static_cast<DWORD>(wUrl.size()), 0, &uc)) {
        result.error = "Invalid URL: " + url;
        return result;
    }

    HINTERNET hSession = WinHttpOpen(L"YandexUchebnikSolver/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                                     WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!hSession) {
        result.error = "WinHttpOpen failed";
        return result;
    }

    WinHttpSetTimeouts(hSession, 15000, 30000, 60000, receiveTimeoutMs);

    HINTERNET hConnect = WinHttpConnect(hSession, hostBuf, uc.nPort, 0);
    if (!hConnect) {
        WinHttpCloseHandle(hSession);
        result.error = "WinHttpConnect failed";
        return result;
    }

    DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
    std::wstring wMethod = utf8ToWide(method);
    HINTERNET hRequest = WinHttpOpenRequest(hConnect, wMethod.c_str(), pathBuf, nullptr,
                                            WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hRequest) {
        WinHttpCloseHandle(hConnect);
        WinHttpCloseHandle(hSession);
        result.error = "WinHttpOpenRequest failed";
        return result;
    }

    for (const auto& h : headers) {
        WinHttpAddRequestHeaders(hRequest, h.c_str(), static_cast<DWORD>(-1), WINHTTP_ADDREQ_FLAG_ADD);
    }

    LPVOID bodyPtr = body.empty() ? WINHTTP_NO_REQUEST_DATA : const_cast<char*>(body.data());
    DWORD bodyLen = static_cast<DWORD>(body.size());

    if (!WinHttpSendRequest(hRequest, WINHTTP_NO_ADDITIONAL_HEADERS, 0, bodyPtr, bodyLen, bodyLen, 0) ||
        !WinHttpReceiveResponse(hRequest, nullptr)) {
        DWORD err = GetLastError();
        WinHttpCloseHandle(hRequest);
        WinHttpCloseHandle(hConnect);
        WinHttpCloseHandle(hSession);
        result.error = "HTTP request failed (WinHTTP error " + std::to_string(err) + ")";
        return result;
    }

    DWORD status = 0;
    DWORD statusSize = sizeof(status);
    WinHttpQueryHeaders(hRequest, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                        WINHTTP_HEADER_NAME_BY_INDEX, &status, &statusSize, WINHTTP_NO_HEADER_INDEX);
    result.statusCode = static_cast<int>(status);

    DWORD avail = 0;
    while (WinHttpQueryDataAvailable(hRequest, &avail) && avail > 0) {
        std::string chunk(avail, '\0');
        DWORD readBytes = 0;
        if (WinHttpReadData(hRequest, &chunk[0], avail, &readBytes) && readBytes > 0) {
            chunk.resize(readBytes);
            result.body += chunk;
        } else {
            break;
        }
    }

    WinHttpCloseHandle(hRequest);
    WinHttpCloseHandle(hConnect);
    WinHttpCloseHandle(hSession);
    return result;
}

std::string normalizeBaseUrl(std::string url) {
    while (!url.empty() && url.back() == '/') {
        url.pop_back();
    }
    return url;
}

}

extern "C" {

SOLVER_API int ys_http_get(const char* url, char* outBuf, int outBufSize) {
    if (!url || !*url) {
        writeOutput("Empty URL", outBuf, outBufSize);
        return -1;
    }
    HttpResponse resp = executeHttp("GET", url, {}, "");
    if (!resp.error.empty()) {
        writeOutput(resp.error, outBuf, outBufSize);
        return -1;
    }
    if (resp.statusCode < 200 || resp.statusCode >= 300) {
        writeOutput("HTTP " + std::to_string(resp.statusCode) + ": " + resp.body, outBuf, outBufSize);
        return -1;
    }
    writeOutput(resp.body, outBuf, outBufSize);
    return 0;
}

SOLVER_API int ys_solve_task_vision(
    const char* provider,
    const char* baseUrl,
    const char* apiKey,
    const char* model,
    const char* systemPromptUtf8,
    const char* promptUtf8,
    const char* imageBase64Utf8,
    double temperature,
    char* outBuf,
    int outBufSize) {
    std::string prov = provider ? provider : "openai";
    std::string base = normalizeBaseUrl(baseUrl ? baseUrl : "");
    std::string key = apiKey ? apiKey : "";
    std::string mdl = model ? model : "";
    std::string prompt = promptUtf8 ? promptUtf8 : "";
    std::string extraSys = systemPromptUtf8 ? systemPromptUtf8 : "";
    std::string imgB64 = imageBase64Utf8 ? imageBase64Utf8 : "";

    if (mdl.empty() || prompt.empty()) {
        writeOutput("Model and prompt must not be empty.", outBuf, outBufSize);
        return -1;
    }

    if (temperature < 0.0) temperature = 0.0;
    if (temperature > 2.0) temperature = 2.0;

    char tempBuf[32];
    snprintf(tempBuf, sizeof(tempBuf), "%.2f", temperature);
    std::string tempStr(tempBuf);

    std::string endpoint;
    std::string textPayload;
    std::string visionPayload;
    std::vector<std::wstring> headers;
    headers.push_back(L"Content-Type: application/json; charset=utf-8");

    std::string sysPrompt =
        "You solve Russian school interactive tasks from Yandex Uchebnik. "
        "Return ONLY valid JSON without markdown or explanations matching the requested schema.";
    if (!extraSys.empty()) {
        sysPrompt += "\n" + extraSys;
    }

    if (prov == "gemini") {
        if (base.empty()) {
            base = "https://generativelanguage.googleapis.com/v1beta";
        }
        endpoint = base + "/models/" + mdl + ":generateContent?key=" + key;
        textPayload = "{\"contents\":[{\"parts\":[{\"text\":\"" +
                      escapeJsonString(sysPrompt + "\n\n" + prompt) +
                      "\"}]}],\"generationConfig\":{\"temperature\":" + tempStr + "}}";
        if (!imgB64.empty()) {
            visionPayload = "{\"contents\":[{\"parts\":[{\"text\":\"" +
                            escapeJsonString(sysPrompt + "\n\n" + prompt) +
                            "\"},{\"inlineData\":{\"mimeType\":\"image/jpeg\",\"data\":\"" +
                            escapeJsonString(imgB64) +
                            "\"}}]}],\"generationConfig\":{\"temperature\":" + tempStr + "}}";
        }
    } else {
        if (base.empty()) {
            base = "https://api.openai.com/v1";
        }
        endpoint = base + "/chat/completions";
        if (!key.empty()) {
            headers.push_back(L"Authorization: Bearer " + utf8ToWide(key));
        }
        headers.push_back(L"HTTP-Referer: https://education.yandex.ru");
        headers.push_back(L"X-Title: YandexUchebnikSolver");
        textPayload = "{\"model\":\"" + escapeJsonString(mdl) +
                      "\",\"temperature\":" + tempStr +
                      ",\"messages\":[{\"role\":\"system\",\"content\":\"" +
                      escapeJsonString(sysPrompt) + "\"},{\"role\":\"user\",\"content\":\"" +
                      escapeJsonString(prompt) + "\"}]}";
        if (!imgB64.empty()) {
            visionPayload = "{\"model\":\"" + escapeJsonString(mdl) +
                            "\",\"temperature\":" + tempStr +
                            ",\"messages\":[{\"role\":\"system\",\"content\":\"" +
                            escapeJsonString(sysPrompt) +
                            "\"},{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"" +
                            escapeJsonString(prompt) +
                            "\"},{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/jpeg;base64," +
                            escapeJsonString(imgB64) + "\"}}]}]}";
        }
    }

    HttpResponse resp = imgB64.empty()
        ? executeHttp("POST", endpoint, headers, textPayload, 45000)
        : executeHttp("POST", endpoint, headers, visionPayload, 20000);
    if (!imgB64.empty() && (!resp.error.empty() || resp.statusCode == 400 || resp.statusCode == 422)) {
        resp = executeHttp("POST", endpoint, headers, textPayload, 45000);
    }
    if (!resp.error.empty()) {
        writeOutput(resp.error, outBuf, outBufSize);
        return -1;
    }

    if (resp.statusCode < 200 || resp.statusCode >= 300) {
        std::string errMsg;
        if (findJsonStringFieldAfter(resp.body, "\"error\"", "message", errMsg)) {
            writeOutput("API HTTP " + std::to_string(resp.statusCode) + ": " + errMsg, outBuf, outBufSize);
        } else {
            writeOutput("API HTTP " + std::to_string(resp.statusCode) + ": " + resp.body, outBuf, outBufSize);
        }
        return -1;
    }

    std::string content;
    bool ok = false;
    if (prov == "gemini") {
        ok = findJsonStringFieldAfter(resp.body, "\"candidates\"", "text", content);
    } else {
        ok = findJsonStringFieldAfter(resp.body, "\"choices\"", "content", content);
    }

    if (!ok || content.empty()) {
        writeOutput("Failed to parse LLM response: " + resp.body, outBuf, outBufSize);
        return -1;
    }

    writeOutput(stripMarkdownFences(content), outBuf, outBufSize);
    return 0;
}

SOLVER_API int ys_solve_task_ex(
    const char* provider,
    const char* baseUrl,
    const char* apiKey,
    const char* model,
    const char* systemPromptUtf8,
    const char* promptUtf8,
    double temperature,
    char* outBuf,
    int outBufSize) {
    return ys_solve_task_vision(provider, baseUrl, apiKey, model, systemPromptUtf8, promptUtf8, "", temperature, outBuf, outBufSize);
}

SOLVER_API int ys_solve_task(
    const char* provider,
    const char* baseUrl,
    const char* apiKey,
    const char* model,
    const char* promptUtf8,
    char* outBuf,
    int outBufSize) {
    return ys_solve_task_ex(provider, baseUrl, apiKey, model, "", promptUtf8, 0.1, outBuf, outBufSize);
}

}
