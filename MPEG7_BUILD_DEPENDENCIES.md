ad# MPEG-7 Signature Build Dependencies

## Overview
This document explains the dependencies added to the minimal FFmpeg build configuration to support MPEG-7 video signature generation and comparison.

## Added Core Libraries
- `--enable-avfilter` - Required for the signature filter
- `--enable-avutil` - Core utilities needed by signature processing
- `--enable-swscale` - Video scaling/format conversion for signature analysis

## Added Protocols
- `--enable-protocol=data` - Enables data:// protocol for inline data

## Added Muxers/Demuxers
- `--enable-muxer=avi` - Support for AVI container format
- `--enable-demuxer=avi` - Support for reading AVI files
- `--enable-demuxer=concat` - Concatenation demuxer for joining video segments

## Added Parsers
- `--enable-parser=mpeg4video` - MPEG-4 video stream parsing

## Added Decoders
- `--enable-decoder=mpeg4` - MPEG-4 video decoder
- `--enable-decoder=rawvideo` - Raw video decoder for uncompressed formats

## Added Filters
- `--enable-filter=signature` - **Core MPEG-7 signature filter**
- `--enable-filter=scale` - Video scaling filter
- `--enable-filter=format` - Pixel format conversion filter  
- `--enable-filter=null` - Null filter (required for some operations)
- `--enable-filter=concat` - Video concatenation filter

## Added Encoders
- `--enable-encoder=rawvideo` - Raw video encoder for output

## Added Platform Support
- **Ubuntu 22.04** - Added complete Linux build configuration

## MPEG-7 Signature Usage

### Basic Signature Generation
```bash
ffmpeg -i input.mp4 -vf "signature=format=xml:filename=video.sig.xml" -f null -
```

### Signature Comparison (using detectmode)
```bash
ffmpeg -i video1.mp4 -i video2.mp4 \
  -filter_complex "signature=detectmode=full:format=xml:filename1=video1.sig.xml:filename2=video2.sig.xml" \
  -f null -
```

### Binary Format Output
```bash
ffmpeg -i input.mp4 -vf "signature=format=binary:filename=video.sig.bin" -f null -
```

## Key Dependencies Explained

1. **avfilter**: The signature filter is part of libavfilter
2. **swscale**: Needed for pixel format conversions during signature analysis
3. **scale filter**: Required for normalizing video dimensions for signature calculation
4. **format filter**: Ensures proper pixel format for signature processing
5. **rawvideo codec**: Provides fallback encoding/decoding capabilities
6. **Additional demuxers**: Support for various input container formats

## Testing the Build
Use the included `test_build.py` script to verify MPEG-7 signature functionality:

```bash
python test_build.py
```

This will:
1. Generate test videos
2. Create MPEG-7 signature files
3. Compare signatures between videos
4. Verify the complete workflow

## Missing Components Note
The original configuration was missing several critical components:
- Core filtering library (avfilter)
- Video scaling support (swscale) 
- Format conversion filters
- Support for common video containers (AVI)
- Raw video codec support

These additions ensure a robust minimal build capable of handling diverse input formats while maintaining the MPEG-7 signature functionality.