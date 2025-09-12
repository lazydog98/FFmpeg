import subprocess
import shutil
import os

FFMPEG = r"C:\Users\user\Downloads\ffmpeg-windows-latest\windows\static\ffmpeg.exe"  # Path to your custom ffmpeg binary

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

# Decide whether we can save MP4s
can_encode_mp4 = has_encoder("mpeg4") or has_encoder("libx264")

# 1) Create two synthetic videos or just feed lavfi directly
if can_encode_mp4:
    # Save MP4s for inspection
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "color=c=red:size=320x240:rate=25:d=5",
        "-c:v", "mpeg4",  # or libx264 if available
        "video1.mp4"
    ])
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "color=c=red:size=320x240:rate=25:d=3",
        "-f", "lavfi", "-i", "color=c=blue:size=320x240:rate=25:d=2",
        "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0",
        "-c:v", "mpeg4",
        "video2.mp4"
    ])
else:
    print("⚠ No MP4 encoder found — skipping MP4 save, using lavfi directly.")

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
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "color=c=red:size=320x240:rate=25:d=5",
        "-vf", "signature=format=xml:filename=video1.sig.xml",
        "-f", "null", "-"
    ])
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "color=c=red:size=320x240:rate=25:d=3",
        "-f", "lavfi", "-i", "color=c=blue:size=320x240:rate=25:d=2",
        "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0",
        "-vf", "signature=format=xml:filename=video2.sig.xml",
        "-f", "null", "-"
    ])

# 3) Compare the two signatures
if can_encode_mp4:
    run([
        FFMPEG, "-y",
        "-i", "video1.mp4",
        "-i", "video2.mp4",
        "-filter_complex",
        "signature_cmp=filename1=video1.sig.xml:filename2=video2.sig.xml",
        "-f", "null", "-"
    ])
else:
    # Compare using lavfi sources directly
    run([
        FFMPEG, "-y",
        "-f", "lavfi", "-i", "color=c=red:size=320x240:rate=25:d=5",
        "-f", "lavfi", "-i", "color=c=red:size=320x240:rate=25:d=3",
        "-f", "lavfi", "-i", "color=c=blue:size=320x240:rate=25:d=2",
        "-filter_complex",
        "[1:v][2:v]concat=n=2:v=1:a=0[v2];"
        "[0:v][v2]signature_cmp="
        "filename1=video1.sig.xml:"
        "filename2=video2.sig.xml",
        "-map", "[v2]",
        "-f", "null", "-"
    ])
