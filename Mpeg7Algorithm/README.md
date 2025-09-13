# MPEG-7 Video Signature C# Implementation

This directory contains a complete C# implementation of the MPEG-7 video signature algorithm and XML parser, ported from the FFmpeg source code.

## Components

### Core Algorithm
- **`Mpeg7SignatureAlgorithm.cs`** - Core MPEG-7 signature calculation algorithm
- **`Mpeg7XmlExporter.cs`** - XML export functionality for MPEG-7 format
- **`Mpeg7SignatureParser.cs`** - XML parser and signature comparison engine

### Examples
- **`Mpeg7SignatureExample.cs`** - Basic signature calculation example
- **`XmlExporterExample.cs`** - Complete example showing multi-frame XML generation
- **`Mpeg7SignatureParserExample.cs`** - Command-line parser and comparison tool

## Quick Start

### 1. Build the Project
```bash
cd Mpeg7Algorithm
dotnet build
```

### 2. Run the XML Export Example
```bash
dotnet run
```
This will:
- Generate 125 frames with different test patterns (checkerboard, stripes, gradients, etc.)
- Calculate MPEG-7 signatures for each frame
- Create coarse signatures (video segments)
- Export everything to `generated_signature.xml`
- Validate the generated XML by parsing it back
- Display statistics and perform self-comparison test

### 3. Use the Parser Tool
```bash
# Change startup object in .csproj to Mpeg7Signature.SignatureParserExample
dotnet run -- parse video1.sig.xml
dotnet run -- compare video1.sig.xml video2.sig.xml
dotnet run -- batch . results.json
```

## Algorithm Overview

The MPEG-7 signature algorithm works in these steps:

1. **Frame Preprocessing**: Convert input frame to 32×32 summed area table
2. **Block Analysis**: Process 10 element categories with predefined block patterns
3. **Signature Calculation**: Compute differences between block averages
4. **Ternary Encoding**: Convert to ternary values (0,1,2) using adaptive thresholds
5. **Word Generation**: Create 5 "words" for bag-of-words representation
6. **Frame Signature**: Generate 380-element signature encoded as ternary values

## Key Features

### Signature Components
- **Words**: 5 bytes representing most significant signature elements
- **Frame Signature**: 76 bytes (380 ternary values)
- **Confidence**: Quality metric based on signature stability
- **Coarse Signatures**: 90-frame segments for temporal analysis

### Comparison Engine
- **Multi-level analysis**: Segment (40%) + Frame (40%) + Temporal (20%)
- **Fast fingerprinting**: MD5 hash for quick duplicate detection
- **Performance optimizations**: Sampling for large datasets
- **Similarity thresholds**: >95% = duplicate, >80% = similar

### XML Format
Standard MPEG-7 VideoSignatureType with:
- Spatial region definitions
- Temporal information (time units, start/end times)
- Video segments with bag-of-words features
- Individual frame signatures with confidence scores

## Example Output

The XML export example generates signatures that look like:
```xml
<VideoFrame>
  <MediaTimeOfFrame>512</MediaTimeOfFrame>
  <FrameConfidence>255</FrameConfidence>
  <Word>121 121 121 121 121</Word>
  <FrameSignature>1 0 0 1 2 0 1 2 0 1 2 ...</FrameSignature>
</VideoFrame>
```

## Performance

- **Generation**: ~1000 frames/second on modern hardware
- **Comparison**: Sub-millisecond for fingerprint matches
- **Memory**: Efficient processing of large video files
- **Scalability**: Batch processing with sampling optimizations

## Compatibility

- Compatible with FFmpeg MPEG-7 signature filter output
- Handles XML with and without MPEG-7 namespaces
- Cross-platform .NET 8.0 implementation
- JSON export for analysis results

## Usage Scenarios

1. **Video Duplicate Detection**: Find exact and near-duplicate videos
2. **Content Similarity**: Identify related video content
3. **Video Fingerprinting**: Create compact video representations
4. **Batch Analysis**: Process large video archives
5. **Integration**: Embed in .NET applications for video analysis