using System;
using System.Xml;
using System.Text;
using System.Collections.Generic;

namespace Mpeg7Signature
{
    public class Mpeg7XmlExporter
    {
        /// <summary>
        /// Export MPEG-7 signatures to XML format
        /// </summary>
        /// <param name="fineSignatures">List of fine signatures</param>
        /// <param name="coarseSignatures">List of coarse signatures</param>
        /// <param name="width">Video width</param>
        /// <param name="height">Video height</param>
        /// <param name="timeBaseDen">Time base denominator</param>
        /// <param name="timeBaseNum">Time base numerator</param>
        /// <returns>XML string in MPEG-7 format</returns>
        public static string ExportToXml(
            List<FineSignature> fineSignatures,
            List<CoarseSignature> coarseSignatures,
            int width, int height,
            int timeBaseDen, int timeBaseNum)
        {
            var doc = new XmlDocument();
            
            // Create root element with namespaces
            var root = doc.CreateElement("Mpeg7");
            root.SetAttribute("xmlns", "urn:mpeg:mpeg7:schema:2001");
            root.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
            root.SetAttribute("xsi:schemaLocation", "urn:mpeg:mpeg7:schema:2001 schema/Mpeg7-2001.xsd");
            doc.AppendChild(root);

            // Description unit
            var descUnit = doc.CreateElement("DescriptionUnit");
            descUnit.SetAttribute("xsi:type", "DescriptorCollectionType");
            root.AppendChild(descUnit);

            // Descriptor
            var descriptor = doc.CreateElement("Descriptor");
            descriptor.SetAttribute("xsi:type", "VideoSignatureType");
            descUnit.AppendChild(descriptor);

            // Video signature region
            var sigRegion = doc.CreateElement("VideoSignatureRegion");
            descriptor.AppendChild(sigRegion);

            // Spatial region
            var spatialRegion = doc.CreateElement("VideoSignatureSpatialRegion");
            sigRegion.AppendChild(spatialRegion);

            var pixel1 = doc.CreateElement("Pixel");
            pixel1.InnerText = "0 0 ";
            spatialRegion.AppendChild(pixel1);

            var pixel2 = doc.CreateElement("Pixel");
            pixel2.InnerText = $"{width - 1} {height - 1} ";
            spatialRegion.AppendChild(pixel2);

            // Start frame
            var startFrame = doc.CreateElement("StartFrameOfSpatialRegion");
            startFrame.InnerText = "0";
            sigRegion.AppendChild(startFrame);

            // Media time unit
            var mediaTimeUnit = doc.CreateElement("MediaTimeUnit");
            mediaTimeUnit.InnerText = (timeBaseDen / timeBaseNum).ToString();
            sigRegion.AppendChild(mediaTimeUnit);

            // Media time of spatial region
            var mediaTime = doc.CreateElement("MediaTimeOfSpatialRegion");
            sigRegion.AppendChild(mediaTime);

            var startMediaTime = doc.CreateElement("StartMediaTimeOfSpatialRegion");
            startMediaTime.InnerText = "0";
            mediaTime.AppendChild(startMediaTime);

            var endMediaTime = doc.CreateElement("EndMediaTimeOfSpatialRegion");
            if (fineSignatures.Count > 0)
                endMediaTime.InnerText = fineSignatures[fineSignatures.Count - 1].Pts.ToString();
            else
                endMediaTime.InnerText = "0";
            mediaTime.AppendChild(endMediaTime);

            // Coarse signatures (video segments)
            foreach (var cs in coarseSignatures)
            {
                var segment = doc.CreateElement("VSVideoSegment");
                sigRegion.AppendChild(segment);

                var startFrameOfSegment = doc.CreateElement("StartFrameOfSegment");
                startFrameOfSegment.InnerText = cs.First?.Index.ToString() ?? "0";
                segment.AppendChild(startFrameOfSegment);

                var endFrameOfSegment = doc.CreateElement("EndFrameOfSegment");
                endFrameOfSegment.InnerText = cs.Last?.Index.ToString() ?? "0";
                segment.AppendChild(endFrameOfSegment);

                var mediaTimeOfSegment = doc.CreateElement("MediaTimeOfSegment");
                segment.AppendChild(mediaTimeOfSegment);

                var startMediaTimeOfSegment = doc.CreateElement("StartMediaTimeOfSegment");
                startMediaTimeOfSegment.InnerText = cs.First?.Pts.ToString() ?? "0";
                mediaTimeOfSegment.AppendChild(startMediaTimeOfSegment);

                var endMediaTimeOfSegment = doc.CreateElement("EndMediaTimeOfSegment");
                endMediaTimeOfSegment.InnerText = cs.Last?.Pts.ToString() ?? "0";
                mediaTimeOfSegment.AppendChild(endMediaTimeOfSegment);

                // Bag of words (5 words per segment)
                for (int i = 0; i < 5; i++)
                {
                    var bagOfWords = doc.CreateElement("BagOfWords");
                    var sb = new StringBuilder();
                    
                    for (int j = 0; j < 31; j++)
                    {
                        byte n = cs.Data[i, j];
                        if (j < 30)
                        {
                            // Output 8 bits
                            for (int bit = 7; bit >= 0; bit--)
                            {
                                sb.Append(((n >> bit) & 1).ToString());
                                sb.Append("  ");
                            }
                        }
                        else
                        {
                            // Output only 3 bits for last byte
                            for (int bit = 7; bit >= 5; bit--)
                            {
                                sb.Append(((n >> bit) & 1).ToString());
                                sb.Append("  ");
                            }
                        }
                    }
                    
                    bagOfWords.InnerText = sb.ToString().TrimEnd();
                    segment.AppendChild(bagOfWords);
                }
            }

            // Fine signatures (video frames)
            foreach (var fs in fineSignatures)
            {
                var frame = doc.CreateElement("VideoFrame");
                sigRegion.AppendChild(frame);

                var mediaTimeOfFrame = doc.CreateElement("MediaTimeOfFrame");
                mediaTimeOfFrame.InnerText = fs.Pts.ToString();
                frame.AppendChild(mediaTimeOfFrame);

                var frameConfidence = doc.CreateElement("FrameConfidence");
                frameConfidence.InnerText = fs.Confidence.ToString();
                frame.AppendChild(frameConfidence);

                // Words (5 words per frame)
                var word = doc.CreateElement("Word");
                var wordBuilder = new StringBuilder();
                for (int i = 0; i < 5; i++)
                {
                    wordBuilder.Append(fs.Words[i]);
                    if (i < 4) wordBuilder.Append(" ");
                }
                word.InnerText = wordBuilder.ToString();
                frame.AppendChild(word);

                // Frame signature
                var frameSig = doc.CreateElement("FrameSignature");
                var sigBuilder = new StringBuilder();
                var pot3 = new uint[] { 81, 27, 9, 3, 1 }; // 3^4, 3^3, 3^2, 3^1, 3^0
                
                for (int i = 0; i < fs.FrameSig.Length; i++)
                {
                    if (i > 0) sigBuilder.Append(" ");
                    
                    // Decode ternary values
                    sigBuilder.Append((fs.FrameSig[i] / pot3[0]).ToString());
                    for (int j = 1; j < 5; j++)
                    {
                        sigBuilder.Append(" ");
                        sigBuilder.Append(((fs.FrameSig[i] % pot3[j - 1]) / pot3[j]).ToString());
                    }
                }
                
                frameSig.InnerText = sigBuilder.ToString();
                frame.AppendChild(frameSig);
            }

            // Format and return XML
            var stringBuilder = new StringBuilder();
            using (var xmlWriter = XmlWriter.Create(stringBuilder, new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                Encoding = Encoding.UTF8
            }))
            {
                doc.Save(xmlWriter);
            }

            return stringBuilder.ToString();
        }
    }
}