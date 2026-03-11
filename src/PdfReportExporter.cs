using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenMBD
{
    /// <summary>
    /// Generates a PDF MBD/PMI report without requiring the paid SOLIDWORKS 3D PDF add-on.
    /// <para>
    /// The report is produced using only built-in .NET Framework libraries — no external
    /// NuGet packages or paid SOLIDWORKS add-ons are needed.  It contains:
    /// <list type="bullet">
    ///   <item><description>A title section with model name, generation date, and annotation summary.</description></item>
    ///   <item><description>A paginated table listing every extracted PMI annotation (ID, type,
    ///     characteristic, value, unit, tolerances, and datum references).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The generated file conforms to PDF 1.4 and uses only the 14 standard Type 1 fonts
    /// (Helvetica / Helvetica-Bold), so it opens correctly in every PDF viewer without
    /// font-embedding or special rendering settings.
    /// </para>
    /// </summary>
    internal sealed class PdfReportExporter
    {
        // ------------------------------------------------------------------ //
        //  Page layout constants                                               //
        // ------------------------------------------------------------------ //

        private const float PageW    = 612f;   // 8.5" × 72 dpi
        private const float PageH    = 792f;   // 11"  × 72 dpi
        private const float MarginH  = 50f;    // horizontal margin (pts)
        private const float MarginV  = 50f;    // vertical margin (pts)
        private const float ContentW = PageW - 2 * MarginH;  // 512 pts

        // Table column widths (must sum to ContentW = 512).
        private static readonly float[] ColW = { 52f, 68f, 108f, 52f, 36f, 72f, 124f };
        // Columns: ID | Type | Characteristic | Value | Unit | Tol +/- | Datums

        private const float RowH     = 16f;
        private const float HeaderH  = 20f;
        private const float TitleH   = 24f;
        private const float SubH     = 13f;
        private const float FooterH  = 30f;

        // ------------------------------------------------------------------ //
        //  Public API                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Builds a PDF MBD report from the supplied annotation list.
        /// </summary>
        /// <param name="mbdItems">PMI annotations extracted from the active model.</param>
        /// <param name="outputPath">Destination path for the generated PDF file.</param>
        /// <param name="modelFileName">Display name (or full path) of the source model.</param>
        public void Export(List<MBDDataModel> mbdItems, string outputPath, string modelFileName)
        {
            if (mbdItems == null) mbdItems = new List<MBDDataModel>();

            var pdf = new MiniPdfDocument();

            // Compute annotation type counts for the summary.
            int nGtol  = mbdItems.Count(i => i.AnnotationType == "Gtol");
            int nDatum = mbdItems.Count(i => i.AnnotationType == "DatumTag");
            int nDim   = mbdItems.Count(i => i.AnnotationType == "Dimension");

            string modelDisplay = string.IsNullOrWhiteSpace(modelFileName)
                ? "(untitled)"
                : Path.GetFileName(modelFileName);

            // ---- Build pages -----------------------------------------------

            // We lay out content manually, starting a new page whenever the
            // remaining vertical space is insufficient for the next row.

            float y = PageH - MarginV;  // current vertical cursor (PDF origin = bottom-left)

            pdf.BeginPage();
            y = DrawTitleBlock(pdf, y, modelDisplay, nGtol, nDatum, nDim,
                mbdItems.Count);

            y = DrawTableHeader(pdf, y);

            int pageNum  = 1;
            int rowIndex = 0;
            foreach (var item in mbdItems)
            {
                if (y - RowH < MarginV + FooterH)
                {
                    DrawPageFooter(pdf, pageNum);
                    pdf.EndPage();
                    pdf.BeginPage();
                    pageNum++;
                    y = PageH - MarginV;
                    y = DrawTableHeader(pdf, y);
                }
                y = DrawTableRow(pdf, y, item, rowIndex % 2 != 0);
                rowIndex++;
            }

            DrawPageFooter(pdf, pageNum);
            pdf.EndPage();

            pdf.Save(outputPath);
        }

        // ------------------------------------------------------------------ //
        //  Layout helpers                                                      //
        // ------------------------------------------------------------------ //

        private static float DrawTitleBlock(
            MiniPdfDocument pdf, float y,
            string modelDisplay, int nGtol, int nDatum, int nDim, int total)
        {
            // Report title
            pdf.FillRect(MarginH, y - TitleH, ContentW, TitleH, 0.18f, 0.36f, 0.58f);
            pdf.DrawText(
                $"{modelDisplay} – MBD / PMI Report",
                MarginH + 6f, y - TitleH + 6f, 13f, bold: true,
                r: 1f, g: 1f, b: 1f);
            y -= TitleH + 4f;

            // Metadata row
            string dateLine = $"Generated by OpenMBD  |  {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
            pdf.DrawText(dateLine, MarginH, y - SubH + 2f, 8.5f, bold: false);
            y -= SubH + 2f;

            // Summary
            string summary =
                $"Total annotations: {total}  " +
                $"(Geometric tolerances: {nGtol},  " +
                $"Datum features: {nDatum},  " +
                $"Dimensions: {nDim})";
            pdf.DrawText(summary, MarginH, y - SubH + 2f, 8.5f, bold: false);
            y -= SubH + 8f;

            // Horizontal rule
            pdf.DrawLine(MarginH, y, MarginH + ContentW, y, 0.5f);
            y -= 6f;

            return y;
        }

        private static float DrawTableHeader(MiniPdfDocument pdf, float y)
        {
            pdf.FillRect(MarginH, y - HeaderH, ContentW, HeaderH, 0.22f, 0.42f, 0.64f);

            string[] headers = { "ID", "Type", "Characteristic", "Value", "Unit", "Tol ±", "Datums" };
            float x = MarginH + 3f;
            for (int c = 0; c < headers.Length; c++)
            {
                pdf.DrawText(headers[c], x, y - HeaderH + 5f, 8.5f,
                    bold: true, r: 1f, g: 1f, b: 1f);
                x += ColW[c];
            }

            y -= HeaderH;

            // Bottom border of header
            pdf.DrawLine(MarginH, y, MarginH + ContentW, y, 0.4f);
            return y;
        }

        private static float DrawTableRow(
            MiniPdfDocument pdf, float y, MBDDataModel item, bool shade)
        {
            if (shade)
                pdf.FillRect(MarginH, y - RowH, ContentW, RowH, 0.94f, 0.96f, 0.98f);

            string tolStr = FormatTolerance(item.TolerancePlus, item.ToleranceMinus, item.Unit);
            string datums = FormatDatums(item.DatumReferences);

            string[] cells =
            {
                Trunc(item.Id              ?? "", 10),
                Trunc(TypeLabel(item.AnnotationType), 11),
                Trunc(item.CharacteristicName ?? "", 18),
                FormatValue(item.Value, item.Unit),
                Trunc(item.Unit            ?? "", 6),
                tolStr,
                Trunc(datums               ,    22)
            };

            float x   = MarginH + 3f;
            float textY = y - RowH + 4f;
            for (int c = 0; c < cells.Length; c++)
            {
                pdf.DrawText(cells[c], x, textY, 7.5f, bold: false);
                x += ColW[c];
            }

            // Bottom border of row
            pdf.DrawLine(MarginH, y - RowH, MarginH + ContentW, y - RowH, 0.2f,
                r: 0.80f, g: 0.80f, b: 0.80f);

            return y - RowH;
        }

        private static void DrawPageFooter(MiniPdfDocument pdf, int pageNum)
        {
            float footerY = MarginV - 12f;
            pdf.DrawLine(MarginH, footerY + 10f, MarginH + ContentW, footerY + 10f, 0.4f);
            pdf.DrawText(
                $"OpenMBD MBD/PMI Report  |  Page {pageNum}",
                MarginH, footerY, 7.5f, bold: false,
                r: 0.40f, g: 0.40f, b: 0.40f);
        }

        // ------------------------------------------------------------------ //
        //  Formatting helpers                                                  //
        // ------------------------------------------------------------------ //

        private static string TypeLabel(string t)
        {
            switch (t)
            {
                case "Gtol":     return "Geom. Tol.";
                case "DatumTag": return "Datum";
                case "Dimension":return "Dimension";
                default:         return t ?? "";
            }
        }

        private static string FormatValue(double v, string unit)
        {
            if (v == 0) return "";
            // Keep 4 significant figures.
            return v.ToString("G4", CultureInfo.InvariantCulture);
        }

        private static string FormatTolerance(double plus, double minus, string unit)
        {
            if (plus == 0 && minus == 0) return "";
            string p = plus.ToString("G4", CultureInfo.InvariantCulture);
            // minus is stored as a negative number; negate it for the display sign.
            string m = (-minus).ToString("G4", CultureInfo.InvariantCulture);
            return $"+{p} / -{m}";
        }

        private static string FormatDatums(List<DatumReference> refs)
        {
            if (refs == null || refs.Count == 0) return "";
            var parts = new List<string>();
            string[] precedence = { "1°", "2°", "3°" };
            for (int i = 0; i < refs.Count; i++)
            {
                var dr = refs[i];
                string mc = string.IsNullOrWhiteSpace(dr.MaterialCondition)
                    ? "" : $"({dr.MaterialCondition})";
                string prefix = i < precedence.Length ? precedence[i] + " " : "";
                parts.Add($"{prefix}{dr.Label}{mc}");
            }
            return string.Join(", ", parts);
        }

        private static string Trunc(string s, int maxChars)
        {
            if (s == null) return "";
            return s.Length <= maxChars ? s : s.Substring(0, maxChars - 1) + "…";
        }

        // ================================================================== //
        //  Minimal PDF 1.4 document builder (no external dependencies)        //
        // ================================================================== //

        /// <summary>
        /// A self-contained PDF 1.4 document builder that uses only built-in .NET
        /// libraries.  It supports text (Helvetica / Helvetica-Bold), filled
        /// rectangles, and thin lines.  The font resources use WinAnsiEncoding so
        /// that Western Latin characters render correctly without font embedding.
        /// </summary>
        private sealed class MiniPdfDocument
        {
            // PDF objects (1-based; index 0 is unused).
            private readonly List<byte[]> _objs = new List<byte[]> { new byte[0] };

            // Fixed object IDs assigned in the constructor.
            private readonly int _catalogId;
            private readonly int _pagesId;
            private readonly int _fontNId;   // /F1 Helvetica
            private readonly int _fontBId;   // /F2 Helvetica-Bold

            // Page and content stream object IDs collected during build.
            private readonly List<int> _pageIds = new List<int>();

            // Current page content buffer.
            private StringBuilder _pageContent;

            public MiniPdfDocument()
            {
                _catalogId = Alloc(null);  // placeholder; filled in Save()
                _pagesId   = Alloc(null);  // placeholder; filled in Save()
                _fontNId   = Alloc(Enc(
                    "<< /Type /Font /Subtype /Type1 " +
                    "/BaseFont /Helvetica " +
                    "/Encoding /WinAnsiEncoding >>"));
                _fontBId   = Alloc(Enc(
                    "<< /Type /Font /Subtype /Type1 " +
                    "/BaseFont /Helvetica-Bold " +
                    "/Encoding /WinAnsiEncoding >>"));
            }

            // ------------------------------------------------------------ //
            //  Public drawing API                                           //
            // ------------------------------------------------------------ //

            /// <summary>Starts a new page and resets the drawing cursor.</summary>
            public void BeginPage()
            {
                _pageContent = new StringBuilder();
                // Set default line width, cap, and join.
                _pageContent.AppendLine("1 J 1 j");
            }

            /// <summary>Finishes the current page and registers its objects.</summary>
            public void EndPage()
            {
                byte[] content = Enc(_pageContent.ToString());

                int contentId = Alloc(Concat(
                    Enc($"<< /Length {content.Length} >>\nstream\n"),
                    content,
                    Enc("\nendstream")));

                int pageId = Alloc(Enc(
                    $"<< /Type /Page /Parent {_pagesId} 0 R " +
                    $"/MediaBox [0 0 {I(PageW)} {I(PageH)}] " +
                    $"/Contents {contentId} 0 R " +
                    $"/Resources << /Font << /F1 {_fontNId} 0 R " +
                    $"/F2 {_fontBId} 0 R >> >> >>"));

                _pageIds.Add(pageId);
                _pageContent = null;
            }

            /// <summary>
            /// Draws a filled, axis-aligned rectangle.
            /// All coordinates are in PDF user space (origin = bottom-left corner).
            /// </summary>
            public void FillRect(float x, float y, float w, float h,
                float r, float g, float b)
            {
                _pageContent.AppendLine(
                    $"{F(r)} {F(g)} {F(b)} rg " +
                    $"{F(x)} {F(y)} {F(w)} {F(h)} re f");
                // Reset fill color to black after drawing.
                _pageContent.AppendLine("0 0 0 rg");
            }

            /// <summary>
            /// Draws a horizontal or diagonal line.
            /// </summary>
            public void DrawLine(float x1, float y1, float x2, float y2,
                float lineWidth = 0.5f, float r = 0f, float g = 0f, float b = 0f)
            {
                _pageContent.AppendLine(
                    $"{F(lineWidth)} w {F(r)} {F(g)} {F(b)} RG " +
                    $"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S " +
                    "0 0 0 RG");
            }

            /// <summary>
            /// Draws a text string at the given position.
            /// </summary>
            public void DrawText(string text, float x, float y, float size,
                bool bold = false, float r = 0f, float g = 0f, float b = 0f)
            {
                if (string.IsNullOrEmpty(text)) return;
                string font = bold ? "F2" : "F1";
                string safe = EscapePdf(text);
                _pageContent.AppendLine(
                    $"{F(r)} {F(g)} {F(b)} rg " +
                    $"BT /{font} {F(size)} Tf {F(x)} {F(y)} Td ({safe}) Tj ET " +
                    "0 0 0 rg");
            }

            /// <summary>
            /// Serialises the complete PDF to <paramref name="path"/>.
            /// </summary>
            public void Save(string path)
            {
                // Finalise the /Pages object (needs the complete Kids array).
                string kids  = string.Join(" ", _pageIds.Select(id => $"{id} 0 R"));
                Set(_pagesId,  Enc(
                    $"<< /Type /Pages " +
                    $"/Kids [{kids}] /Count {_pageIds.Count} >>"));
                Set(_catalogId, Enc(
                    $"<< /Type /Catalog /Pages {_pagesId} 0 R >>"));

                // Write to a MemoryStream so we can record exact byte offsets.
                using (var ms = new MemoryStream())
                {
                    Write(ms, "%PDF-1.4\n");

                    var offsets = new long[_objs.Count];

                    for (int i = 1; i < _objs.Count; i++)
                    {
                        offsets[i] = ms.Position;
                        Write(ms, $"{i} 0 obj\n");
                        ms.Write(_objs[i], 0, _objs[i].Length);
                        Write(ms, "\nendobj\n");
                    }

                    long xrefPos = ms.Position;
                    Write(ms, "xref\n");
                    Write(ms, $"0 {_objs.Count}\n");
                    // Free-list entry: exactly 20 bytes (10 + 1 + 5 + 1 + 1 + \r\n).
                    Write(ms, "0000000000 65535 f\r\n");

                    for (int i = 1; i < _objs.Count; i++)
                    {
                        // Each xref entry is exactly 20 bytes:
                        // 10 (offset) + 1 (sp) + 5 (generation) + 1 (sp) + 1 ('n') + \r\n (2) = 20.
                        Write(ms, $"{offsets[i]:D10} 00000 n\r\n");
                    }

                    Write(ms, "trailer\n");
                    Write(ms, $"<< /Size {_objs.Count} /Root {_catalogId} 0 R >>\n");
                    Write(ms, "startxref\n");
                    Write(ms, $"{xrefPos}\n");
                    Write(ms, "%%EOF\n");

                    File.WriteAllBytes(path, ms.ToArray());
                }
            }

            // ------------------------------------------------------------ //
            //  Internal object management                                   //
            // ------------------------------------------------------------ //

            /// <summary>Allocates a new object slot (1-based) and returns its ID.</summary>
            private int Alloc(byte[] body)
            {
                _objs.Add(body ?? new byte[0]);
                return _objs.Count - 1;
            }

            /// <summary>Replaces an already-allocated object's body.</summary>
            private void Set(int id, byte[] body) => _objs[id] = body ?? new byte[0];

            // ------------------------------------------------------------ //
            //  Static helpers                                               //
            // ------------------------------------------------------------ //

            private static void Write(Stream s, string ascii)
            {
                byte[] b = Encoding.ASCII.GetBytes(ascii);
                s.Write(b, 0, b.Length);
            }

            /// <summary>Concatenates any number of byte arrays into one.</summary>
            private static byte[] Concat(params byte[][] arrays)
            {
                int total = 0;
                foreach (var a in arrays) total += a.Length;
                var result = new byte[total];
                int pos = 0;
                foreach (var a in arrays)
                {
                    Buffer.BlockCopy(a, 0, result, pos, a.Length);
                    pos += a.Length;
                }
                return result;
            }

            /// <summary>Encodes an ASCII string to bytes.</summary>
            private static byte[] Enc(string s) => Encoding.ASCII.GetBytes(s ?? "");

            /// <summary>Formats a float for PDF stream output.</summary>
            private static string F(float v) =>
                v.ToString("F2", CultureInfo.InvariantCulture);

            /// <summary>Formats a float as an integer (for MediaBox).</summary>
            private static string I(float v) =>
                ((int)v).ToString(CultureInfo.InvariantCulture);

            /// <summary>
            /// Escapes a string for use inside PDF parenthesis-delimited strings.
            /// Non-ASCII characters are replaced with '?' to stay within ISO Latin-1.
            /// </summary>
            private static string EscapePdf(string s)
            {
                if (s == null) return "";
                var sb = new StringBuilder(s.Length);
                foreach (char c in s)
                {
                    if (c == '\\')      { sb.Append("\\\\"); }
                    else if (c == '(')  { sb.Append("\\("); }
                    else if (c == ')')  { sb.Append("\\)"); }
                    else if (c == '\r' || c == '\n') { /* skip newlines */ }
                    else if (c > 126)   { sb.Append('?'); } // keep within Latin-1
                    else                { sb.Append(c); }
                }
                return sb.ToString();
            }
        }
    }
}
