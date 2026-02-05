using System;
using FlashHSI.Core.Analysis;

namespace FlashHSI.Core.Control
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// Logical controller for calculating ejection timing and mapping pixels to valves.
    /// Note: Actual Hardware IO is outside the scope of this class (just Logging).
    /// </summary>
    public class EjectionService
    {
        // Settings (should be injected via SettingsService, but simplified here)
        public int PixelsPerValve { get; set; } = 10; 
        public int DistanceLines { get; set; } = 500; // Camera to Gun distance in Lines
        public int MaxBlobLength { get; set; } = 200; // Threshold for Long Object (Head Hit)

        public event Action<string> OnEjectionSignal;

        public void Process(ActiveBlob blob)
        {
            if (blob == null) return;

            int bestClass = blob.GetBestClass();
            if (bestClass < 0) return; // Background or Invalid

            // 1. Spatial Mapping
            // Center X -> Valve ID
            double centerX = blob.CenterX;
            int valveId = (int)(centerX / PixelsPerValve);

            // 2. Temporal Calculation (Delay)
            int delayLines;
            string hitType;

            // Strategy: Hybrid
            if (blob.Length > MaxBlobLength)
            {
                // Long Object -> Head Hit (Immediate / Low Delay)
                // We want to hit the HEAD, so we calculate delay relative to StartLine.
                // However, 'blob' is already CLOSED, meaning Tail has passed.
                // So Head passed (Length) lines ago.
                // Distance to Head = DistanceLines - (CurrentLine - StartLine?? No, blob tracks globally)
                
                // Let's simplify: 
                // We are at CurrentLine (approx EndLine).
                // Gun is at DistanceLines ahead of Camera.
                // The object Head is at (EndLine - Length).
                // If we want to hit Head: Delay = DistanceLines - Length.
                // If Length > DistanceLines, we missed the head! (Should have triggered earlier).
                // For now, assume simple Immediate Fire if Long.
                
                delayLines = Math.Max(0, DistanceLines - blob.Length);
                hitType = "HEAD";
            }
            else
            {
                // Normal Object -> Center Hit
                // We are at EndLine (Tail).
                // Center is at Length/2 behind Tail.
                // We need to wait until Center reaches Distance.
                // Current Pos of Center = 0 (Camera) - Length/2 (Passed) -> this is confusing.
                
                // Relative to "NOW" (Tail at Camera):
                // Center is at -Length/2 (upstream). 
                // We want Center to reach DistanceLines (downstream).
                // Total Travel = DistanceLines + Length/2? NO.
                
                // Correct Logic:
                // Tail is at Camera (Pos 0). Center is at Camera - Length/2.
                // AirGun is at Pos +Distance.
                // Distance to travel for Center = Distance - (-Length/2) = Distance + Length/2 ??
                // Wait. 
                // StartLine passed Camera at T_start. EndLine passed at T_end (Now).
                // Center passed Camera at T_center = Now - Length/2.
                // So Center is ALREADY at Pos = (Length/2) * Speed downstream.
                // Dist Remaining to Gun = Distance - (Length/2).
                
                delayLines = Math.Max(0, DistanceLines - (blob.Length / 2));
                hitType = "CENTER";
            }

            // Output
            string msg = $"[EJECT] Blob #{blob.Id} (Class {bestClass}) -> Valve {valveId} | Delay {delayLines} lines ({hitType})";
            OnEjectionSignal?.Invoke(msg);
            
            // Console Debug
            // System.Diagnostics.Debug.WriteLine(msg);
        }
    }
}
