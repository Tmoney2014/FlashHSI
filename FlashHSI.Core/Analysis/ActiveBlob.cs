using System;

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

        // Voting
        public long[] ClassVotes { get; private set; }
        public int TotalPixels { get; set; }

        public ActiveBlob(int startX, int endX, int currentLine, int classCount)
        {
            Id = ++_globalIdCounter;
            StartX = startX;
            EndX = endX;
            StartLine = currentLine;
            EndLine = currentLine;
            LastSeenLine = currentLine;
            ClassVotes = new long[classCount];
            IsClosed = false;
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
            StartX = Math.Min(StartX, startX);
            EndX = Math.Max(EndX, endX);
            EndLine = Math.Max(EndLine, lineIndex); // Always growing forward
            LastSeenLine = lineIndex;
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

        public double CenterX => (StartX + EndX) / 2.0;
        public int Length => EndLine - StartLine + 1;

        public static void ResetCounter() => _globalIdCounter = 0;
    }
}
