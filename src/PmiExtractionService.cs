using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace OpenMBD
{
    /// <summary>
    /// Extracts Model-Based Definition (MBD) / PMI data from the active SOLIDWORKS
    /// model using the IPMIData interface exposed via
    /// <see cref="ModelDocExtension.GetPMIData"/>.
    /// <para>
    /// Recognised annotation types:
    /// <list type="bullet">
    ///   <item><description><c>Gtol</c> – Geometric tolerances (position, flatness, etc.)</description></item>
    ///   <item><description><c>DatumTag</c> – Datum feature symbols</description></item>
    ///   <item><description><c>DisplayDimension</c> – Driven / driving dimensions</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public class PmiExtractionService
    {
        private readonly ISldWorks _swApp;

        public PmiExtractionService(ISldWorks swApp)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
        }

        /// <summary>
        /// Extracts all PMI annotations from the currently active model document and
        /// returns them as a list of <see cref="MBDDataModel"/> objects.
        /// </summary>
        /// <returns>
        /// A list of extracted MBD items, or an empty list when the active document is
        /// not a part or assembly, or when no PMI data is present.
        /// </returns>
        public List<MBDDataModel> ExtractFromActiveDocument()
        {
            var results = new List<MBDDataModel>();

            var swModel = _swApp.IActiveDoc2 as ModelDoc2;
            if (swModel == null)
            {
                _swApp.SendMsgToUser2(
                    "OpenMBD: No active document found. Please open a part or assembly.",
                    (int)swMessageBoxIcon_e.swMbWarning,
                    (int)swMessageBoxBtn_e.swMbOk);
                return results;
            }

            int docType = swModel.GetType();
            if (docType != (int)swDocumentTypes_e.swDocPART &&
                docType != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                _swApp.SendMsgToUser2(
                    "OpenMBD: Active document must be a Part or Assembly.",
                    (int)swMessageBoxIcon_e.swMbWarning,
                    (int)swMessageBoxBtn_e.swMbOk);
                return results;
            }

            ModelDocExtension swExt = swModel.Extension;

            // GetPMIData returns an object array of IPMIData instances.
            object[] pmiDataArray = swExt.GetPMIData() as object[];
            if (pmiDataArray == null || pmiDataArray.Length == 0)
            {
                _swApp.SendMsgToUser2(
                    "OpenMBD: No PMI data found in the active document.",
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
                return results;
            }

            int idCounter = 0;
            foreach (object pmiObj in pmiDataArray)
            {
                var pmiData = pmiObj as IPMIData;
                if (pmiData == null) continue;

                // Retrieve the underlying annotation object to determine its type.
                var annotation = pmiData.GetAnnotation() as Annotation;
                if (annotation == null) continue;

                int annotType = annotation.GetType();
                MBDDataModel model = null;

                if (annotType == (int)swAnnotationType_e.swGTOL)
                {
                    model = ProcessGtol(pmiData, annotation, idCounter);
                }
                else if (annotType == (int)swAnnotationType_e.swDATUMTAG)
                {
                    model = ProcessDatumTag(pmiData, annotation, idCounter);
                }
                else if (annotType == (int)swAnnotationType_e.swDISPLAYDIMENSION)
                {
                    model = ProcessDisplayDimension(pmiData, annotation, idCounter);
                }

                if (model != null)
                {
                    results.Add(model);
                    idCounter++;
                }
            }

            return results;
        }

        // ------------------------------------------------------------------ //
        //  Private helpers                                                     //
        // ------------------------------------------------------------------ //

        private MBDDataModel ProcessGtol(IPMIData pmiData, Annotation annotation, int id)
        {
            var gtol = annotation.GetSpecificAnnotation() as Gtol;
            if (gtol == null) return null;

            var model = new MBDDataModel
            {
                Id = $"GTOL_{id:D4}",
                AnnotationType = "Gtol",
                RawCalloutText = annotation.GetName()
            };

            // GtolData provides structured access to the tolerance frame cells.
            GtolData gtolData = gtol.GetGtolData();
            if (gtolData != null)
            {
                model.CharacteristicName = gtolData.GetSymbolString();
                model.Value = SafeParseDouble(gtolData.GetToleranceValue1());
                model.Unit = gtolData.GetUnit();

                // Datum references – up to three datum compartments in the frame.
                string[] datumLabels = new string[]
                {
                    gtolData.GetDatumRef1(), gtolData.GetDatumRef2(), gtolData.GetDatumRef3()
                };
                string[] datumMods = new string[]
                {
                    gtolData.GetDatumRef1ModifySymbol(),
                    gtolData.GetDatumRef2ModifySymbol(),
                    gtolData.GetDatumRef3ModifySymbol()
                };

                for (int i = 0; i < datumLabels.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(datumLabels[i]))
                    {
                        model.DatumReferences.Add(new DatumReference
                        {
                            Label = datumLabels[i].Trim(),
                            MaterialCondition = datumMods[i]?.Trim()
                        });
                    }
                }
            }

            return model;
        }

        private MBDDataModel ProcessDatumTag(IPMIData pmiData, Annotation annotation, int id)
        {
            var datumTag = annotation.GetSpecificAnnotation() as DatumTag;
            if (datumTag == null) return null;

            return new MBDDataModel
            {
                Id = $"DATUM_{id:D4}",
                AnnotationType = "DatumTag",
                CharacteristicName = datumTag.GetLabel(),
                RawCalloutText = annotation.GetName()
            };
        }

        private MBDDataModel ProcessDisplayDimension(IPMIData pmiData, Annotation annotation, int id)
        {
            var dispDim = annotation.GetSpecificAnnotation() as DisplayDimension;
            if (dispDim == null) return null;

            var dim = dispDim.GetDimension2(0) as Dimension;
            if (dim == null) return null;

            double nominalValue = dim.GetSystemValue3(
                (int)swInConfigurationOpts_e.swThisConfiguration, null);

            DimensionTolerance tol = dim.DimensionTolerances;
            double tolPlus = 0, tolMinus = 0;
            if (tol != null)
            {
                tolPlus = tol.MaxValue;
                tolMinus = tol.MinValue;
            }

            return new MBDDataModel
            {
                Id = $"DIM_{id:D4}",
                AnnotationType = "Dimension",
                CharacteristicName = dim.Name,
                Value = nominalValue,
                TolerancePlus = tolPlus,
                ToleranceMinus = tolMinus,
                Unit = LengthUnitToString(dim.GetUnit()),
                RawCalloutText = annotation.GetName()
            };
        }

        private static string LengthUnitToString(int swUnit)
        {
            switch (swUnit)
            {
                case (int)swLengthUnit_e.swMM:          return "mm";
                case (int)swLengthUnit_e.swCM:          return "cm";
                case (int)swLengthUnit_e.swMETER:       return "m";
                case (int)swLengthUnit_e.swINCHES:      return "in";
                case (int)swLengthUnit_e.swFEET:        return "ft";
                case (int)swLengthUnit_e.swFEETINCHES:  return "ft-in";
                case (int)swLengthUnit_e.swANGSTROM:    return "angstrom";
                case (int)swLengthUnit_e.swNANOMETER:   return "nm";
                case (int)swLengthUnit_e.swMICRON:      return "µm";
                case (int)swLengthUnit_e.swMIL:         return "mil";
                case (int)swLengthUnit_e.swUIN:         return "µin";
                default:                                return "mm"; // safe fallback
            }
        }

        private static double SafeParseDouble(string value)
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
                return result;
            return 0.0;
        }
    }
}
