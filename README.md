# OpenMBD

**OpenMBD** is an open-source SOLIDWORKS Add-in that extracts semantic
Model-Based Definition (MBD) / PMI data from SOLIDWORKS parts and assemblies and
exports it to industry-standard interchange formats.

> **No paid SOLIDWORKS add-ons required.**  OpenMBD works with the most basic
> standard SOLIDWORKS license.  It does not use the SOLIDWORKS MBD, STEP AP242,
> or 3D PDF add-ons.

| Format | Status |
|--------|--------|
| **QIF 3.0** (ANSI/ASME QIF 3000-2018) | ✅ Implemented (semantic XML export) |
| **STEP AP242** (ISO 10303-242) | ✅ Implemented (free, no MBD add-on needed) |
| **PDF MBD Report** | ✅ Implemented (free, no 3D PDF add-on needed) |

---

## Why OpenMBD?

SOLIDWORKS can export STEP AP242 and 3D PDF natively, **but both features
require paid add-ons** (SOLIDWORKS MBD for AP242, and the 3D PDF add-on for 3D
PDF) that are not part of the base SOLIDWORKS license.

OpenMBD implements both exports without those add-ons:

* **STEP AP242** – geometry is exported via the standard SOLIDWORKS STEP
  exporter (AP203/AP214, available in every SOLIDWORKS license).  The exported
  file is then post-processed: the `FILE_SCHEMA` header is upgraded to
  `AP242_MANAGED_MODEL_BASED_3D_ENGINEERING` and PMI annotations (extracted via
  `IPMIData`) are appended as ISO 10303-242 GD&T entities.

* **PDF MBD Report** – a formatted PDF document is generated using only
  built-in .NET Framework libraries.  It contains a complete PMI annotation
  table (type, characteristic, value, unit, tolerances, datum references) and
  opens in any standard PDF viewer.  No 3D viewer is required.

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
│   ├── Step242Exporter.cs      ← STEP AP242 exporter (free, no MBD add-on)
│   ├── PdfReportExporter.cs    ← PDF MBD report exporter (free, no 3D PDF add-on)
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
* A licensed installation of SOLIDWORKS (2021 or later recommended) — **standard
  license; no MBD, AP242, or 3D PDF add-ons required**
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
   - **Export STEP 242** – exports the model as a STEP AP242 file using
     `Step242Exporter`.  Geometry is captured via the standard SOLIDWORKS STEP
     exporter and PMI annotations are appended as AP242 GD&T entities.
     No SOLIDWORKS MBD add-on required.
   - **Export PDF** – generates a formatted MBD/PMI report PDF using
     `PdfReportExporter`.  The report contains a complete annotation table
     produced with built-in .NET libraries only.
     No SOLIDWORKS 3D PDF add-on required.

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

### STEP AP242 Export (`Step242Exporter`)

`Step242Exporter.cs` uses a two-step, no-add-on approach:

1. **Geometry export** – calls `model.Extension.SaveAs3(path, ..., null, null, ...)`
   with a `null` `ExportStepData` argument.  This invokes SOLIDWORKS' default
   STEP exporter (AP203/AP214), which is available in every SOLIDWORKS license.

2. **Post-processing** – reads the exported file and:
   * Replaces the `FILE_SCHEMA` declaration with
     `AP242_MANAGED_MODEL_BASED_3D_ENGINEERING{}{}`.
   * Locates the `PRODUCT_DEFINITION_SHAPE` entity and creates a
     `SHAPE_ASPECT` anchor.
   * Appends ISO 10303-47 / 10303-242 GD&T entities for every extracted PMI
     annotation (e.g. `FLATNESS_TOLERANCE`, `DATUM_FEATURE`,
     `DIMENSIONAL_SIZE`, `PLUS_MINUS_TOLERANCE`).
   * Injects all new entities immediately before the final `ENDSEC;`.

### PDF MBD Report (`PdfReportExporter`)

`PdfReportExporter.cs` generates a PDF 1.4 document using only built-in .NET
Framework libraries (`System.IO`, `System.Text`).  The embedded `MiniPdfDocument`
class writes a valid PDF byte stream with a correct cross-reference table.

The report includes:
* A title block with model name, generation date, and annotation counts.
* A paginated PMI annotation table (ID, type, characteristic, value, unit,
  tolerances, datum references).

The fonts used are Helvetica and Helvetica-Bold – standard Type 1 PDF fonts
that require no embedding and render correctly in every PDF viewer.

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
