using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlashHSI.Core;
using FlashHSI.Core.Analysis;

namespace FlashHSI.UI.Services
{
    public class WaterfallService
    {
        public WriteableBitmap? DisplayImage { get; private set; }
        private Dictionary<int, (byte B, byte G, byte R)> _colorMap = new();

        // Default color for unknown/background
        private (byte B, byte G, byte R) _defaultColor = (0, 0, 0);

        public void Initialize(int width, int height)
        {
            // BGR24 format
            DisplayImage = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
        }

        public void UpdateColorMap(ModelConfig config)
        {
            _colorMap.Clear();
            foreach (var kvp in config.Labels)
            {
                if (int.TryParse(kvp.Key, out int id))
                {
                    string hex = config.Colors.ContainsKey(kvp.Key) ? config.Colors[kvp.Key] : "#888888";
                    _colorMap[id] = ParseHexColor(hex);
                }
            }
            // Add default or special handling if needed
        }

        public unsafe void AddLine(int[] classificationRow, int width, int[]? contourData, int contourLen)
        {
            if (DisplayImage == null) return;
            if (width != DisplayImage.PixelWidth)
            {
                // Re-init if dimension changes? Or just safety check
                Initialize(width, DisplayImage.PixelHeight);
            }

            var bitmap = DisplayImage;
            int height = bitmap.PixelHeight;
            int stride = bitmap.BackBufferStride;
            int bytesPerPixel = 3; // BGR24

            bitmap.Lock();
            try
            {
                byte* backBuffer = (byte*)bitmap.BackBuffer;

                // 1. Scroll Up (Shift Memory)
                // Copy lines 1..Height-1 to 0..Height-2
                // Length = (Height - 1) * Stride
                // Source = Line 1 (backBuffer + stride)
                // Dest = Line 0 (backBuffer)

                int copyLength = (height - 1) * stride;
                if (copyLength > 0)
                {
                    Buffer.MemoryCopy(
                        backBuffer + stride, // Source
                        backBuffer,          // Dest
                        copyLength + stride, // DestSize (safe margin)
                        copyLength           // Count
                    );
                }

                // 2. Draw New Line at Bottom (Height - 1)
                byte* bottomLine = backBuffer + copyLength; // This effectively points to the last line start

                for (int x = 0; x < width; x++)
                {
                    int cls = classificationRow[x];
                    (byte b, byte g, byte r) color;

                    if (cls >= 0 && _colorMap.TryGetValue(cls, out var c))
                    {
                        color = c;
                    }
                    else if (cls == -1) // Background
                    {
                        color = (0, 0, 0); // Black
                    }
                    else if (cls == 999) // AI가 추가함: Blob Outline (White)
                    {
                        color = (255, 255, 255);
                    }
                    else
                    {
                        color = (128, 128, 128); // Unknown/Gray
                    }

                    int pixelOffset = x * bytesPerPixel;
                    if (pixelOffset + 2 < stride)
                    {
                        bottomLine[pixelOffset] = color.b;
                        bottomLine[pixelOffset + 1] = color.g;
                        bottomLine[pixelOffset + 2] = color.r;
                    }
                }

                // 3. AI: Precise Contour Rendering (Pixel-Perfect, Zero-Allocation)
                // Using Range Difference extracted from packed contourData array

                if (contourData != null && contourLen > 0 && height >= 2)
                {
                    byte* prevLine = backBuffer + (height - 2) * stride;
                    byte* currLine = backBuffer + (height - 1) * stride;

                    int blobCount = contourData[0];
                    int idx = 1;

                    for (int b = 0; b < blobCount; b++)
                    {
                        if (idx >= contourLen) break;

                        int cCount = contourData[idx++];
                        int pCount = contourData[idx++];

                        // Span으로 래핑하여 할당 없이 슬라이싱
                        ReadOnlySpan<int> cSegs = new ReadOnlySpan<int>(contourData, idx, cCount * 2);
                        idx += cCount * 2;
                        ReadOnlySpan<int> pSegs = new ReadOnlySpan<int>(contourData, idx, pCount * 2);
                        idx += pCount * 2;

                        // A. Top Edge & Side Walls (CurrentSegments 기준)
                        for (int i = 0; i < cCount; i++)
                        {
                            int start = cSegs[i * 2];
                            int end = cSegs[i * 2 + 1];
                            for (int x = start; x <= end; x++)
                            {
                                if (!IsContainedZeroAlloc(x, pSegs))
                                {
                                    DrawPixel(prevLine, x, stride, bytesPerPixel, width);
                                }
                            }

                            // Side Walls
                            DrawPixel(currLine, start - 1, stride, bytesPerPixel, width);
                            DrawPixel(currLine, end + 1, stride, bytesPerPixel, width);
                        }

                        // B. Bottom Edge (PrevSegments 기준)
                        for (int i = 0; i < pCount; i++)
                        {
                            int start = pSegs[i * 2];
                            int end = pSegs[i * 2 + 1];
                            for (int x = start; x <= end; x++)
                            {
                                if (!IsContainedZeroAlloc(x, cSegs))
                                {
                                    DrawPixel(currLine, x, stride, bytesPerPixel, width);
                                }
                            }
                        }
                    }
                }
                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                bitmap.Unlock();
            }
        }

        private bool IsContained(int x, List<ActiveBlob.BlobSegment> segments)
        {
            foreach (var seg in segments)
            {
                if (x >= seg.Start && x <= seg.End) return true;
            }
            return false;
        }

        private bool IsContainedZeroAlloc(int x, ReadOnlySpan<int> segments)
        {
            for (int i = 0; i < segments.Length / 2; i++)
            {
                int start = segments[i * 2];
                int end = segments[i * 2 + 1];
                if (x >= start && x <= end) return true;
            }
            return false;
        }

        private unsafe void DrawPixel(byte* linePtr, int x, int stride, int bpp, int width)
        {
            if (x < 0 || x >= width) return;

            int offset = x * bpp;
            if (offset + 2 < stride)
            {
                // White Outline
                linePtr[offset] = 255;     // B
                linePtr[offset + 1] = 255; // G
                linePtr[offset + 2] = 255; // R
            }
        }

        private (byte B, byte G, byte R) ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return (128, 128, 128);
            hex = hex.TrimStart('#');
            byte r = 0, g = 0, b = 0;

            if (hex.Length == 6)
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
            }
            else if (hex.Length == 8) // ARGB
            {
                // Skip Alpha
                r = Convert.ToByte(hex.Substring(2, 2), 16);
                g = Convert.ToByte(hex.Substring(4, 2), 16);
                b = Convert.ToByte(hex.Substring(6, 2), 16);
            }

            return (b, g, r);
        }
    }
}
