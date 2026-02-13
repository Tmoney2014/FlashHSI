using System;
using System.Collections.Generic;

namespace FlashHSI.Core.Analysis
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// Represents a tracked object in the line-scan stream.
    /// Tracks position, class votes, and lifecycle state.
    /// </summary>
    public class ActiveBlob
    {
        private static int _globalIdCounter = 0;

        public int Id { get; }
        
        // Spatial Check (X-axis)
        public int StartX { get; set; }
        public int EndX { get; set; }
        
        // Temporal Check (Y-axis / Line)
        public int StartLine { get; set; }
        public int EndLine { get; set; }

        public int LastSeenLine { get; set; }
        public bool IsClosed { get; set; }
        
        // AI: Visualization Support (Line-by-Line Outline)
        public int CurrentStartX { get; set; }
        public int CurrentEndX { get; set; }
        public int PrevStartX { get; set; }
        public int PrevEndX { get; set; }

        // Voting
        public long[] ClassVotes { get; private set; }
        public int TotalPixels { get; set; }

        // AI: Centroid Calculation (Moment Accumulation)
        public long MomentX { get; private set; }
        public long MomentY { get; private set; } 

        public struct BlobSegment
        {
            public int Start;
            public int End;
        }

        public List<BlobSegment> CurrentSegments { get; private set; } = new List<BlobSegment>();
        public List<BlobSegment> PrevSegments { get; private set; } = new List<BlobSegment>();

        public ActiveBlob(int startX, int endX, int currentLine, int classCount)
        {
            Id = ++_globalIdCounter;
            StartX = startX;
            EndX = endX;
            StartLine = currentLine;
            EndLine = currentLine;
            LastSeenLine = currentLine;
            
            // AI: Initialize Line Scope
            CurrentStartX = startX;
            CurrentEndX = endX;
            PrevStartX = startX;
            PrevEndX = endX;
            
            // Initialize Segments
            CurrentSegments.Add(new BlobSegment { Start = startX, End = endX });
            
            ClassVotes = new long[classCount];
            IsClosed = false;
        }

        public void PrepareForNewLine(int lineIndex)
        {
            // If we skipped lines, PrevSegments should effectively be empty relative to the new line
            // But strict line-by-line processing means 'Prev' is always the immediate previous line processed by engine.
            // Engine calls ProcessLine sequentially.
            
            PrevSegments.Clear();
            PrevSegments.AddRange(CurrentSegments);
            CurrentSegments.Clear();
            
            // AI: Update legacy bounds for next line processing
            PrevStartX = CurrentStartX;
            PrevEndX = CurrentEndX;
            
            // Reset Current bounds for new segments (will be updated by AddSegment)
            // But we can reset them to extreme values or keep them?
            // AddSegment initializes if count==1. So no need to reset?
            // Actually AddSegment logic: if (count==1) { set } else { min/max }.
            // But if we clear segments, count becomes 0.
            // Next add will be count 1. So it resets naturally.
            // But what if no segments added? Then Current bounds are stale.
            // That's fine, inactive blob? No, blob is removed if inactive.
        }

        // AI: Overload for Moment Calculation
        public void AddSegment(int start, int end, int lineIndex)
        {
            AddSegment(start, end);
            
            // AI: Accumulate Moments
            // Segment has (endX - startX + 1) pixels.
            // Sum of X for range [S, E] = (S + E) * Count / 2.0
            int count = end - start + 1;
            long sumX = (long)(start + end) * count / 2;
            
            MomentX += sumX;
            MomentY += (long)lineIndex * count; // Y moment is LineIndex * PixelCount
        }

        public void AddSegment(int start, int end)
        {
            CurrentSegments.Add(new BlobSegment { Start = start, End = end });
            
            // Update legacy line bounds for backward compatibility
            if (CurrentSegments.Count == 1)
            {
                CurrentStartX = start;
                CurrentEndX = end;
            }
            else
            {
                CurrentStartX = Math.Min(CurrentStartX, start);
                CurrentEndX = Math.Max(CurrentEndX, end);
            }
        }

        public void AddVote(int classIndex, int count)
        {
            if (classIndex >= 0 && classIndex < ClassVotes.Length)
            {
                ClassVotes[classIndex] += count;
                TotalPixels += count;
            }
        }

        public void UpdateBounds(int startX, int endX, int lineIndex)
        {
            // Global Bounds
            StartX = Math.Min(StartX, startX);
            EndX = Math.Max(EndX, endX);
            EndLine = Math.Max(EndLine, lineIndex); // Always growing forward
            LastSeenLine = lineIndex;
        }

        /// <summary>
        /// AI가 추가함: 다른 Blob의 데이터를 병합 (중복/분리된 객체 합치기)
        /// </summary>
        public void MergeFrom(ActiveBlob other)
        {
            // Update Bounds
            StartX = Math.Min(StartX, other.StartX);
            EndX = Math.Max(EndX, other.EndX);
            StartLine = Math.Min(StartLine, other.StartLine);
            EndLine = Math.Max(EndLine, other.EndLine);
            LastSeenLine = Math.Max(LastSeenLine, other.LastSeenLine);
            
            // Merge Class Votes
            for (int i = 0; i < ClassVotes.Length; i++)
            {
                if (i < other.ClassVotes.Length)
                {
                    ClassVotes[i] += other.ClassVotes[i];
                }
            }
            TotalPixels += other.TotalPixels;

            // AI: Merge Moments
            MomentX += other.MomentX;
            MomentY += other.MomentY;
            
            // Merge Segments
            foreach(var seg in other.CurrentSegments)
            {
                CurrentSegments.Add(seg);
            }
            foreach(var seg in other.PrevSegments)
            {
                PrevSegments.Add(seg);
            }
            
            // Re-calc line bounds
            if (other.CurrentStartX < CurrentStartX) CurrentStartX = other.CurrentStartX;
            if (other.CurrentEndX > CurrentEndX) CurrentEndX = other.CurrentEndX;
        }

        public int GetBestClass()
        {
            long maxVotes = -1;
            int bestClass = -1;
            for (int i = 0; i < ClassVotes.Length; i++)
            {
                if (ClassVotes[i] > maxVotes)
                {
                    maxVotes = ClassVotes[i];
                    bestClass = i;
                }
            }
            return bestClass;
        }

        public class BlobSnapshot
        {
            public List<BlobSegment> CurrentSegments { get; }
            public List<BlobSegment> PrevSegments { get; }
            public int StartLine;
            public int LastSeenLine;
            public int CurrentStartX; // For backward compatibility if needed
            public int CurrentEndX;

            public BlobSnapshot(ActiveBlob blob)
            {
                // Deep Copy Segments
                CurrentSegments = new List<BlobSegment>(blob.CurrentSegments);
                PrevSegments = new List<BlobSegment>(blob.PrevSegments);
                StartLine = blob.StartLine;
                LastSeenLine = blob.LastSeenLine;
                CurrentStartX = blob.CurrentStartX;
                CurrentEndX = blob.CurrentEndX;
            }
        }

        public BlobSnapshot GetSnapshot()
        {
            return new BlobSnapshot(this);
        }

        public double CenterX => TotalPixels > 0 ? (double)MomentX / TotalPixels : (StartX + EndX) / 2.0;
        public double CenterY => TotalPixels > 0 ? (double)MomentY / TotalPixels : (StartLine + EndLine) / 2.0;
        public int Length => EndLine - StartLine + 1;

        public static void ResetCounter() => _globalIdCounter = 0;
    }
}
