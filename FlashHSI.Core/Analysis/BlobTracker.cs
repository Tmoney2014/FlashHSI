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
        private List<ActiveBlob> _activeBlobs = new List<ActiveBlob>();
        private readonly int _classCount;
        private readonly int _minPixels; // Noise Filter
        
        // Configuration
        public bool MergeDifferentClasses { get; set; } = false; // If true, merges touching blobs even if different class (Not standard)

        public BlobTracker(int classCount, int minPixels = 5)
        {
            _classCount = classCount;
            _minPixels = minPixels;
            ActiveBlob.ResetCounter();
        }

        // Buffer for segments to avoid allocation
        private Segment[] _segmentBuffer = Array.Empty<Segment>();

        /// <summary>
        /// Process one line of classification results.
        /// Returns a list of blobs that have CLOSED (finished) in this step.
        /// </summary>
        public List<ActiveBlob> ProcessLine(int lineIndex, int[] pxClasses)
        {
            var closedBlobs = new List<ActiveBlob>();
            
            // 1. RLE Segmentation: Use Reusable Buffer
            if (_segmentBuffer.Length < pxClasses.Length)
            {
                _segmentBuffer = new Segment[pxClasses.Length]; // Worst case: 1 pixel per segment
            }

            int segmentCount = RunLengthEncode(pxClasses, _segmentBuffer);

            // 2. Matching: Compare new segments with ActiveBlobs
            // Basic logic: If X-range overlaps, merge.
            
            // Re-use matching flags? 
            // Since activeBlobs count is small, we can keep using bool array or just a checked property?
            // Let's use a BitArray or stackalloc if small. Segment count can be large (640).
            // Heap allocation for bool[] matchFlags is still there. 
            // Optimization: Use `Span<bool>` if possible or just `stackalloc` if count is safe (<1024).
            // 640 is safe for stackalloc.
            
            // Handling unsafe in this context requires `unsafe` keyword or just careful logic.
            // Let's stick to bool[] for now, it's one allocation vs many segments.
            // Or better: We can modify the Segment struct to have a 'Matched' flag if we want.
            // But _segmentBuffer is persistent. We must clear flags.
            
            bool[] segmentMatched = new bool[segmentCount]; // This is still an allocation. 
            // Optimize: Move segmentMatched to field?
            
            foreach (var blob in _activeBlobs)
            {
                // Optimization: Track if blob matched this frame
                // Since we iterate ALL segments for EACH blob, this is O(M*N).
                // M (blobs) is small (~20), N (segments) can be ~50. 20*50 = 1000 iter. Fast enough.

                for (int i = 0; i < segmentCount; i++)
                {
                    ref var seg = ref _segmentBuffer[i];
                    
                    // Overlap Check: A.Start <= B.End && B.Start <= A.End
                    if (blob.StartX - 2 <= seg.EndX && seg.StartX <= blob.EndX + 2)
                    {
                        // Match found!
                        blob.UpdateBounds(seg.StartX, seg.EndX, lineIndex);
                        blob.AddVote(seg.ClassIndex, seg.Count);
                        
                        segmentMatched[i] = true;
                    }
                }
            }

            // 3. Create New Blobs from unmatched segments
            for (int i = 0; i < segmentCount; i++)
            {
                if (!segmentMatched[i])
                {
                    ref var seg = ref _segmentBuffer[i];
                    var newBlob = new ActiveBlob(seg.StartX, seg.EndX, lineIndex, _classCount);
                    newBlob.AddVote(seg.ClassIndex, seg.Count);
                    _activeBlobs.Add(newBlob);
                }
            }

            // 4. Closing: Remove blobs not seen in this line
            int gapTolerance = 5; 
            
            for (int i = _activeBlobs.Count - 1; i >= 0; i--)
            {
                var blob = _activeBlobs[i];
                if (lineIndex - blob.LastSeenLine > gapTolerance) 
                {
                    blob.IsClosed = true;
                    _activeBlobs.RemoveAt(i);
                    
                    // Filter Noise (Too small)
                    if (blob.TotalPixels >= _minPixels)
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
