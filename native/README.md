# Building OpenMBD.OcctBridge

The native DLL `OpenMBD.OcctBridge.dll` wraps the Open CASCADE Technology
(OCCT) C++ library so that the C# SOLIDWORKS add-in can call it via P/Invoke.

## Prerequisites

| Tool | Minimum version |
|------|----------------|
| CMake | 3.16 |
| C++ compiler | MSVC 2019+ (Windows) or GCC 11+ (Linux) |
| OCCT | 7.7.0 |

### Installing OCCT

**Windows (recommended)**

1. Download the official OCCT installer from
   [https://dev.opencascade.org/release](https://dev.opencascade.org/release).
2. Run the installer (default path: `C:\OpenCASCADE-7.7.0`).
3. The installer registers the CMake config file automatically.

**Windows via vcpkg**

```powershell
vcpkg install opencascade:x64-windows
```

**Ubuntu / Debian**

```bash
sudo apt-get install libocct-dev
```

**macOS (Homebrew)**

```bash
brew install opencascade
```

## Build steps

```powershell
# From the repository root
cd native

# Configure (point to your OCCT installation if cmake cannot find it automatically)
cmake -B build -DCMAKE_BUILD_TYPE=Release `
      -DOpenCASCADE_DIR="C:\OpenCASCADE-7.7.0\cmake"

# Build
cmake --build build --config Release

# Copy the DLL next to OpenMBD.dll
copy build\Release\OpenMBD.OcctBridge.dll ..\src\bin\Release\net48\
```

On Linux / macOS replace back-slashes with forward-slashes and `copy` with
`cp`.

## Runtime dependencies

The bridge DLL dynamically links against several OCCT DLLs.  All of them
must be present in the application directory or on `PATH` / `LD_LIBRARY_PATH`
at runtime.  The required OCCT DLLs are:

```
TKernel.dll   TKMath.dll    TKBRep.dll    TKTopAlgo.dll
TKG3d.dll     TKGeomBase.dll TKGeomAlgo.dll
TKCAF.dll     TKLCAF.dll    TKXCAF.dll    TKXSBase.dll
TKSTEP.dll    TKSTEPAttr.dll TKSTEPBase.dll TKXDESTEP.dll
```

These are installed alongside OCCT and can be redistributed under the
[LGPL-2.1-or-later licence](https://dev.opencascade.org/doc/overview/html/index.html).
