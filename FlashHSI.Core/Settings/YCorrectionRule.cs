using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace FlashHSI.Core.Settings
{
    /// <summary>
    /// Represents a rule to apply additional delay offset based on the Y-position (Centroid) of the blob.
    /// Usage: If Blob.CenterY >= ThresholdY, then FinalDelay += CorrectionMs.
    /// </summary>
    public partial class YCorrectionRule : ObservableObject
    {
        [ObservableProperty] private double _thresholdY;
        [ObservableProperty] private int _correctionMs;

        public override string ToString()
        {
            return $"Y >= {ThresholdY:F1} : {CorrectionMs} ms";
        }
    }
}
