using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Messages;

namespace FlashHSI.Core.Analysis
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// Line-by-Line Object Tracker using RLE and Overlap Matching.
    /// Performance: O(N) where N is number of segments (blobs), much smaller than Width.
    /// </summary>
    public class BlobTracker
    {
        // 간단한 ObjectPool 구현 (GC 할당 최소화)
        private class SimpleObjectPool<T> where T : class
        {
            private readonly Func<T> _factory;
            private readonly Stack<T> _stack = new Stack<T>();
            private readonly int _maxSize;

            public SimpleObjectPool(Func<T> factory, int maxSize = 64)
            {
                _factory = factory;
                _maxSize = maxSize;
            }

            public T Rent()
            {
                lock (_stack)
                {
                    return _stack.Count > 0 ? _stack.Pop() : _factory();
                }
            }

            public void Return(T item)
            {
                lock (_stack)
                {
                    if (_stack.Count < _maxSize)
                        _stack.Push(item);
                }
            }
        }
        
        // Configuration Properties (Runtime Tunable) - 메시지로 업데이트
        public int MinPixels { get; set; } = 5;       // Noise Filter
        public int MaxLineGap { get; set; } = 5;      // Vertical Gap Tolerance
        public int MaxPixelGap { get; set; } = 10;    // Horizontal/Diagonal Gap Tolerance
        
        public bool MergeDifferentClasses { get; set; } = true; // Always true by design (voting)

        private List<ActiveBlob> _activeBlobs = new List<ActiveBlob>();
        private List<ActiveBlob> _closedBlobs = new List<ActiveBlob>();  // GC 최적화: 프레임마다 재사용
        private List<ActiveBlob> _matchedBlobs = new List<ActiveBlob>();  // GC 최적화: 프레임마다 재사용
        private readonly int _classCount;
        
        // GC 최적화: ActiveBlob 풀링
        private readonly SimpleObjectPool<ActiveBlob> _blobPool;

        public BlobTracker(int classCount)
        {
            _classCount = classCount;
            ActiveBlob.ResetCounter();
            
            // GC 최적화: ObjectPool 초기화
            _blobPool = new SimpleObjectPool<ActiveBlob>(() => new ActiveBlob(0, 0, 0, classCount));
            
            // 메시지 구독
            WeakReferenceMessenger.Default.Register<BlobTracker, SettingsChangedMessage<int>>(this, static (recipient, message) =>
            {
                switch (message.PropertyName)
                {
                    case nameof(FlashHSI.Core.Settings.SystemSettings.BlobMinPixels):
                        recipient.MinPixels = message.Value;
                        break;
                    case nameof(FlashHSI.Core.Settings.SystemSettings.BlobLineGap):
                        recipient.MaxLineGap = message.Value;
                        break;
                    case nameof(FlashHSI.Core.Settings.SystemSettings.BlobPixelGap):
                        recipient.MaxPixelGap = message.Value;
                        break;
                }
            });
        }

        public IReadOnlyList<ActiveBlob> GetActiveBlobs() => _activeBlobs.ToList(); // Return snapshot for thread safety

        // Buffer for segments to avoid allocation
        private Segment[] _segmentBuffer = Array.Empty<Segment>();

        /// <summary>
        /// Process one line of classification results.
        /// Returns a list of blobs that have CLOSED (finished) in this step.
        /// </summary>
        public List<ActiveBlob> ProcessLine(int lineIndex, int[] pxClasses)
        {
            // GC 최적화: 리스트 재사용
            _closedBlobs.Clear();
            
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
                // GC 최적화: 리스트 재사용 ( 루프마다 클리어)
                _matchedBlobs.Clear();

                // Find all overlapping blobs
                foreach (var blob in _activeBlobs)
                {
                    // Overlap Check with Tolerance
                    if (blob.StartX - MaxPixelGap <= seg.EndX && seg.StartX <= blob.EndX + MaxPixelGap)
                    {
                        _matchedBlobs.Add(blob);
                    }
                }

                if (_matchedBlobs.Count > 0)
                {
                    // Merge everything into the first blob (primary)
                    var primaryBlob = _matchedBlobs[0];
                    primaryBlob.UpdateBounds(seg.StartX, seg.EndX, lineIndex);
                    primaryBlob.AddSegment(seg.StartX, seg.EndX, lineIndex); // AI: Pass LineIndex for Moment
                    primaryBlob.AddVote(seg.ClassIndex, seg.Count);

                    // If multiple blobs matched, merge them into primaryBlob
                    if (_matchedBlobs.Count > 1)
                    {
                        for (int k = 1; k < _matchedBlobs.Count; k++)
                        {
                            var targetBlob = _matchedBlobs[k];
                            // Merge target into primary
                            primaryBlob.MergeFrom(targetBlob);
                            // Remove target from active list (single removal)
                            _activeBlobs.Remove(targetBlob);
                            // GC 최적화: 병합된 블롭은 Pool 반환
                            _blobPool.Return(targetBlob);
                        }
                    }
                }
                else
                {
                    // No match -> New Blob (GC 최적화: ObjectPool 사용)
                    var newBlob = _blobPool.Rent();
                    newBlob.Reset(seg.StartX, seg.EndX, lineIndex, _classCount);
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
                        _closedBlobs.Add(blob);
                    }
                    else
                    {
                        // GC 최적화: 노이즈 블롭은 pool에 즉시 반환
                        _blobPool.Return(blob);
                    }
                }
            }

            // GC 최적화: 매 프레임마다 새 리스트 할당 대신 직접 반환
            // Note: 호출자가 foreach로 읽기만 하므로安全问题. ReleaseClosedBlobs 호출 후 다음 프레임에서 Clear됨.
            return _closedBlobs;
        }

        /// <summary>
        /// GC 최적화: 호출자가 closed blobs 처리完后 pool에 반환
        /// </summary>
        public void ReleaseClosedBlobs(IEnumerable<ActiveBlob> blobs)
        {
            foreach (var blob in blobs)
            {
                _blobPool.Return(blob);
            }
            // _closedBlobs.Clear() 제거 - ProcessLine 시작 시Clear() 호출함
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
