#!/usr/bin/env python3
"""
MPEG-7 Signature Performance Comparison Test

Compares performance between custom FFmpeg build and standard FFmpeg build
for MPEG-7 signature generation across different video scenarios.
"""

import subprocess
import time
import os
import tempfile
import statistics
from pathlib import Path
from typing import List, Dict, Tuple

# FFmpeg binary paths
custom_ffmpeg = r"C:\Users\user\Downloads\ffmpeg-windows-latest (1)\windows\static\ffmpeg.exe"
standard_ffmpeg = r"C:\shortcuts\.tools\ffmpeg\ffmpeg.exe"

class PerformanceTest:
    def __init__(self):
        self.results = []
        self.temp_dir = tempfile.mkdtemp(prefix="mpeg7_perf_")
        print(f"📁 Using temporary directory: {self.temp_dir}")
    
    def verify_ffmpeg_builds(self) -> bool:
        """Verify both FFmpeg builds exist and support signature filter"""
        print("🔍 Verifying FFmpeg builds...")
        
        for name, path in [("Custom", custom_ffmpeg), ("Standard", standard_ffmpeg)]:
            if not os.path.exists(path):
                print(f"❌ {name} FFmpeg not found: {path}")
                return False
            
            try:
                # Check if signature filter is available
                result = subprocess.run(
                    [path, "-hide_banner", "-filters"], 
                    capture_output=True, text=True, timeout=10
                )
                if "signature" not in result.stdout:
                    print(f"⚠️  {name} FFmpeg may not support signature filter")
                else:
                    print(f"✅ {name} FFmpeg verified with signature filter support")
            except Exception as e:
                print(f"❌ Error checking {name} FFmpeg: {e}")
                return False
        
        return True
    
    def time_command(self, cmd: List[str], description: str) -> Tuple[float, bool]:
        """Time a command execution and return (duration, success)"""
        try:
            start_time = time.perf_counter()
            result = subprocess.run(
                cmd, 
                capture_output=True, 
                text=True, 
                timeout=120,  # 2 minute timeout
                cwd=self.temp_dir
            )
            end_time = time.perf_counter()
            
            duration = end_time - start_time
            success = result.returncode == 0
            
            if not success:
                print(f"⚠️  Command failed: {description}")
                print(f"   Error: {result.stderr[:200]}..." if result.stderr else "No error output")
            
            return duration, success
            
        except subprocess.TimeoutExpired:
            print(f"⏰ Command timed out: {description}")
            return 120.0, False
        except Exception as e:
            print(f"❌ Command error: {description} - {e}")
            return 0.0, False
    
    def test_signature_generation(self, ffmpeg_path: str, ffmpeg_name: str, 
                                test_name: str, video_input: str, duration: int = 5) -> Dict:
        """Test MPEG-7 signature generation performance"""
        output_file = f"{ffmpeg_name.lower()}_{test_name}_sig.xml"
        
        cmd = [
            ffmpeg_path, "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", f"{video_input}=size=320x240:rate=25:duration={duration}",
            "-vf", f"signature=format=xml:filename={output_file}",
            "-f", "null", "-"
        ]
        
        description = f"{ffmpeg_name} - {test_name} ({duration}s)"
        duration_time, success = self.time_command(cmd, description)
        
        # Check if output file was created and get its size
        output_path = os.path.join(self.temp_dir, output_file)
        file_size = 0
        if success and os.path.exists(output_path):
            file_size = os.path.getsize(output_path)
        
        return {
            'ffmpeg': ffmpeg_name,
            'test': test_name,
            'duration': duration_time,
            'success': success,
            'video_duration': duration,
            'output_size': file_size,
            'fps': duration / duration_time if duration_time > 0 and success else 0
        }
    
    def run_test_suite(self, runs_per_test: int = 3) -> None:
        """Run comprehensive performance test suite"""
        print(f"🚀 Starting MPEG-7 Performance Test Suite ({runs_per_test} runs per test)\n")
        
        # Define test scenarios
        test_scenarios = [
            ("testsrc_simple", "testsrc", 5),           # Simple test pattern, 5 seconds
            ("testsrc2_stripes", "testsrc2", 5),        # Horizontal stripes, 5 seconds  
            ("rgbtestsrc_color", "rgbtestsrc", 5),       # RGB color bars, 5 seconds
            ("testsrc_long", "testsrc", 30),             # Longer duration test
            ("mandelbrot", "mandelbrot", 10),            # Complex fractal pattern
        ]
        
        # Run tests for both FFmpeg builds
        for test_name, video_input, duration in test_scenarios:
            print(f"📊 Testing: {test_name} ({duration}s video)")
            
            # Test each FFmpeg build multiple times
            for ffmpeg_name, ffmpeg_path in [("Custom", custom_ffmpeg), ("Standard", standard_ffmpeg)]:
                run_times = []
                successful_runs = 0
                
                for run in range(runs_per_test):
                    result = self.test_signature_generation(
                        ffmpeg_path, ffmpeg_name, f"{test_name}_run{run+1}", 
                        video_input, duration
                    )
                    
                    if result['success']:
                        run_times.append(result['duration'])
                        successful_runs += 1
                    
                    self.results.append(result)
                
                # Calculate statistics for this test
                if run_times:
                    avg_time = statistics.mean(run_times)
                    min_time = min(run_times)
                    max_time = max(run_times)
                    std_dev = statistics.stdev(run_times) if len(run_times) > 1 else 0
                    
                    print(f"  {ffmpeg_name:8} | Avg: {avg_time:.3f}s | Min: {min_time:.3f}s | Max: {max_time:.3f}s | Std: {std_dev:.3f}s | Success: {successful_runs}/{runs_per_test}")
                else:
                    print(f"  {ffmpeg_name:8} | ❌ All runs failed")
            
            print()
    
    def analyze_results(self) -> None:
        """Analyze and display comprehensive results"""
        print("\n" + "="*80)
        print("📈 PERFORMANCE ANALYSIS RESULTS")
        print("="*80)
        
        # Group results by test scenario
        test_groups = {}
        for result in self.results:
            test_base = result['test'].rsplit('_run', 1)[0]  # Remove run number
            if test_base not in test_groups:
                test_groups[test_base] = {'Custom': [], 'Standard': []}
            
            if result['success']:
                test_groups[test_base][result['ffmpeg']].append(result['duration'])
        
        # Analyze each test group
        overall_custom_times = []
        overall_standard_times = []
        
        for test_name, data in test_groups.items():
            print(f"\n🎯 Test: {test_name}")
            print("-" * 50)
            
            custom_times = data['Custom']
            standard_times = data['Standard']
            
            if custom_times and standard_times:
                custom_avg = statistics.mean(custom_times)
                standard_avg = statistics.mean(standard_times)
                
                improvement = ((standard_avg - custom_avg) / standard_avg) * 100
                
                print(f"Custom FFmpeg   : {custom_avg:.3f}s (avg), {min(custom_times):.3f}s (min), {max(custom_times):.3f}s (max)")
                print(f"Standard FFmpeg : {standard_avg:.3f}s (avg), {min(standard_times):.3f}s (min), {max(standard_times):.3f}s (max)")
                print(f"Performance     : {improvement:+.1f}% {'🚀 faster' if improvement > 0 else '🐌 slower'} with custom build")
                
                overall_custom_times.extend(custom_times)
                overall_standard_times.extend(standard_times)
            else:
                print("⚠️  Insufficient data for comparison")
        
        # Overall performance summary
        if overall_custom_times and overall_standard_times:
            print(f"\n🏆 OVERALL PERFORMANCE SUMMARY")
            print("-" * 50)
            
            overall_custom_avg = statistics.mean(overall_custom_times)
            overall_standard_avg = statistics.mean(overall_standard_times)
            overall_improvement = ((overall_standard_avg - overall_custom_avg) / overall_standard_avg) * 100
            
            print(f"Total tests run     : {len(overall_custom_times)} per build")
            print(f"Custom build avg    : {overall_custom_avg:.3f}s per signature")
            print(f"Standard build avg  : {overall_standard_avg:.3f}s per signature")
            print(f"Overall improvement : {overall_improvement:+.1f}%")
            
            if overall_improvement > 5:
                print("🎉 Custom build shows significant performance improvement!")
            elif overall_improvement < -5:
                print("⚠️  Standard build performs better")
            else:
                print("📊 Performance is roughly equivalent")
        
        # File size analysis
        print(f"\n📁 OUTPUT FILE SIZE ANALYSIS")
        print("-" * 50)
        
        successful_results = [r for r in self.results if r['success']]
        if successful_results:
            avg_size = statistics.mean([r['output_size'] for r in successful_results])
            print(f"Average signature file size: {avg_size:.0f} bytes ({avg_size/1024:.1f} KB)")
        
        print(f"\n🧹 Temporary files location: {self.temp_dir}")
        print("   (You can manually clean this up if needed)")
    
    def cleanup(self) -> None:
        """Clean up temporary files"""
        try:
            import shutil
            shutil.rmtree(self.temp_dir)
            print(f"🧹 Cleaned up temporary directory: {self.temp_dir}")
        except Exception as e:
            print(f"⚠️  Could not clean up temporary directory: {e}")

def main():
    """Main performance test function"""
    print("🎬 MPEG-7 Signature Performance Comparison")
    print("=" * 50)
    
    test = PerformanceTest()
    
    try:
        # Verify FFmpeg builds
        if not test.verify_ffmpeg_builds():
            print("❌ FFmpeg verification failed. Please check your paths.")
            return
        
        # Run performance tests
        test.run_test_suite(runs_per_test=3)
        
        # Analyze results
        test.analyze_results()
        
    except KeyboardInterrupt:
        print("\n⏹️  Test interrupted by user")
    except Exception as e:
        print(f"\n❌ Test failed with error: {e}")
        import traceback
        traceback.print_exc()
    finally:
        # Cleanup
        test.cleanup()

if __name__ == "__main__":
    main()

