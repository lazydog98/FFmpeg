using System;
using System.Collections.Generic;
using System.Linq;

namespace Mpeg7Signature
{
    public struct Point
    {
        public byte X { get; set; }
        public byte Y { get; set; }
        
        public Point(byte x, byte y)
        {
            X = x;
            Y = y;
        }
    }

    public struct Block
    {
        public Point Up { get; set; }
        public Point To { get; set; }
        
        public Block(Point up, Point to)
        {
            Up = up;
            To = to;
        }
    }

    public class ElemCat
    {
        public bool AvElem { get; set; } // average element category
        public short LeftCount { get; set; } // count of blocks that will be added together
        public short BlockCount { get; set; } // count of blocks per element
        public short ElemCount { get; set; }
        public Block[] Blocks { get; set; }
    }

    public class FineSignature
    {
        public ulong Pts { get; set; }
        public uint Index { get; set; }
        public byte Confidence { get; set; }
        public byte[] Words { get; set; } = new byte[5];
        public byte[] FrameSig { get; set; } = new byte[76]; // SIGELEM_SIZE/5 = 380/5 = 76
    }

    public class CoarseSignature
    {
        public byte[,] Data { get; set; } = new byte[5, 31]; // 5 words with min. 243 bit
        public FineSignature First { get; set; }
        public FineSignature Last { get; set; }
    }

    public class Mpeg7SignatureCalculator
    {
        private const int ELEMENT_COUNT = 10;
        private const int SIGELEM_SIZE = 380;
        private const int DIFFELEM_SIZE = 348;
        private const long BLOCK_LCM = 476985600;

        // Ternary power lookup table
        private static readonly byte[] Pot3 = { 81, 27, 9, 3, 1 }; // 3^4, 3^3, 3^2, 3^1, 3^0
        
        // Word vector indices for bag-of-words
        private static readonly uint[] WordVec = {44,57,70,100,101,102,103,111,175,210,217,219,233,237,269,270,273,274,275,285,295,296,334,337,354};
        private static readonly byte[] S2usw = { 5,10,11, 15, 20, 21, 12, 22,  6,  0,  1,  2,  7, 13, 14,  8,  9,  3, 23, 16, 17, 24,  4, 18, 19};

        private readonly ElemCat[] _elements;

        public Mpeg7SignatureCalculator()
        {
            _elements = InitializeElements();
        }

        /// <summary>
        /// Calculate MPEG-7 signature for a grayscale frame
        /// </summary>
        /// <param name="frameData">8-bit grayscale pixel data</param>
        /// <param name="width">Frame width</param>
        /// <param name="height">Frame height</param>
        /// <param name="pts">Presentation timestamp</param>
        /// <param name="index">Frame index</param>
        /// <returns>Fine signature containing words and frame signature</returns>
        public FineSignature CalculateSignature(byte[] frameData, int width, int height, ulong pts, uint index)
        {
            var fs = new FineSignature
            {
                Pts = pts,
                Index = index
            };

            // Step 1: Create 32x32 summed area table
            var intpic = Create32x32SummedAreaTable(frameData, width, height);

            // Step 2: Calculate denominator for normalization
            int dh1 = height / 32;
            int dh2 = (height % 32 != 0) ? dh1 + 1 : dh1;
            int dw1 = width / 32;
            int dw2 = (width % 32 != 0) ? dw1 + 1 : dw1;
            
            bool divide = ((ulong)(width / 32) * (width / 32 + 1) * (height / 32) * (height / 32 + 1) > long.MaxValue / (BLOCK_LCM * 255));
            long precfactor = divide ? 65536 : BLOCK_LCM;
            long denom = divide ? 1 : dh1 * (long)dh2 * dw1 * dw2;

            // Step 3: Normalize the summed area table
            NormalizeSummedAreaTable(intpic, width, height, dh1, dh2, dw1, dw2, precfactor, denom);

            // Step 4: Process each element category
            var conflist = new List<ulong>();
            int f = 0, w = 0;
            var wordt2b = new byte[5];

            for (int i = 0; i < ELEMENT_COUNT; i++)
            {
                var elemcat = _elements[i];
                var elemsignature = new long[elemcat.ElemCount];
                var sortsignature = new ulong[elemcat.ElemCount];

                // Calculate element signatures
                for (int j = 0; j < elemcat.ElemCount; j++)
                {
                    // Calculate block sum for left blocks
                    ulong blocksum = 0;
                    int blocksize = 0;
                    
                    for (int k = 0; k < elemcat.LeftCount; k++)
                    {
                        var block = elemcat.Blocks[j * elemcat.BlockCount + k];
                        blocksum += GetBlockSum(intpic, block);
                        blocksize += GetBlockSize(block);
                    }
                    
                    long sum = (long)(blocksum / blocksize);
                    
                    if (elemcat.AvElem)
                    {
                        sum -= 128 * precfactor * denom;
                    }
                    else
                    {
                        // Calculate block sum for right blocks
                        blocksum = 0;
                        blocksize = 0;
                        
                        for (int k = elemcat.LeftCount; k < elemcat.BlockCount; k++)
                        {
                            var block = elemcat.Blocks[j * elemcat.BlockCount + k];
                            blocksum += GetBlockSum(intpic, block);
                            blocksize += GetBlockSize(block);
                        }
                        
                        sum -= (long)(blocksum / blocksize);
                        conflist.Add((ulong)Math.Abs(sum * 8 / (precfactor * denom)));
                    }

                    elemsignature[j] = sum;
                    sortsignature[j] = (ulong)Math.Abs(sum);
                }

                // Get threshold (33.3% percentile)
                Array.Sort(sortsignature);
                long th = (long)sortsignature[(int)(elemcat.ElemCount * 0.333)];

                // Ternarize and build signature
                for (int j = 0; j < elemcat.ElemCount; j++)
                {
                    byte ternary;
                    if (elemsignature[j] < -th)
                        ternary = 0;
                    else if (elemsignature[j] <= th)
                        ternary = 1;
                    else
                        ternary = 2;

                    fs.FrameSig[f / 5] += (byte)(ternary * Pot3[f % 5]);

                    // Build word signature
                    if (w < WordVec.Length && f == WordVec[w])
                    {
                        fs.Words[S2usw[w] / 5] += (byte)(ternary * Pot3[wordt2b[S2usw[w] / 5]++]);
                        w++;
                    }
                    f++;
                }
            }

            // Calculate confidence (median of confidence list)
            if (conflist.Count > 0)
            {
                conflist.Sort();
                fs.Confidence = (byte)Math.Min(conflist[conflist.Count / 2], 255);
            }

            return fs;
        }

        private ulong[,] Create32x32SummedAreaTable(byte[] frameData, int width, int height)
        {
            var intpic = new ulong[32, 32];
            var intjlut = new int[width];
            
            // Create lookup table for width mapping
            for (int i = 0; i < width; i++)
            {
                intjlut[i] = (i * 32) / width;
            }

            // Build initial sum
            for (int i = 0; i < height; i++)
            {
                int inti = (i * 32) / height;
                for (int j = 0; j < width; j++)
                {
                    int intj = intjlut[j];
                    intpic[inti, intj] += frameData[i * width + j];
                }
            }

            return intpic;
        }

        private void NormalizeSummedAreaTable(ulong[,] intpic, int width, int height, 
            int dh1, int dh2, int dw1, int dw2, long precfactor, long denom)
        {
            // Convert to summed area table with proper scaling
            for (int i = 0; i < 32; i++)
            {
                ulong rowcount = 0;
                int a = 1;
                
                if (dh2 > 1)
                {
                    int temp_a = ((height * (i + 1)) % 32 == 0) ? (height * (i + 1)) / 32 - 1 : (height * (i + 1)) / 32;
                    temp_a -= ((height * i) % 32 == 0) ? (height * i) / 32 - 1 : (height * i) / 32;
                    a = (temp_a == dh1) ? dh2 : dh1;
                }

                for (int j = 0; j < 32; j++)
                {
                    int b = 1;
                    
                    if (dw2 > 1)
                    {
                        int temp_b = ((width * (j + 1)) % 32 == 0) ? (width * (j + 1)) / 32 - 1 : (width * (j + 1)) / 32;
                        temp_b -= ((width * j) % 32 == 0) ? (width * j) / 32 - 1 : (width * j) / 32;
                        b = (temp_b == dw1) ? dw2 : dw1;
                    }

                    rowcount += intpic[i, j] * (ulong)a * (ulong)b * (ulong)precfactor / (ulong)denom;
                    
                    if (i > 0)
                        intpic[i, j] = intpic[i - 1, j] + rowcount;
                    else
                        intpic[i, j] = rowcount;
                }
            }
        }

        private static ulong GetBlockSum(ulong[,] intpic, Block block)
        {
            int x0 = block.Up.X;
            int y0 = block.Up.Y;
            int x1 = block.To.X;
            int y1 = block.To.Y;

            ulong sum;
            if (x0 - 1 >= 0 && y0 - 1 >= 0)
            {
                sum = intpic[y1, x1] + intpic[y0 - 1, x0 - 1] - intpic[y1, x0 - 1] - intpic[y0 - 1, x1];
            }
            else if (x0 - 1 >= 0)
            {
                sum = intpic[y1, x1] - intpic[y1, x0 - 1];
            }
            else if (y0 - 1 >= 0)
            {
                sum = intpic[y1, x1] - intpic[y0 - 1, x1];
            }
            else
            {
                sum = intpic[y1, x1];
            }
            
            return sum;
        }

        private static int GetBlockSize(Block block)
        {
            return (block.To.Y - block.Up.Y + 1) * (block.To.X - block.Up.X + 1);
        }

        private ElemCat[] InitializeElements()
        {
            // Initialize all element categories with their block definitions
            // This is a simplified version - you'd need to implement the full block definitions
            // from the signature.h file for production use
            
            return new ElemCat[]
            {
                CreateElemA1(), CreateElemA2(), CreateElemD1(), CreateElemD2(),
                CreateElemD3(), CreateElemD4(), CreateElemD5(), CreateElemD6(),
                CreateElemD7(), CreateElemD8()
            };
        }

        //implementation for elem_a1
        private ElemCat CreateElemA1()
        {
            var blocks = new Block[]
            {
                new Block(new Point(0, 0), new Point(7, 7)),
                new Block(new Point(8, 0), new Point(15, 7)),
                new Block(new Point(0, 8), new Point(7, 15)),
                new Block(new Point(8, 8), new Point(15, 15)),
                new Block(new Point(16, 0), new Point(23, 7)),
                new Block(new Point(24, 0), new Point(31, 7)),
                new Block(new Point(16, 8), new Point(23, 15)),
                new Block(new Point(24, 8), new Point(31, 15)),
                new Block(new Point(0, 16), new Point(7, 23)),
                new Block(new Point(8, 16), new Point(15, 23)),
                new Block(new Point(0, 24), new Point(7, 31)),
                new Block(new Point(8, 24), new Point(15, 31)),
                new Block(new Point(16, 16), new Point(23, 23)),
                new Block(new Point(24, 16), new Point(31, 23)),
                new Block(new Point(16, 24), new Point(23, 31)),
                new Block(new Point(24, 24), new Point(31, 31)),
                new Block(new Point(0, 0), new Point(15, 15)),
                new Block(new Point(16, 0), new Point(31, 15)),
                new Block(new Point(0, 16), new Point(15, 31)),
                new Block(new Point(16, 16), new Point(31, 31))
            };

            return new ElemCat
            {
                AvElem = true,
                LeftCount = 1,
                BlockCount = 1,
                ElemCount = 20,
                Blocks = blocks
            };
        }
        
        //implementation for elem_a2
        private ElemCat CreateElemA2()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(2, 2), new Point(9, 9)),
                new Block(new Point(12, 2), new Point(19, 9)),
                new Block(new Point(22, 2), new Point(29, 9)),
                new Block(new Point(2, 12), new Point(9, 19)),
                new Block(new Point(12, 12), new Point(19, 19)),
                new Block(new Point(22, 12), new Point(29, 19)),
                new Block(new Point(2, 22), new Point(9, 29)),
                new Block(new Point(12, 22), new Point(19, 29)),
                new Block(new Point(22, 22), new Point(29, 29)),
                new Block(new Point(9, 9), new Point(22, 22)),
                new Block(new Point(6, 6), new Point(25, 25)),
                new Block(new Point(3, 3), new Point(28, 28)),
                
            };

            return new ElemCat
            {
                AvElem = true,
                LeftCount = 1,
                BlockCount = 1,
                ElemCount = 12,
                Blocks = blocks
            };
        }


        //implementation for elem_d1
        private ElemCat CreateElemD1()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(0, 0), new Point(1, 3)),new Block(new Point( 2, 0),new Point( 3, 3)),
                new Block(new Point(4, 0), new Point(7, 1)),new Block(new Point(4, 2), new Point(7, 3)),
                new Block(new Point(0, 6), new Point(3, 7)),new Block(new Point(0, 4), new Point(3, 5)),
                new Block(new Point(6, 4), new Point(7, 7)),new Block(new Point(4, 4), new Point(5, 7)),
                new Block(new Point(8, 0), new Point(9, 3)),new Block(new Point(10, 0), new Point(11, 3)),
                new Block(new Point(12, 0), new Point(15, 1)),new Block(new Point(12, 2), new Point(15, 3)),
                new Block(new Point(8, 6), new Point(11, 7)),new Block(new Point(8, 4), new Point(11, 5)),
                new Block(new Point(14, 4), new Point(15, 7)),new Block(new Point(12, 4), new Point(13, 7)),
                new Block(new Point(0, 8), new Point(1, 11)),new Block(new Point(2, 8), new Point(3, 11)),
                new Block(new Point(4, 8), new Point(7, 9)),new Block(new Point(4, 10), new Point(7, 11)),
                new Block(new Point(0, 14), new Point(3, 15)),new Block(new Point(0, 12), new Point(3, 13)),
                new Block(new Point(6, 12), new Point(7, 15)),new Block(new Point(4, 12), new Point(5, 15)),
                new Block(new Point(8, 8), new Point(9, 11)),new Block(new Point(10, 8), new Point(11,11)),
                new Block(new Point(12, 8), new Point(15, 9)),new Block(new Point(12,10), new Point(15,11)),
                new Block(new Point( 8,14), new Point(11,15)),new Block(new Point( 8,12), new Point(11,13)),
                new Block(new Point(14,12), new Point(15,15)),new Block(new Point(12,12), new Point(13,15)),
                new Block(new Point(16, 0), new Point(19, 1)),new Block(new Point(16, 2), new Point(19, 3)),
                new Block(new Point(22, 0), new Point(23, 3)),new Block(new Point(20, 0), new Point(21, 3)),
                new Block(new Point(16, 4), new Point(17, 7)),new Block(new Point(18, 4), new Point(19, 7)),
                new Block(new Point(20, 6), new Point(23, 7)),new Block(new Point(20, 4), new Point(23, 5)),
                new Block(new Point(24, 0), new Point(27, 1)),new Block(new Point(24, 2), new Point(27, 3)),
                new Block(new Point(16, 0), new Point(19, 1)),new Block(new Point(16, 2), new Point(19, 3)),
                new Block(new Point(22, 0), new Point(23, 3)),new Block(new Point(20, 0), new Point(21, 3)),
                new Block(new Point(16, 4), new Point(17, 7)),new Block(new Point(18, 4), new Point(19, 7)),
                new Block(new Point(20, 6), new Point(23, 7)),new Block(new Point(20, 4), new Point(23, 5)),
                new Block(new Point(24, 0), new Point(27, 1)),new Block(new Point(24, 2), new Point(27, 3)),
                new Block(new Point(30, 0), new Point(31, 3)),new Block(new Point(28, 0), new Point(29, 3)),
                new Block(new Point(24, 4), new Point(25, 7)),new Block(new Point(26, 4), new Point(27, 7)),
                new Block(new Point(28, 6), new Point(31, 7)),new Block(new Point(28, 4), new Point(31, 5)),
                new Block(new Point(16, 8), new Point(19, 9)),new Block(new Point(16, 10), new Point(19, 11)),
                new Block(new Point(22, 8), new Point(23, 11)),new Block(new Point(20, 8), new Point(21, 11)),
                new Block(new Point(16, 12), new Point(17, 15)),new Block(new Point(18, 12), new Point(19, 15)),
                new Block(new Point(20, 14), new Point(23, 15)),new Block(new Point(20, 12), new Point(23, 13)),
                new Block(new Point(24, 8), new Point(27, 9)),new Block(new Point(24, 10), new Point(27, 11)),
                new Block(new Point(30, 8), new Point(31, 11)),new Block(new Point(28, 8), new Point(29, 11)),
                new Block(new Point(24, 12), new Point(25, 15)),new Block(new Point(26, 12), new Point(27, 15)),
                new Block(new Point(28, 14), new Point(31, 15)),new Block(new Point(28, 12), new Point(31, 13)),
                new Block(new Point(0, 16), new Point(3, 17)),new Block(new Point(0, 18), new Point(3, 19)),
                new Block(new Point(6, 16), new Point(7, 19)),new Block(new Point(4, 16), new Point(5, 19)),
                new Block(new Point(0, 20), new Point(1, 23)),new Block(new Point(2, 20), new Point(3, 23)),
                new Block(new Point(4, 22), new Point(7, 23)),new Block(new Point(4, 20), new Point(7, 21)),
                new Block(new Point(8, 16), new Point(11, 17)),new Block(new Point(8, 18), new Point(11, 19)),
                new Block(new Point(14, 16), new Point(15, 19)),new Block(new Point(12, 16), new Point(13, 19)),
                new Block(new Point(8, 20), new Point(9, 23)),new Block(new Point(10, 20), new Point(11, 23)),
                new Block(new Point(12, 22), new Point(15, 23)),new Block(new Point(12, 20), new Point(15, 21)),
                new Block(new Point(0, 24), new Point(3, 25)),new Block(new Point(0, 26), new Point(3, 27)),
                new Block(new Point(6, 24), new Point(7, 27)),new Block(new Point(4, 24), new Point(5, 27)),
                new Block(new Point(0, 28), new Point(1, 31)),new Block(new Point(2, 28), new Point(3, 31)),
                new Block(new Point(4, 30), new Point(7, 31)),new Block(new Point(4, 28), new Point(7, 29)),
                new Block(new Point(8, 24), new Point(11, 25)),new Block(new Point(8, 26), new Point(11, 27)),
                new Block(new Point(14, 24), new Point(15, 27)),new Block(new Point(12, 24), new Point(13, 27)),
                new Block(new Point(8, 28), new Point(9, 31)),new Block(new Point(10, 28), new Point(11, 31)),
                new Block(new Point(12, 30), new Point(15, 31)),new Block(new Point(12, 28), new Point(15, 29)),
                new Block(new Point(16, 16), new Point(17, 19)),new Block(new Point(18, 16), new Point(19, 19)),
                new Block(new Point(20, 16), new Point(23, 17)),new Block(new Point(20, 18), new Point(23, 19)),
                new Block(new Point(16, 22), new Point(19, 23)),new Block(new Point(16, 20), new Point(19, 21)),
                new Block(new Point(22, 20), new Point(23, 23)),new Block(new Point(20, 20), new Point(21, 23)),
                new Block(new Point(24, 16), new Point(25, 19)),new Block(new Point(26, 16), new Point(27, 19)),
                new Block(new Point(28, 16), new Point(31, 17)),new Block(new Point(28, 18), new Point(31, 19)),
                new Block(new Point(24, 22), new Point(27, 23)),new Block(new Point(24, 20), new Point(27, 21)),
                new Block(new Point(30, 20), new Point(31, 23)),new Block(new Point(28, 20), new Point(29, 23)),
                new Block(new Point(16, 24), new Point(17, 27)),new Block(new Point(18, 24), new Point(19, 27)),
                new Block(new Point(20, 24), new Point(23, 25)),new Block(new Point(20, 26), new Point(23, 27)),
                new Block(new Point(16, 30), new Point(19, 31)),new Block(new Point(16, 28), new Point(19, 29)),
                new Block(new Point(22, 28), new Point(23, 31)),new Block(new Point(20, 28), new Point(21, 31)),
                new Block(new Point(24, 24), new Point(25, 27)),new Block(new Point(26, 24), new Point(27, 27)),
                new Block(new Point(28, 24), new Point(31, 25)),new Block(new Point(28, 26), new Point(31, 27)),
                new Block(new Point(24, 30), new Point(27, 31)),new Block(new Point(24, 28), new Point(27, 29)),
                new Block(new Point(30, 28), new Point(31, 31)),new Block(new Point(28, 28), new Point(29, 31)),
                new Block(new Point(2, 2), new Point(3, 5)),new Block(new Point(4, 2), new Point(5, 5)),
                new Block(new Point(6, 2), new Point(9, 3)),new Block(new Point(6, 4), new Point(9, 5)),
                new Block(new Point(2, 8), new Point(5, 9)),new Block(new Point(2, 6), new Point(5, 7)),
                new Block(new Point(8, 6), new Point(9, 9)),new Block(new Point(6, 6), new Point(7, 9)),
                new Block(new Point(12, 2), new Point(13, 5)),new Block(new Point(14, 2), new Point(15, 5)),
                new Block(new Point(16, 2), new Point(19, 3)),new Block(new Point(16, 4), new Point(19, 5)),
                new Block(new Point(12, 8), new Point(15, 9)),new Block(new Point(12, 6), new Point(15, 7)),
                new Block(new Point(18, 6), new Point(19, 9)),new Block(new Point(16, 6), new Point(17, 9)),
                new Block(new Point(22, 2), new Point(23, 5)),new Block(new Point(24, 2), new Point(25, 5)),
                new Block(new Point(26, 2), new Point(29, 3)),new Block(new Point(26, 4), new Point(29, 5)),
                new Block(new Point(22, 8), new Point(25, 9)),new Block(new Point(22, 6), new Point(25, 7)),
                new Block(new Point(28, 6), new Point(29, 9)),new Block(new Point(26, 6), new Point(27, 9)),
                new Block(new Point(2, 12), new Point(3, 15)),new Block(new Point(4, 12), new Point(5, 15)),
                new Block(new Point(6, 12), new Point(9, 13)),new Block(new Point(6, 14), new Point(9, 15)),
                new Block(new Point(2, 18), new Point(5, 19)),new Block(new Point(2, 16), new Point(5, 17)),
                new Block(new Point(8, 16), new Point(9, 19)),new Block(new Point(6, 16), new Point(7, 19)),
                new Block(new Point(12, 12), new Point(15, 13)),new Block(new Point(12, 14), new Point(15, 15)),
                new Block(new Point(16, 12), new Point(19, 13)),new Block(new Point(16, 14), new Point(19, 15)),
                new Block(new Point(12, 18), new Point(15, 19)),new Block(new Point(12, 16), new Point(15, 17)),
                new Block(new Point(16, 18), new Point(19, 19)),new Block(new Point(16, 16), new Point(19, 17)),
                new Block(new Point(22, 12), new Point(23, 15)),new Block(new Point(24, 12), new Point(25, 15)),
                new Block(new Point(26, 12), new Point(29, 13)),new Block(new Point(26, 14), new Point(29, 15)),
                new Block(new Point(22, 18), new Point(25, 19)),new Block(new Point(22, 16), new Point(25, 17)),
                new Block(new Point(28, 16), new Point(29, 19)),new Block(new Point(26, 16), new Point(27, 19)),
                new Block(new Point(2, 22), new Point(3, 25)),new Block(new Point(4, 22), new Point(5, 25)),
                new Block(new Point(6, 22), new Point(9, 23)),new Block(new Point(6, 24), new Point(9, 25)),
                new Block(new Point(2, 28), new Point(5, 29)),new Block(new Point(2, 26), new Point(5, 27)),
                new Block(new Point(8, 26), new Point(9, 29)),new Block(new Point(6, 26), new Point(7, 29)),
                new Block(new Point(12, 22), new Point(13, 25)),new Block(new Point(14, 22), new Point(15, 25)),
                new Block(new Point(16, 22), new Point(19, 23)),new Block(new Point(16, 24), new Point(19, 25)),
                new Block(new Point(12, 28), new Point(15, 29)),new Block(new Point(12, 26), new Point(15, 27)),
                new Block(new Point(18, 26), new Point(19, 29)),new Block(new Point(16, 26), new Point(17, 29)),
                new Block(new Point(22, 22), new Point(23, 25)),new Block(new Point(24, 22), new Point(25, 25)),
                new Block(new Point(26, 22), new Point(29, 23)),new Block(new Point(26, 24), new Point(29, 25)),
                new Block(new Point(22, 28), new Point(25, 29)),new Block(new Point(22, 26), new Point(25, 27)),
                new Block(new Point(28, 26), new Point(29, 29)),new Block(new Point(26, 26), new Point(27, 29)),
                new Block(new Point(7, 7), new Point(10, 8)),new Block(new Point(7, 9), new Point(10, 10)),
                new Block(new Point(11, 7), new Point(12, 10)),new Block(new Point(13, 7), new Point(14, 10)),
                new Block(new Point(7, 11), new Point(8, 14)),new Block(new Point(9, 11), new Point(10, 14)),
                new Block(new Point(11, 11), new Point(14, 12)),new Block(new Point(11, 13), new Point(14, 14)),
                new Block(new Point(17, 7), new Point(20, 8)),new Block(new Point(17, 9), new Point(20, 10)),
                new Block(new Point(21, 7), new Point(22, 10)),new Block(new Point(23, 7), new Point(24, 10)),
                new Block(new Point(17, 11), new Point(18, 14)),new Block(new Point(19, 11), new Point(20, 14)),
                new Block(new Point(21, 11), new Point(24, 12)),new Block(new Point(21, 13), new Point(24, 14)),
                new Block(new Point(7, 17), new Point(10, 18)),new Block(new Point(7, 19), new Point(10, 20)),
                new Block(new Point(11, 17), new Point(12, 20)),new Block(new Point(13, 17), new Point(14, 20)),
                new Block(new Point(7, 21), new Point(8, 24)),new Block(new Point(9, 21), new Point(10, 24)),
                new Block(new Point(11, 21), new Point(14, 22)),new Block(new Point(11, 23), new Point(14, 24)),
                new Block(new Point(17, 17), new Point(20, 18)),new Block(new Point(17, 19), new Point(20, 20)),
                new Block(new Point(21, 17), new Point(22, 20)),new Block(new Point(23, 17), new Point(24, 20)),
                new Block(new Point(17, 21), new Point(18, 24)),new Block(new Point(19, 21), new Point(20, 24)),
                new Block(new Point(21, 21), new Point(24, 22)),new Block(new Point(21, 23), new Point(24, 24)),

                
            };

            return new ElemCat
            { //0, 1, 2, 116,
                AvElem = false,
                LeftCount = 1,
                BlockCount = 2,
                ElemCount = 116,
                Blocks = blocks
            };
        }



        //implementation for elem_d2
        private ElemCat CreateElemD2()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(0, 0), new Point(3, 3)),new Block(new Point(4, 4), new Point(7, 7)),new Block(new Point(4, 0), new Point(7, 3)),new Block(new Point(0, 4), new Point(3, 7)),
                new Block(new Point(8, 0), new Point(11, 3)),new Block(new Point(12, 4), new Point(15, 7)),new Block(new Point(12, 0), new Point(15, 3)),new Block(new Point(8, 4), new Point(11, 7)),
                new Block(new Point(16, 0), new Point(19, 3)),new Block(new Point(20, 4), new Point(23, 7)),new Block(new Point(20, 0), new Point(23, 3)),new Block(new Point(16, 4), new Point(19, 7)),
                new Block(new Point(24, 0), new Point(27, 3)),new Block(new Point(28, 4), new Point(31, 7)),new Block(new Point(28, 0), new Point(31, 3)),new Block(new Point(24, 4), new Point(27, 7)),
                new Block(new Point(0, 8), new Point(3, 11)),new Block(new Point(4, 12), new Point(7, 15)),new Block(new Point(4, 8), new Point(7, 11)),new Block(new Point(0, 12), new Point(3, 15)),
                new Block(new Point(8, 8), new Point(11, 11)),new Block(new Point(12, 12), new Point(15, 15)),new Block(new Point(12, 8), new Point(15, 11)),new Block(new Point(8, 12), new Point(11, 15)),
                new Block(new Point(16, 8), new Point(19, 11)),new Block(new Point(20, 12), new Point(23, 15)),new Block(new Point(20, 8), new Point(23, 11)),new Block(new Point(16, 12), new Point(19, 15)),
                new Block(new Point(24, 8), new Point(27, 11)),new Block(new Point(28, 12), new Point(31, 15)),new Block(new Point(28, 8), new Point(31, 11)),new Block(new Point(24, 12), new Point(27, 15)),
                new Block(new Point(0, 16), new Point(3, 19)),new Block(new Point(4, 20), new Point(7, 23)),new Block(new Point(4, 16), new Point(7, 19)),new Block(new Point(0, 20), new Point(3, 23)),
                new Block(new Point(8, 16), new Point(11, 19)),new Block(new Point(12, 20), new Point(15, 23)),new Block(new Point(12, 16), new Point(15, 19)),new Block(new Point(8, 20), new Point(11, 23)),
                new Block(new Point(16, 16), new Point(19, 19)),new Block(new Point(20, 20), new Point(23, 23)),new Block(new Point(20, 16), new Point(23, 19)),new Block(new Point(16, 20), new Point(19, 23)),
                new Block(new Point(24, 16), new Point(27, 19)),new Block(new Point(28, 20), new Point(31, 23)),new Block(new Point(28, 16), new Point(31, 19)),new Block(new Point(24, 20), new Point(27, 23)),
                new Block(new Point(0, 24), new Point(3, 27)),new Block(new Point(4, 28), new Point(7, 31)),new Block(new Point(4, 24), new Point(7, 27)),new Block(new Point(0, 28), new Point(3, 31)),
                new Block(new Point(8, 24), new Point(11, 27)),new Block(new Point(12, 28), new Point(15, 31)),new Block(new Point(12, 24), new Point(15, 27)),new Block(new Point(8, 28), new Point(11, 31)),
                new Block(new Point(16, 24), new Point(19, 27)),new Block(new Point(20, 28), new Point(23, 31)),new Block(new Point(20, 24), new Point(23, 27)),new Block(new Point(16, 28), new Point(19, 31)),
                new Block(new Point(24, 24), new Point(27, 27)),new Block(new Point(28, 28), new Point(31, 31)),new Block(new Point(28, 24), new Point(31, 27)),new Block(new Point(24, 28), new Point(27, 31)),
                new Block(new Point(4, 4), new Point(7, 7)),new Block(new Point(8, 8), new Point(11, 11)),new Block(new Point(8, 4), new Point(11, 7)),new Block(new Point(4, 8), new Point(7, 11)),
                new Block(new Point(12, 4), new Point(15, 7)),new Block(new Point(16, 8), new Point(19, 11)),new Block(new Point(16, 4), new Point(19, 7)),new Block(new Point(12, 8), new Point(15, 11)),
                new Block(new Point(20, 4), new Point(23, 7)),new Block(new Point(24, 8), new Point(27, 11)),new Block(new Point(24, 4), new Point(27, 7)),new Block(new Point(20, 8), new Point(23, 11)),
                new Block(new Point(4, 12), new Point(7, 15)),new Block(new Point(8, 16), new Point(11, 19)),new Block(new Point(8, 12), new Point(11, 15)),new Block(new Point(4, 16), new Point(7, 19)),
                new Block(new Point(12, 12), new Point(15, 15)),new Block(new Point(16, 16), new Point(19, 19)),new Block(new Point(16, 12), new Point(19, 15)),new Block(new Point(12, 16), new Point(15, 19)),
                new Block(new Point(20, 12), new Point(23, 15)),new Block(new Point(24, 16), new Point(27, 19)),new Block(new Point(24, 12), new Point(27, 15)),new Block(new Point(20, 16), new Point(23, 19)),
                new Block(new Point(4, 20), new Point(7, 23)),new Block(new Point(8, 24), new Point(11, 27)),new Block(new Point(8, 20), new Point(11, 23)),new Block(new Point(4, 24), new Point(7, 27)),
                new Block(new Point(12, 20), new Point(15, 23)),new Block(new Point(16, 24), new Point(19, 27)),new Block(new Point(16, 20), new Point(19, 23)),new Block(new Point(12, 24), new Point(15, 27)),
                new Block(new Point(20, 20), new Point(23, 23)),new Block(new Point(24, 24), new Point(27, 27)),new Block(new Point(24, 20), new Point(27, 23)),new Block(new Point(20, 24), new Point(23, 27)),

                
            };

            return new ElemCat
            {  //0, 2, 4, 25
                AvElem = false,
                LeftCount = 2,
                BlockCount = 4,
                ElemCount = 25,
                Blocks = blocks
            };
        }



        //implementation for elem_d3
        private ElemCat CreateElemD3()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(1, 1), new Point(10, 10)),new Block(new Point(11, 1), new Point(20, 10)),
                new Block(new Point(1, 1), new Point(10, 10)),new Block(new Point(21, 1), new Point(30, 10)),
                new Block(new Point(1, 1), new Point(10, 10)),new Block(new Point(1, 11), new Point(10, 20)),
                new Block(new Point(1, 1), new Point(10, 10)),new Block(new Point(11, 11), new Point(20, 20)),
                new Block(new Point(1, 1), new Point(10, 10)),new Block(new Point(21, 11), new Point(30, 20)),
                new Block(new Point(1, 1), new Point(10, 10)),new Block(new Point(1, 21), new Point(10, 30)),
                new Block(new Point(1, 1), new Point(10, 10)),new Block(new Point(11, 21), new Point(20, 30)),
                new Block(new Point(1, 1), new Point(10, 10)),new Block(new Point(21, 21), new Point(30, 30)),
                new Block(new Point(11, 1), new Point(20, 10)),new Block(new Point(21, 1), new Point(30, 10)),
                new Block(new Point(11, 1), new Point(20, 10)),new Block(new Point(1, 11), new Point(10, 20)),
                new Block(new Point(11, 1), new Point(20, 10)),new Block(new Point(11, 11), new Point(20, 20)),
                new Block(new Point(11, 1), new Point(20, 10)),new Block(new Point(21, 11), new Point(30, 20)),
                new Block(new Point(11, 1), new Point(20, 10)),new Block(new Point(1, 21), new Point(10, 30)),
                new Block(new Point(11, 1), new Point(20, 10)),new Block(new Point(11, 21), new Point(20, 30)),
                new Block(new Point(11, 1), new Point(20, 10)),new Block(new Point(21, 21), new Point(30, 30)),
                new Block(new Point(21, 1), new Point(30, 10)),new Block(new Point(1, 11), new Point(10, 20)),
                new Block(new Point(21, 1), new Point(30, 10)),new Block(new Point(11, 11), new Point(20, 20)),
                new Block(new Point(21, 1), new Point(30, 10)),new Block(new Point(21, 11), new Point(30, 20)),
                new Block(new Point(21, 1), new Point(30, 10)),new Block(new Point(1, 21), new Point(10, 30)),
                new Block(new Point(21, 1), new Point(30, 10)),new Block(new Point(11, 21), new Point(20, 30)),
                new Block(new Point(21, 1), new Point(30, 10)),new Block(new Point(21, 21), new Point(30, 30)),
                new Block(new Point(1, 11), new Point(10, 20)),new Block(new Point(11, 11), new Point(20, 20)),
                new Block(new Point(1, 11), new Point(10, 20)),new Block(new Point(21, 11), new Point(30, 20)),
                new Block(new Point(1, 11), new Point(10, 20)),new Block(new Point(1, 21), new Point(10, 30)),
                new Block(new Point(1, 11), new Point(10, 20)),new Block(new Point(11, 21), new Point(20, 30)),
                new Block(new Point(1, 11), new Point(10, 20)),new Block(new Point(21, 21), new Point(30, 30)),
                new Block(new Point(11, 11), new Point(20, 20)),new Block(new Point(21, 11), new Point(30, 20)),
                new Block(new Point(11, 11), new Point(20, 20)),new Block(new Point(1, 21), new Point(10, 30)),
                new Block(new Point(11, 11), new Point(20, 20)),new Block(new Point(11, 21), new Point(20, 30)),
                new Block(new Point(11, 11), new Point(20, 20)),new Block(new Point(21, 21), new Point(30, 30)),
                new Block(new Point(21, 11), new Point(30, 20)),new Block(new Point(1, 21), new Point(10, 30)),
                new Block(new Point(21, 11), new Point(30, 20)),new Block(new Point(11, 21), new Point(20, 30)),
                new Block(new Point(21, 11), new Point(30, 20)),new Block(new Point(21, 21), new Point(30, 30)),
                new Block(new Point(1, 21), new Point(10, 30)),new Block(new Point(11, 21), new Point(20, 30)),
                new Block(new Point(1, 21), new Point(10, 30)),new Block(new Point(21, 21), new Point(30, 30)),
                new Block(new Point(11, 21), new Point(20, 30)),new Block(new Point(21, 21), new Point(30, 30)),

                
            };

            return new ElemCat
            {  // 0, 1, 2, 36
                AvElem = false,
                LeftCount = 1,
                BlockCount = 2,
                ElemCount = 36,
                Blocks = blocks
            };
        }


        //implementation for elem_d4
        private ElemCat CreateElemD4()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(7, 13), new Point(12, 18)),new Block(new Point(19, 13), new Point(24, 18)),
            new Block(new Point(13, 7), new Point(18, 12)),new Block(new Point(13, 19), new Point(18, 24)),
            new Block(new Point(7, 7), new Point(12, 12)),new Block(new Point(19, 19), new Point(24, 24)),
            new Block(new Point(19, 7), new Point(24, 12)),new Block(new Point(7, 19), new Point(12, 24)),
            new Block(new Point(13, 7), new Point(18, 12)),new Block(new Point(19, 13), new Point(24, 18)),
            new Block(new Point(19, 13), new Point(24, 18)),new Block(new Point(13, 19), new Point(18, 24)),
            new Block(new Point(13, 19), new Point(18, 24)),new Block(new Point(7, 13), new Point(12, 18)),
            new Block(new Point(7, 13), new Point(12, 18)),new Block(new Point(13, 7), new Point(18, 12)),
            new Block(new Point(7, 7), new Point(12, 12)),new Block(new Point(19, 7), new Point(24, 12)),
            new Block(new Point(19, 7), new Point(24, 12)),new Block(new Point(19, 19), new Point(24, 24)),
            new Block(new Point(19, 19), new Point(24, 24)),new Block(new Point(7, 19), new Point(12, 24)),
            new Block(new Point(7, 19), new Point(12, 24)),new Block(new Point(7, 7), new Point(12, 12)),
            new Block(new Point(13, 13), new Point(18, 18)),new Block(new Point(13, 1), new Point(18, 6)),
            new Block(new Point(13, 13), new Point(18, 18)),new Block(new Point(25, 13), new Point(30, 18)),
            new Block(new Point(13, 13), new Point(18, 18)),new Block(new Point(13, 25), new Point(18, 30)),
            new Block(new Point(13, 13), new Point(18, 18)),new Block(new Point(1, 13), new Point(6, 18)),
            new Block(new Point(13, 1), new Point(18, 6)),new Block(new Point(13, 25), new Point(18, 30)),
            new Block(new Point(1, 13), new Point(6, 18)),new Block(new Point(25, 13), new Point(30, 18)),
            new Block(new Point(7, 1), new Point(12, 6)),new Block(new Point(19, 1), new Point(24, 6)),
            new Block(new Point(7, 25), new Point(12, 30)),new Block(new Point(19, 25), new Point(24, 30)),
            new Block(new Point(1, 7), new Point(6, 12)),new Block(new Point(1, 19), new Point(6, 24)),
            new Block(new Point(25, 7), new Point(30, 12)),new Block(new Point(25, 19), new Point(30, 24)),
            new Block(new Point(7, 1), new Point(12, 6)),new Block(new Point(1, 7), new Point(6, 12)),
            new Block(new Point(19, 1), new Point(24, 6)),new Block(new Point(25, 7), new Point(30, 12)),
            new Block(new Point(25, 19), new Point(30, 24)),new Block(new Point(19, 25), new Point(24, 30)),
            new Block(new Point(1, 19), new Point(6, 24)),new Block(new Point(7, 25), new Point(12, 30)),
            new Block(new Point(1, 1), new Point(6, 6)),new Block(new Point(25, 1), new Point(30, 6)),
            new Block(new Point(25, 1), new Point(30, 6)),new Block(new Point(25, 25), new Point(30, 30)),
            new Block(new Point(25, 25), new Point(30, 30)),new Block(new Point(1, 25), new Point(6, 30)),
            new Block(new Point(1, 25), new Point(6, 30)),new Block(new Point(1, 1), new Point(6, 6)),

                
            };

            return new ElemCat
            {  // 0, 1, 2, 30
                AvElem = false,
                LeftCount = 1,
                BlockCount = 2,
                ElemCount = 30,
                Blocks = blocks
            };
        }


        //implementation for elem_d5
        private ElemCat CreateElemD5()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(1, 1), new Point(10, 3)),new Block(new Point(1, 4), new Point(3, 7)),new Block(new Point(8, 4), new Point(10, 7)),new Block(new Point(1, 8), new Point(10, 10)),new Block(new Point(4, 4), new Point(7, 7)),
            new Block(new Point(11, 1), new Point(20, 3)),new Block(new Point(11, 4), new Point(13, 7)),new Block(new Point(18, 4), new Point(20, 7)),new Block(new Point(11, 8), new Point(20, 10)),new Block(new Point(14, 4), new Point(17, 7)),
            new Block(new Point(21, 1), new Point(30, 3)),new Block(new Point(21, 4), new Point(23, 7)),new Block(new Point(28, 4), new Point(30, 7)),new Block(new Point(21, 8), new Point(30, 10)),new Block(new Point(24, 4), new Point(27, 7)),
            new Block(new Point(1, 11), new Point(10, 13)),new Block(new Point(1, 14), new Point(3, 17)),new Block(new Point(8, 14), new Point(10, 17)),new Block(new Point(1, 18), new Point(10, 20)),new Block(new Point(4, 14), new Point(7, 17)),
            new Block(new Point(11, 11), new Point(20, 13)),new Block(new Point(11, 14), new Point(13, 17)),new Block(new Point(18, 14), new Point(20, 17)),new Block(new Point(11, 18), new Point(20, 20)),new Block(new Point(14, 14), new Point(17, 17)),
            new Block(new Point(21, 11), new Point(30, 13)),new Block(new Point(21, 14), new Point(23, 17)),new Block(new Point(28, 14), new Point(30, 17)),new Block(new Point(21, 18), new Point(30, 20)),new Block(new Point(24, 14), new Point(27, 17)),
            new Block(new Point(1, 21), new Point(10, 23)),new Block(new Point(1, 24), new Point(3, 27)),new Block(new Point(8, 24), new Point(10, 27)),new Block(new Point(1, 28), new Point(10, 30)),new Block(new Point(4, 24), new Point(7, 27)),
            new Block(new Point(11, 21), new Point(20, 23)),new Block(new Point(11, 24), new Point(13, 27)),new Block(new Point(18, 24), new Point(20, 27)),new Block(new Point(11, 28), new Point(20, 30)),new Block(new Point(14, 24), new Point(17, 27)),
            new Block(new Point(21, 21), new Point(30, 23)),new Block(new Point(21, 24), new Point(23, 27)),new Block(new Point(28, 24), new Point(30, 27)),new Block(new Point(21, 28), new Point(30, 30)),new Block(new Point(24, 24), new Point(27, 27)),
            new Block(new Point(6, 6), new Point(15, 8)),new Block(new Point(6, 9), new Point(8, 12)),new Block(new Point(13, 9), new Point(15, 12)),new Block(new Point(6, 13), new Point(15, 15)),new Block(new Point(9, 9), new Point(12, 12)),
            new Block(new Point(16, 6), new Point(25, 8)),new Block(new Point(16, 9), new Point(18, 12)),new Block(new Point(23, 9), new Point(25, 12)),new Block(new Point(16, 13), new Point(25, 15)),new Block(new Point(19, 9), new Point(22, 12)),
            new Block(new Point(6, 16), new Point(15, 18)),new Block(new Point(6, 19), new Point(8, 22)),new Block(new Point(13, 19), new Point(15, 22)),new Block(new Point(6, 23), new Point(15, 25)),new Block(new Point(9, 19), new Point(12, 22)),
            new Block(new Point(16, 16), new Point(25, 18)),new Block(new Point(16, 19), new Point(18, 22)),new Block(new Point(23, 19), new Point(25, 22)),new Block(new Point(16, 23), new Point(25, 25)),new Block(new Point(19, 19), new Point(22, 22)),
            new Block(new Point(6, 1), new Point(15, 3)),new Block(new Point(6, 4), new Point(8, 7)),new Block(new Point(13, 4), new Point(15, 7)),new Block(new Point(6, 8), new Point(15, 10)),new Block(new Point(9, 4), new Point(12, 7)),
            new Block(new Point(16, 1), new Point(25, 3)),new Block(new Point(16, 4), new Point(18, 7)),new Block(new Point(23, 4), new Point(25, 7)),new Block(new Point(16, 8), new Point(25, 10)),new Block(new Point(19, 4), new Point(22, 7)),
            new Block(new Point(1, 6), new Point(10, 8)),new Block(new Point(1, 9), new Point(3, 12)),new Block(new Point(8, 9), new Point(10, 12)),new Block(new Point(1, 13), new Point(10, 15)),new Block(new Point(4, 9), new Point(7, 12)),
            new Block(new Point(11, 6), new Point(20, 8)),new Block(new Point(11, 9), new Point(13, 12)),new Block(new Point(18, 9), new Point(20, 12)),new Block(new Point(11, 13), new Point(20, 15)),new Block(new Point(14, 9), new Point(17, 12)),
            new Block(new Point(21, 6), new Point(30, 8)),new Block(new Point(21, 9), new Point(23, 12)),new Block(new Point(28, 9), new Point(30, 12)),new Block(new Point(21, 13), new Point(30, 15)),new Block(new Point(24, 9), new Point(27, 12)),
            new Block(new Point(6, 11), new Point(15, 13)),new Block(new Point(6, 14), new Point(8, 17)),new Block(new Point(13, 14), new Point(15, 17)),new Block(new Point(6, 18), new Point(15, 20)),new Block(new Point(9, 14), new Point(12, 17)),
            new Block(new Point(16, 11), new Point(25, 13)),new Block(new Point(16, 14), new Point(18, 17)),new Block(new Point(23, 14), new Point(25, 17)),new Block(new Point(16, 18), new Point(25, 20)),new Block(new Point(19, 14), new Point(22, 17)),
            new Block(new Point(1, 16), new Point(10, 18)),new Block(new Point(1, 19), new Point(3, 22)),new Block(new Point(8, 19), new Point(10, 22)),new Block(new Point(1, 23), new Point(10, 25)),new Block(new Point(4, 19), new Point(7, 22)),
            new Block(new Point(11, 16), new Point(20, 18)),new Block(new Point(11, 19), new Point(13, 22)),new Block(new Point(18, 19), new Point(20, 22)),new Block(new Point(11, 23), new Point(20, 25)),new Block(new Point(14, 19), new Point(17, 22)),
            new Block(new Point(21, 16), new Point(30, 18)),new Block(new Point(21, 19), new Point(23, 22)),new Block(new Point(28, 19), new Point(30, 22)),new Block(new Point(21, 23), new Point(30, 25)),new Block(new Point(24, 19), new Point(27, 22)),
            new Block(new Point(6, 21), new Point(15, 23)),new Block(new Point(6, 24), new Point(8, 27)),new Block(new Point(13, 24), new Point(15, 27)),new Block(new Point(6, 28), new Point(15, 30)),new Block(new Point(9, 24), new Point(12, 27)),
            new Block(new Point(16, 21), new Point(25, 23)),new Block(new Point(16, 24), new Point(18, 27)),new Block(new Point(23, 24), new Point(25, 27)),new Block(new Point(16, 28), new Point(25, 30)),new Block(new Point(19, 24), new Point(22, 27)),
            new Block(new Point(2, 2), new Point(14, 6)),new Block(new Point(2, 7), new Point(6, 9)),new Block(new Point(10, 7), new Point(14, 9)),new Block(new Point(2, 10), new Point(14, 14)),new Block(new Point(7, 7), new Point(9, 9)),
            new Block(new Point(7, 2), new Point(19, 6)),new Block(new Point(7, 7), new Point(11, 9)),new Block(new Point(15, 7), new Point(19, 9)),new Block(new Point(7, 10), new Point(19, 14)),new Block(new Point(12, 7), new Point(14, 9)),
            new Block(new Point(12, 2), new Point(24, 6)),new Block(new Point(12, 7), new Point(16, 9)),new Block(new Point(20, 7), new Point(24, 9)),new Block(new Point(12, 10), new Point(24, 14)),new Block(new Point(17, 7), new Point(19, 9)),
            new Block(new Point(17, 2), new Point(29, 6)),new Block(new Point(17, 7), new Point(21, 9)),new Block(new Point(25, 7), new Point(29, 9)),new Block(new Point(17, 10), new Point(29, 14)),new Block(new Point(22, 7), new Point(24, 9)),
            new Block(new Point(2, 7), new Point(14, 11)),new Block(new Point(2, 12), new Point(6, 14)),new Block(new Point(10, 12), new Point(14, 14)),new Block(new Point(2, 15), new Point(14, 19)),new Block(new Point(7, 12), new Point(9, 14)),
            new Block(new Point(7, 7), new Point(19, 11)),new Block(new Point(7, 12), new Point(11, 14)),new Block(new Point(15, 12), new Point(19, 14)),new Block(new Point(7, 15), new Point(19, 19)),new Block(new Point(12, 12), new Point(14, 14)),
            new Block(new Point(12, 7), new Point(24, 11)),new Block(new Point(12, 12), new Point(16, 14)),new Block(new Point(20, 12), new Point(24, 14)),new Block(new Point(12, 15), new Point(24, 19)),new Block(new Point(17, 12), new Point(19, 14)),
            new Block(new Point(17, 7), new Point(29, 11)),new Block(new Point(17, 12), new Point(21, 14)),new Block(new Point(25, 12), new Point(29, 14)),new Block(new Point(17, 15), new Point(29, 19)),new Block(new Point(22, 12), new Point(24, 14)),
            new Block(new Point(2, 12), new Point(14, 16)),new Block(new Point(2, 17), new Point(6, 19)),new Block(new Point(10, 17), new Point(14, 19)),new Block(new Point(2, 20), new Point(14, 24)),new Block(new Point(7, 17), new Point(9, 19)),
            new Block(new Point(7, 12), new Point(19, 16)),new Block(new Point(7, 17), new Point(11, 19)),new Block(new Point(15, 17), new Point(19, 19)),new Block(new Point(7, 20), new Point(19, 24)),new Block(new Point(12, 17), new Point(14, 19)),
            new Block(new Point(12, 12), new Point(24, 16)),new Block(new Point(12, 17), new Point(16, 19)),new Block(new Point(20, 17), new Point(24, 19)),new Block(new Point(12, 20), new Point(24, 24)),new Block(new Point(17, 17), new Point(19, 19)),
            new Block(new Point(17, 12), new Point(29, 16)),new Block(new Point(17, 17), new Point(21, 19)),new Block(new Point(25, 17), new Point(29, 19)),new Block(new Point(17, 20), new Point(29, 24)),new Block(new Point(22, 17), new Point(24, 19)),
            new Block(new Point(2, 17), new Point(14, 21)),new Block(new Point(2, 22), new Point(6, 24)),new Block(new Point(10, 22), new Point(14, 24)),new Block(new Point(2, 25), new Point(14, 29)),new Block(new Point(7, 22), new Point(9, 24)),
            new Block(new Point(7, 17), new Point(19, 21)),new Block(new Point(7, 22), new Point(11, 24)),new Block(new Point(15, 22), new Point(19, 24)),new Block(new Point(7, 25), new Point(19, 29)),new Block(new Point(12, 22), new Point(14, 24)),
            new Block(new Point(12, 17), new Point(24, 21)),new Block(new Point(12, 22), new Point(16, 24)),new Block(new Point(20, 22), new Point(24, 24)),new Block(new Point(12, 25), new Point(24, 29)),new Block(new Point(17, 22), new Point(19, 24)),
            new Block(new Point(17, 17), new Point(29, 21)),new Block(new Point(17, 22), new Point(21, 24)),new Block(new Point(25, 22), new Point(29, 24)),new Block(new Point(17, 25), new Point(29, 29)),new Block(new Point(22, 22), new Point(24, 24)),
            new Block(new Point(8, 3), new Point(13, 4)),new Block(new Point(8, 5), new Point(9, 6)),new Block(new Point(12, 5), new Point(13, 6)),new Block(new Point(8, 7), new Point(13, 8)),new Block(new Point(10, 5), new Point(11, 6)),
            new Block(new Point(13, 3), new Point(18, 4)),new Block(new Point(13, 5), new Point(14, 6)),new Block(new Point(17, 5), new Point(18, 6)),new Block(new Point(13, 7), new Point(18, 8)),new Block(new Point(15, 5), new Point(16, 6)),
            new Block(new Point(18, 3), new Point(23, 4)),new Block(new Point(18, 5), new Point(19, 6)),new Block(new Point(22, 5), new Point(23, 6)),new Block(new Point(18, 7), new Point(23, 8)),new Block(new Point(20, 5), new Point(21, 6)),
            new Block(new Point(3, 8), new Point(8, 9)),new Block(new Point(3, 10), new Point(4, 11)),new Block(new Point(7, 10), new Point(8, 11)),new Block(new Point(3, 12), new Point(8, 13)),new Block(new Point(5, 10), new Point(6, 11)),
            new Block(new Point(8, 8), new Point(13, 9)),new Block(new Point(8, 10), new Point(9, 11)),new Block(new Point(12, 10), new Point(13, 11)),new Block(new Point(8, 12), new Point(13, 13)),new Block(new Point(10, 10), new Point(11, 11)),
            new Block(new Point(13, 8), new Point(18, 9)),new Block(new Point(13, 10), new Point(14, 11)),new Block(new Point(17, 10), new Point(18, 11)),new Block(new Point(13, 12), new Point(18, 13)),new Block(new Point(15, 10), new Point(16, 11)),
            new Block(new Point(18, 8), new Point(23, 9)),new Block(new Point(18, 10), new Point(19, 11)),new Block(new Point(22, 10), new Point(23, 11)),new Block(new Point(18, 12), new Point(23, 13)),new Block(new Point(20, 10), new Point(21, 11)),
            new Block(new Point(23, 8), new Point(28, 9)),new Block(new Point(23, 10), new Point(24, 11)),new Block(new Point(27, 10), new Point(28, 11)),new Block(new Point(23, 12), new Point(28, 13)),new Block(new Point(25, 10), new Point(26, 11)),
            new Block(new Point(3, 13), new Point(8, 14)),new Block(new Point(3, 15), new Point(4, 16)),new Block(new Point(7, 15), new Point(8, 16)),new Block(new Point(3, 17), new Point(8, 18)),new Block(new Point(5, 15), new Point(6, 16)),
            new Block(new Point(8, 13), new Point(13, 14)),new Block(new Point(8, 15), new Point(9, 16)),new Block(new Point(12, 15), new Point(13, 16)),new Block(new Point(8, 17), new Point(13, 18)),new Block(new Point(10, 15), new Point(11, 16)),
            new Block(new Point(13, 13), new Point(18, 14)),new Block(new Point(13, 15), new Point(14, 16)),new Block(new Point(17, 15), new Point(18, 16)),new Block(new Point(13, 17), new Point(18, 18)),new Block(new Point(15, 15), new Point(16, 16)),
            new Block(new Point(18, 13), new Point(23, 14)),new Block(new Point(18, 15), new Point(19, 16)),new Block(new Point(22, 15), new Point(23, 16)),new Block(new Point(18, 17), new Point(23, 18)),new Block(new Point(20, 15), new Point(21, 16)),
            new Block(new Point(23, 13), new Point(28, 14)),new Block(new Point(23, 15), new Point(24, 16)),new Block(new Point(27, 15), new Point(28, 16)),new Block(new Point(23, 17), new Point(28, 18)),new Block(new Point(25, 15), new Point(26, 16)),
            new Block(new Point(3, 18), new Point(8, 19)),new Block(new Point(3, 20), new Point(4, 21)),new Block(new Point(7, 20), new Point(8, 21)),new Block(new Point(3, 22), new Point(8, 23)),new Block(new Point(5, 20), new Point(6, 21)),
            new Block(new Point(8, 18), new Point(13, 19)),new Block(new Point(8, 20), new Point(9, 21)),new Block(new Point(12, 20), new Point(13, 21)),new Block(new Point(8, 22), new Point(13, 23)),new Block(new Point(10, 20), new Point(11, 21)),
            new Block(new Point(13, 18), new Point(18, 19)),new Block(new Point(13, 20), new Point(14, 21)),new Block(new Point(17, 20), new Point(18, 21)),new Block(new Point(13, 22), new Point(18, 23)),new Block(new Point(15, 20), new Point(16, 21)),
            new Block(new Point(18, 18), new Point(23, 19)),new Block(new Point(18, 20), new Point(19, 21)),new Block(new Point(22, 20), new Point(23, 21)),new Block(new Point(18, 22), new Point(23, 23)),new Block(new Point(20, 20), new Point(21, 21)),
            new Block(new Point(23, 18), new Point(28, 19)),new Block(new Point(23, 20), new Point(24, 21)),new Block(new Point(27, 20), new Point(28, 21)),new Block(new Point(23, 22), new Point(28, 23)),new Block(new Point(25, 20), new Point(26, 21)),
            new Block(new Point(8, 23), new Point(13, 24)),new Block(new Point(8, 25), new Point(9, 26)),new Block(new Point(12, 25), new Point(13, 26)),new Block(new Point(8, 27), new Point(13, 28)),new Block(new Point(10, 25), new Point(11, 26)),
            new Block(new Point(13, 23), new Point(18, 24)),new Block(new Point(13, 25), new Point(14, 26)),new Block(new Point(17, 25), new Point(18, 26)),new Block(new Point(13, 27), new Point(18, 28)),new Block(new Point(15, 25), new Point(16, 26)),
            new Block(new Point(18, 23), new Point(23, 24)),new Block(new Point(18, 25), new Point(19, 26)),new Block(new Point(22, 25), new Point(23, 26)),new Block(new Point(18, 27), new Point(23, 28)),new Block(new Point(20, 25), new Point(21, 26)),

                
            };

            return new ElemCat
            {  // 0, 4, 5, 62
                AvElem = false,
                LeftCount = 4,
                BlockCount = 5,
                ElemCount = 62,
                Blocks = blocks
            };
        }


        //implementation for elem_d6
        private ElemCat CreateElemD6()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(3, 5), new Point(12, 10)),new Block(new Point(5, 3), new Point(10, 12)),
                new Block(new Point(11, 5), new Point(20, 10)),new Block(new Point(13, 3), new Point(18, 12)),
                new Block(new Point(19, 5), new Point(28, 10)),new Block(new Point(21, 3), new Point(26, 12)),
                new Block(new Point(3, 13), new Point(12, 18)),new Block(new Point(5, 11), new Point(10, 20)),
                new Block(new Point(11, 13), new Point(20, 18)),new Block(new Point(13, 11), new Point(18, 20)),
                new Block(new Point(19, 13), new Point(28, 18)),new Block(new Point(21, 11), new Point(26, 20)),
                new Block(new Point(3, 21), new Point(12, 26)),new Block(new Point(5, 19), new Point(10, 28)),
                new Block(new Point(11, 21), new Point(20, 26)),new Block(new Point(13, 19), new Point(18, 28)),
                new Block(new Point(19, 21), new Point(28, 26)),new Block(new Point(21, 19), new Point(26, 28)),

                
            };

            return new ElemCat
            { //0, 1, 2, 9
                AvElem = false,
                LeftCount = 1,
                BlockCount = 2,
                ElemCount = 9,
                Blocks = blocks
            };
        }

        //implementation for elem_d7
        private ElemCat CreateElemD7()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(0, 4), new Point(3, 7)),new Block(new Point(8, 4), new Point(11, 7)),new Block(new Point(4, 4), new Point(7, 7)),
                new Block(new Point(4, 0), new Point(7, 3)),new Block(new Point(4, 8), new Point(7, 11)),new Block(new Point(4, 4), new Point(7, 7)),
                new Block(new Point(5, 4), new Point(8, 7)),new Block(new Point(13, 4), new Point(16, 7)),new Block(new Point(9, 4), new Point(12, 7)),
                new Block(new Point(9, 0), new Point(12, 3)),new Block(new Point(9, 8), new Point(12, 11)),new Block(new Point(9, 4), new Point(12, 7)),
                new Block(new Point(10, 4), new Point(13, 7)),new Block(new Point(18, 4), new Point(21, 7)),new Block(new Point(14, 4), new Point(17, 7)),
                new Block(new Point(14, 0), new Point(17, 3)),new Block(new Point(14, 8), new Point(17, 11)),new Block(new Point(14, 4), new Point(17, 7)),
                new Block(new Point(15, 4), new Point(18, 7)),new Block(new Point(23, 4), new Point(26, 7)),new Block(new Point(19, 4), new Point(22, 7)),
                new Block(new Point(19, 0), new Point(22, 3)),new Block(new Point(19, 8), new Point(22, 11)),new Block(new Point(19, 4), new Point(22, 7)),
                new Block(new Point(20, 4), new Point(23, 7)),new Block(new Point(28, 4), new Point(31, 7)),new Block(new Point(24, 4), new Point(27, 7)),
                new Block(new Point(24, 0), new Point(27, 3)),new Block(new Point(24, 8), new Point(27, 11)),new Block(new Point(24, 4), new Point(27, 7)),
                new Block(new Point(0, 9), new Point(3, 12)),new Block(new Point(8, 9), new Point(11, 12)),new Block(new Point(4, 9), new Point(7, 12)),
                new Block(new Point(4, 5), new Point(7, 8)),new Block(new Point(4, 13), new Point(7, 16)),new Block(new Point(4, 9), new Point(7, 12)),
                new Block(new Point(5, 9), new Point(8, 12)),new Block(new Point(13, 9), new Point(16, 12)),new Block(new Point(9, 9), new Point(12, 12)),
                new Block(new Point(9, 5), new Point(12, 8)),new Block(new Point(9, 13), new Point(12, 16)),new Block(new Point(9, 9), new Point(12, 12)),
                new Block(new Point(10, 9), new Point(13, 12)),new Block(new Point(18, 9), new Point(21, 12)),new Block(new Point(14, 9), new Point(17, 12)),
                new Block(new Point(14, 5), new Point(17, 8)),new Block(new Point(14, 13), new Point(17, 16)),new Block(new Point(14, 9), new Point(17, 12)),
                new Block(new Point(15, 9), new Point(18, 12)),new Block(new Point(23, 9), new Point(26, 12)),new Block(new Point(19, 9), new Point(22, 12)),
                new Block(new Point(19, 5), new Point(22, 8)),new Block(new Point(19, 13), new Point(22, 16)),new Block(new Point(19, 9), new Point(22, 12)),
                new Block(new Point(20, 9), new Point(23, 12)),new Block(new Point(28, 9), new Point(31, 12)),new Block(new Point(24, 9), new Point(27, 12)),
                new Block(new Point(24, 5), new Point(27, 8)),new Block(new Point(24, 13), new Point(27, 16)),new Block(new Point(24, 9), new Point(27, 12)),
                new Block(new Point(0, 14), new Point(3, 17)),new Block(new Point(8, 14), new Point(11, 17)),new Block(new Point(4, 14), new Point(7, 17)),
                new Block(new Point(4, 10), new Point(7, 13)),new Block(new Point(4, 18), new Point(7, 21)),new Block(new Point(4, 14), new Point(7, 17)),
                new Block(new Point(5, 14), new Point(8, 17)),new Block(new Point(13, 14), new Point(16, 17)),new Block(new Point(9, 14), new Point(12, 17)),
                new Block(new Point(9, 10), new Point(12, 13)),new Block(new Point(9, 18), new Point(12, 21)),new Block(new Point(9, 14), new Point(12, 17)),
                new Block(new Point(10, 14), new Point(13, 17)),new Block(new Point(18, 14), new Point(21, 17)),new Block(new Point(14, 14), new Point(17, 17)),
                new Block(new Point(14, 10), new Point(17, 13)),new Block(new Point(14, 18), new Point(17, 21)),new Block(new Point(14, 14), new Point(17, 17)),
                new Block(new Point(15, 14), new Point(18, 17)),new Block(new Point(23, 14), new Point(26, 17)),new Block(new Point(19, 14), new Point(22, 17)),
                new Block(new Point(19, 10), new Point(22, 13)),new Block(new Point(19, 18), new Point(22, 21)),new Block(new Point(19, 14), new Point(22, 17)),
                new Block(new Point(20, 14), new Point(23, 17)),new Block(new Point(28, 14), new Point(31, 17)),new Block(new Point(24, 14), new Point(27, 17)),
                new Block(new Point(24, 10), new Point(27, 13)),new Block(new Point(24, 18), new Point(27, 21)),new Block(new Point(24, 14), new Point(27, 17)),
                new Block(new Point(0, 19), new Point(3, 22)),new Block(new Point(8, 19), new Point(11, 22)),new Block(new Point(4, 19), new Point(7, 22)),
                new Block(new Point(4, 15), new Point(7, 18)),new Block(new Point(4, 23), new Point(7, 26)),new Block(new Point(4, 19), new Point(7, 22)),
                new Block(new Point(5, 19), new Point(8, 22)),new Block(new Point(13, 19), new Point(16, 22)),new Block(new Point(9, 19), new Point(12, 22)),
                new Block(new Point(9, 15), new Point(12, 18)),new Block(new Point(9, 23), new Point(12, 26)),new Block(new Point(9, 19), new Point(12, 22)),
                new Block(new Point(10, 19), new Point(13, 22)),new Block(new Point(18, 19), new Point(21, 22)),new Block(new Point(14, 19), new Point(17, 22)),
                new Block(new Point(14, 15), new Point(17, 18)),new Block(new Point(14, 23), new Point(17, 26)),new Block(new Point(14, 19), new Point(17, 22)),
                new Block(new Point(15, 19), new Point(18, 22)),new Block(new Point(23, 19), new Point(26, 22)),new Block(new Point(19, 19), new Point(22, 22)),
                new Block(new Point(19, 15), new Point(22, 18)),new Block(new Point(19, 23), new Point(22, 26)),new Block(new Point(19, 19), new Point(22, 22)),
                new Block(new Point(20, 19), new Point(23, 22)),new Block(new Point(28, 19), new Point(31, 22)),new Block(new Point(24, 19), new Point(27, 22)),
                new Block(new Point(24, 15), new Point(27, 18)),new Block(new Point(24, 23), new Point(27, 26)),new Block(new Point(24, 19), new Point(27, 22)),
                new Block(new Point(0, 24), new Point(3, 27)),new Block(new Point(8, 24), new Point(11, 27)),new Block(new Point(4, 24), new Point(7, 27)),
                new Block(new Point(4, 20), new Point(7, 23)),new Block(new Point(4, 28), new Point(7, 31)),new Block(new Point(4, 24), new Point(7, 27)),
                new Block(new Point(5, 24), new Point(8, 27)),new Block(new Point(13, 24), new Point(16, 27)),new Block(new Point(9, 24), new Point(12, 27)),
                new Block(new Point(9, 20), new Point(12, 23)),new Block(new Point(9, 28), new Point(12, 31)),new Block(new Point(9, 24), new Point(12, 27)),
                new Block(new Point(10, 24), new Point(13, 27)),new Block(new Point(18, 24), new Point(21, 27)),new Block(new Point(14, 24), new Point(17, 27)),
                new Block(new Point(14, 20), new Point(17, 23)),new Block(new Point(14, 28), new Point(17, 31)),new Block(new Point(14, 24), new Point(17, 27)),
                new Block(new Point(15, 24), new Point(18, 27)),new Block(new Point(23, 24), new Point(26, 27)),new Block(new Point(19, 24), new Point(22, 27)),
                new Block(new Point(19, 20), new Point(22, 23)),new Block(new Point(19, 28), new Point(22, 31)),new Block(new Point(19, 24), new Point(22, 27)),
                new Block(new Point(20, 24), new Point(23, 27)),new Block(new Point(28, 24), new Point(31, 27)),new Block(new Point(24, 24), new Point(27, 27)),
                new Block(new Point(24, 20), new Point(27, 23)),new Block(new Point(24, 28), new Point(27, 31)),new Block(new Point(24, 24), new Point(27, 27)),

                
            };

            return new ElemCat
            {  // 0, 2, 3, 50
                AvElem = false,
                LeftCount = 2,
                BlockCount = 3,
                ElemCount = 50,
                Blocks = blocks
            };
        }

        //implementation for elem_d8
        private ElemCat CreateElemD8()
        {

            
            var blocks = new Block[]
            {
                new Block(new Point(0, 0), new Point(7, 3)),new Block(new Point(0, 4), new Point(7, 7)),
            new Block(new Point(8, 0), new Point(11, 7)),new Block(new Point(12, 0), new Point(15, 7)),
            new Block(new Point(0, 8), new Point(3, 15)),new Block(new Point(4, 8), new Point(7, 15)),
            new Block(new Point(8, 8), new Point(15, 11)),new Block(new Point(8, 12), new Point(15, 15)),
            new Block(new Point(16, 0), new Point(19, 7)),new Block(new Point(20, 0), new Point(23, 7)),
            new Block(new Point(24, 0), new Point(31, 3)),new Block(new Point(24, 4), new Point(31, 7)),
            new Block(new Point(16, 8), new Point(23, 11)),new Block(new Point(16, 12), new Point(23, 15)),
            new Block(new Point(24, 8), new Point(27, 15)),new Block(new Point(28, 8), new Point(31, 15)),
            new Block(new Point(0, 16), new Point(3, 23)),new Block(new Point(4, 16), new Point(7, 23)),
            new Block(new Point(8, 16), new Point(15, 19)),new Block(new Point(8, 20), new Point(15, 23)),
            new Block(new Point(0, 24), new Point(7, 27)),new Block(new Point(0, 28), new Point(7, 31)),
            new Block(new Point(8, 24), new Point(11, 31)),new Block(new Point(12, 24), new Point(15, 31)),
            new Block(new Point(16, 16), new Point(23, 19)),new Block(new Point(16, 20), new Point(23, 23)),
            new Block(new Point(24, 16), new Point(27, 23)),new Block(new Point(28, 16), new Point(31, 23)),
            new Block(new Point(16, 24), new Point(19, 31)),new Block(new Point(20, 24), new Point(23, 31)),
            new Block(new Point(24, 24), new Point(31, 27)),new Block(new Point(24, 28), new Point(31, 31)),
            new Block(new Point(0, 0), new Point(7, 15)),new Block(new Point(8, 0), new Point(15, 15)),
            new Block(new Point(16, 0), new Point(31, 7)),new Block(new Point(16, 8), new Point(31, 15)),
            new Block(new Point(0, 16), new Point(15, 23)),new Block(new Point(0, 24), new Point(15, 31)),
            new Block(new Point(16, 16), new Point(23, 31)),new Block(new Point(24, 16), new Point(31, 31)),

            };

            return new ElemCat
            {  // 0, 1, 2, 20
                AvElem = false,
                LeftCount = 1,
                BlockCount = 2,
                ElemCount = 20,
                Blocks = blocks
            };
        }

    }
}