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

        public unsafe void AddLine(int[] classificationRow, int width)
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

                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                bitmap.Unlock();
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
