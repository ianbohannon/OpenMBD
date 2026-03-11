# CMakeLists.txt – OpenMBD.OcctBridge native DLL
#
# Builds the thin C-compatible wrapper around Open CASCADE Technology (OCCT)
# that the C# SOLIDWORKS add-in calls via P/Invoke.
#
# Tested with OCCT 7.7.x on Windows (MSVC 2019/2022) and Linux (GCC 11+).
#
# Usage:
#   cmake -B build -DCMAKE_BUILD_TYPE=Release
#   cmake --build build --config Release
#
# Copy the resulting OpenMBD.OcctBridge.dll (Windows) or
# libOpenMBD.OcctBridge.so (Linux) to the same directory as OpenMBD.dll.

cmake_minimum_required(VERSION 3.16)
project(OpenMBD.OcctBridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# ---- Locate OCCT -----------------------------------------------------------
# OCCT provides its own CMake config file (OpenCASCADEConfig.cmake).
# Either install OCCT via an official installer / package manager, or set
# OpenCASCADE_DIR to point to the directory containing that config file.
find_package(OpenCASCADE REQUIRED COMPONENTS
    TKernel
    TKMath
    TKBRep
    TKTopAlgo
    TKG3d
    TKGeomBase
    TKGeomAlgo
    TKCAF
    TKLCAF
    TKXCAF
    TKXSBase
    TKSTEP
    TKSTEPAttr
    TKSTEPBase
    TKXDESTEP
)

# ---- Build the shared library ----------------------------------------------
add_library(OpenMBD.OcctBridge SHARED OcctBridge.cpp)

target_compile_definitions(OpenMBD.OcctBridge PRIVATE OCCTBRIDGE_EXPORTS)

target_include_directories(OpenMBD.OcctBridge
    PUBLIC  ${CMAKE_CURRENT_SOURCE_DIR}
    PRIVATE ${OpenCASCADE_INCLUDE_DIR}
)

target_link_libraries(OpenMBD.OcctBridge PRIVATE
    TKernel
    TKMath
    TKBRep
    TKTopAlgo
    TKG3d
    TKGeomBase
    TKGeomAlgo
    TKCAF
    TKLCAF
    TKXCAF
    TKXSBase
    TKSTEP
    TKSTEPAttr
    TKSTEPBase
    TKXDESTEP
)

# On Windows, ensure the output name matches what the C# P/Invoke expects.
set_target_properties(OpenMBD.OcctBridge PROPERTIES OUTPUT_NAME "OpenMBD.OcctBridge")

# ---- Install ---------------------------------------------------------------
install(TARGETS OpenMBD.OcctBridge
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
)
