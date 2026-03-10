# /schemas – QIF and STEP AP242 XML Schema Definitions

This directory is the intended home for the official XML Schema Definition (`.xsd`)
files that define the QIF and STEP AP242 exchange formats.  The schemas themselves
are **not** bundled here for licensing / file-size reasons; the instructions below
explain where to obtain them.

---

## 1. QIF 3.0 (ANSI/ASME QIF 3000-2018)

**Purpose:** Defines the XML structure used by `QifExporter.cs` to produce
machine-readable geometric tolerance data.

**Official source:**

```
https://qifstandards.org/schemas/
```

**Directory layout after download:**

```
schemas/
└── QIF3/
    ├── QIFApplications/
    │   └── QIFDocument.xsd         ← root document schema
    ├── QIFLibrary/
    │   ├── Characteristics/
    │   │   ├── Characteristics.xsd
    │   │   ├── CharacteristicItems.xsd
    │   │   └── ...
    │   ├── Features/
    │   │   └── Features.xsd
    │   ├── Traceability/
    │   └── ...
    └── QIFStatisticsApplications/
```

**Key schemas for MBD:**

| File | Description |
|------|-------------|
| `QIFDocument.xsd` | Root document element – `QIFDocumentType` |
| `Characteristics.xsd` | Geometric and dimensional characteristic definitions |
| `CharacteristicItems.xsd` | Actual measurement results per characteristic |
| `Features.xsd` | Geometric feature definitions (surfaces, axes, etc.) |
| `Traceability.xsd` | Product definition traceability (design intent ↔ measurement) |

---

## 2. STEP AP242 (ISO 10303-242)

**Purpose:** STEP Application Protocol 242 "Managed Model-Based 3D Engineering"
covers 3D CAD, GD&T, and PMI in a single exchange file.

### 2a. STEP Physical File (`.stp`)

SOLIDWORKS exports AP242 STEP files natively (File → Save As → STEP AP242).  No
additional schema is required for this export path.

### 2b. STEP AP242 XML (Business Object Model – BOM)

For XML-based STEP AP242 exchange (used in PLM and enterprise integrations) the
schema is maintained by PDES Inc. / prostep ivip:

```
https://www.prostep.org/en/projects/openpdm/step-ap242-xml/
```

Place the downloaded `.xsd` files here:

```
schemas/
└── STEP_AP242/
    ├── ap242_business_object_model.xsd
    └── ...
```

---

## 3. Referencing Schemas from Generated Code

Once the XSD files are in this directory, use the Visual Studio `xsd.exe` tool to
regenerate the C# proxy classes (see `/lib/README.md` for the exact commands).

The XML namespace constants in `QifExporter.cs` must match the `targetNamespace`
declared in `QIFDocument.xsd`:

```csharp
// src/QifExporter.cs
private const string QifNamespace = "http://qifstandards.org/xsd/qif3";
```

Verify this value against the downloaded schema before deploying.
