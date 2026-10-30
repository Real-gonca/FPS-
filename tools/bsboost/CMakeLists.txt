cmake_minimum_required(VERSION 3.20)
project(bsboost C)

# DLL nativa x64 + CLI. Sem dependencia de runtime extra alem do CRT do MSVC.
if(NOT MSVC)
    message(WARNING "bsboost foi escrito para MSVC x64 (Windows SDK).")
endif()

set(CMAKE_C_STANDARD 11)

add_library(bsboost SHARED
    bsboost.c
    bsboost.def
)
target_include_directories(bsboost PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(bsboost PRIVATE _CRT_SECURE_NO_WARNINGS)
target_link_libraries(bsboost PRIVATE advapi32)
set_target_properties(bsboost PROPERTIES
    OUTPUT_NAME "bsboost"
    PREFIX ""              # no "lib" prefix
)

add_executable(bsboostctl host/bsboostctl.c)
target_compile_definitions(bsboostctl PRIVATE _CRT_SECURE_NO_WARNINGS)
target_link_libraries(bsboostctl PRIVATE bsboost advapi32)
set_target_properties(bsboostctl PROPERTIES OUTPUT_NAME "bsboostctl")

if(MSVC)
    target_compile_options(bsboost PRIVATE /W4 /O2)
    target_compile_options(bsboostctl PRIVATE /W4 /O2)
endif()
