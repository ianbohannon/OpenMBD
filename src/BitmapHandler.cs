using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace OpenMBD
{
    /// <summary>
    /// Manages temporary bitmap files required by the SOLIDWORKS CommandManager API.
    /// SOLIDWORKS reads toolbar icon images from physical files on disk; this helper
    /// extracts embedded resources to a temp folder and cleans them up on dispose.
    /// </summary>
    internal sealed class BitmapHandler : IDisposable
    {
        private string _tempDir;
        private bool   _disposed;

        public BitmapHandler()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"OpenMBD_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        /// <summary>
        /// Writes an embedded PNG/BMP resource to a temp file and returns its path.
        /// Falls back to a generated placeholder image if the resource is not found.
        /// </summary>
        /// <param name="resourceName">Fully-qualified resource name.</param>
        /// <param name="assembly">Assembly that contains the resource.</param>
        /// <returns>Path to the written file.</returns>
        public string CreateFileFromResourceBitmap(string resourceName, Assembly assembly)
        {
            string outputPath = Path.Combine(_tempDir, Path.GetFileName(resourceName));

            using (Stream stream = assembly?.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var fs = File.Create(outputPath))
                        stream.CopyTo(fs);
                }
                else
                {
                    // Resource not found – write a 40×20 placeholder (2 icons × 20px).
                    using (var bmp = new Bitmap(40, 20))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        // Draw simple coloured blocks for each of the 2 commands.
                        g.FillRectangle(Brushes.DodgerBlue,  0,  0, 18, 18);  // QIF
                        g.FillRectangle(Brushes.LimeGreen,  20,  0, 18, 18);  // STEP
                        bmp.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }

            return outputPath;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* best effort */ }
        }
    }
}
