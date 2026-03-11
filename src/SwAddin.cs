using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorks.Interop.swconst;

namespace OpenMBD
{
    /// <summary>
    /// SOLIDWORKS Add-in entry point for the OpenMBD tool.
    /// <para>
    /// This class implements <see cref="ISwAddin"/> and registers itself as a COM
    /// in-process server so that SOLIDWORKS can load it at startup.  Three toolbar
    /// buttons are added to a custom CommandManager group:
    /// <list type="bullet">
    ///   <item><description>Export QIF – exports semantic MBD data to a QIF 3.0 XML file.</description></item>
    ///   <item><description>
    ///     Export STEP 242 – exports to STEP AP242 using <see cref="Step242Exporter"/>.
    ///     Geometry is exported via the standard SOLIDWORKS STEP exporter (AP203/AP214, available
    ///     in every license) and PMI annotations are appended as ISO 10303-242 GD&amp;T entities.
    ///     No paid SOLIDWORKS MBD add-on is required.
    ///   </description></item>
    ///   <item><description>
    ///     Export PDF – generates a formatted MBD/PMI report PDF using <see cref="PdfReportExporter"/>.
    ///     The report contains a full annotation table and is created with built-in .NET libraries only.
    ///     No paid SOLIDWORKS 3D PDF add-on is required.
    ///   </description></item>
    /// </list>
    /// </para>
    /// </summary>
    [Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class SwAddin : ISwAddin
    {
        // ------------------------------------------------------------------ //
        //  Constants                                                           //
        // ------------------------------------------------------------------ //

        private const string AddInName        = "OpenMBD";
        private const string AddInDescription = "Model-Based Definition (MBD) data exporter for QIF, STEP 242, and 3D PDF";
        private const int    CmdGroupId       = 201;   // must be unique across all add-ins
        private const int    CmdIdExportQif   = 0;
        private const int    CmdIdExportStep  = 1;
        private const int    CmdIdExport3DPdf = 2;

        // ------------------------------------------------------------------ //
        //  Private state                                                       //
        // ------------------------------------------------------------------ //

        private ISldWorks           _swApp;
        private ICommandManager     _cmdMgr;
        private int[]               _cmdIds;
        private BitmapHandler       _bmpHandler;
        private PmiExtractionService _pmiService;

        // ------------------------------------------------------------------ //
        //  COM registration helpers                                            //
        // ------------------------------------------------------------------ //

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.Registry.LocalMachine
                    .CreateSubKey(GetRegKeyPath(t));
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(
                    "OpenMBD COM registration failed. Run the installer as Administrator.", ex);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.Registry.LocalMachine
                    .DeleteSubKey(GetRegKeyPath(t), throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(
                    "OpenMBD COM un-registration failed. Run the uninstaller as Administrator.", ex);
            }
        }

        private static string GetRegKeyPath(Type t) =>
            $@"SOFTWARE\SolidWorks\AddIns\{{{t.GUID}}}";

        // ------------------------------------------------------------------ //
        //  ISwAddin implementation                                             //
        // ------------------------------------------------------------------ //

        /// <inheritdoc/>
        public bool ConnectToSW(object ThisSW, int cookie)
        {
            _swApp = (ISldWorks)ThisSW;
            _pmiService = new PmiExtractionService(_swApp);

            // Register the add-in with SOLIDWORKS so it appears in Tools > Add-Ins.
            _swApp.SetAddinCallbackInfo2(0, this, cookie);

            _cmdMgr = _swApp.GetCommandManager(cookie);
            AddCommandManager();

            return true;
        }

        /// <inheritdoc/>
        public bool DisconnectFromSW()
        {
            RemoveCommandManager();
            Marshal.ReleaseComObject(_cmdMgr);
            Marshal.ReleaseComObject(_swApp);
            _cmdMgr = null;
            _swApp  = null;
            return true;
        }

        // ------------------------------------------------------------------ //
        //  CommandManager setup                                                //
        // ------------------------------------------------------------------ //

        private void AddCommandManager()
        {
            // Icon images – 16x16 bitmaps embedded in the assembly or loaded from
            // the add-in installation directory.  BitmapHandler is a small helper
            // class (included below) that manages the temp file lifetime required
            // by ICommandGroup.
            _bmpHandler = new BitmapHandler();

            // Tab-image toolbar strip: 20px × 20px per icon, 3 icons.
            string toolbarPath = _bmpHandler.CreateFileFromResourceBitmap(
                "OpenMBD.Resources.toolbar.png", GetType().Assembly);
            string toolbarMaskPath = _bmpHandler.CreateFileFromResourceBitmap(
                "OpenMBD.Resources.toolbarMask.png", GetType().Assembly);

            bool docTypes = true;
            int[] knownIds = new int[3] { CmdIdExportQif, CmdIdExportStep, CmdIdExport3DPdf };

            ICommandGroup cmdGroup = _cmdMgr.CreateCommandGroup2(
                CmdGroupId,
                AddInName,
                AddInDescription,
                AddInDescription,
                -1,        // use default icon index
                docTypes,
                ref knownIds);

            cmdGroup.LargeIconList  = toolbarPath;
            cmdGroup.SmallIconList  = toolbarPath;
            cmdGroup.LargeMainIcon  = toolbarPath;
            cmdGroup.SmallMainIcon  = toolbarPath;

            // Add buttons ---------------------------------------------------
            int menuToolbarOpts = (int)(swCommandItemType_e.swMenuItem |
                                        swCommandItemType_e.swToolbarItem);

            cmdGroup.AddCommandItem2("Export QIF",   -1,
                "Export semantic MBD data to a QIF 3.0 XML file",
                "Export QIF", 0,
                nameof(OnExportQif), nameof(CanExportQif),
                CmdIdExportQif, menuToolbarOpts);

            cmdGroup.AddCommandItem2("Export STEP 242", -1,
                "Export the model to a STEP AP242 file",
                "Export STEP 242", 1,
                nameof(OnExportStep242), nameof(CanExportStep242),
                CmdIdExportStep, menuToolbarOpts);

            cmdGroup.AddCommandItem2("Export PDF", -1,
                "Export MBD/PMI data to a PDF report (no 3D PDF add-on required)",
                "Export PDF", 2,
                nameof(OnExport3DPdf), nameof(CanExport3DPdf),
                CmdIdExport3DPdf, menuToolbarOpts);

            cmdGroup.HasToolbar = true;
            cmdGroup.HasMenu    = true;
            cmdGroup.Activate();

            // Add a CommandTab to the Part and Assembly environments --------
            AddCommandTab(swDocumentTypes_e.swDocPART,     cmdGroup);
            AddCommandTab(swDocumentTypes_e.swDocASSEMBLY, cmdGroup);

            _cmdIds = new int[] { CmdIdExportQif, CmdIdExportStep, CmdIdExport3DPdf };
        }

        private void AddCommandTab(swDocumentTypes_e docType, ICommandGroup cmdGroup)
        {
            ICommandTab tab = _cmdMgr.GetCommandTab((int)docType, AddInName)
                              ?? _cmdMgr.AddCommandTab((int)docType, AddInName);

            ICommandTabBox tabBox = tab.AddCommandTabBox();

            int[] cmdArr = new int[]
            {
                cmdGroup.CommandID[CmdIdExportQif],
                cmdGroup.CommandID[CmdIdExportStep],
                cmdGroup.CommandID[CmdIdExport3DPdf]
            };
            int[] txtDisplay = new int[]
            {
                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow
            };

            tabBox.AddCommands(cmdArr, txtDisplay);
        }

        private void RemoveCommandManager()
        {
            _bmpHandler?.Dispose();

            if (_cmdIds != null)
            {
                // Remove command tabs from Part and Assembly environments.
                foreach (swDocumentTypes_e docType in new[]
                    { swDocumentTypes_e.swDocPART, swDocumentTypes_e.swDocASSEMBLY })
                {
                    ICommandTab tab = _cmdMgr.GetCommandTab((int)docType, AddInName);
                    if (tab != null)
                        _cmdMgr.RemoveCommandTab(tab);
                }
            }

            _cmdMgr.RemoveCommandGroup2(CmdGroupId, true);
        }

        // ------------------------------------------------------------------ //
        //  Command callbacks                                                   //
        // ------------------------------------------------------------------ //

        /// <summary>Invoked when the user clicks 'Export QIF'.</summary>
        public void OnExportQif()
        {
            try
            {
                var mbdItems = _pmiService.ExtractFromActiveDocument();
                if (mbdItems.Count == 0)
                    return; // message already shown by the service

                using (var dlg = new System.Windows.Forms.SaveFileDialog
                {
                    Title            = "Export QIF",
                    Filter           = "QIF Files (*.qif)|*.qif|XML Files (*.xml)|*.xml",
                    DefaultExt       = "qif",
                    FileName         = GetDefaultFileName("qif")
                })
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return;

                    var exporter = new QifExporter();
                    exporter.Export(mbdItems, dlg.FileName,
                        _swApp.IActiveDoc2?.GetPathName() ?? string.Empty);

                    _swApp.SendMsgToUser2(
                        $"OpenMBD: QIF export complete.\n{dlg.FileName}",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                }
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    $"OpenMBD – Export QIF error:\n{ex.Message}",
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        /// <summary>
        /// Invoked when the user clicks 'Export STEP 242'.
        /// <para>
        /// Uses <see cref="Step242Exporter"/> to produce an AP242 file without the
        /// paid SOLIDWORKS MBD add-on.  The geometry is captured via the standard
        /// SOLIDWORKS STEP exporter (AP203/AP214, available in every license) and
        /// PMI annotations are appended as ISO 10303-242 GD&amp;T entities.
        /// </para>
        /// </summary>
        public void OnExportStep242()
        {
            try
            {
                var swModel = _swApp.IActiveDoc2 as ModelDoc2;
                if (swModel == null) return;

                var mbdItems = _pmiService.ExtractFromActiveDocument();

                using (var dlg = new System.Windows.Forms.SaveFileDialog
                {
                    Title      = "Export STEP AP242",
                    Filter     = "STEP Files (*.step;*.stp)|*.step;*.stp",
                    DefaultExt = "step",
                    FileName   = GetDefaultFileName("step")
                })
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return;

                    var exporter = new Step242Exporter();
                    exporter.Export(swModel, mbdItems, dlg.FileName);

                    _swApp.SendMsgToUser2(
                        $"OpenMBD: STEP AP242 export complete.\n{dlg.FileName}",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                }
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    $"OpenMBD – Export STEP 242 error:\n{ex.Message}",
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        /// <summary>
        /// Invoked when the user clicks 'Export PDF'.
        /// <para>
        /// Uses <see cref="PdfReportExporter"/> to generate a formatted MBD/PMI
        /// report PDF without the paid SOLIDWORKS 3D PDF add-on.  The report
        /// contains a full annotation table built with built-in .NET libraries only.
        /// </para>
        /// </summary>
        public void OnExport3DPdf()
        {
            try
            {
                var mbdItems = _pmiService.ExtractFromActiveDocument();

                using (var dlg = new System.Windows.Forms.SaveFileDialog
                {
                    Title      = "Export PDF MBD Report",
                    Filter     = "PDF Files (*.pdf)|*.pdf",
                    DefaultExt = "pdf",
                    FileName   = GetDefaultFileName("pdf")
                })
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return;

                    var exporter = new PdfReportExporter();
                    exporter.Export(mbdItems, dlg.FileName,
                        _swApp.IActiveDoc2?.GetPathName() ?? string.Empty);

                    _swApp.SendMsgToUser2(
                        $"OpenMBD: PDF MBD report export complete.\n{dlg.FileName}",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                }
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    $"OpenMBD – Export PDF error:\n{ex.Message}",
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // ------------------------------------------------------------------ //
        //  Enable/disable callbacks (return 1 = enabled)                      //
        // ------------------------------------------------------------------ //

        public int CanExportQif()    => HasActivePartOrAssembly() ? 1 : 0;
        public int CanExportStep242() => HasActivePartOrAssembly() ? 1 : 0;
        public int CanExport3DPdf()  => HasActivePartOrAssembly() ? 1 : 0;

        // ------------------------------------------------------------------ //
        //  Utility                                                             //
        // ------------------------------------------------------------------ //

        private bool HasActivePartOrAssembly()
        {
            var doc = _swApp?.IActiveDoc2 as ModelDoc2;
            if (doc == null) return false;
            int t = doc.GetType();
            return t == (int)swDocumentTypes_e.swDocPART ||
                   t == (int)swDocumentTypes_e.swDocASSEMBLY;
        }

        private string GetDefaultFileName(string extension)
        {
            string path = _swApp?.IActiveDoc2?.GetPathName();
            return string.IsNullOrEmpty(path)
                ? $"export.{extension}"
                : System.IO.Path.ChangeExtension(
                    System.IO.Path.GetFileName(path), extension);
        }
    }
}
