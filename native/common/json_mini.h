#pragma once

#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

struct JsonObjectView {
    std::string_view raw;
};

std::vector<JsonObjectView> json_root_array_objects(std::string_view json);
std::vector<JsonObjectView> json_object_array_objects(std::string_view object_json,
                                                      std::string_view key);
std::string json_object_string(std::string_view object_json, std::string_view key);
std::int64_t json_object_int64(std::string_view object_json, std::string_view key,
                               std::int64_t fallback = 0);
bool json_object_bool(std::string_view object_json, std::string_view key, bool fallback = false);
bool json_object_has(std::string_view object_json, std::string_view key);
