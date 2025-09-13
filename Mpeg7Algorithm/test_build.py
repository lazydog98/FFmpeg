import subprocess
import shutil
import os

FFMPEG = r"C:\Users\user\Downloads\ffmpeg-windows-latest (1)\windows\static\ffmpeg.exe"  # Path to your custom ffmpeg binary

def run(cmd):
    print(">>>", " ".join(cmd))
    subprocess.run(cmd, check=True)

def has_encoder(codec):
    """Check if the build has a given encoder."""
    try:
        out = subprocess.check_output([FFMPEG, "-hide_banner", "-encoders"], text=True)
        return codec in out
    except subprocess.CalledProcessError:
        return False

print("🎯 Testing MPEG-7 Signature Generation and Comparison")
print("="*60)

# Decide whether we can save MP4s
can_encode_mp4 = has_encoder("mpeg4") or has_encoder("libx264")

if can_encode_mp4:
    print("✅ MP4 encoding available")
else:
    print("⚠ No MP4 encoder found — skipping MP4 save, using lavfi directly.")

print("\n📹 Step 1: Creating test videos with patterns and shapes...")
print("Note: Using structural patterns instead of solid colors for better MPEG-7 detection")

# 1) Create two synthetic videos with different patterns/shapes
if can_encode_mp4:
    # Video 1: Horizontal stripes pattern for 5 seconds
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=5",
        "-c:v", "mpeg4",
        "video1.mp4"
    ])
    # Video 2: Horizontal stripes for 3 seconds + checkerboard pattern for 2 seconds
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=3",
        "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=2",
        "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0",
        "-c:v", "mpeg4",
        "video2.mp4"
    ])
else:
    print("⚠ No MP4 encoder found — skipping MP4 save, using lavfi directly.")

print("\n📋 Step 2: Generating MPEG-7 signatures (XML format)...")

# 2) Generate MPEG‑7 signatures (XML format)
if can_encode_mp4:
    run([
        FFMPEG, "-y",
        "-i", "video1.mp4",
        "-vf", "signature=format=xml:filename=video1.sig.xml",
        "-f", "null", "-"
    ])
    run([
        FFMPEG, "-y",
        "-i", "video2.mp4",
        "-vf", "signature=format=xml:filename=video2.sig.xml",
        "-f", "null", "-"
    ])
else:
    # Use lavfi directly with patterns
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=5",
        "-vf", "signature=format=xml:filename=video1.sig.xml",
        "-f", "null", "-"
    ])
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=3",
        "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=2",
        "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0",
        "-vf", "signature=format=xml:filename=video2.sig.xml",
        "-f", "null", "-"
    ])

print("\n🔍 Step 3: Comparing signatures using detectmode=full...")
print("Note: The signature filter will analyze both videos simultaneously")
print("and output comparison results to comparison_result.xml\n")

# 3) Compare the two signatures using detectmode
if can_encode_mp4:
    # For MP4 files, use signature filter with detectmode for comparison
    run([
        FFMPEG, "-y",
        "-i", "video1.mp4",
        "-i", "video2.mp4", 
        "-filter_complex",
        "[0:v][1:v]signature=detectmode=full:nb_inputs=2:format=xml:filename=comparison_%d.xml",
        "-f", "null", "-"
    ])
else:
    # Compare using lavfi sources directly with different patterns
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=5",
        "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=3",
        "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=2",
        "-filter_complex",
        "[1:v][2:v]concat=n=2:v=1:a=0[v2];"
        "[0:v][v2]signature=detectmode=full:nb_inputs=2:format=xml:filename=comparison_%d.xml",
        "-f", "null", "-"
    ])

print("\n✅ MPEG-7 signature test completed successfully!")
print("Generated files:")
print("  - video1.sig.xml (signature for testsrc2 pattern - horizontal stripes)")
print("  - video2.sig.xml (signature for testsrc2 + testsrc pattern - stripes + checkerboard)")
print("  - comparison_0.xml (comparison analysis for input 0)")
print("  - comparison_1.xml (comparison analysis for input 1)")
print("\n🎉 Your FFmpeg build supports MPEG-7 signatures!")
print("\n🔍 Note: Using testsrc2 (horizontal stripes) vs testsrc (checkerboard) patterns")
print("     should produce different structural signatures compared to solid colors.")
