#!/usr/bin/env python3
"""
Sanity check script to verify if signature files truly represent different content
"""

import hashlib

def calculate_file_hash(filepath):
    """Calculate MD5 hash of a file"""
    with open(filepath, 'rb') as f:
        return hashlib.md5(f.read()).hexdigest()

def compare_signature_files():
    """Compare the two signature files"""
    file1 = "video1.sig.xml"
    file2 = "video2.sig.xml"
    file3 = "video2_new.sig.xml"
    
    print("🔍 SIGNATURE FILE SANITY CHECK")
    print("=" * 50)
    
    # Calculate file hashes
    hash1 = calculate_file_hash(file1)
    hash2 = calculate_file_hash(file2)
    
    print(f"📄 {file1}: {hash1}")
    print(f"📄 {file2}: {hash2}")
    
    # Check new file if it exists
    import os
    if os.path.exists(file3):
        hash3 = calculate_file_hash(file3)
        print(f"📄 {file3}: {hash3}")
        
        if hash2 == hash3:
            print("⚠️  New signature file is also identical to video2.sig.xml")
        else:
            print("✅ New signature file is different!")
    print()
    
    if hash1 == hash2:
        print("❌ PROBLEM FOUND: Files are IDENTICAL!")
        print("   Both signature files have exactly the same content.")
        print("   This explains why the similarity is 100%.")
        print("   The video signatures don't actually represent different videos.")
    else:
        print("✅ Files are different - signatures represent different content")
    
    print()
    
    # Check Word values to see if they vary
    print("🎨 CHECKING COLOR SIGNATURES:")
    
    def extract_word_values(filepath):
        with open(filepath, 'r') as f:
            content = f.read()
        
        import re
        word_pattern = r'<Word>([^<]+)</Word>'
        words = re.findall(word_pattern, content)
        return set(words)  # Use set to get unique values
    
    words1 = extract_word_values(file1)
    words2 = extract_word_values(file2)
    
    print(f"📊 {file1} unique Word values: {words1}")
    print(f"📊 {file2} unique Word values: {words2}")
    
    if len(words1) == 1 and len(words2) == 1 and words1 == words2:
        word_value = list(words1)[0].strip()
        print(f"⚠️  ISSUE: Both files contain only ONE color signature: '{word_value}'")
        print(f"   Expected: video2 should have different values for blue portions")
        print(f"   Reality: Both videos appear to be the same solid color throughout")
    else:
        print("✅ Color signatures show variation as expected")

if __name__ == "__main__":
    compare_signature_files()