using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace Mpeg7Signature
{
    class SignatureParserExample
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            try
            {
                var command = args[0].ToLower();
                
                switch (command)
                {
                    case "parse":
                        if (args.Length >= 2)
                            ParseSingleFile(args[1]);
                        else
                            Console.WriteLine("Error: Please specify a file to parse");
                        break;
                        
                    case "compare":
                        if (args.Length >= 3)
                            CompareTwoFiles(args[1], args[2]);
                        else
                            Console.WriteLine("Error: Please specify two files to compare");
                        break;
                        
                    case "batch":
                        if (args.Length >= 2)
                            ProcessBatch(args[1], args.Length > 2 ? args[2] : null);
                        else
                            Console.WriteLine("Error: Please specify a directory to process");
                        break;
                        
                    default:
                        ShowUsage();
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }
        }

        static void ShowUsage()
        {
            Console.WriteLine("MPEG-7 Signature Parser and Comparison Tool");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  parse <file.xml>                    - Parse a single MPEG-7 signature file");
            Console.WriteLine("  compare <file1.xml> <file2.xml>     - Compare two signature files");
            Console.WriteLine("  batch <directory> [output.json]     - Process all XML files in directory");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Mpeg7SignatureParser.exe parse video1.sig.xml");
            Console.WriteLine("  Mpeg7SignatureParser.exe compare video1.sig.xml video2.sig.xml");
            Console.WriteLine("  Mpeg7SignatureParser.exe batch ./signatures results.json");
        }

        static void ParseSingleFile(string filepath)
        {
            Console.WriteLine($"Parsing file: {filepath}");
            
            var parser = new MPEG7SignatureParser();
            var signature = parser.ParseFile(filepath);

            Console.WriteLine($"\n=== SIGNATURE INFO ===");
            Console.WriteLine($"Filename: {signature.Filename}");
            Console.WriteLine($"Spatial Region: ({signature.SpatialRegion.x1}, {signature.SpatialRegion.y1}) to ({signature.SpatialRegion.x2}, {signature.SpatialRegion.y2})");
            Console.WriteLine($"Duration: {signature.Duration} time units");
            Console.WriteLine($"Time Unit: {signature.TimeUnit}");
            Console.WriteLine($"Total Frames: {signature.TotalFrames}");
            Console.WriteLine($"Frame Rate: {signature.FrameRate:F2} fps");
            Console.WriteLine($"Segments: {signature.Segments.Count}");
            Console.WriteLine($"Fingerprint: {signature.Fingerprint}");

            // Show sample frames
            Console.WriteLine($"\n=== SAMPLE FRAMES ===");
            var sampleFrames = signature.Frames.Take(5).ToList();
            foreach (var frame in sampleFrames)
            {
                Console.WriteLine($"Frame @ {frame.Timestamp}: Confidence={frame.Confidence}, Words=[{string.Join(", ", frame.Words)}]");
            }

            // Show sample segments
            Console.WriteLine($"\n=== SAMPLE SEGMENTS ===");
            var sampleSegments = signature.Segments.Take(3).ToList();
            foreach (var segment in sampleSegments)
            {
                Console.WriteLine($"Segment {segment.StartFrame}-{segment.EndFrame}: {segment.BagOfWords.Count} bags, Duration={segment.EndTime - segment.StartTime}");
            }
        }

        static void CompareTwoFiles(string file1, string file2)
        {
            Console.WriteLine($"Comparing files:");
            Console.WriteLine($"  File 1: {file1}");
            Console.WriteLine($"  File 2: {file2}");

            var parser = new MPEG7SignatureParser();
            var comparator = new SignatureComparator();

            var sig1 = parser.ParseFile(file1);
            var sig2 = parser.ParseFile(file2);

            Console.WriteLine($"\n=== FILE INFO ===");
            Console.WriteLine($"File 1: {sig1.TotalFrames} frames, {sig1.Segments.Count} segments, Duration: {sig1.Duration}");
            Console.WriteLine($"File 2: {sig2.TotalFrames} frames, {sig2.Segments.Count} segments, Duration: {sig2.Duration}");

            var result = comparator.CompareSignatures(sig1, sig2);

            Console.WriteLine($"\n=== COMPARISON RESULTS ===");
            Console.WriteLine($"Overall Similarity: {result.SimilarityScore:F3}");
            Console.WriteLine($"Segment Similarity: {result.SegmentSimilarity:F3}");
            Console.WriteLine($"Frame Similarity: {result.FrameSimilarity:F3}");
            Console.WriteLine($"Temporal Similarity: {result.TemporalSimilarity:F3}");
            Console.WriteLine($"Is Duplicate: {result.IsDuplicate}");
            Console.WriteLine($"Is Similar: {result.IsSimilar}");
            Console.WriteLine($"Comparison Time: {result.ComparisonTime.TotalMilliseconds:F1}ms");
            Console.WriteLine($"Method: {result.Method}");

            // Interpretation
            Console.WriteLine($"\n=== INTERPRETATION ===");
            if (result.IsDuplicate)
            {
                Console.WriteLine("✓ These videos appear to be duplicates or nearly identical");
            }
            else if (result.IsSimilar)
            {
                Console.WriteLine("~ These videos have significant similarities");
            }
            else if (result.SimilarityScore > 0.5)
            {
                Console.WriteLine("? These videos have some similarities");
            }
            else
            {
                Console.WriteLine("✗ These videos appear to be different");
            }
        }

        static void ProcessBatch(string directory, string outputFile)
        {
            Console.WriteLine($"Processing directory: {directory}");
            
            var processor = new BatchProcessor();
            var results = processor.ProcessDirectory(directory);

            Console.WriteLine($"\n=== BATCH PROCESSING RESULTS ===");
            var stats = results.Statistics;
            Console.WriteLine($"Total files processed: {stats.TotalFiles}");
            Console.WriteLine($"Total comparisons: {stats.TotalComparisons}");
            Console.WriteLine($"Duplicates found: {stats.DuplicatesFound}");
            Console.WriteLine($"Similar pairs found: {stats.SimilarPairsFound}");
            Console.WriteLine($"Average similarity: {stats.AverageSimilarity:F3}");
            Console.WriteLine($"Median similarity: {stats.MedianSimilarity:F3}");
            Console.WriteLine($"Similarity range: {stats.MinSimilarity:F3} - {stats.MaxSimilarity:F3}");

            // Show duplicates
            if (results.Duplicates.Any())
            {
                Console.WriteLine($"\n=== DUPLICATES DETECTED ===");
                foreach (var (file1, file2, score) in results.Duplicates)
                {
                    Console.WriteLine($"  {file1} ≈ {file2} (similarity: {score:F3})");
                }
            }

            // Show similar pairs
            if (results.SimilarPairs.Any())
            {
                Console.WriteLine($"\n=== SIMILAR PAIRS ===");
                foreach (var (file1, file2, score) in results.SimilarPairs.Take(10)) // Show top 10
                {
                    Console.WriteLine($"  {file1} ~ {file2} (similarity: {score:F3})");
                }
                
                if (results.SimilarPairs.Count > 10)
                {
                    Console.WriteLine($"  ... and {results.SimilarPairs.Count - 10} more similar pairs");
                }
            }

            // Save to JSON if requested
            if (!string.IsNullOrEmpty(outputFile))
            {
                SaveResultsToJson(results, outputFile);
                Console.WriteLine($"\nResults saved to: {outputFile}");
            }
        }

        static void SaveResultsToJson(BatchComparisonResult results, string outputFile)
        {
            // Create a simplified version for JSON serialization
            var jsonData = new
            {
                files = results.Files,
                statistics = new
                {
                    total_files = results.Statistics.TotalFiles,
                    total_comparisons = results.Statistics.TotalComparisons,
                    duplicates_found = results.Statistics.DuplicatesFound,
                    similar_pairs_found = results.Statistics.SimilarPairsFound,
                    average_similarity = Math.Round(results.Statistics.AverageSimilarity, 3),
                    median_similarity = Math.Round(results.Statistics.MedianSimilarity, 3),
                    max_similarity = Math.Round(results.Statistics.MaxSimilarity, 3),
                    min_similarity = Math.Round(results.Statistics.MinSimilarity, 3)
                },
                duplicates = results.Duplicates.Select(d => new
                {
                    file1 = d.file1,
                    file2 = d.file2,
                    similarity_score = Math.Round(d.score, 3)
                }),
                similar_pairs = results.SimilarPairs.Select(p => new
                {
                    file1 = p.file1,
                    file2 = p.file2,
                    similarity_score = Math.Round(p.score, 3)
                }),
                comparisons = results.Comparisons.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        similarity_score = Math.Round(kvp.Value.SimilarityScore, 3),
                        segment_similarity = Math.Round(kvp.Value.SegmentSimilarity, 3),
                        frame_similarity = Math.Round(kvp.Value.FrameSimilarity, 3),
                        temporal_similarity = Math.Round(kvp.Value.TemporalSimilarity, 3),
                        is_duplicate = kvp.Value.IsDuplicate,
                        is_similar = kvp.Value.IsSimilar,
                        comparison_time_ms = Math.Round(kvp.Value.ComparisonTime.TotalMilliseconds, 1),
                        method = kvp.Value.Method
                    }
                )
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(jsonData, options);
            File.WriteAllText(outputFile, json);
        }
    }
}