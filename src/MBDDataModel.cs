using System.Collections.Generic;

namespace OpenMBD
{
    /// <summary>
    /// Represents a single datum reference (e.g., datum A, B, C) extracted from a
    /// geometric tolerance frame.
    /// </summary>
    public class DatumReference
    {
        /// <summary>Label of the datum (e.g., "A", "B", "C").</summary>
        public string Label { get; set; }

        /// <summary>Material condition modifier (e.g., "M" for maximum, "L" for least).</summary>
        public string MaterialCondition { get; set; }
    }

    /// <summary>
    /// Stores semantic MBD data extracted from a SOLIDWORKS model annotation before it
    /// is passed to a file writer (QIF, STEP 242, or 3D PDF).
    /// </summary>
    public class MBDDataModel
    {
        /// <summary>Unique identifier for this annotation within the model.</summary>
        public string Id { get; set; }

        /// <summary>
        /// Type of annotation: "Gtol", "DatumTag", or "Dimension".
        /// </summary>
        public string AnnotationType { get; set; }

        /// <summary>
        /// Human-readable name of the geometric characteristic (e.g.,
        /// "STRAIGHTNESS", "FLATNESS", "POSITION") for Gtol annotations,
        /// or the datum label for DatumTag annotations.
        /// </summary>
        public string CharacteristicName { get; set; }

        /// <summary>Nominal / measured value of the tolerance or dimension.</summary>
        public double Value { get; set; }

        /// <summary>Upper tolerance bound (positive deviation).</summary>
        public double TolerancePlus { get; set; }

        /// <summary>Lower tolerance bound (negative deviation, stored as a negative number).</summary>
        public double ToleranceMinus { get; set; }

        /// <summary>Unit string (e.g., "mm", "in").</summary>
        public string Unit { get; set; }

        /// <summary>
        /// Ordered list of datum references associated with a geometric tolerance frame.
        /// </summary>
        public List<DatumReference> DatumReferences { get; set; } = new List<DatumReference>();

        /// <summary>
        /// Free-form text callout as it appears in the annotation balloon.
        /// Useful for debugging / round-trip verification.
        /// </summary>
        public string RawCalloutText { get; set; }
    }
}
