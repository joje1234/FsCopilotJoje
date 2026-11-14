#pragma once
#include <cstdint>
using DWORD = uint32_t;
using HANDLE = void*;
using HRESULT = int32_t;
#define CALLBACK
#define FAILED(value) ((value) < 0)
#define SUCCEEDED(value) ((value) >= 0)
