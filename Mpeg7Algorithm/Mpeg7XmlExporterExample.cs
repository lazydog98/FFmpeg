using System;
using System.Collections.Generic;
using System.IO;

namespace Mpeg7Signature
{
    class XmlExporterExample
    {
        static void Main(string[] args)
        {
            Console.WriteLine("MPEG-7 Signature XML Export Example");
            Console.WriteLine("====================================");

            // Create signature calculator
            var calculator = new Mpeg7SignatureCalculator();
            
            // Generate multiple test frames with different patterns
            var fineSignatures = new List<FineSignature>();
            var coarseSignatures = new List<CoarseSignature>();
            
            // Video properties
            int width = 320;
            int height = 240;
            int timeBaseDen = 25; // 25 fps
            int timeBaseNum = 1;
            int totalFrames = 125; // 5 seconds at 25fps
            
            Console.WriteLine($"Generating signatures for {totalFrames} frames ({width}x{height})...");
            
            // Generate signatures for different frame patterns
            for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
            {
                byte[] frameData = GenerateTestFrame(width, height, frameIndex);
                ulong pts = (ulong)(frameIndex * 512); // Increment PTS
                
                var signature = calculator.CalculateSignature(frameData, width, height, pts, (uint)frameIndex);
                fineSignatures.Add(signature);
                
                // Progress indicator
                if ((frameIndex + 1) % 25 == 0)
                {
                    Console.WriteLine($"  Generated {frameIndex + 1}/{totalFrames} signatures...");
                }
            }
            
            // Create coarse signatures (segments of 90 frames each)
            Console.WriteLine("Creating coarse signatures...");
            coarseSignatures = CreateCoarseSignatures(fineSignatures);
            
            // Export to XML
            Console.WriteLine("Exporting to XML...");
            string xmlContent = Mpeg7XmlExporter.ExportToXml(
                fineSignatures, 
                coarseSignatures, 
                width, 
                height, 
                timeBaseDen, 
                timeBaseNum
            );
            
            // Save to file
            string outputFile = "generated_signature.xml";
            File.WriteAllText(outputFile, xmlContent);
            
            Console.WriteLine($"\n=== EXPORT COMPLETED ===");
            Console.WriteLine($"Output file: {outputFile}");
            Console.WriteLine($"File size: {new FileInfo(outputFile).Length / 1024.0:F1} KB");
            Console.WriteLine($"Fine signatures: {fineSignatures.Count}");
            Console.WriteLine($"Coarse signatures: {coarseSignatures.Count}");
            
            // Display sample statistics
            DisplaySignatureStatistics(fineSignatures);
            
            // Validate by parsing the generated XML
            Console.WriteLine("\n=== VALIDATION ===");
            ValidateGeneratedXml(outputFile);
            
            Console.WriteLine("\nExample completed successfully!");
        }
        
        /// <summary>
        /// Generate test frame data with various patterns
        /// </summary>
        static byte[] GenerateTestFrame(int width, int height, int frameIndex)
        {
            byte[] frameData = new byte[width * height];
            
            // Create different patterns based on frame index
            switch (frameIndex % 5)
            {
                case 0: // Checkerboard pattern
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            frameData[y * width + x] = (byte)(((x / 8 + y / 8) % 2) * 255);
                        }
                    }
                    break;
                    
                case 1: // Horizontal stripes
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            frameData[y * width + x] = (byte)((y / 10 % 2) * 255);
                        }
                    }
                    break;
                    
                case 2: // Vertical stripes
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            frameData[y * width + x] = (byte)((x / 10 % 2) * 255);
                        }
                    }
                    break;
                    
                case 3: // Gradient
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            frameData[y * width + x] = (byte)((x * 255) / width);
                        }
                    }
                    break;
                    
                case 4: // Solid with varying intensity
                    byte intensity = (byte)((frameIndex * 255) / 125);
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            frameData[y * width + x] = intensity;
                        }
                    }
                    break;
            }
            
            return frameData;
        }
        
        /// <summary>
        /// Create coarse signatures from fine signatures (90 frames per segment)
        /// </summary>
        static List<CoarseSignature> CreateCoarseSignatures(List<FineSignature> fineSignatures)
        {
            var coarseSignatures = new List<CoarseSignature>();
            
            for (int i = 0; i < fineSignatures.Count; i += 90)
            {
                var segment = new CoarseSignature();
                segment.First = fineSignatures[i];
                segment.Last = fineSignatures[Math.Min(i + 89, fineSignatures.Count - 1)];
                
                // Generate bag-of-words data from fine signatures in this segment
                for (int wordIndex = 0; wordIndex < 5; wordIndex++)
                {
                    for (int byteIndex = 0; byteIndex < 31; byteIndex++)
                    {
                        byte value = 0;
                        
                        // Sample frames in this segment to create bag-of-words
                        for (int frameOffset = 0; frameOffset < Math.Min(90, fineSignatures.Count - i); frameOffset++)
                        {
                            if (frameOffset < 8) // Use first 8 frames for each byte
                            {
                                var frame = fineSignatures[i + frameOffset];
                                if (wordIndex < frame.Words.Length && frame.Words[wordIndex] > 120)
                                {
                                    value |= (byte)(1 << (7 - frameOffset));
                                }
                            }
                        }
                        
                        segment.Data[wordIndex, byteIndex] = value;
                    }
                }
                
                coarseSignatures.Add(segment);
            }
            
            return coarseSignatures;
        }
        
        /// <summary>
        /// Display statistics about the generated signatures
        /// </summary>
        static void DisplaySignatureStatistics(List<FineSignature> signatures)
        {
            Console.WriteLine("\n=== SIGNATURE STATISTICS ===");
            
            // Calculate word statistics
            var wordStats = new int[5][];
            for (int i = 0; i < 5; i++)
            {
                wordStats[i] = signatures.Select(s => (int)s.Words[i]).ToArray();
            }
            
            for (int i = 0; i < 5; i++)
            {
                var avg = wordStats[i].Average();
                var min = wordStats[i].Min();
                var max = wordStats[i].Max();
                Console.WriteLine($"Word {i}: avg={avg:F1}, range=[{min}-{max}]");
            }
            
            // Calculate confidence statistics
            var confidences = signatures.Select(s => (int)s.Confidence).ToArray();
            Console.WriteLine($"Confidence: avg={confidences.Average():F1}, range=[{confidences.Min()}-{confidences.Max()}]");
            
            // Show temporal progression
            Console.WriteLine($"Temporal range: {signatures.First().Pts} - {signatures.Last().Pts}");
            Console.WriteLine($"Frame indices: {signatures.First().Index} - {signatures.Last().Index}");
        }
        
        /// <summary>
        /// Validate the generated XML by parsing it back
        /// </summary>
        static void ValidateGeneratedXml(string xmlFile)
        {
            try
            {
                var parser = new MPEG7SignatureParser();
                var parsedSignature = parser.ParseFile(xmlFile);
                
                Console.WriteLine($"✓ XML validation successful!");
                Console.WriteLine($"  Parsed {parsedSignature.TotalFrames} frames");
                Console.WriteLine($"  Parsed {parsedSignature.Segments.Count} segments");
                Console.WriteLine($"  Duration: {parsedSignature.Duration} time units");
                Console.WriteLine($"  Spatial region: ({parsedSignature.SpatialRegion.x1},{parsedSignature.SpatialRegion.y1}) to ({parsedSignature.SpatialRegion.x2},{parsedSignature.SpatialRegion.y2})");
                
                // Verify some sample data
                if (parsedSignature.Frames.Count > 0)
                {
                    var firstFrame = parsedSignature.Frames[0];
                    Console.WriteLine($"  First frame: PTS={firstFrame.Timestamp}, Confidence={firstFrame.Confidence}, Words=[{string.Join(",", firstFrame.Words)}]");
                }
                
                if (parsedSignature.Frames.Count > 1)
                {
                    var lastFrame = parsedSignature.Frames[parsedSignature.Frames.Count - 1];
                    Console.WriteLine($"  Last frame: PTS={lastFrame.Timestamp}, Confidence={lastFrame.Confidence}, Words=[{string.Join(",", lastFrame.Words)}]");
                }
                
                // Test comparison with itself (should be 100% similar)
                Console.WriteLine("\n=== SELF-COMPARISON TEST ===");
                var comparator = new SignatureComparator();
                var selfComparison = comparator.CompareSignatures(parsedSignature, parsedSignature);
                Console.WriteLine($"Self-comparison similarity: {selfComparison.SimilarityScore:F3} (should be 1.000)");
                Console.WriteLine($"Is duplicate: {selfComparison.IsDuplicate} (should be True)");
                
            }
            catch (Exception e)
            {
                Console.WriteLine($"✗ XML validation failed: {e.Message}");
            }
        }
    }
}