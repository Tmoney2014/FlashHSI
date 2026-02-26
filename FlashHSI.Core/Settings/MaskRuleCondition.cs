using CommunityToolkit.Mvvm.ComponentModel;

namespace FlashHSI.Core.Settings
{
    /// <summary>
    /// Represents a single mask rule condition.
    /// Usage: If pixel[BandIndex] {operator} Threshold, then it's NOT background (object).
    /// </summary>
    public partial class MaskRuleCondition : ObservableObject
    {
        [ObservableProperty] private int _bandIndex = 80;
        [ObservableProperty] private double _threshold = 35000.0;
        [ObservableProperty] private bool _isLess = true;  // true: value < threshold (배경), false: value > threshold (배경)
        
        /// <summary>
        /// Operator to connect to the next condition (AND or OR).
        /// This is the operator that comes AFTER this condition.
        /// </summary>
        [ObservableProperty] private MaskRuleLogicalOperator _nextOperator = MaskRuleLogicalOperator.AND;

        public override string ToString()
        {
            string op = IsLess ? "<" : ">";
            return $"b{BandIndex} {op} {Threshold:F0}";
        }
    }
}
