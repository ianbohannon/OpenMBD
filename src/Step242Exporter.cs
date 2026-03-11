using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace OpenMBD
{
    /// <summary>
    /// Exports a SOLIDWORKS model to a STEP AP242 file with embedded PMI
    /// annotations using Open CASCADE Technology (OCCT).
    /// <para>
    /// The export uses a two-step pipeline:
    /// <list type="number">
    ///   <item><description>
    ///     <b>Geometry export</b> – the model is saved to a temporary STEP
    ///     AP203/AP214 file via the standard SOLIDWORKS STEP exporter
    ///     (available in every license; no MBD add-on required).
    ///   </description></item>
    ///   <item><description>
    ///     <b>OCCT authoring</b> – <c>OpenMBD.OcctBridge.dll</c> reads the
    ///     temporary geometry file into an XCAF document, attaches the PMI
    ///     annotations (geometric tolerances, datum features, linear
    ///     dimensions) as proper ISO 10303-242 / ISO 10303-47 GD&amp;T
    ///     entities, and writes the final STEP AP242 Edition 2 file.
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The native bridge DLL (<c>OpenMBD.OcctBridge.dll</c>) must be present
    /// in the same directory as <c>OpenMBD.dll</c> at runtime.  Build
    /// instructions are in <c>/native/README.md</c>.
    /// </para>
    /// </summary>
    internal sealed class Step242Exporter
    {
        // ------------------------------------------------------------------ //
        //  Public API                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Exports the given SOLIDWORKS model to a STEP AP242 file with PMI.
        /// </summary>
        /// <param name="model">The active SOLIDWORKS model document.</param>
        /// <param name="mbdItems">PMI annotations to embed as AP242 GD&amp;T entities.</param>
        /// <param name="outputPath">Destination path for the generated .step / .stp file.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="model"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///   When the geometry export or the OCCT write step fails.
        /// </exception>
        public void Export(ModelDoc2 model, List<MBDDataModel> mbdItems, string outputPath)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            string tempStep = Path.Combine(
                Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".stp");
            try
            {
                ExportGeometry(model, tempStep);
                WriteWithOcct(tempStep, mbdItems ?? new List<MBDDataModel>(), outputPath);
            }
            finally
            {
                if (File.Exists(tempStep))
                    File.Delete(tempStep);
            }
        }

        // ------------------------------------------------------------------ //
        //  Step 1 – export geometry via the standard SOLIDWORKS STEP exporter //
        // ------------------------------------------------------------------ //

        private static void ExportGeometry(ModelDoc2 model, string tempPath)
        {
            int errors = 0, warnings = 0;

            // Passing null for the ExportStepData argument invokes SOLIDWORKS'
            // default STEP exporter (AP203/AP214), which is available in every
            // SOLIDWORKS license — no MBD add-on is required.
            bool ok = model.Extension.SaveAs3(
                tempPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null, null,
                ref errors, ref warnings);

            if (!ok || !File.Exists(tempPath))
                throw new InvalidOperationException(
                    $"STEP geometry export failed (errors={errors}, warnings={warnings}).");
        }

        // ------------------------------------------------------------------ //
        //  Step 2 – load into OCCT, attach PMI, write AP242                   //
        // ------------------------------------------------------------------ //

        private static void WriteWithOcct(
            string tempStepPath, List<MBDDataModel> items, string outputPath)
        {
            IntPtr ctx = OcctBridge.OcctCreateContext();
            if (ctx == IntPtr.Zero)
                throw new InvalidOperationException(
                    "OCCT context allocation failed.  Ensure OpenMBD.OcctBridge.dll " +
                    "is present in the add-in directory.");

            try
            {
                // 2a. Read the SOLIDWORKS-exported geometry into the XCAF document.
                int rc = OcctBridge.OcctReadStep(ctx, tempStepPath);
                if (rc != 0)
                    throw new InvalidOperationException(
                        "OCCT failed to read the temporary STEP geometry file: " +
                        OcctBridge.GetLastError(ctx));

                // 2b. Attach PMI annotations to the XCAF document.
                foreach (var item in items)
                {
                    if (item == null) continue;
                    AddPmiAnnotation(ctx, item);
                }

                // 2c. Write the final STEP AP242 file.
                rc = OcctBridge.OcctWriteStep242(ctx, outputPath);
                if (rc != 0)
                    throw new InvalidOperationException(
                        "OCCT failed to write the STEP AP242 file: " +
                        OcctBridge.GetLastError(ctx));
            }
            finally
            {
                OcctBridge.OcctDestroyContext(ctx);
            }
        }

        // ------------------------------------------------------------------ //
        //  PMI annotation dispatch                                             //
        // ------------------------------------------------------------------ //

        private static void AddPmiAnnotation(IntPtr ctx, MBDDataModel item)
        {
            switch (item.AnnotationType)
            {
                case "Gtol":
                    AddGtol(ctx, item);
                    break;
                case "DatumTag":
                    AddDatumTag(ctx, item);
                    break;
                case "Dimension":
                    AddDimension(ctx, item);
                    break;
                // Unknown annotation types are silently skipped.
            }
        }

        private static void AddGtol(IntPtr ctx, MBDDataModel item)
        {
            string toleranceType = MapToOcctToleranceType(item.CharacteristicName);
            string datumRefs     = BuildDatumRefString(item.DatumReferences);

            int rc = OcctBridge.OcctAddGeomTolerance(
                ctx,
                toleranceType,
                item.Value,
                item.Unit ?? "mm",
                item.RawCalloutText ?? string.Empty,
                datumRefs);

            if (rc != 0)
                throw new InvalidOperationException(
                    $"OCCT OcctAddGeomTolerance failed (rc={rc}): " +
                    OcctBridge.GetLastError(ctx));
        }

        private static void AddDatumTag(IntPtr ctx, MBDDataModel item)
        {
            string label = item.CharacteristicName ?? item.RawCalloutText ?? string.Empty;

            int rc = OcctBridge.OcctAddDatumTag(ctx, label);
            if (rc != 0)
                throw new InvalidOperationException(
                    $"OCCT OcctAddDatumTag failed (rc={rc}): " +
                    OcctBridge.GetLastError(ctx));
        }

        private static void AddDimension(IntPtr ctx, MBDDataModel item)
        {
            string name = item.CharacteristicName ?? "DIMENSION";

            int rc = OcctBridge.OcctAddDimension(
                ctx,
                name,
                item.Value,
                item.TolerancePlus,
                item.ToleranceMinus,
                item.Unit ?? "mm");

            if (rc != 0)
                throw new InvalidOperationException(
                    $"OCCT OcctAddDimension failed (rc={rc}): " +
                    OcctBridge.GetLastError(ctx));
        }

        // ------------------------------------------------------------------ //
        //  Tolerance type mapping                                              //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Maps a SOLIDWORKS characteristic name string to the tolerance type
        /// key expected by <c>OcctAddGeomTolerance</c> (and parsed by
        /// <c>OcctBridge.cpp → ParseToleranceType()</c>).
        /// </summary>
        private static string MapToOcctToleranceType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            switch (name.Trim().ToUpperInvariant())
            {
                case "STRAIGHTNESS":         return "STRAIGHTNESS";
                case "FLATNESS":             return "FLATNESS";
                case "CIRCULARITY":
                case "ROUNDNESS":            return "CIRCULARITY";
                case "CYLINDRICITY":         return "CYLINDRICITY";
                case "LINE_PROFILE":
                case "PROFILE_OF_A_LINE":    return "LINE_PROFILE";
                case "SURFACE_PROFILE":
                case "PROFILE_OF_A_SURFACE": return "SURFACE_PROFILE";
                case "ANGULARITY":           return "ANGULARITY";
                case "PERPENDICULARITY":
                case "SQUARENESS":           return "PERPENDICULARITY";
                case "PARALLELISM":          return "PARALLELISM";
                case "POSITION":             return "POSITION";
                case "CONCENTRICITY":        return "CONCENTRICITY";
                case "SYMMETRY":             return "SYMMETRY";
                case "CIRCULAR_RUNOUT":
                case "RUNOUT":               return "CIRCULAR_RUNOUT";
                case "TOTAL_RUNOUT":         return "TOTAL_RUNOUT";
                default:                     return name.Trim().ToUpperInvariant();
            }
        }

        // ------------------------------------------------------------------ //
        //  Datum-reference encoding                                            //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Encodes a list of datum references as the pipe-separated string
        /// expected by <c>OcctAddGeomTolerance</c>:
        /// <c>"LABEL:MATERIAL_CONDITION|LABEL:MATERIAL_CONDITION|..."</c>.
        /// <para>
        /// The <c>|</c> and <c>:</c> characters are the delimiters used by the
        /// C bridge parser (<c>ParseDatumRefs</c> in <c>OcctBridge.cpp</c>).
        /// Any <c>|</c> or <c>:</c> that appear literally inside a label or
        /// material-condition string are replaced with <c>_</c> to keep the
        /// encoding unambiguous.  In practice, SOLIDWORKS datum labels are
        /// single uppercase letters (A–Z) and material-condition strings are
        /// short keywords ("RFS", "MMC", "LMC"), so this substitution has no
        /// effect on real-world data.
        /// </para>
        /// </summary>
        private static string BuildDatumRefString(List<DatumReference> refs)
        {
            if (refs == null || refs.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < refs.Count; i++)
            {
                if (i > 0) sb.Append('|');
                string label = refs[i].Label ?? string.Empty;
                string mc    = refs[i].MaterialCondition ?? string.Empty;
                sb.Append(label.Replace('|', '_').Replace(':', '_'));
                sb.Append(':');
                sb.Append(mc.Replace('|', '_').Replace(':', '_'));
            }
            return sb.ToString();
        }
    }
}
