using System;
using System.Runtime.InteropServices;

namespace OpenMBD
{
    /// <summary>
    /// P/Invoke declarations for <c>OpenMBD.OcctBridge.dll</c>, a thin native
    /// wrapper around Open CASCADE Technology (OCCT) that provides STEP AP242
    /// authoring with proper PMI / GD&amp;T support.
    /// <para>
    /// The DLL must reside in the same directory as <c>OpenMBD.dll</c> at runtime.
    /// Build instructions are in <c>/native/README.md</c>.
    /// </para>
    /// </summary>
    internal static class OcctBridge
    {
        private const string DllName = "OpenMBD.OcctBridge";

        // ------------------------------------------------------------------ //
        //  Context lifecycle                                                   //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Creates a new OCCT XDE authoring context.
        /// Returns <see cref="IntPtr.Zero"/> if allocation fails.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr OcctCreateContext();

        /// <summary>
        /// Releases all resources held by the context.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void OcctDestroyContext(IntPtr ctx);

        // ------------------------------------------------------------------ //
        //  Geometry input                                                      //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Reads a STEP AP203/AP214 geometry file into the context so that PMI
        /// annotations can be attached to it.
        /// </summary>
        /// <param name="ctx">Context handle.</param>
        /// <param name="filePath">ASCII path of the STEP geometry file.</param>
        /// <returns>0 on success; non-zero on failure.</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false,
                   ThrowOnUnmappableChar = true)]
        internal static extern int OcctReadStep(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPStr)] string filePath);

        // ------------------------------------------------------------------ //
        //  PMI annotation writers                                              //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Adds a geometric tolerance (GD&amp;T) annotation to the context.
        /// </summary>
        /// <param name="ctx">Context handle.</param>
        /// <param name="toleranceType">
        ///   Tolerance type key (e.g. <c>"FLATNESS"</c>, <c>"POSITION"</c>).
        ///   See <c>OcctBridge.cpp → ParseToleranceType()</c> for the full list.
        /// </param>
        /// <param name="value">Tolerance zone magnitude in model units.</param>
        /// <param name="unit">Unit string: <c>"mm"</c> or <c>"in"</c>.</param>
        /// <param name="description">Human-readable callout text; may be null.</param>
        /// <param name="datumRefs">
        ///   Pipe-separated datum reference entries of the form
        ///   <c>"LABEL:MATERIAL_CONDITION"</c>, ordered by precedence
        ///   (primary|secondary|tertiary).  Pass null or <c>""</c> when there are
        ///   no datum references.  Example: <c>"A:RFS|B:MMC|C:"</c>.
        /// </param>
        /// <returns>0 on success; non-zero on failure.</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false,
                   ThrowOnUnmappableChar = true)]
        internal static extern int OcctAddGeomTolerance(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPStr)] string toleranceType,
            double value,
            [MarshalAs(UnmanagedType.LPStr)] string unit,
            [MarshalAs(UnmanagedType.LPStr)] string description,
            [MarshalAs(UnmanagedType.LPStr)] string datumRefs);

        /// <summary>
        /// Adds a datum feature symbol annotation to the context.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false,
                   ThrowOnUnmappableChar = true)]
        internal static extern int OcctAddDatumTag(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPStr)] string label);

        /// <summary>
        /// Adds a linear dimension annotation to the context.
        /// </summary>
        /// <param name="ctx">Context handle.</param>
        /// <param name="name">Characteristic name / identifier.</param>
        /// <param name="nominalValue">Nominal dimension value in model units.</param>
        /// <param name="tolerancePlus">Upper deviation (0.0 when none).</param>
        /// <param name="toleranceMinus">Lower deviation (0.0 when none).</param>
        /// <param name="unit">Unit string: <c>"mm"</c> or <c>"in"</c>.</param>
        /// <returns>0 on success; non-zero on failure.</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false,
                   ThrowOnUnmappableChar = true)]
        internal static extern int OcctAddDimension(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            double nominalValue,
            double tolerancePlus,
            double toleranceMinus,
            [MarshalAs(UnmanagedType.LPStr)] string unit);

        // ------------------------------------------------------------------ //
        //  Output                                                              //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Writes the XCAF document as a STEP AP242 Edition 2 file.
        /// All PMI annotations added via the <c>OcctAdd*</c> functions are
        /// written as proper ISO 10303-242 / ISO 10303-47 GD&amp;T entities.
        /// </summary>
        /// <param name="ctx">Context handle.</param>
        /// <param name="filePath">ASCII output path.</param>
        /// <returns>0 on success; non-zero on failure.</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false,
                   ThrowOnUnmappableChar = true)]
        internal static extern int OcctWriteStep242(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPStr)] string filePath);

        // ------------------------------------------------------------------ //
        //  Diagnostics                                                         //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns a pointer to a NUL-terminated ASCII string describing the
        /// last error in <paramref name="ctx"/>.  The pointer is valid until the
        /// next call on the same context or until <see cref="OcctDestroyContext"/>.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr OcctGetLastError(IntPtr ctx);

        /// <summary>
        /// Convenience wrapper: reads the last-error pointer as a managed string.
        /// </summary>
        internal static string GetLastError(IntPtr ctx)
        {
            IntPtr ptr = OcctGetLastError(ctx);
            return ptr == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }
    }
}
