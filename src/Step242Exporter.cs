using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace OpenMBD
{
    /// <summary>
    /// Generates STEP AP242 files without requiring the paid SOLIDWORKS MBD add-on.
    /// <para>
    /// The approach has two steps:
    /// <list type="number">
    ///   <item><description>
    ///     Geometry is exported via the standard SOLIDWORKS STEP exporter (AP203 / AP214),
    ///     which is available in every SOLIDWORKS license at no extra cost.
    ///   </description></item>
    ///   <item><description>
    ///     The exported file is post-processed: the <c>FILE_SCHEMA</c> header is upgraded
    ///     to <c>AP242_MANAGED_MODEL_BASED_3D_ENGINEERING</c> and PMI annotations
    ///     (extracted by <see cref="PmiExtractionService"/>) are appended as ISO 10303-242
    ///     GD&amp;T entities before the final <c>ENDSEC;</c>.
    ///   </description></item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class Step242Exporter
    {
        /// <summary>FILE_SCHEMA identifier for STEP AP242 Edition 2.
        /// The <c>{}{}</c> suffix is part of the ISO 10303-1 EXPRESS schema naming
        /// convention; the two sets of empty braces denote the (empty) configuration
        /// parameter list and the (empty) version parameter list for the schema.</summary>
        private const string Ap242Schema =
            "AP242_MANAGED_MODEL_BASED_3D_ENGINEERING{}{}";

        /// <summary>
        /// Minimum entity ID used as the starting point when scanning the STEP file
        /// for the highest existing entity number.  Set high enough to ensure that
        /// injected PMI entity IDs do not collide with any entity in a typical
        /// SOLIDWORKS-exported AP203/AP214 file.
        /// </summary>
        private const int MinStartEntityId = 1000;

        // ------------------------------------------------------------------ //
        //  Public API                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Exports the given SOLIDWORKS model to a STEP AP242 file.
        /// </summary>
        /// <param name="model">The active SOLIDWORKS model document.</param>
        /// <param name="mbdItems">PMI annotations to embed as AP242 GD&amp;T entities.</param>
        /// <param name="outputPath">Destination path for the generated .step / .stp file.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="model"/> is null.</exception>
        /// <exception cref="InvalidOperationException">When the geometry export fails.</exception>
        public void Export(ModelDoc2 model, List<MBDDataModel> mbdItems, string outputPath)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            string tempStep = Path.Combine(
                Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".stp");
            try
            {
                ExportGeometry(model, tempStep);
                PostProcess(tempStep, mbdItems ?? new List<MBDDataModel>(), outputPath);
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

            // Passing null for the ExportStepData argument tells SOLIDWORKS to use
            // its default STEP exporter (AP203 / AP214).  This path is available in
            // every SOLIDWORKS license — no MBD add-on is required.
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
        //  Step 2 – upgrade schema to AP242 and inject PMI entities           //
        // ------------------------------------------------------------------ //

        private static void PostProcess(
            string sourcePath, List<MBDDataModel> items, string outputPath)
        {
            string text = File.ReadAllText(sourcePath, Encoding.ASCII);

            // 2a. Replace the FILE_SCHEMA declaration with the AP242 identifier.
            text = Regex.Replace(
                text,
                @"FILE_SCHEMA\s*\(\s*\(.*?\)\s*\)\s*;",
                $"FILE_SCHEMA (('{Ap242Schema}'));",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // 2b. Locate the PRODUCT_DEFINITION_SHAPE entity so that our PMI
            //     annotations can reference it as their shape anchor.
            int pdsId  = FindProductDefinitionShapeId(text);
            int nextId = FindMaxId(text) + 1;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("/* ----- OpenMBD: PMI Annotations (ISO 10303-242) ----- */");

            // A SHAPE_ASPECT entity that references the product shape and acts as
            // the common anchor for every PMI entity below.
            string pdsRef      = pdsId > 0 ? $"#{pdsId}" : "$";
            int shapeAspectId  = nextId++;
            sb.AppendLine(
                $"#{shapeAspectId}=SHAPE_ASPECT(" +
                $"'GD&T annotations','MBD',{pdsRef},.F.);");

            // A NAMED_UNIT entity re-used by every MEASURE_WITH_UNIT below.
            int unitId = nextId++;
            sb.AppendLine($"#{unitId}=NAMED_UNIT(*);");

            foreach (var item in items)
            {
                if (item == null) continue;
                nextId = AppendPmiEntity(sb, item, nextId, shapeAspectId, unitId);
            }

            sb.AppendLine("/* ----- end OpenMBD PMI ----- */");

            // 2c. Insert the PMI block just before the last ENDSEC; (the one that
            //     closes the DATA section, immediately before END-ISO-10303-21;).
            int insertAt = FindDataEndsec(text);
            text = insertAt >= 0
                ? text.Insert(insertAt, sb.ToString())
                : text + sb;

            File.WriteAllText(outputPath, text, Encoding.ASCII);
        }

        // ------------------------------------------------------------------ //
        //  PMI entity builders                                                 //
        // ------------------------------------------------------------------ //

        private static int AppendPmiEntity(
            StringBuilder sb, MBDDataModel item,
            int nextId, int shapeAspectId, int unitId)
        {
            switch (item.AnnotationType)
            {
                case "Gtol":     return AppendGtol(sb, item, nextId, shapeAspectId, unitId);
                case "DatumTag": return AppendDatum(sb, item, nextId, shapeAspectId);
                case "Dimension":return AppendDimension(sb, item, nextId, shapeAspectId, unitId);
                default:         return nextId;
            }
        }

        private static int AppendGtol(
            StringBuilder sb, MBDDataModel item,
            int nextId, int shapeAspectId, int unitId)
        {
            string typeName = GetToleranceEntityName(item.CharacteristicName);
            string name     = Esc(item.CharacteristicName);
            string desc     = Esc(item.RawCalloutText);

            // Tolerance magnitude.
            int measureId = nextId++;
            sb.AppendLine(
                $"#{measureId}=MEASURE_WITH_UNIT(" +
                $"LENGTH_MEASURE({F(item.Value)}),#{unitId});");

            // Geometric tolerance entity referencing the shape aspect.
            int tolId = nextId++;
            sb.AppendLine(
                $"#{tolId}={typeName}('{name}','{desc}',#{measureId},#{shapeAspectId});");

            // Datum references (primary, secondary, tertiary).
            for (int i = 0; i < (item.DatumReferences?.Count ?? 0); i++)
            {
                var dr   = item.DatumReferences[i];
                int drId = nextId++;
                sb.AppendLine(
                    $"#{drId}=DATUM_REFERENCE({i + 1}," +
                    $"'{Esc(dr.Label)}','{Esc(dr.MaterialCondition)}');");
            }

            return nextId;
        }

        private static int AppendDatum(
            StringBuilder sb, MBDDataModel item, int nextId, int shapeAspectId)
        {
            string label = Esc(item.CharacteristicName ?? item.RawCalloutText);

            int dfId = nextId++;
            sb.AppendLine($"#{dfId}=DATUM_FEATURE(#{shapeAspectId},.T.);");

            int dlId = nextId++;
            sb.AppendLine(
                $"#{dlId}=APPLIED_DATUM_FEATURE_DEFINITION('{label}',#{dfId});");

            return nextId;
        }

        private static int AppendDimension(
            StringBuilder sb, MBDDataModel item,
            int nextId, int shapeAspectId, int unitId)
        {
            string name = Esc(item.CharacteristicName ?? "DIMENSION");

            int measureId = nextId++;
            sb.AppendLine(
                $"#{measureId}=MEASURE_WITH_UNIT(" +
                $"LENGTH_MEASURE({F(item.Value)}),#{unitId});");

            int dimId = nextId++;
            sb.AppendLine(
                $"#{dimId}=DIMENSIONAL_SIZE(#{shapeAspectId},'{name}',#{measureId});");

            // Append plus/minus tolerance if present.
            if (item.TolerancePlus != 0 || item.ToleranceMinus != 0)
            {
                int plusId = nextId++;
                sb.AppendLine(
                    $"#{plusId}=MEASURE_WITH_UNIT(" +
                    $"LENGTH_MEASURE({F(item.TolerancePlus)}),#{unitId});");

                int minusId = nextId++;
                sb.AppendLine(
                    $"#{minusId}=MEASURE_WITH_UNIT(" +
                    $"LENGTH_MEASURE({F(item.ToleranceMinus)}),#{unitId});");

                int tolId = nextId++;
                sb.AppendLine(
                    $"#{tolId}=PLUS_MINUS_TOLERANCE(#{plusId},#{minusId},#{dimId});");
            }

            return nextId;
        }

        // ------------------------------------------------------------------ //
        //  STEP file parsing helpers                                           //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Locates the first PRODUCT_DEFINITION_SHAPE entity in the STEP text and
        /// returns its numeric entity ID, or 0 when not found.
        /// </summary>
        private static int FindProductDefinitionShapeId(string text)
        {
            var m = Regex.Match(
                text,
                @"#(\d+)\s*=\s*PRODUCT_DEFINITION_SHAPE\s*\(",
                RegexOptions.IgnoreCase);

            return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
        }

        /// <summary>
        /// Returns the highest entity ID number present in the STEP text,
        /// defaulting to 1000 so that injected IDs do not collide with any
        /// existing entity.
        /// </summary>
        private static int FindMaxId(string text)
        {
            int max = MinStartEntityId;
            foreach (Match m in Regex.Matches(text, @"^#(\d+)\s*=", RegexOptions.Multiline))
            {
                if (int.TryParse(m.Groups[1].Value, out int id) && id > max)
                    max = id;
            }
            return max;
        }

        /// <summary>
        /// Returns the character position of the last <c>ENDSEC;</c> that closes
        /// the DATA section (the one directly before <c>END-ISO-10303-21;</c>).
        /// Returns -1 when the marker cannot be located.
        /// </summary>
        private static int FindDataEndsec(string text)
        {
            int endIso = text.LastIndexOf("END-ISO-10303-21;",
                StringComparison.OrdinalIgnoreCase);
            if (endIso < 0) return -1;

            int endsec = text.LastIndexOf("ENDSEC;", endIso,
                StringComparison.OrdinalIgnoreCase);
            return endsec >= 0 ? endsec : -1;
        }

        // ------------------------------------------------------------------ //
        //  Tolerance type mapping                                              //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Maps a SOLIDWORKS characteristic name string to the corresponding
        /// ISO 10303-47 geometric tolerance entity name.
        /// </summary>
        private static string GetToleranceEntityName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "GEOMETRIC_TOLERANCE";

            switch (name.Trim().ToUpperInvariant())
            {
                case "STRAIGHTNESS":          return "STRAIGHTNESS_TOLERANCE";
                case "FLATNESS":              return "FLATNESS_TOLERANCE";
                case "CIRCULARITY":
                case "ROUNDNESS":             return "CIRCULARITY_TOLERANCE";
                case "CYLINDRICITY":          return "CYLINDRICITY_TOLERANCE";
                case "LINE_PROFILE":
                case "PROFILE_OF_A_LINE":     return "LINE_PROFILE_TOLERANCE";
                case "SURFACE_PROFILE":
                case "PROFILE_OF_A_SURFACE":  return "SURFACE_PROFILE_TOLERANCE";
                case "ANGULARITY":            return "ANGULARITY_TOLERANCE";
                case "PERPENDICULARITY":
                case "SQUARENESS":            return "PERPENDICULARITY_TOLERANCE";
                case "PARALLELISM":           return "PARALLELISM_TOLERANCE";
                case "POSITION":              return "POSITION_TOLERANCE";
                case "CONCENTRICITY":         return "CONCENTRICITY_TOLERANCE";
                case "SYMMETRY":              return "SYMMETRY_TOLERANCE";
                case "CIRCULAR_RUNOUT":
                case "RUNOUT":                return "CIRCULAR_RUNOUT_TOLERANCE";
                case "TOTAL_RUNOUT":          return "TOTAL_RUNOUT_TOLERANCE";
                default:                      return "GEOMETRIC_TOLERANCE";
            }
        }

        // ------------------------------------------------------------------ //
        //  String / number helpers                                             //
        // ------------------------------------------------------------------ //

        /// <summary>Formats a double for STEP ASCII output (full precision).</summary>
        private static string F(double v) =>
            v.ToString("G17", CultureInfo.InvariantCulture);

        /// <summary>Escapes a string for use inside STEP single-quoted strings.</summary>
        private static string Esc(string s) =>
            (s ?? string.Empty).Replace("'", "''").Replace("\\", "\\\\");
    }
}
