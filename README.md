# OpenMBD

**OpenMBD** is an open-source SOLIDWORKS Add-in that extracts semantic
Model-Based Definition (MBD) / PMI data from SOLIDWORKS parts and assemblies and
exports it to industry-standard interchange formats.

> **No paid SOLIDWORKS add-ons required.**  OpenMBD works with the most basic
> standard SOLIDWORKS license.  It does not use the SOLIDWORKS MBD or STEP AP242
> add-ons.

| Format | Status |
|--------|--------|
| **QIF 3.0** (ANSI/ASME QIF 3000-2018) | ✅ Implemented (semantic XML export) |
| **STEP AP242** (ISO 10303-242) | ✅ Implemented via Open CASCADE Technology (OCCT) |

---

## Why OpenMBD?

SOLIDWORKS can export STEP AP242 natively, **but this feature requires the paid
SOLIDWORKS MBD add-on** that is not included in the base SOLIDWORKS license.

OpenMBD implements the export without that add-on using
[Open CASCADE Technology (OCCT)](https://dev.opencascade.org/):

* **STEP AP242** – geometry is exported via the standard SOLIDWORKS STEP exporter
  (AP203/AP214, available in every SOLIDWORKS license).  The exported geometry
  is then read by OCCT's `STEPCAFControl_Reader` into an XCAF document.  PMI
  annotations (extracted via `IPMIData`) are attached to the document using
  `XCAFDoc_DimTolTool`, and the final AP242 Edition 2 file is written by
  `STEPCAFControl_Writer` with `write.step.schema` set to `AP242DED`.  All PMI
  entities are encoded as proper ISO 10303-242 / ISO 10303-47 GD&T constructs —
  not as raw text strings or post-processed entity appends.

Additionally, there is no freely available tool that bridges the SOLIDWORKS PMI
data model to the **QIF format**.  QIF is the semantic standard used by CMMs,
metrology software, and digital quality systems.  OpenMBD uses the `IPMIData`
interface to read structured, machine-readable PMI data and serialises it into
a QIF 3.0 XML document that downstream quality systems can consume.

---

## Project Structure

```
OpenMBD/
├── src/                        ← C# SOLIDWORKS Add-in source code
│   ├── OpenMBD.csproj          ← .NET 4.8 project file
│   ├── SwAddin.cs              ← ISwAddin entry point + CommandManager UI
│   ├── PmiExtractionService.cs ← IPMIData extraction (Gtol, DatumTag, Dimension)
│   ├── MBDDataModel.cs         ← Semantic data schema (value, tolerance, datums)
│   ├── QifExporter.cs          ← QIF 3.0 XML serialisation stub
│   ├── Step242Exporter.cs      ← STEP AP242 exporter (OCCT-based, no MBD add-on)
│   ├── OcctBridge.cs           ← P/Invoke declarations for OpenMBD.OcctBridge.dll
│   └── BitmapHandler.cs        ← Toolbar icon helper
├── native/                     ← C++ source for the OCCT bridge DLL
│   ├── OcctBridge.h            ← C-compatible API header
│   ├── OcctBridge.cpp          ← OCCT implementation
│   ├── CMakeLists.txt          ← CMake build file
│   └── README.md               ← Build instructions for the native DLL
├── lib/                        ← Third-party / generated code (see lib/README.md)
│   └── README.md               ← Instructions for generating QIF C# classes from XSD
├── schemas/                    ← XML schema definitions (see schemas/README.md)
│   └── README.md               ← Where to obtain QIF 3.0 and STEP AP242 XSD files
├── LICENSE
└── README.md
```

---

## Building

### Prerequisites

| Dependency | Minimum version | Notes |
|-----------|----------------|-------|
| Visual Studio | 2019 | or the .NET 4.8 SDK for C# only |
| SOLIDWORKS | 2021 | standard license; no MBD add-on |
| SOLIDWORKS interop DLLs | — | `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\` |
| CMake | 3.16 | for the native OCCT bridge |
| C++ compiler | MSVC 2019+ | for the native OCCT bridge |
| Open CASCADE Technology | 7.7.0 | [dev.opencascade.org](https://dev.opencascade.org/release) |

### 1. Build the native OCCT bridge

```powershell
cd native
cmake -B build -DCMAKE_BUILD_TYPE=Release `
      -DOpenCASCADE_DIR="C:\OpenCASCADE-7.7.0\cmake"
cmake --build build --config Release
```

See [`native/README.md`](native/README.md) for detailed installation and build
instructions.

### 2. Build the C# add-in

```powershell
# Set the path to your SOLIDWORKS interop DLLs (or edit OpenMBD.csproj directly)
$env:SW_INTEROP_DIR = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"

dotnet build src/OpenMBD.csproj -c Release
```

### 3. Deploy and register

```powershell
# Copy the OCCT bridge DLL next to the add-in
copy native\build\Release\OpenMBD.OcctBridge.dll src\bin\Release\net48\

# Register the add-in (run as Administrator)
regasm /codebase src\bin\Release\net48\OpenMBD.dll
```

After registration, restart SOLIDWORKS and enable the add-in under
**Tools → Add-Ins → OpenMBD**.

---

## Usage

1. Open a Part (`.sldprt`) or Assembly (`.sldasm`) that contains PMI annotations.
2. The **OpenMBD** tab appears in the CommandManager ribbon.
3. Click one of the two export buttons:
   - **Export QIF** – extracts all PMI annotations via `IPMIData` and writes a
     QIF 3.0 XML file.
   - **Export STEP 242** – exports the model as a STEP AP242 Edition 2 file using
     `Step242Exporter` backed by OCCT.  Geometry is captured via the standard
     SOLIDWORKS STEP exporter and PMI annotations are written as proper
     ISO 10303-242 GD&T entities.  No SOLIDWORKS MBD add-on required.

---

## Technical Notes

### The `IPMIData` Interface

The key SOLIDWORKS API entry point is:

```csharp
object[] pmiDataArray = swModel.Extension.GetPMIData();
```

This returns an array of `IPMIData` objects.  Each object exposes a
`GetAnnotation()` method that returns the underlying annotation.  By checking
`annotation.GetType()` against `swAnnotationType_e`, OpenMBD identifies:

| `swAnnotationType_e` value | Class | Description |
|---|---|---|
| `swGTOL` | `Gtol` | Geometric tolerance frames (position, flatness, etc.) |
| `swDATUMTAG` | `DatumTag` | Datum feature symbols |
| `swDISPLAYDIMENSION` | `DisplayDimension` | Driven / driving dimensions |

Without `IPMIData`, the only alternative is to iterate drawing annotations and
parse text strings – producing semantically meaningless output that inspection
software cannot act on.

### STEP AP242 Export (`Step242Exporter` + OCCT)

`Step242Exporter.cs` uses a two-step, no-add-on approach backed by
[Open CASCADE Technology (OCCT)](https://dev.opencascade.org/):

1. **Geometry export** – calls `model.Extension.SaveAs3(path, ..., null, null, ...)`
   with a `null` `ExportStepData` argument.  This invokes SOLIDWORKS' default
   STEP exporter (AP203/AP214), which is available in every SOLIDWORKS license.

2. **OCCT authoring** – the native bridge `OpenMBD.OcctBridge.dll` (built from
   `native/OcctBridge.cpp`):
   * Reads the temporary STEP geometry file into an OCCT XCAF document via
     `STEPCAFControl_Reader`.
   * Attaches PMI annotations to the document using `XCAFDoc_DimTolTool` and
     the `XCAFDimTolObjects_*` family of objects:
     * `XCAFDimTolObjects_GeomToleranceObject` for geometric tolerances.
     * `XCAFDimTolObjects_DatumObject` for datum feature symbols.
     * `XCAFDimTolObjects_DimensionObject` for linear dimensions.
   * Writes the final STEP AP242 Edition 2 file via `STEPCAFControl_Writer`
     with `Interface_Static::SetCVal("write.step.schema", "AP242DED")`.

The result is a fully conformant STEP AP242 file with PMI encoded as native
ISO 10303-242 / ISO 10303-47 GD&T entities — not as raw text appended outside
the schema.

### QIF Integration

`QifExporter.cs` is a functional stub that:

1. Creates a well-formed `QIFDocument` root element with the QIF 3.0 namespace.
2. Writes a `Header` section (timestamp, application name, source file).
3. Writes a `Product` section referencing the SOLIDWORKS model file.
4. Maps each `MBDDataModel` to the appropriate QIF characteristic type:
   - `GeometricCharacteristicDefinition` for Gtol annotations
   - `DatumDefinition` for datum tags
   - `LinearCharacteristicDefinition` for dimensions

A production implementation should replace the hand-written XML with strongly-typed
C# classes generated from the official QIF XSD files (see `/lib/README.md`).

---

## Contributing

Pull requests are welcome.  Please open an issue first to discuss any significant
changes.  See `/schemas/README.md` for the official schema sources and
`/lib/README.md` for instructions on generating the QIF C# proxy classes.

---

## License

[MIT](LICENSE)
