import re
from pathlib import Path

def convert_line_to_csharp(line):
    # Extract all coordinate pairs {x, y}
    pairs = re.findall(r"\{\s*(\d+)\s*,\s*(\d+)\s*\}", line)
    blocks = []
    for i in range(0, len(pairs), 2):
        if i + 1 < len(pairs):  # Only process complete pairs
            p1 = pairs[i]
            p2 = pairs[i + 1]
            blocks.append(f"new Block(new Point({p1[0]}, {p1[1]}), new Point({p2[0]}, {p2[1]}))")
    return ",".join(blocks) + ("," if blocks else "")

def process_file(input_path, output_path):
    input_path = Path(input_path)
    output_path = Path(output_path)

    with input_path.open("r", encoding="utf-8") as infile, \
         output_path.open("w", encoding="utf-8") as outfile:
        for line in infile:
            line = line.strip()
            if not line:
                continue  # skip empty lines
            converted = convert_line_to_csharp(line)
            outfile.write(converted + "\n")

# Example usage
if __name__ == "__main__":
    process_file("convert_arrays_input.txt", "convert_arrays_output.txt")
    print("Conversion complete. See convert_arrays_output.txt")
