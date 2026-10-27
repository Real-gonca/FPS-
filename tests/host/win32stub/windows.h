/*++
    Minimal Windows declarations, used ONLY to syntax-check tools/pcctl on a
    non-Windows build machine. Never shipped, never included by the driver.
--*/
#pragma once
#include <cstdint>
#include <cstdio>
#include <cstring>

typedef int32_t   LONG;
typedef uint32_t  ULONG;
typedef uint32_t  DWORD;
typedef uint64_t  ULONGLONG;
typedef int       BOOL;
typedef void*     HANDLE;
typedef const wchar_t* LPCWSTR;

#define INVALID_HANDLE_VALUE ((HANDLE)(intptr_t)-1)
#define ERROR_FILE_NOT_FOUND 2
#define GENERIC_READ  0x80000000u
#define GENERIC_WRITE 0x40000000u
#define FILE_SHARE_READ  0x00000001u
#define FILE_SHARE_WRITE 0x00000002u
#define OPEN_EXISTING 3
#define TRUE 1
#define FALSE 0

#define FILE_DEVICE_UNKNOWN 0x00000022
#define METHOD_BUFFERED 0
#define FILE_ANY_ACCESS 0
#define FILE_READ_DATA 0x0001
#define FILE_WRITE_DATA 0x0002
#define CTL_CODE(t, f, m, a) (((t) << 16) | ((a) << 14) | ((f) << 2) | (m))

typedef union _LARGE_INTEGER {
    struct { DWORD LowPart; LONG HighPart; };
    long long QuadPart;
} LARGE_INTEGER;

inline HANDLE CreateFileW(const wchar_t*, DWORD, DWORD, void*, DWORD, DWORD, HANDLE)
{ return INVALID_HANDLE_VALUE; }
inline BOOL CloseHandle(HANDLE) { return TRUE; }
inline BOOL DeviceIoControl(HANDLE, DWORD, void*, DWORD, void*, DWORD, DWORD*, void*)
{ return TRUE; }
inline DWORD GetLastError() { return 0; }
inline void Sleep(DWORD) {}
inline BOOL QueryPerformanceFrequency(LARGE_INTEGER* f) { f->QuadPart = 1000; return TRUE; }
inline BOOL QueryPerformanceCounter(LARGE_INTEGER* c) { c->QuadPart = 0; return TRUE; }
