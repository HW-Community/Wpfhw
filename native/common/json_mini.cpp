#include "json_mini.h"

#include <cctype>
#include <cstdlib>

namespace {

const char* skip_ws(const char* p, const char* end)
{
    while (p < end && std::isspace(static_cast<unsigned char>(*p))) {
        ++p;
    }
    return p;
}

const char* skip_string(const char* p, const char* end)
{
    if (p >= end || *p != '"') {
        return p;
    }
    ++p;
    while (p < end) {
        if (*p == '\\') {
            p += (p + 1 < end) ? 2 : 1;
            continue;
        }
        if (*p == '"') {
            return p + 1;
        }
        ++p;
    }
    return end;
}

const char* skip_value(const char* p, const char* end);

const char* skip_object_or_array(const char* p, const char* end, char open_ch, char close_ch)
{
    if (p >= end || *p != open_ch) {
        return p;
    }
    ++p;
    int depth = 1;
    while (p < end && depth > 0) {
        p = skip_ws(p, end);
        if (p >= end) {
            break;
        }
        if (*p == '"') {
            p = skip_string(p, end);
            continue;
        }
        if (*p == open_ch) {
            ++depth;
            ++p;
            continue;
        }
        if (*p == close_ch) {
            --depth;
            ++p;
            continue;
        }
        if (*p == '{' || *p == '[') {
            p = skip_value(p, end);
            continue;
        }
        ++p;
    }
    return p;
}

const char* skip_value(const char* p, const char* end)
{
    p = skip_ws(p, end);
    if (p >= end) {
        return end;
    }
    if (*p == '"') {
        return skip_string(p, end);
    }
    if (*p == '{') {
        return skip_object_or_array(p, end, '{', '}');
    }
    if (*p == '[') {
        return skip_object_or_array(p, end, '[', ']');
    }
    while (p < end && *p != ',' && *p != '}' && *p != ']' && !std::isspace(static_cast<unsigned char>(*p))) {
        ++p;
    }
    return p;
}

bool match_key(const char* p, const char* end, std::string_view key)
{
    if (p >= end || *p != '"') {
        return false;
    }
    ++p;
    if (static_cast<size_t>(end - p) < key.size() + 1) {
        return false;
    }
    if (std::string_view(p, key.size()) != key) {
        return false;
    }
    return p[key.size()] == '"';
}

const char* find_key_value(std::string_view object_json, std::string_view key)
{
    const char* p = object_json.data();
    const char* end = p + object_json.size();
    p = skip_ws(p, end);
    if (p >= end || *p != '{') {
        return nullptr;
    }
    ++p;
    while (p < end) {
        p = skip_ws(p, end);
        if (p >= end) {
            break;
        }
        if (*p == '}') {
            break;
        }
        if (*p != '"') {
            p = skip_value(p, end);
            p = skip_ws(p, end);
            if (p < end && *p == ',') {
                ++p;
            }
            continue;
        }
        const bool hit = match_key(p, end, key);
        p = skip_string(p, end);
        p = skip_ws(p, end);
        if (p < end && *p == ':') {
            ++p;
        }
        p = skip_ws(p, end);
        if (hit) {
            return p;
        }
        p = skip_value(p, end);
        p = skip_ws(p, end);
        if (p < end && *p == ',') {
            ++p;
        }
    }
    return nullptr;
}

void append_utf8(std::string& out, unsigned int cp)
{
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

unsigned int parse_hex4(const char* p, const char* end)
{
    unsigned int v = 0;
    for (int i = 0; i < 4; ++i) {
        if (p + i >= end) {
            return 0;
        }
        const char c = p[i];
        v <<= 4;
        if (c >= '0' && c <= '9') {
            v |= static_cast<unsigned int>(c - '0');
        } else if (c >= 'a' && c <= 'f') {
            v |= static_cast<unsigned int>(c - 'a' + 10);
        } else if (c >= 'A' && c <= 'F') {
            v |= static_cast<unsigned int>(c - 'A' + 10);
        }
    }
    return v;
}

std::string unescape_json_string(const char* p, const char* end)
{
    std::string out;
    if (p >= end || *p != '"') {
        return out;
    }
    ++p;
    while (p < end && *p != '"') {
        if (*p == '\\' && p + 1 < end) {
            ++p;
            switch (*p) {
            case '"':
            case '\\':
            case '/':
                out.push_back(*p);
                ++p;
                break;
            case 'b':
                out.push_back('\b');
                ++p;
                break;
            case 'f':
                out.push_back('\f');
                ++p;
                break;
            case 'n':
                out.push_back('\n');
                ++p;
                break;
            case 'r':
                out.push_back('\r');
                ++p;
                break;
            case 't':
                out.push_back('\t');
                ++p;
                break;
            case 'u': {
                unsigned int cp = parse_hex4(p + 1, end);
                p += 5;
                if (cp >= 0xD800 && cp <= 0xDBFF && p + 5 < end && p[0] == '\\' && p[1] == 'u') {
                    const unsigned int low = parse_hex4(p + 2, end);
                    if (low >= 0xDC00 && low <= 0xDFFF) {
                        cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                        p += 6;
                    }
                }
                append_utf8(out, cp);
                break;
            }
            default:
                out.push_back(*p);
                ++p;
                break;
            }
            continue;
        }
        out.push_back(*p);
        ++p;
    }
    return out;
}

std::string_view slice_value(const char* p, const char* end)
{
    const char* start = skip_ws(p, end);
    const char* stop = skip_value(start, end);
    return std::string_view(start, static_cast<size_t>(stop - start));
}

std::vector<JsonObjectView> parse_object_array(std::string_view array_json)
{
    std::vector<JsonObjectView> out;
    const char* p = array_json.data();
    const char* end = p + array_json.size();
    p = skip_ws(p, end);
    if (p >= end || *p != '[') {
        return out;
    }
    ++p;
    while (p < end) {
        p = skip_ws(p, end);
        if (p >= end || *p == ']') {
            break;
        }
        if (*p == '{') {
            const char* start = p;
            const char* stop = skip_object_or_array(p, end, '{', '}');
            out.push_back(JsonObjectView{std::string_view(start, static_cast<size_t>(stop - start))});
            p = stop;
        } else {
            p = skip_value(p, end);
        }
        p = skip_ws(p, end);
        if (p < end && *p == ',') {
            ++p;
        }
    }
    return out;
}

} // namespace

std::vector<JsonObjectView> json_root_array_objects(std::string_view json)
{
    const char* p = skip_ws(json.data(), json.data() + json.size());
    const char* end = json.data() + json.size();
    if (p < end && *p == '[') {
        return parse_object_array(std::string_view(p, static_cast<size_t>(end - p)));
    }
    return {};
}

std::vector<JsonObjectView> json_object_array_objects(std::string_view object_json,
                                                      std::string_view key)
{
    const char* value = find_key_value(object_json, key);
    if (!value) {
        return {};
    }
    const char* end = object_json.data() + object_json.size();
    std::string_view raw = slice_value(value, end);
    return parse_object_array(raw);
}

std::string json_object_string(std::string_view object_json, std::string_view key)
{
    const char* value = find_key_value(object_json, key);
    if (!value) {
        return {};
    }
    const char* end = object_json.data() + object_json.size();
    value = skip_ws(value, end);
    return unescape_json_string(value, end);
}

std::int64_t json_object_int64(std::string_view object_json, std::string_view key,
                               std::int64_t fallback)
{
    const char* value = find_key_value(object_json, key);
    if (!value) {
        return fallback;
    }
    const char* end = object_json.data() + object_json.size();
    value = skip_ws(value, end);
    if (value >= end) {
        return fallback;
    }
    char* stop = nullptr;
    const std::int64_t parsed = std::strtoll(value, &stop, 10);
    if (stop == value) {
        return fallback;
    }
    return parsed;
}

bool json_object_bool(std::string_view object_json, std::string_view key, bool fallback)
{
    const char* value = find_key_value(object_json, key);
    if (!value) {
        return fallback;
    }
    const char* end = object_json.data() + object_json.size();
    value = skip_ws(value, end);
    if (value + 4 <= end && std::string_view(value, 4) == "true") {
        return true;
    }
    if (value + 5 <= end && std::string_view(value, 5) == "false") {
        return false;
    }
    return fallback;
}

bool json_object_has(std::string_view object_json, std::string_view key)
{
    return find_key_value(object_json, key) != nullptr;
}
