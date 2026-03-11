/**
 * OcctBridge.h
 *
 * C-compatible API for the OpenMBD.OcctBridge native DLL, which wraps
 * Open CASCADE Technology (OCCT) for proper STEP AP242 + PMI authoring.
 *
 * The bridge is intentionally kept C-compatible so that it can be consumed
 * directly via P/Invoke from the C# SOLIDWORKS add-in without requiring
 * C++/CLI or COM registration.
 *
 * Build the bridge DLL with CMake (see CMakeLists.txt) and copy it to the
 * same directory as OpenMBD.dll before registering the add-in.
 */

#pragma once

#ifdef _WIN32
  #ifdef OCCTBRIDGE_EXPORTS
    #define OCCT_API __declspec(dllexport)
  #else
    #define OCCT_API __declspec(dllimport)
  #endif
#else
  #define OCCT_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Opaque handle to an OCCT authoring context.
 * Create with OcctCreateContext(); destroy with OcctDestroyContext().
 */
typedef void* OcctHandle;

/* ------------------------------------------------------------------ *
 *  Context lifecycle                                                   *
 * ------------------------------------------------------------------ */

/**
 * Creates a new OCCT XDE (Extended Data Exchange) authoring context.
 * Returns NULL if allocation fails.
 */
OCCT_API OcctHandle OcctCreateContext(void);

/**
 * Releases all resources held by the context.
 * After this call the handle is invalid and must not be used.
 */
OCCT_API void OcctDestroyContext(OcctHandle ctx);

/* ------------------------------------------------------------------ *
 *  Geometry input                                                      *
 * ------------------------------------------------------------------ */

/**
 * Reads geometry from an existing STEP file (AP203 or AP214) into the
 * context.  The shapes are loaded into the XCAF document's ShapeTool so
 * that PMI annotations can be attached to them.
 *
 * @param ctx      Context handle.
 * @param filePath NUL-terminated ASCII path of the input STEP file.
 * @return         0 on success; non-zero on failure (call OcctGetLastError).
 */
OCCT_API int OcctReadStep(OcctHandle ctx, const char* filePath);

/* ------------------------------------------------------------------ *
 *  PMI annotation writers                                              *
 * ------------------------------------------------------------------ */

/**
 * Adds a geometric tolerance (GD&T) annotation to the context.
 *
 * @param ctx           Context handle.
 * @param toleranceType OCCT tolerance type string, e.g. "FLATNESS",
 *                      "POSITION", "PERPENDICULARITY", etc.
 *                      See OcctBridge.cpp for the full list of accepted values.
 * @param value         Tolerance zone magnitude in model units.
 * @param unit          Unit string: "mm" or "in".
 * @param description   Human-readable description / callout text; may be NULL.
 * @param datumRefs     Pipe-separated datum reference entries in the form
 *                      "LABEL:MATERIAL_CONDITION", ordered by precedence
 *                      (primary|secondary|tertiary).  Pass NULL or "" when there
 *                      are no datum references.  Example: "A:RFS|B:MMC|C:".
 * @return              0 on success; non-zero on failure.
 */
OCCT_API int OcctAddGeomTolerance(OcctHandle  ctx,
                                  const char* toleranceType,
                                  double      value,
                                  const char* unit,
                                  const char* description,
                                  const char* datumRefs);

/**
 * Adds a datum feature symbol annotation to the context.
 *
 * @param ctx   Context handle.
 * @param label Datum label (single uppercase letter or compound string).
 * @return      0 on success; non-zero on failure.
 */
OCCT_API int OcctAddDatumTag(OcctHandle ctx, const char* label);

/**
 * Adds a linear dimension annotation to the context.
 *
 * @param ctx            Context handle.
 * @param name           Characteristic name / identifier.
 * @param nominalValue   Nominal dimension value in model units.
 * @param tolerancePlus  Upper deviation (0.0 when bilateral or no tolerance).
 * @param toleranceMinus Lower deviation (0.0 when bilateral or no tolerance).
 * @param unit           Unit string: "mm" or "in".
 * @return               0 on success; non-zero on failure.
 */
OCCT_API int OcctAddDimension(OcctHandle  ctx,
                               const char* name,
                               double      nominalValue,
                               double      tolerancePlus,
                               double      toleranceMinus,
                               const char* unit);

/* ------------------------------------------------------------------ *
 *  Output                                                              *
 * ------------------------------------------------------------------ */

/**
 * Writes the XCAF document as a STEP AP242 Edition 2 file.
 * The FILE_SCHEMA is set to AP242_MANAGED_MODEL_BASED_3D_ENGINEERING{}{}.
 * All PMI annotations added via the OcctAdd* functions are written as
 * proper ISO 10303-242 / ISO 10303-47 GD&T entities.
 *
 * @param ctx      Context handle.
 * @param filePath NUL-terminated ASCII path of the output STEP file.
 * @return         0 on success; non-zero on failure.
 */
OCCT_API int OcctWriteStep242(OcctHandle ctx, const char* filePath);

/* ------------------------------------------------------------------ *
 *  Diagnostics                                                         *
 * ------------------------------------------------------------------ */

/**
 * Returns a NUL-terminated ASCII string describing the last error that
 * occurred in the given context.  The returned pointer is valid until
 * the next call on the same context or until OcctDestroyContext().
 * Returns an empty string when no error has occurred.
 */
OCCT_API const char* OcctGetLastError(OcctHandle ctx);

#ifdef __cplusplus
} /* extern "C" */
#endif
