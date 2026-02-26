using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Text;

namespace FlashHSI.Core.Settings
{
    /// <summary>
    /// Represents a group of conditions connected by AND.
    /// This is the inner level of the 2-level mask rule structure.
    /// </summary>
    public partial class MaskRuleConditionGroup : ObservableObject
    {
        /// <summary>
        /// Operator to connect this group to the next group (AND or OR).
        /// </summary>
        [ObservableProperty] private MaskRuleLogicalOperator _groupOperator = MaskRuleLogicalOperator.AND;

        /// <summary>
        /// Conditions within this group (connected by AND).
        /// </summary>
        public ObservableCollection<MaskRuleCondition> Conditions { get; } = new ObservableCollection<MaskRuleCondition>();

        /// <summary>
        /// Generates the mask rule string for this group.
        /// Example: "b80 > 35000 & b100 < 40000 | b120 > 5000"
        /// Each condition has its own NextOperator to connect to the next one.
        /// </summary>
        public string ToMaskRuleString()
        {
            if (Conditions.Count == 0)
                return "";

            var sb = new StringBuilder();
            for (int i = 0; i < Conditions.Count; i++)
            {
                var cond = Conditions[i];
                string op = cond.IsLess ? "<" : ">";
                sb.Append($"b{cond.BandIndex} {op} {cond.Threshold:F0}");
                
                // Use the NextOperator from this condition to connect to the next one
                if (i < Conditions.Count - 1)
                {
                    string connector = cond.NextOperator == MaskRuleLogicalOperator.AND ? " & " : " | ";
                    sb.Append(connector);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Adds a new condition with default values and specifies the operator to connect to the previous condition.
        /// </summary>
        public void AddCondition(MaskRuleLogicalOperator nextOperator = MaskRuleLogicalOperator.AND)
        {
            var condition = new MaskRuleCondition 
            { 
                BandIndex = 80, 
                Threshold = 35000, 
                IsLess = true,
                NextOperator = nextOperator  // 새 Rule의 NextOperator만 설정
            };
            
            // 이전 조건들의 NextOperator는 변경하지 않음 - 각 Rule이 개별적으로 AND/OR 유지
            
            Conditions.Add(condition);
        }

        /// <summary>
        /// Removes a condition from this group.
        /// </summary>
        public void RemoveCondition(MaskRuleCondition condition)
        {
            if (Conditions.Contains(condition))
            {
                Conditions.Remove(condition);
            }
        }
    }
}
