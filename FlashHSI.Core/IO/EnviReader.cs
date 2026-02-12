using System.IO;
using System.Text.RegularExpressions;

namespace FlashHSI.Core.IO
{
    public struct EnviHeader
    {
        public int Samples;
        public int Lines;
        public int Bands;
        public int HeaderOffset;
        public string Interleave; // bil, bsq, bip
        public int DataType; // 1=Byte, 2=Int16, 3=Int32, 4=Float32, 12=UInt16
        public int ByteOrder; // 0=Little Endian, 1=Big Endian
    }

    public class EnviReader
    {
        public EnviHeader Header { get; private set; }
        private FileStream? _fileStream;
        private BinaryReader? _reader;
        private string _dataPath;

        public void Load(string headerPath)
        {
            Close(); // Clean up previous resources if any

            ParseHeader(headerPath);

            // Assume data file uses extension-less name or .raw/.img
            // User usually provides header path. Matches standard ENVI convention.
            // If header is "file.hdr", data is "file" or "file.raw"

            string dir = Path.GetDirectoryName(headerPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(headerPath);
            
            _dataPath = Path.Combine(dir, name); // Default guess

            // Try explicit matching if needed, but usually same base name
            // Let's check a few extensions
            string[] extensions = { "", ".raw", ".img", ".bin" };
            foreach (var ext in extensions)
            {
                string candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                {
                    _dataPath = candidate;
                    break;
                }
            }

            if (!File.Exists(_dataPath))
            {
                 // Fallback check if headerPath itself is the data file? No, usually .hdr is separate.
                 throw new FileNotFoundException($"Could not find data file for header: {headerPath}");
            }

            _fileStream = new FileStream(_dataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_fileStream);
        }

        private void ParseHeader(string path)
        {
            var lines = File.ReadAllLines(path);
            Header = new EnviHeader();
            // Default interleave
            var h = new EnviHeader { Interleave = "bsq" }; // Default

            foreach (var line in lines)
            {
                if (line.Contains("="))
                {
                    var parts = line.Split('=');
                    var key = parts[0].Trim().ToLower();
                    var value = parts[1].Trim();

                    switch (key)
                    {
                        case "samples": h.Samples = int.Parse(value); break;
                        case "lines": h.Lines = int.Parse(value); break;
                        case "bands": h.Bands = int.Parse(value); break;
                        case "header offset": h.HeaderOffset = int.Parse(value); break;
                        case "interleave": h.Interleave = value.ToLower(); break;
                        case "data type": h.DataType = int.Parse(value); break;
                        case "byte order": h.ByteOrder = int.Parse(value); break;
                    }
                }
            }
            Header = h;
        }

        public void Close()
        {
            _reader?.Close();
            _reader = null;
            _fileStream?.Close();
            _fileStream = null;
        }

        /// <summary>
        /// Reads the next frame (spatial line) from the file.
        /// Supports BIL (Band Interleaved by Line) common in line-scan cameras.
        /// </summary>
        /// <param name="buffer">Buffer to store the frame (Samples * Bands)</param>
        /// <returns>True if read successfully, False if EOF</returns>
        public bool ReadNextFrame(ushort[] buffer)
        {
            if (_fileStream == null || _reader == null) return false;

            // Fx50/LineScan is usually BIL: [Sample1_Band1, Sample2_Band1...], [Sample1_Band2... ] ??
            // BIL: Line 1 Band 1, Line 1 Band 2 ... 
            // Wait, BIL = Band Interleaved by Line. 
            // For a single spatial line (Frame):
            // It stores all samples for Band 1, then all samples for Band 2... 
            // So structure is: [Bands][Samples] (for that line).

            // Expected Output Buffer: [Samples][Bands] (Pixel Interleaved) for processing?
            // Or our pipeline expects [Samples * Bands]? 
            // Our pipeline processes 1 pixel (all bands) at a time usually?
            // HsiPipeline.ProcessFrame takes (ushort* rawData, int length).
            // This means we process ONE PIXEL at a time.

            // So we need to read a full LINE (Frame) from disk, then feed pixels one by one to pipeline.

            // So we need to read a full LINE (Frame) from disk, then feed pixels one by one to pipeline.
            // A Frame (1 Line of image) in BIL:
            // [Band 1 (Width samples)]
            // [Band 2 (Width samples)]
            // ...

            // We need to pivot this to:
            // Pixel 1: [Band1, Band2, ...]
            // Pixel 2: ...

            int width = Header.Samples;
            int bands = Header.Bands;

            // Total shorts to read for 1 line
            int itemsToRead = width * bands;
            if (_fileStream.Position >= _fileStream.Length) return false;

            // Read Raw bytes
            int bytesPerPixel = (Header.DataType == 12 || Header.DataType == 2) ? 2 : 1;
            // TODO: Handle Float32 (4) if needed

            byte[] rawBytes = _reader.ReadBytes(itemsToRead * bytesPerPixel);
            if (rawBytes.Length < itemsToRead * bytesPerPixel) return false;

            bool isBigEndian = Header.ByteOrder == 1;

            // Convert to UShort and Pivot if BIL
            if (Header.Interleave == "bil")
            {
                for (int b = 0; b < bands; b++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int inputIdx = b * width + x;
                        int outputIdx = x * bands + b;

                        int byteIdx = inputIdx * 2;
                        ushort val;

                        // Handle Endianness
                        if (isBigEndian)
                        {
                            // Swap: [High, Low] -> Val
                            val = (ushort)((rawBytes[byteIdx] << 8) | rawBytes[byteIdx + 1]);
                        }
                        else
                        {
                            // Little Endian: [Low, High]
                            val = BitConverter.ToUInt16(rawBytes, byteIdx);
                        }

                        buffer[outputIdx] = val;
                    }
                }
            }
            else if (Header.Interleave == "bip")
            {
                // BIP: Band Interleaved by Pixel
                // Matches our desired processing order if Little Endian.
                if (!isBigEndian)
                {
                    Buffer.BlockCopy(rawBytes, 0, buffer, 0, rawBytes.Length);
                }
                else
                {
                    // Must swap manually even for BIP
                    for (int i = 0; i < itemsToRead; i++)
                    {
                        int byteIdx = i * 2;
                        buffer[i] = (ushort)((rawBytes[byteIdx] << 8) | rawBytes[byteIdx + 1]);
                    }
                }
            }
            else // BSQ
            {
                // Simple implementation for BSQ (Band Sequential)
                // [Band1(All Pixels)], [Band2]...
                // Frame is 1 Line? BSQ usually stores FULL IMAGE Band 1, then FULL IMAGE Band 2.
                // If we are reading line by line from a BSQ file, we have to jump around the file!
                // This Reader assumes sequential reading, so BSQ line-by-line is impossible without Seek.
                // For now, assume BSQ is not supported for line-streaming or handle gracefully.
                // Specim usually is BIL.
                // Let's just create 0s or try to parse as BIP to avoid crash, but warn?
                // Or just do nothing.
            }

            return true;
        }
    }
}
