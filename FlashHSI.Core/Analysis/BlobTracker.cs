using System;
using System.Collections.Generic;
using System.Linq;

namespace FlashHSI.Core.Analysis
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// Line-by-Line Object Tracker using RLE and Overlap Matching.
    /// Performance: O(N) where N is number of segments (blobs), much smaller than Width.
    /// </summary>
    public class BlobTracker
    {
        // Configuration Properties (Runtime Tunable)
        public int MinPixels { get; set; } = 5;       // Noise Filter
        public int MaxLineGap { get; set; } = 5;      // Vertical Gap Tolerance
        public int MaxPixelGap { get; set; } = 10;    // Horizontal/Diagonal Gap Tolerance (AI: Increased default)
        
        public bool MergeDifferentClasses { get; set; } = true; // Always true by design (voting)

        private List<ActiveBlob> _activeBlobs = new List<ActiveBlob>();
        private readonly int _classCount;

        public BlobTracker(int classCount)
        {
            _classCount = classCount;
            ActiveBlob.ResetCounter();
        }

        public IReadOnlyList<ActiveBlob> GetActiveBlobs() => _activeBlobs;

        // Buffer for segments to avoid allocation
        private Segment[] _segmentBuffer = Array.Empty<Segment>();

        /// <summary>
        /// Process one line of classification results.
        /// Returns a list of blobs that have CLOSED (finished) in this step.
        /// </summary>
        public List<ActiveBlob> ProcessLine(int lineIndex, int[] pxClasses)
        {
            var closedBlobs = new List<ActiveBlob>();
            
            // 1. Prepare Blobs for potentially new line data (visualization support)
            foreach (var blob in _activeBlobs)
            {
                blob.PrepareForNewLine(lineIndex);
            }

            // 2. RLE Segmentation: Use Reusable Buffer
            if (_segmentBuffer.Length < pxClasses.Length)
            {
                _segmentBuffer = new Segment[pxClasses.Length]; // Worst case: 1 pixel per segment
            }

            int segmentCount = RunLengthEncode(pxClasses, buffer: _segmentBuffer);

            // 3. Matching & Merging
            // Check each segment against active blobs.
            // If a segment overlaps multiple blobs, merge those blobs into one.
            
            for (int i = 0; i < segmentCount; i++)
            {
                ref var seg = ref _segmentBuffer[i];
                var matchedBlobs = new List<ActiveBlob>();

                // Find all overlapping blobs
                foreach (var blob in _activeBlobs)
                {
                    // Overlap Check with Tolerance
                    if (blob.StartX - MaxPixelGap <= seg.EndX && seg.StartX <= blob.EndX + MaxPixelGap)
                    {
                        matchedBlobs.Add(blob);
                    }
                }

                if (matchedBlobs.Count > 0)
                {
                    // Merge everything into the first blob (primary)
                    var primaryBlob = matchedBlobs[0];
                    primaryBlob.UpdateBounds(seg.StartX, seg.EndX, lineIndex);
                    primaryBlob.AddSegment(seg.StartX, seg.EndX, lineIndex); // AI: Pass LineIndex for Moment
                    primaryBlob.AddVote(seg.ClassIndex, seg.Count);

                    // If multiple blobs matched, merge them into primaryBlob
                    if (matchedBlobs.Count > 1)
                    {
                        for (int k = 1; k < matchedBlobs.Count; k++)
                        {
                            var targetBlob = matchedBlobs[k];
                            // Merge target into primary
                            primaryBlob.MergeFrom(targetBlob);
                            // Remove target from active list
                            _activeBlobs.Remove(targetBlob);
                        }
                    }
                }
                else
                {
                    // No match -> New Blob
                    var newBlob = new ActiveBlob(seg.StartX, seg.EndX, lineIndex, _classCount);
                    newBlob.AddVote(seg.ClassIndex, seg.Count);
                    _activeBlobs.Add(newBlob);
                }
            }

            // 3. Closing: Remove blobs not seen in this line
            // (Create New Blobs step is merged into step 2)
            
            for (int i = _activeBlobs.Count - 1; i >= 0; i--)
            {
                var blob = _activeBlobs[i];
                // Check if blob has been unseen for longer than tolerance
                if (lineIndex - blob.LastSeenLine > MaxLineGap) 
                {
                    blob.IsClosed = true;
                    _activeBlobs.RemoveAt(i);
                    
                    // Filter Noise (Too small)
                    if (blob.TotalPixels >= MinPixels)
                    {
                        closedBlobs.Add(blob);
                    }
                }
            }

            return closedBlobs;
        }

        private struct Segment
        {
            public int StartX;
            public int EndX;
            public int ClassIndex;
            public int Count => EndX - StartX + 1;
        }

        // Returns count of segments
        private int RunLengthEncode(int[] data, Segment[] buffer)
        {
            if (data == null || data.Length == 0) return 0;

            int count = 0;
            int currentClass = data[0];
            int startX = 0;

            for (int x = 1; x < data.Length; x++)
            {
                if (data[x] != currentClass)
                {
                    // End of run
                    if (currentClass >= 0) // Ignore Background (-1)
                    {
                        buffer[count++] = new Segment { StartX = startX, EndX = x - 1, ClassIndex = currentClass };
                    }
                    currentClass = data[x];
                    startX = x;
                }
            }
            // Last run
            if (currentClass >= 0)
            {
                buffer[count++] = new Segment { StartX = startX, EndX = data.Length - 1, ClassIndex = currentClass };
            }

            return count;
        }
    }
}
