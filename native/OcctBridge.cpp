/**
 * OcctBridge.cpp
 *
 * Implementation of the C-compatible OCCT bridge API declared in OcctBridge.h.
 *
 * This file uses the Open CASCADE Technology (OCCT) 7.7+ XDE (Extended Data
 * Exchange) API to:
 *   1. Read a SOLIDWORKS-exported STEP AP203/AP214 geometry file.
 *   2. Attach semantic PMI annotations (geometric tolerances, datum features,
 *      and linear dimensions) to the XCAF document.
 *   3. Write the result as a STEP AP242 Edition 2 file with all PMI entities
 *      encoded as proper ISO 10303-242 / ISO 10303-47 GD&T entities.
 *
 * Key OCCT toolkits required:
 *   TKernel, TKMath, TKBRep, TKTopAlgo, TKG3d, TKGeomBase, TKGeomAlgo,
 *   TKCAF, TKLCAF, TKXCAF, TKXSBase, TKSTEP, TKSTEPAttr, TKSTEPBase,
 *   TKXDESTEP
 *
 * Build with CMakeLists.txt in this directory.
 */

#define OCCTBRIDGE_EXPORTS
#include "OcctBridge.h"

/* ---- OCCT application framework ---------------------------------- */
#include <Standard_Handle.hxx>
#include <TDocStd_Document.hxx>
#include <XCAFApp_Application.hxx>

/* ---- OCCT XDE shape / GD&T tools --------------------------------- */
#include <XCAFDoc_DocumentTool.hxx>
#include <XCAFDoc_ShapeTool.hxx>
#include <XCAFDoc_DimTolTool.hxx>
#include <XCAFDimTolObjects_GeomToleranceObject.hxx>
#include <XCAFDimTolObjects_GeomToleranceType.hxx>
#include <XCAFDimTolObjects_GeomToleranceTypeValue.hxx>
#include <XCAFDimTolObjects_DatumObject.hxx>
#include <XCAFDimTolObjects_DimensionObject.hxx>
#include <XCAFDimTolObjects_DimensionType.hxx>
#include <XCAFDimTolObjects_DatumModifiersSequence.hxx>
#include <XCAFDimTolObjects_DatumSingleModif.hxx>

/* ---- OCCT topology ----------------------------------------------- */
#include <BRep_Builder.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Shape.hxx>
#include <TDF_Label.hxx>
#include <TDF_LabelSequence.hxx>

/* ---- OCCT STEP I/O ----------------------------------------------- */
#include <STEPControl_Reader.hxx>
#include <STEPCAFControl_Reader.hxx>
#include <STEPCAFControl_Writer.hxx>
#include <Interface_Static.hxx>
#include <IFSelect_ReturnStatus.hxx>

/* ---- Standard library -------------------------------------------- */
#include <TCollection_AsciiString.hxx>
#include <TCollection_HAsciiString.hxx>
#include <Quantity_AbsorbedDose.hxx>

#include <cstring>
#include <string>
#include <vector>
#include <sstream>
#include <stdexcept>

// ---------------------------------------------------------------------------
//  Internal context structure
// ---------------------------------------------------------------------------

struct OcctContextImpl
{
    Handle(TDocStd_Document)   doc;
    Handle(XCAFDoc_ShapeTool)  shapeTool;
    Handle(XCAFDoc_DimTolTool) dimTolTool;
    std::string                lastError;

    OcctContextImpl()
    {
        // Initialise the XDE application and create a new document.
        Handle(XCAFApp_Application) app = XCAFApp_Application::GetApplication();
        app->NewDocument("MDTV-XCAF", doc);

        shapeTool  = XCAFDoc_DocumentTool::ShapeTool (doc->Main());
        dimTolTool = XCAFDoc_DocumentTool::DimTolTool(doc->Main());
    }
};

// ---------------------------------------------------------------------------
//  Internal helpers
// ---------------------------------------------------------------------------

namespace
{

/// Safely cast the opaque handle to our context struct.
static OcctContextImpl* ToCtx(OcctHandle h)
{
    return static_cast<OcctContextImpl*>(h);
}

// ---- Tolerance type mapping -----------------------------------------------

/// Maps the tolerance-type string accepted by OcctAddGeomTolerance to the
/// corresponding XCAFDimTolObjects_GeomToleranceType enum value.
static XCAFDimTolObjects_GeomToleranceType ParseToleranceType(const std::string& s)
{
    if (s == "STRAIGHTNESS")          return XCAFDimTolObjects_GeomToleranceType_Straightness;
    if (s == "FLATNESS")              return XCAFDimTolObjects_GeomToleranceType_Flatness;
    if (s == "CIRCULARITY"  ||
        s == "ROUNDNESS")             return XCAFDimTolObjects_GeomToleranceType_Circularity;
    if (s == "CYLINDRICITY")          return XCAFDimTolObjects_GeomToleranceType_Cylindricity;
    if (s == "LINE_PROFILE"    ||
        s == "PROFILE_OF_A_LINE")     return XCAFDimTolObjects_GeomToleranceType_Line_Profile;
    if (s == "SURFACE_PROFILE" ||
        s == "PROFILE_OF_A_SURFACE")  return XCAFDimTolObjects_GeomToleranceType_Surface_Profile;
    if (s == "ANGULARITY")            return XCAFDimTolObjects_GeomToleranceType_Angularity;
    if (s == "PERPENDICULARITY" ||
        s == "SQUARENESS")            return XCAFDimTolObjects_GeomToleranceType_Perpendicularity;
    if (s == "PARALLELISM")           return XCAFDimTolObjects_GeomToleranceType_Parallelism;
    if (s == "POSITION")              return XCAFDimTolObjects_GeomToleranceType_Position;
    if (s == "CONCENTRICITY")         return XCAFDimTolObjects_GeomToleranceType_Concentricity;
    if (s == "SYMMETRY")              return XCAFDimTolObjects_GeomToleranceType_Symmetry;
    if (s == "CIRCULAR_RUNOUT" ||
        s == "RUNOUT")                return XCAFDimTolObjects_GeomToleranceType_CircularRunout;
    if (s == "TOTAL_RUNOUT")          return XCAFDimTolObjects_GeomToleranceType_TotalRunout;

    return XCAFDimTolObjects_GeomToleranceType_None;
}

// ---- Datum-reference string parsing ---------------------------------------

struct DatumRefEntry
{
    std::string label;
    std::string materialCondition; // "RFS", "MMC", "LMC", or ""
};

/// Parses the pipe-separated datum reference string used by
/// OcctAddGeomTolerance (e.g. "A:RFS|B:MMC|C:").
static std::vector<DatumRefEntry> ParseDatumRefs(const char* datumRefs)
{
    std::vector<DatumRefEntry> result;
    if (!datumRefs || *datumRefs == '\0')
        return result;

    std::string src(datumRefs);
    std::istringstream stream(src);
    std::string token;

    while (std::getline(stream, token, '|'))
    {
        if (token.empty()) continue;
        DatumRefEntry entry;
        auto colon = token.find(':');
        if (colon == std::string::npos)
        {
            entry.label = token;
        }
        else
        {
            entry.label             = token.substr(0, colon);
            entry.materialCondition = token.substr(colon + 1);
        }
        if (!entry.label.empty())
            result.push_back(entry);
    }
    return result;
}

/// Maps a material-condition string to the corresponding OCCT modifier.
static XCAFDimTolObjects_DatumSingleModif ParseMaterialCondition(
    const std::string& mc)
{
    if (mc == "MMC" || mc == "M") return XCAFDimTolObjects_DatumSingleModif_M;
    if (mc == "LMC" || mc == "L") return XCAFDimTolObjects_DatumSingleModif_L;
    // RFS or empty → no modifier (use the default)
    return XCAFDimTolObjects_DatumSingleModif_None;
}

} // anonymous namespace

// ---------------------------------------------------------------------------
//  Context lifecycle
// ---------------------------------------------------------------------------

extern "C"
{

OCCT_API OcctHandle OcctCreateContext(void)
{
    try
    {
        return new OcctContextImpl();
    }
    catch (...)
    {
        return nullptr;
    }
}

OCCT_API void OcctDestroyContext(OcctHandle ctx)
{
    delete ToCtx(ctx);
}

// ---------------------------------------------------------------------------
//  Geometry input
// ---------------------------------------------------------------------------

OCCT_API int OcctReadStep(OcctHandle ctx, const char* filePath)
{
    auto* c = ToCtx(ctx);
    if (!c || !filePath) return 1;

    try
    {
        STEPCAFControl_Reader reader;
        reader.SetColorMode(Standard_True);
        reader.SetNameMode (Standard_True);

        IFSelect_ReturnStatus status = reader.ReadFile(filePath);
        if (status != IFSelect_RetDone)
        {
            c->lastError = "STEPCAFControl_Reader::ReadFile failed for: ";
            c->lastError += filePath;
            return 2;
        }

        if (!reader.Transfer(c->doc))
        {
            c->lastError = "STEPCAFControl_Reader::Transfer failed.";
            return 3;
        }

        return 0;
    }
    catch (const Standard_Failure& e)
    {
        c->lastError = e.GetMessageString();
        return 4;
    }
}

// ---------------------------------------------------------------------------
//  PMI annotation writers
// ---------------------------------------------------------------------------

OCCT_API int OcctAddGeomTolerance(OcctHandle  ctx,
                                   const char* toleranceType,
                                   double      value,
                                   const char* unit,
                                   const char* description,
                                   const char* datumRefs)
{
    auto* c = ToCtx(ctx);
    if (!c || !toleranceType) return 1;

    try
    {
        // Build the geometric tolerance object.
        Handle(XCAFDimTolObjects_GeomToleranceObject) gtObj =
            new XCAFDimTolObjects_GeomToleranceObject();

        gtObj->SetType(ParseToleranceType(toleranceType));

        // Tolerance zone value.
        gtObj->SetValue(value);
        gtObj->SetValueType(XCAFDimTolObjects_GeomToleranceTypeValue_Diameter);

        // Optional textual description mapped to the object name.
        if (description && *description != '\0')
            gtObj->SetDescription(
                new TCollection_HAsciiString(description));

        // Add to the DimTolTool and obtain the label.
        TDF_Label gtLabel = c->dimTolTool->AddDimTol();
        c->dimTolTool->SetDimTol(gtLabel, gtObj);

        // Attach datum references.
        auto refs = ParseDatumRefs(datumRefs);
        for (int i = 0; i < (int)refs.size(); ++i)
        {
            Handle(XCAFDimTolObjects_DatumObject) datumObj =
                new XCAFDimTolObjects_DatumObject();

            datumObj->SetName(
                new TCollection_HAsciiString(refs[i].label.c_str()));

            XCAFDimTolObjects_DatumModifiersSequence mods;
            XCAFDimTolObjects_DatumSingleModif mc =
                ParseMaterialCondition(refs[i].materialCondition);
            if (mc != XCAFDimTolObjects_DatumSingleModif_None)
                mods.Append(mc);
            datumObj->SetModifiers(mods);
            datumObj->SetDatumTargetNumber(i + 1); // 1-based precedence

            TDF_Label datumLabel = c->dimTolTool->AddDatum();
            c->dimTolTool->SetDatum(datumLabel, datumObj);
            c->dimTolTool->SetDatumToGeomTol(datumLabel, gtLabel);
        }

        return 0;
    }
    catch (const Standard_Failure& e)
    {
        c->lastError = e.GetMessageString();
        return 2;
    }
}

OCCT_API int OcctAddDatumTag(OcctHandle ctx, const char* label)
{
    auto* c = ToCtx(ctx);
    if (!c || !label) return 1;

    try
    {
        Handle(XCAFDimTolObjects_DatumObject) datumObj =
            new XCAFDimTolObjects_DatumObject();
        datumObj->SetName(new TCollection_HAsciiString(label));

        TDF_Label datumLabel = c->dimTolTool->AddDatum();
        c->dimTolTool->SetDatum(datumLabel, datumObj);

        return 0;
    }
    catch (const Standard_Failure& e)
    {
        c->lastError = e.GetMessageString();
        return 2;
    }
}

OCCT_API int OcctAddDimension(OcctHandle  ctx,
                               const char* name,
                               double      nominalValue,
                               double      tolerancePlus,
                               double      toleranceMinus,
                               const char* unit)
{
    auto* c = ToCtx(ctx);
    if (!c) return 1;

    try
    {
        Handle(XCAFDimTolObjects_DimensionObject) dimObj =
            new XCAFDimTolObjects_DimensionObject();

        dimObj->SetType(XCAFDimTolObjects_DimensionType_Location_LinearDistance);
        dimObj->SetValue(nominalValue);

        if (name && *name != '\0')
            dimObj->SetDescription(new TCollection_HAsciiString(name));

        // Bilateral plus/minus tolerance.
        if (tolerancePlus != 0.0 || toleranceMinus != 0.0)
        {
            Handle(TCollection_HAsciiString) qualifier;
            dimObj->SetUpperBound(nominalValue + tolerancePlus);
            dimObj->SetLowerBound(nominalValue - toleranceMinus);
        }

        TDF_Label dimLabel = c->dimTolTool->AddDimTol();
        c->dimTolTool->SetDimTol(dimLabel, dimObj);

        return 0;
    }
    catch (const Standard_Failure& e)
    {
        c->lastError = e.GetMessageString();
        return 2;
    }
}

// ---------------------------------------------------------------------------
//  STEP AP242 output
// ---------------------------------------------------------------------------

OCCT_API int OcctWriteStep242(OcctHandle ctx, const char* filePath)
{
    auto* c = ToCtx(ctx);
    if (!c || !filePath) return 1;

    try
    {
        // Select STEP AP242 Edition 2 schema.
        Interface_Static::SetCVal(
            "write.step.schema",
            "AP242DED");

        STEPCAFControl_Writer writer;

        // Enable PMI transfer.
        writer.SetDimTolMode(Standard_True);
        writer.SetColorMode  (Standard_True);
        writer.SetNameMode   (Standard_True);

        if (!writer.Transfer(c->doc, STEPControl_AsIs))
        {
            c->lastError = "STEPCAFControl_Writer::Transfer failed.";
            return 2;
        }

        IFSelect_ReturnStatus status = writer.Write(filePath);
        if (status != IFSelect_RetDone)
        {
            c->lastError = "STEPCAFControl_Writer::Write failed for: ";
            c->lastError += filePath;
            return 3;
        }

        return 0;
    }
    catch (const Standard_Failure& e)
    {
        c->lastError = e.GetMessageString();
        return 4;
    }
}

// ---------------------------------------------------------------------------
//  Diagnostics
// ---------------------------------------------------------------------------

OCCT_API const char* OcctGetLastError(OcctHandle ctx)
{
    auto* c = ToCtx(ctx);
    if (!c) return "";
    return c->lastError.c_str();
}

} // extern "C"
