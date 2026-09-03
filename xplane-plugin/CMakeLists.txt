cmake_minimum_required(VERSION 3.24)
project(FreeFlightCabinBridge VERSION 0.4.4 LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The v0.4.4 package currently builds the Windows X-Plane plugin only.")
endif()

set(XPLANE_SDK_ROOT "" CACHE PATH "Path to the extracted X-Plane SDK/SDK directory")
if(NOT EXISTS "${XPLANE_SDK_ROOT}/CHeaders/XPLM/XPLMPlugin.h")
    message(FATAL_ERROR "Set XPLANE_SDK_ROOT to the extracted SDK directory containing CHeaders and Libraries.")
endif()

add_library(FreeFlightCabinBridge MODULE src/FreeFlightCabinBridge.cpp)
target_compile_features(FreeFlightCabinBridge PRIVATE cxx_std_20)
target_compile_definitions(FreeFlightCabinBridge PRIVATE
    IBM=1
    NOMINMAX=1
    WIN32_LEAN_AND_MEAN=1
    XPLM200=1
    XPLM210=1
    XPLM300=1
    XPLM301=1
    XPLM303=1
    XPLM400=1)
target_include_directories(FreeFlightCabinBridge PRIVATE
    "${XPLANE_SDK_ROOT}/CHeaders/XPLM"
    "${XPLANE_SDK_ROOT}/CHeaders/Widgets")
target_link_libraries(FreeFlightCabinBridge PRIVATE "${XPLANE_SDK_ROOT}/Libraries/Win/XPLM_64.lib")
target_compile_options(FreeFlightCabinBridge PRIVATE /W4 /permissive- /EHsc)
set_target_properties(FreeFlightCabinBridge PROPERTIES
    PREFIX ""
    SUFFIX ".xpl"
    OUTPUT_NAME "win")
