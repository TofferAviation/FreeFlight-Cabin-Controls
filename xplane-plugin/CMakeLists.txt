cmake_minimum_required(VERSION 3.24)
project(FreeFlightCabinBridge VERSION 0.5.0 LANGUAGES CXX)

set(XPLANE_SDK_ROOT "" CACHE PATH "Path to the extracted X-Plane SDK/SDK directory")
if(NOT EXISTS "${XPLANE_SDK_ROOT}/CHeaders/XPLM/XPLMPlugin.h")
    message(FATAL_ERROR "Set XPLANE_SDK_ROOT to the extracted SDK directory containing CHeaders and Libraries.")
endif()

add_library(FreeFlightCabinBridge MODULE src/FreeFlightCabinBridge.cpp)
target_compile_features(FreeFlightCabinBridge PRIVATE cxx_std_20)
target_compile_definitions(FreeFlightCabinBridge PRIVATE
    XPLM200=1
    XPLM210=1
    XPLM300=1
    XPLM301=1
    XPLM303=1
    XPLM400=1)
target_include_directories(FreeFlightCabinBridge PRIVATE
    "${XPLANE_SDK_ROOT}/CHeaders/XPLM"
    "${XPLANE_SDK_ROOT}/CHeaders/Widgets")

if(WIN32)
    target_compile_definitions(FreeFlightCabinBridge PRIVATE IBM=1 NOMINMAX=1 WIN32_LEAN_AND_MEAN=1)
    target_link_libraries(FreeFlightCabinBridge PRIVATE "${XPLANE_SDK_ROOT}/Libraries/Win/XPLM_64.lib")
    target_compile_options(FreeFlightCabinBridge PRIVATE /W4 /permissive- /EHsc)
    set(platform_plugin_name "win")
elseif(UNIX AND NOT APPLE)
    target_compile_definitions(FreeFlightCabinBridge PRIVATE LIN=1)
    target_link_libraries(FreeFlightCabinBridge PRIVATE "${XPLANE_SDK_ROOT}/Libraries/Lin/XPLM_64.so")
    target_compile_options(FreeFlightCabinBridge PRIVATE -Wall -Wextra -Wpedantic -fPIC)
    set(platform_plugin_name "lin")
else()
    message(FATAL_ERROR "FreeFlightCabinBridge currently supports Windows and Linux X-Plane builds.")
endif()

set_target_properties(FreeFlightCabinBridge PROPERTIES
    PREFIX ""
    SUFFIX ".xpl"
    OUTPUT_NAME "${platform_plugin_name}")
