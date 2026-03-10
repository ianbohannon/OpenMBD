# /lib – Third-party Libraries and Generated QIF Classes

This directory is intended to hold third-party libraries and auto-generated C# code
that are **not** part of the core SOLIDWORKS add-in source but are required for a
production QIF export implementation.

---

## 1. Generating C# Classes from the QIF 3.0 XSD Schema

The QIF standard is defined by a set of XML Schema (`.xsd`) files maintained by the
[NIST MBE Standards Repository](https://qifstandards.org/).  Because QIF is pure XML,
the recommended approach for .NET is to generate strongly-typed C# proxy classes
directly from those schemas using the `xsd.exe` tool included with Visual Studio.

### Steps

```powershell
# 1. Download the QIF 3.0 schema bundle from https://qifstandards.org/schemas/
#    and unzip it to a local folder, e.g. C:\QIF\schemas\

# 2. Open a Visual Studio Developer Command Prompt and run:
xsd.exe /classes /language:CS /namespace:OpenMBD.QIF /out:lib\QIF `
    C:\QIF\schemas\QIFApplications\QIFDocument.xsd `
    C:\QIF\schemas\QIFLibrary\Characteristics\Characteristics.xsd `
    C:\QIF\schemas\QIFLibrary\Features\Features.xsd

# 3. Add the generated .cs files to the OpenMBD project:
#    Right-click the OpenMBD project in Visual Studio → Add → Existing Item…
#    Select the generated files in lib\QIF\.
```

The generated classes expose every element and attribute from the QIF schema as
strongly-typed properties.  Replace the hand-written XML in `QifExporter.cs` with
calls to these classes and serialize the root `QIFDocumentType` object using
`System.Xml.Serialization.XmlSerializer`.

---

## 2. NIST QIF Reference Implementation

NIST provides a reference C++ library for QIF at:

```
https://github.com/QualityInformationFramework/QIF-Community
```

There is no official C# wrapper, but the generated classes from step 1 above are
the standard .NET approach and are equivalent in capability.

---

## 3. Directory Layout (after generation)

```
lib/
└── QIF/
    ├── QIFDocumentType.cs          ← generated from QIFDocument.xsd
    ├── CharacteristicsType.cs      ← generated from Characteristics.xsd
    ├── FeaturesType.cs             ← generated from Features.xsd
    └── ...                         ← additional generated files
```

Place the generated `.cs` files in `lib/QIF/` and reference them from
`src/OpenMBD.csproj` by adding:

```xml
<Compile Include="..\lib\QIF\*.cs" />
```

---

## 4. Notes

* The generated files can be large (100 k+ lines).  They are intentionally excluded
  from this repository because they are derived works of the QIF schema, which is
  published by ASME under its own license.  Always obtain the latest schema from the
  official source.
* SOLIDWORKS interop DLLs (`SolidWorks.Interop.*.dll`) must also be placed in a
  `lib/SolidWorksInterop/` subdirectory if you prefer not to rely on a local
  SOLIDWORKS installation.  These DLLs are **not** redistributable under the
  SOLIDWORKS EULA and must be obtained from a licensed installation.
