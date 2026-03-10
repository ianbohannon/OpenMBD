using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace OpenMBD
{
    /// <summary>
    /// Serializes a list of <see cref="MBDDataModel"/> objects into an XML file that
    /// conforms to the QIF 3.0 schema structure (ANSI/ASME QIF 3000-2018).
    /// <para>
    /// This is an integration stub: it generates a well-formed QIF document skeleton
    /// and maps the most common MBD elements.  A production implementation would
    /// reference the full C# classes generated from the official QIF .xsd files
    /// (see <c>/lib/README.md</c> and <c>/schemas/</c> for details).
    /// </para>
    /// </summary>
    public class QifExporter
    {
        // QIF 3.0 XML namespace URI
        private const string QifNamespace = "http://qifstandards.org/xsd/qif3";

        /// <summary>
        /// Serializes <paramref name="mbdItems"/> to a QIF 3.0 XML file at
        /// <paramref name="outputPath"/>.
        /// </summary>
        /// <param name="mbdItems">Extracted MBD data items to export.</param>
        /// <param name="outputPath">Full path of the target .qif file.</param>
        /// <param name="modelFileName">
        /// Original model file name, recorded in the QIF header.
        /// </param>
        public void Export(List<MBDDataModel> mbdItems, string outputPath, string modelFileName = "")
        {
            if (mbdItems == null) throw new ArgumentNullException(nameof(mbdItems));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("outputPath must not be empty.", nameof(outputPath));

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                NewLineOnAttributes = false
            };

            using (var writer = XmlWriter.Create(outputPath, settings))
            {
                // Root element -----------------------------------------------
                writer.WriteStartDocument();
                writer.WriteStartElement("QIFDocument", QifNamespace);
                writer.WriteAttributeString("xmlns", "xsi", null,
                    "http://www.w3.org/2001/XMLSchema-instance");
                writer.WriteAttributeString("xsi", "schemaLocation", null,
                    QifNamespace + " " +
                    "https://raw.githubusercontent.com/QualityInformationFramework/" +
                    "QIF-Community/master/schema/QIFApplications/QIFDocument.xsd");
                writer.WriteAttributeString("versionQIF", "3.0.0");
                writer.WriteAttributeString("idMax", mbdItems.Count.ToString());

                // Header -------------------------------------------------------
                WriteHeader(writer, modelFileName);

                // Product (model file reference) --------------------------------
                WriteProduct(writer, modelFileName);

                // Characteristics ---------------------------------------------
                WriteCharacteristics(writer, mbdItems);

                writer.WriteEndElement(); // QIFDocument
                writer.WriteEndDocument();
            }
        }

        // ------------------------------------------------------------------ //
        //  Private section writers                                             //
        // ------------------------------------------------------------------ //

        private static void WriteHeader(XmlWriter w, string modelFileName)
        {
            w.WriteStartElement("Header", QifNamespace);

            w.WriteStartElement("QPId", QifNamespace);
            w.WriteString(Guid.NewGuid().ToString("D"));
            w.WriteEndElement();

            w.WriteStartElement("Version", QifNamespace);
            w.WriteStartElement("TimeCreated", QifNamespace);
            w.WriteString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("Application", QifNamespace);
            w.WriteStartElement("Name", QifNamespace);
            w.WriteString("OpenMBD");
            w.WriteEndElement();
            w.WriteStartElement("SourceFileName", QifNamespace);
            w.WriteString(Path.GetFileName(modelFileName));
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteEndElement(); // Header
        }

        private static void WriteProduct(XmlWriter w, string modelFileName)
        {
            w.WriteStartElement("Product", QifNamespace);
            w.WriteStartElement("PartSets", QifNamespace);
            w.WriteStartElement("Part", QifNamespace);
            w.WriteAttributeString("id", "1");

            w.WriteStartElement("Name", QifNamespace);
            w.WriteString(Path.GetFileNameWithoutExtension(modelFileName));
            w.WriteEndElement();

            w.WriteStartElement("ReferencedComponent", QifNamespace);
            w.WriteStartElement("ExternalFileReference", QifNamespace);
            w.WriteStartElement("Path", QifNamespace);
            w.WriteString(modelFileName);
            w.WriteEndElement();
            w.WriteStartElement("FileFormat", QifNamespace);
            w.WriteString("SOLIDWORKS");
            w.WriteEndElement();
            w.WriteEndElement(); // ExternalFileReference
            w.WriteEndElement(); // ReferencedComponent

            w.WriteEndElement(); // Part
            w.WriteEndElement(); // PartSets
            w.WriteEndElement(); // Product
        }

        private static void WriteCharacteristics(XmlWriter w, List<MBDDataModel> mbdItems)
        {
            w.WriteStartElement("Characteristics", QifNamespace);
            w.WriteStartElement("CharacteristicDefinitions", QifNamespace);
            w.WriteAttributeString("n", mbdItems.Count.ToString());

            int xmlId = 2; // id="1" is reserved for the Part element above
            foreach (var item in mbdItems)
            {
                switch (item.AnnotationType)
                {
                    case "Gtol":
                        WriteGtolCharacteristic(w, item, xmlId++);
                        break;
                    case "DatumTag":
                        WriteDatumCharacteristic(w, item, xmlId++);
                        break;
                    case "Dimension":
                        WriteDimensionCharacteristic(w, item, xmlId++);
                        break;
                }
            }

            w.WriteEndElement(); // CharacteristicDefinitions
            w.WriteEndElement(); // Characteristics
        }

        private static void WriteGtolCharacteristic(XmlWriter w, MBDDataModel item, int id)
        {
            // QIF uses specific characteristic definition types per geometric
            // characteristic.  This stub uses the generic form; a full
            // implementation maps swGtolGeomChar_e values to individual QIF types
            // (PositionCharacteristicDefinitionType, FlatnessCharacteristic..., etc.).
            w.WriteStartElement("GeometricCharacteristicDefinition", QifNamespace);
            w.WriteAttributeString("id", id.ToString());

            w.WriteStartElement("Name", QifNamespace);
            w.WriteString(item.CharacteristicName ?? string.Empty);
            w.WriteEndElement();

            w.WriteStartElement("Tolerance", QifNamespace);
            w.WriteStartElement("LinearTolerance", QifNamespace);
            w.WriteStartElement("Value", QifNamespace);
            w.WriteString(item.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteEndElement(); // LinearTolerance
            w.WriteEndElement(); // Tolerance

            // Datum references
            if (item.DatumReferences.Count > 0)
            {
                w.WriteStartElement("DatumReferenceFrame", QifNamespace);
                int precedence = 1;
                foreach (var dr in item.DatumReferences)
                {
                    w.WriteStartElement("Datum", QifNamespace);
                    w.WriteAttributeString("precedence",
                        precedence == 1 ? "PRIMARY" : precedence == 2 ? "SECONDARY" : "TERTIARY");
                    w.WriteStartElement("DatumDefinitionId", QifNamespace);
                    w.WriteString(dr.Label);
                    w.WriteEndElement();
                    if (!string.IsNullOrWhiteSpace(dr.MaterialCondition))
                    {
                        w.WriteStartElement("MaterialCondition", QifNamespace);
                        w.WriteString(dr.MaterialCondition);
                        w.WriteEndElement();
                    }
                    w.WriteEndElement(); // Datum
                    precedence++;
                }
                w.WriteEndElement(); // DatumReferenceFrame
            }

            w.WriteEndElement(); // GeometricCharacteristicDefinition
        }

        private static void WriteDatumCharacteristic(XmlWriter w, MBDDataModel item, int id)
        {
            w.WriteStartElement("DatumDefinition", QifNamespace);
            w.WriteAttributeString("id", id.ToString());

            w.WriteStartElement("DatumLabel", QifNamespace);
            w.WriteString(item.CharacteristicName ?? string.Empty);
            w.WriteEndElement();

            w.WriteEndElement(); // DatumDefinition
        }

        private static void WriteDimensionCharacteristic(XmlWriter w, MBDDataModel item, int id)
        {
            w.WriteStartElement("LinearCharacteristicDefinition", QifNamespace);
            w.WriteAttributeString("id", id.ToString());

            w.WriteStartElement("Name", QifNamespace);
            w.WriteString(item.CharacteristicName ?? string.Empty);
            w.WriteEndElement();

            w.WriteStartElement("NominalValue", QifNamespace);
            w.WriteString(item.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteEndElement();

            if (item.TolerancePlus != 0 || item.ToleranceMinus != 0)
            {
                w.WriteStartElement("Tolerance", QifNamespace);
                w.WriteStartElement("PlusValue", QifNamespace);
                w.WriteString(item.TolerancePlus.ToString("G",
                    System.Globalization.CultureInfo.InvariantCulture));
                w.WriteEndElement();
                w.WriteStartElement("MinusValue", QifNamespace);
                w.WriteString(item.ToleranceMinus.ToString("G",
                    System.Globalization.CultureInfo.InvariantCulture));
                w.WriteEndElement();
                w.WriteEndElement(); // Tolerance
            }

            w.WriteStartElement("Unit", QifNamespace);
            w.WriteString(item.Unit ?? "mm");
            w.WriteEndElement();

            w.WriteEndElement(); // LinearCharacteristicDefinition
        }
    }
}
