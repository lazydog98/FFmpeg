using System;

namespace Mpeg7Signature
{
    class Program
    {
        static void Main(string[] args)
        {
            // Example usage of the MPEG-7 signature calculator
            
            // Create a sample 32x24 grayscale frame (for demonstration)
            int width = 32;
            int height = 24;
            byte[] frameData = new byte[width * height];
            
            // Fill with sample pattern (checkerboard)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    frameData[y * width + x] = (byte)(((x + y) % 2) * 255);
                }
            }

            // Calculate signature
            var calculator = new Mpeg7SignatureCalculator();
            var signature = calculator.CalculateSignature(frameData, width, height, 0, 0);

            // Display results
            Console.WriteLine("MPEG-7 Signature Results:");
            Console.WriteLine($"Confidence: {signature.Confidence}");
            Console.WriteLine($"Words: [{string.Join(", ", signature.Words)}]");
            Console.WriteLine($"Frame signature length: {signature.FrameSig.Length}");
            
            // Show first few frame signature values
            Console.Write("Frame signature (first 10 values): [");
            for (int i = 0; i < Math.Min(10, signature.FrameSig.Length); i++)
            {
                Console.Write(signature.FrameSig[i]);
                if (i < Math.Min(9, signature.FrameSig.Length - 1)) Console.Write(", ");
            }
            Console.WriteLine("]");
        }
    }
}