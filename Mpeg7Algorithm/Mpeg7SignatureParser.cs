using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Mpeg7Signature
{
    public class VideoFrame
    {
        public long Timestamp { get; set; }
        public int Confidence { get; set; }
        public List<int> Words { get; set; } = new List<int>();
        public List<int> FrameSignature { get; set; } = new List<int>();
    }

    public class VideoSegment
    {
        public int StartFrame { get; set; }
        public int EndFrame { get; set; }
        public long StartTime { get; set; }
        public long EndTime { get; set; }
        public List<List<int>> BagOfWords { get; set; } = new List<List<int>>(); // 5 bags of 243 bits each
    }

    public class VideoSignature
    {
        public string Filename { get; set; }
        public (int x1, int y1, int x2, int y2) SpatialRegion { get; set; }
        public int TimeUnit { get; set; }
        public long StartTime { get; set; }
        public long EndTime { get; set; }
        public List<VideoSegment> Segments { get; set; } = new List<VideoSegment>();
        public List<VideoFrame> Frames { get; set; } = new List<VideoFrame>();

        // Derived properties for faster comparisons
        public int TotalFrames => Frames.Count;
        public long Duration => EndTime - StartTime;
        public double FrameRate => Duration > 0 ? TotalFrames / (Duration / (double)TimeUnit) : 0;
        
        private string _fingerprint;
        public string Fingerprint => _fingerprint ??= CalculateFingerprint();

        private string CalculateFingerprint()
        {
            var fingerprintData = new List<int>();

            // Sample from segments (use bag-of-words)
            foreach (var segment in Segments.Take(5)) // Sample first 5 segments
            {
                foreach (var bag in segment.BagOfWords)
                {
                    fingerprintData.AddRange(bag.Take(10)); // Sample first 10 words
                }
            }

            // Sample from frames
            var frameStep = Math.Max(1, Frames.Count / 10);
            for (int i = 0; i < Frames.Count; i += frameStep)
            {
                var frame = Frames[i];
                fingerprintData.AddRange(frame.Words);
                fingerprintData.AddRange(frame.FrameSignature.Take(20)); // Sample first 20 bits
            }

            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(string.Join(",", fingerprintData)));
                return Convert.ToHexString(hash).ToLower();
            }
        }
    }

    public class MPEG7SignatureParser
    {
        private readonly XNamespace _mpeg7Namespace = "urn:mpeg:mpeg7:schema:2001";

        public VideoSignature ParseFile(string filepath)
        {
            try
            {
                var doc = XDocument.Load(filepath);
                var root = doc.Root;

                // Extract video signature region info
                var spatialRegion = ExtractSpatialRegion(root);
                var timeInfo = ExtractTimeInfo(root);

                // Extract segments (coarse signatures)
                var segments = ExtractSegments(root);

                // Extract individual frames (fine signatures)
                var frames = ExtractFrames(root);

                return new VideoSignature
                {
                    Filename = Path.GetFileName(filepath),
                    SpatialRegion = spatialRegion,
                    TimeUnit = timeInfo.unit,
                    StartTime = timeInfo.start,
                    EndTime = timeInfo.end,
                    Segments = segments,
                    Frames = frames
                };
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Failed to parse {filepath}: {e.Message}", e);
            }
        }

        private (int x1, int y1, int x2, int y2) ExtractSpatialRegion(XElement root)
        {
            var pixelElements = root.Descendants()
                .Where(e => e.Name.LocalName == "Pixel")
                .ToList();

            if (pixelElements.Count >= 2)
            {
                var p1 = pixelElements[0].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse).ToArray();
                var p2 = pixelElements[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse).ToArray();
                
                return (p1[0], p1[1], p2[0], p2[1]);
            }
            
            return (0, 0, 0, 0);
        }

        private (int unit, long start, long end) ExtractTimeInfo(XElement root)
        {
            var timeUnitElem = root.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "MediaTimeUnit");
            var startTimeElem = root.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "StartMediaTimeOfSpatialRegion");
            var endTimeElem = root.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "EndMediaTimeOfSpatialRegion");

            var timeUnit = timeUnitElem != null ? int.Parse(timeUnitElem.Value) : 1;
            var startTime = startTimeElem != null ? long.Parse(startTimeElem.Value) : 0;
            var endTime = endTimeElem != null ? long.Parse(endTimeElem.Value) : 0;

            return (timeUnit, startTime, endTime);
        }

        private List<VideoSegment> ExtractSegments(XElement root)
        {
            var segments = new List<VideoSegment>();
            var segmentElements = root.Descendants()
                .Where(e => e.Name.LocalName == "VSVideoSegment");

            foreach (var segmentElem in segmentElements)
            {
                var startFrameElem = segmentElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "StartFrameOfSegment");
                var endFrameElem = segmentElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "EndFrameOfSegment");
                var startTimeElem = segmentElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "StartMediaTimeOfSegment");
                var endTimeElem = segmentElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "EndMediaTimeOfSegment");

                if (startFrameElem != null && endFrameElem != null && 
                    startTimeElem != null && endTimeElem != null)
                {
                    var startFrame = int.Parse(startFrameElem.Value);
                    var endFrame = int.Parse(endFrameElem.Value);
                    var startTime = long.Parse(startTimeElem.Value);
                    var endTime = long.Parse(endTimeElem.Value);

                    // Extract bag-of-words (5 bags per segment)
                    var bagOfWords = new List<List<int>>();
                    var bagElements = segmentElem.Descendants()
                        .Where(e => e.Name.LocalName == "BagOfWords");

                    foreach (var bagElem in bagElements)
                    {
                        var words = bagElem.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(int.Parse).ToList();
                        bagOfWords.Add(words);
                    }

                    segments.Add(new VideoSegment
                    {
                        StartFrame = startFrame,
                        EndFrame = endFrame,
                        StartTime = startTime,
                        EndTime = endTime,
                        BagOfWords = bagOfWords
                    });
                }
            }

            return segments;
        }

        private List<VideoFrame> ExtractFrames(XElement root)
        {
            var frames = new List<VideoFrame>();
            var frameElements = root.Descendants()
                .Where(e => e.Name.LocalName == "VideoFrame");

            foreach (var frameElem in frameElements)
            {
                var timestampElem = frameElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "MediaTimeOfFrame");
                var confidenceElem = frameElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "FrameConfidence");
                var wordElem = frameElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Word");
                var signatureElem = frameElem.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "FrameSignature");

                if (timestampElem != null && confidenceElem != null && 
                    wordElem != null && signatureElem != null)
                {
                    var timestamp = long.Parse(timestampElem.Value);
                    var confidence = int.Parse(confidenceElem.Value);
                    var words = wordElem.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(int.Parse).ToList();
                    var frameSignature = signatureElem.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(int.Parse).ToList();

                    frames.Add(new VideoFrame
                    {
                        Timestamp = timestamp,
                        Confidence = confidence,
                        Words = words,
                        FrameSignature = frameSignature
                    });
                }
            }

            return frames;
        }
    }

    public class SignatureComparisonResult
    {
        public double SimilarityScore { get; set; }
        public double SegmentSimilarity { get; set; }
        public double FrameSimilarity { get; set; }
        public double TemporalSimilarity { get; set; }
        public bool IsDuplicate { get; set; }
        public bool IsSimilar { get; set; }
        public TimeSpan ComparisonTime { get; set; }
        public string Method { get; set; }
    }

    public class SignatureComparator
    {
        private readonly int _wordThreshold;
        private readonly int _compositionThreshold;

        public SignatureComparator(int wordThreshold = 9000, int compositionThreshold = 60000)
        {
            _wordThreshold = wordThreshold;
            _compositionThreshold = compositionThreshold;
        }

        public SignatureComparisonResult CompareSignatures(VideoSignature sig1, VideoSignature sig2)
        {
            var startTime = DateTime.Now;

            // Quick fingerprint check for exact duplicates
            if (sig1.Fingerprint == sig2.Fingerprint)
            {
                return new SignatureComparisonResult
                {
                    SimilarityScore = 1.0,
                    SegmentSimilarity = 1.0,
                    FrameSimilarity = 1.0,
                    TemporalSimilarity = 1.0,
                    IsDuplicate = true,
                    IsSimilar = true,
                    ComparisonTime = DateTime.Now - startTime,
                    Method = "fingerprint_match"
                };
            }

            // Segment-level comparison (coarse signatures)
            var segmentSimilarity = CompareSegments(sig1.Segments, sig2.Segments);

            // Frame-level comparison (fine signatures) - sample for performance
            var frameSimilarity = CompareFramesSampled(sig1.Frames, sig2.Frames);

            // Temporal alignment analysis
            var temporalSimilarity = CompareTemporalAlignment(sig1, sig2);

            // Combined similarity score
            var combinedScore = segmentSimilarity * 0.4 + frameSimilarity * 0.4 + temporalSimilarity * 0.2;

            return new SignatureComparisonResult
            {
                SimilarityScore = combinedScore,
                SegmentSimilarity = segmentSimilarity,
                FrameSimilarity = frameSimilarity,
                TemporalSimilarity = temporalSimilarity,
                IsDuplicate = combinedScore > 0.95,
                IsSimilar = combinedScore > 0.8,
                ComparisonTime = DateTime.Now - startTime,
                Method = "multi_level_analysis"
            };
        }

        private double CompareSegments(List<VideoSegment> segments1, List<VideoSegment> segments2)
        {
            if (!segments1.Any() || !segments2.Any())
                return 0.0;

            // Sample segments for performance (use every nth segment)
            var sampleRate = Math.Max(1, Math.Max(segments1.Count, segments2.Count) / 10);
            var sampledSeg1 = segments1.Where((seg, index) => index % sampleRate == 0).ToList();
            var sampledSeg2 = segments2.Where((seg, index) => index % sampleRate == 0).ToList();

            var similarities = new List<double>();

            foreach (var seg1 in sampledSeg1)
            {
                var bestSimilarity = 0.0;
                foreach (var seg2 in sampledSeg2)
                {
                    var sim = CalculateSegmentSimilarity(seg1, seg2);
                    bestSimilarity = Math.Max(bestSimilarity, sim);
                }
                similarities.Add(bestSimilarity);
            }

            return similarities.Any() ? similarities.Average() : 0.0;
        }

        private double CalculateSegmentSimilarity(VideoSegment seg1, VideoSegment seg2)
        {
            if (seg1.BagOfWords.Count != seg2.BagOfWords.Count)
                return 0.0;

            var bagSimilarities = new List<double>();

            for (int i = 0; i < seg1.BagOfWords.Count; i++)
            {
                var bag1 = seg1.BagOfWords[i];
                var bag2 = seg2.BagOfWords[i];

                if (bag1.Count == bag2.Count)
                {
                    // Use Hamming distance for binary features
                    var hammingDist = bag1.Zip(bag2, (a, b) => Math.Abs(a - b)).Sum();
                    var similarity = 1.0 - (hammingDist / (double)bag1.Count);
                    bagSimilarities.Add(similarity);
                }
            }

            return bagSimilarities.Any() ? bagSimilarities.Average() : 0.0;
        }

        private double CompareFramesSampled(List<VideoFrame> frames1, List<VideoFrame> frames2)
        {
            if (!frames1.Any() || !frames2.Any())
                return 0.0;

            // Sample frames (use every nth frame)
            var sampleRate = Math.Max(1, Math.Max(frames1.Count, frames2.Count) / 20);
            var sampledFrames1 = frames1.Where((frame, index) => index % sampleRate == 0).ToList();
            var sampledFrames2 = frames2.Where((frame, index) => index % sampleRate == 0).ToList();

            var similarities = new List<double>();

            foreach (var frame1 in sampledFrames1)
            {
                var bestSimilarity = 0.0;
                foreach (var frame2 in sampledFrames2)
                {
                    var sim = CalculateFrameSimilarity(frame1, frame2);
                    bestSimilarity = Math.Max(bestSimilarity, sim);
                }
                similarities.Add(bestSimilarity);
            }

            return similarities.Any() ? similarities.Average() : 0.0;
        }

        private double CalculateFrameSimilarity(VideoFrame frame1, VideoFrame frame2)
        {
            // Compare word features
            var wordSimilarity = 1.0 - (frame1.Words.Zip(frame2.Words, (a, b) => Math.Abs(a - b)).Sum() / (double)frame1.Words.Count);

            // Compare frame signatures (sample for performance)
            var sigSampleSize = Math.Min(50, Math.Min(frame1.FrameSignature.Count, frame2.FrameSignature.Count));
            var sig1Sample = frame1.FrameSignature.Take(sigSampleSize).ToList();
            var sig2Sample = frame2.FrameSignature.Take(sigSampleSize).ToList();

            var signatureSimilarity = 1.0 - (sig1Sample.Zip(sig2Sample, (a, b) => Math.Abs(a - b)).Sum() / (double)sigSampleSize);

            return (wordSimilarity + signatureSimilarity) / 2;
        }

        private double CompareTemporalAlignment(VideoSignature sig1, VideoSignature sig2)
        {
            // Compare duration
            var durationRatio = Math.Min(sig1.Duration, sig2.Duration) / (double)Math.Max(sig1.Duration, sig2.Duration);
            if (Math.Max(sig1.Duration, sig2.Duration) == 0) durationRatio = 0;

            // Compare frame rates
            var frameRateRatio = Math.Min(sig1.FrameRate, sig2.FrameRate) / Math.Max(sig1.FrameRate, sig2.FrameRate);
            if (Math.Max(sig1.FrameRate, sig2.FrameRate) == 0) frameRateRatio = 0;

            // Compare total frames
            var frameCountRatio = Math.Min(sig1.TotalFrames, sig2.TotalFrames) / (double)Math.Max(sig1.TotalFrames, sig2.TotalFrames);
            if (Math.Max(sig1.TotalFrames, sig2.TotalFrames) == 0) frameCountRatio = 0;

            return (durationRatio + frameRateRatio + frameCountRatio) / 3;
        }
    }

    public class BatchComparisonResult
    {
        public List<string> Files { get; set; } = new List<string>();
        public Dictionary<string, SignatureComparisonResult> Comparisons { get; set; } = new Dictionary<string, SignatureComparisonResult>();
        public List<(string file1, string file2, double score)> Duplicates { get; set; } = new List<(string, string, double)>();
        public List<(string file1, string file2, double score)> SimilarPairs { get; set; } = new List<(string, string, double)>();
        public ComparisonStatistics Statistics { get; set; } = new ComparisonStatistics();
    }

    public class ComparisonStatistics
    {
        public int TotalFiles { get; set; }
        public int TotalComparisons { get; set; }
        public int DuplicatesFound { get; set; }
        public int SimilarPairsFound { get; set; }
        public double AverageSimilarity { get; set; }
        public double MedianSimilarity { get; set; }
        public double MaxSimilarity { get; set; }
        public double MinSimilarity { get; set; }
    }

    public class BatchProcessor
    {
        private readonly MPEG7SignatureParser _parser;
        private readonly SignatureComparator _comparator;

        public BatchProcessor()
        {
            _parser = new MPEG7SignatureParser();
            _comparator = new SignatureComparator();
        }

        public BatchComparisonResult ProcessDirectory(string directory)
        {
            var xmlFiles = Directory.GetFiles(directory, "*.xml", SearchOption.TopDirectoryOnly);
            Console.WriteLine($"Found {xmlFiles.Length} XML files to process...");

            // Parse all files
            var signatures = new Dictionary<string, VideoSignature>();
            foreach (var file in xmlFiles)
            {
                try
                {
                    var signature = _parser.ParseFile(file);
                    signatures[signature.Filename] = signature;
                    Console.WriteLine($"Parsed: {signature.Filename}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error parsing {file}: {e.Message}");
                }
            }

            // Generate comparison matrix
            Console.WriteLine("Generating comparison matrix...");
            return GenerateComparisonMatrix(signatures);
        }

        private BatchComparisonResult GenerateComparisonMatrix(Dictionary<string, VideoSignature> signatures)
        {
            var files = signatures.Keys.ToList();
            var result = new BatchComparisonResult { Files = files };

            var totalComparisons = files.Count * (files.Count - 1) / 2;
            var comparisonCount = 0;

            for (int i = 0; i < files.Count; i++)
            {
                for (int j = i + 1; j < files.Count; j++)
                {
                    var file1 = files[i];
                    var file2 = files[j];
                    var comparisonResult = _comparator.CompareSignatures(signatures[file1], signatures[file2]);

                    var key = $"{file1} vs {file2}";
                    result.Comparisons[key] = comparisonResult;

                    if (comparisonResult.IsDuplicate)
                    {
                        result.Duplicates.Add((file1, file2, comparisonResult.SimilarityScore));
                    }
                    else if (comparisonResult.IsSimilar)
                    {
                        result.SimilarPairs.Add((file1, file2, comparisonResult.SimilarityScore));
                    }

                    comparisonCount++;
                    if (comparisonCount % 100 == 0)
                    {
                        Console.WriteLine($"Progress: {comparisonCount}/{totalComparisons} comparisons completed");
                    }
                }
            }

            // Calculate statistics
            var similarities = result.Comparisons.Values.Select(c => c.SimilarityScore).ToList();
            similarities.Sort();

            result.Statistics = new ComparisonStatistics
            {
                TotalFiles = files.Count,
                TotalComparisons = totalComparisons,
                DuplicatesFound = result.Duplicates.Count,
                SimilarPairsFound = result.SimilarPairs.Count,
                AverageSimilarity = similarities.Average(),
                MedianSimilarity = similarities[similarities.Count / 2],
                MaxSimilarity = similarities.Max(),
                MinSimilarity = similarities.Min()
            };

            return result;
        }
    }
}