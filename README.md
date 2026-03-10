# OpenMBD

**OpenMBD** is an open-source SOLIDWORKS Add-in that extracts semantic
Model-Based Definition (MBD) / PMI data from SOLIDWORKS parts and assemblies and
exports it to industry-standard interchange formats:

| Format | Status |
|--------|--------|
| **QIF 3.0** (ANSI/ASME QIF 3000-2018) | ✅ Implemented (semantic XML export) |
| **STEP AP242** (ISO 10303-242) | ✅ Implemented (wraps SOLIDWORKS native export) |
| **3D PDF** | ✅ Implemented (wraps SOLIDWORKS native export) |

---

## Why OpenMBD?

SOLIDWORKS already exports STEP 242 and 3D PDF natively.  However, there is a
critical gap in the open-source community: **no freely available tool bridges the
SOLIDWORKS PMI data model to the QIF format**.

QIF (Quality Information Framework) is the semantic standard used by CMMs,
metrology software, and digital quality systems to understand *what* a tolerance
means, not just what text is printed on a drawing.  Without QIF, PMI annotations
are exported as "dumb" text strings that inspection software cannot interpret
automatically.

OpenMBD uses the `IPMIData` interface (accessed via
`swModel.Extension.GetPMIData()`) to read structured, machine-readable PMI data
directly from SOLIDWORKS, then serialises it into a QIF 3.0 XML document that
downstream quality systems can consume without manual re-entry.

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
│   └── BitmapHandler.cs        ← Toolbar icon helper
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

* Visual Studio 2019 or later (or the .NET 4.8 SDK)
* A licensed installation of SOLIDWORKS (2021 or later recommended)
* SOLIDWORKS interop DLLs – located at:
  ```
  C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\
  ```

### Build steps

```powershell
# 1. Clone the repository
git clone https://github.com/ianbohannon/OpenMBD.git
cd OpenMBD

# 2. Set the path to your SOLIDWORKS interop DLLs (or edit OpenMBD.csproj directly)
$env:SW_INTEROP_DIR = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"

# 3. Build
dotnet build src/OpenMBD.csproj -c Release

# 4. Register the add-in (run as Administrator)
regasm /codebase src\bin\Release\net48\OpenMBD.dll
```

After registration, restart SOLIDWORKS and enable the add-in under
**Tools → Add-Ins → OpenMBD**.

---

## Usage

1. Open a Part (`.sldprt`) or Assembly (`.sldasm`) that contains PMI annotations.
2. The **OpenMBD** tab appears in the CommandManager ribbon.
3. Click one of the three export buttons:
   - **Export QIF** – extracts all PMI annotations via `IPMIData` and writes a
     QIF 3.0 XML file.
   - **Export STEP 242** – saves the model as a STEP AP242 file (with PMI) using
     SOLIDWORKS' built-in export engine.
   - **Export 3D PDF** – saves the model as a 3D PDF using SOLIDWORKS' built-in
     export engine.

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
