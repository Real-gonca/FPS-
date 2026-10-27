cmake_minimum_required(VERSION 3.20)

# pcctl - PrecisionCurve control (Win32 console client)
#
#   cmake -S tools/pcctl -B build/pcctl -A x64
#   cmake --build build/pcctl --config Release
#
# The shared headers are used verbatim, so the CLI is provably talking the same
# ABI as the driver and the C# GUI.
project(pcctl LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT WIN32)
    message(FATAL_ERROR "pcctl targets Windows only (it talks to a WDM driver).")
endif()

add_executable(pcctl main.cpp)

target_include_directories(pcctl PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/../../shared")

if(MSVC)
    target_compile_options(pcctl PRIVATE /W4 /permissive-)
    target_compile_definitions(pcctl PRIVATE _CRT_SECURE_NO_WARNINGS UNICODE _UNICODE)
else()
    target_compile_options(pcctl PRIVATE -Wall -Wextra)
endif()

target_link_libraries(pcctl PRIVATE advapi32)
