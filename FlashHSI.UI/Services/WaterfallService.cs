using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlashHSI.Core.Settings;
using FlashHSI.Core;

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

        public unsafe void AddLine(int[] classificationRow, int width, System.Collections.Generic.List<FlashHSI.Core.Analysis.ActiveBlob.BlobSnapshot>? blobs = null)
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

                // 3. AI: Precise Contour Rendering (Pixel-Perfect)
                // Using Range Difference between CurrentSegments and PrevSegments
                
                if (blobs != null && height >= 2)
                {
                    byte* prevLine = backBuffer + (height - 2) * stride;
                    byte* currLine = backBuffer + (height - 1) * stride;
                    
                    foreach (var blob in blobs)
                    {
                        // A. Top Edge: Parts of Current that are NOT in Prev -> Draw on PrevLine (y-1)
                        foreach (var currSeg in blob.CurrentSegments)
                        {
                            for (int x = currSeg.Start; x <= currSeg.End; x++)
                            {
                                if (!IsContained(x, blob.PrevSegments))
                                {
                                    DrawPixel(prevLine, x, stride, bytesPerPixel, width);
                                }
                            }
                            
                            // Side Walls (Current) - Draw on CurrLine
                            DrawPixel(currLine, currSeg.Start - 1, stride, bytesPerPixel, width);
                            DrawPixel(currLine, currSeg.End + 1, stride, bytesPerPixel, width);
                        }
                        
                        // B. Bottom Edge: Parts of Prev that are NOT in Current -> Draw on CurrLine (y)
                        // This handles Closed Blobs effectively because their CurrentSegments will be empty, causing PrevSegments to be drawn fully.
                        foreach (var prevSeg in blob.PrevSegments)
                        {
                            for (int x = prevSeg.Start; x <= prevSeg.End; x++)
                            {
                                // If CurrentSegments is empty (Closed Blob), this condition is always true.
                                if (!IsContained(x, blob.CurrentSegments))
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
        
        private bool IsContained(int x, System.Collections.Generic.List<FlashHSI.Core.Analysis.ActiveBlob.BlobSegment> segments)
        {
            foreach (var seg in segments)
            {
                if (x >= seg.Start && x <= seg.End) return true;
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
