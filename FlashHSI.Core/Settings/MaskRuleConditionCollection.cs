using CommunityToolkit.Mvvm.ComponentModel;
using FlashHSI.Core.Masking;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;

namespace FlashHSI.Core.Settings
{
    /// <summary>
    /// Logical operator for combining multiple mask rule conditions.
    /// </summary>
    public enum MaskRuleLogicalOperator
    {
        AND,  // All conditions must be true
        OR    // Any condition can be true
    }

    /// <summary>
    /// 2-Level Mask Rule Structure:
    /// - Outer level: OR groups (connected by OR)
    /// - Inner level: AND groups (connected by AND)
    /// 
    /// Example:
    /// (b80 > 35000 & b100 < 40000) | (b120 > 5000)
    /// </summary>
    public partial class MaskRuleConditionCollection : ObservableObject
    {
        /// <summary>
        /// Event raised when any condition property changes
        /// </summary>
        public event Action? OnConditionChanged;

        /// <summary>
        /// Groups of conditions (connected by OR between groups).
        /// </summary>
        public ObservableCollection<MaskRuleConditionGroup> ConditionGroups { get; } = new ObservableCollection<MaskRuleConditionGroup>();

        public MaskRuleConditionCollection()
        {
            // Subscribe to CollectionChanged to track group additions/removals
            ConditionGroups.CollectionChanged += OnConditionGroupsChanged;
        }

        private void OnConditionGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (MaskRuleConditionGroup group in e.NewItems)
                {
                    // Subscribe to group's conditions collection changes
                    group.Conditions.CollectionChanged += OnConditionsChanged;
                    // Subscribe to each condition's property changes
                    foreach (var condition in group.Conditions)
                    {
                        condition.PropertyChanged += OnConditionPropertyChanged;
                    }
                }
            }
            if (e.OldItems != null)
            {
                foreach (MaskRuleConditionGroup group in e.OldItems)
                {
                    group.Conditions.CollectionChanged -= OnConditionsChanged;
                    foreach (var condition in group.Conditions)
                    {
                        condition.PropertyChanged -= OnConditionPropertyChanged;
                    }
                }
            }
        }

        private void OnConditionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (MaskRuleCondition condition in e.NewItems)
                {
                    condition.PropertyChanged += OnConditionPropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (MaskRuleCondition condition in e.OldItems)
                {
                    condition.PropertyChanged -= OnConditionPropertyChanged;
                }
            }
            // Notify that conditions changed
            OnConditionChanged?.Invoke();
        }

        private void OnConditionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Notify that any condition property changed
            OnConditionChanged?.Invoke();
        }

        /// <summary>
        /// Generates the full MaskRule string.
        /// Example: "(b80 > 35000 & b100 < 40000) | (b120 > 5000)"
        /// </summary>
        public string ToMaskRuleString()
        {
            if (ConditionGroups.Count == 0)
                return "Mean";

            var sb = new StringBuilder();
            for (int i = 0; i < ConditionGroups.Count; i++)
            {
                var group = ConditionGroups[i];
                string groupStr = group.ToMaskRuleString();
                
                if (!string.IsNullOrEmpty(groupStr))
                {
                    // Wrap each group in parentheses
                    sb.Append($"({groupStr})");
                    
                    if (i < ConditionGroups.Count - 1)
                    {
                        // Use this group's operator to connect to the next group
                        string connector = group.GroupOperator == MaskRuleLogicalOperator.AND ? " & " : " | ";
                        sb.Append(connector);
                    }
                }
            }
            
            var result = sb.ToString();
            return string.IsNullOrEmpty(result) ? "Mean" : result;
        }

        /// <summary>
        /// Creates a MaskRule from the current conditions.
        /// </summary>
        public MaskRule? ToMaskRule()
        {
            return MaskRuleParser.Parse(ToMaskRuleString());
        }

        /// <summary>
        /// Adds a new condition group with one default condition.
        /// </summary>
        public void AddGroup(MaskRuleLogicalOperator groupOperator = MaskRuleLogicalOperator.AND)
        {
            var group = new MaskRuleConditionGroup();
            
            // 새 그룹의 GroupOperator를 설정 (새 Group과 다음 Group 사이의 연결)
            group.GroupOperator = groupOperator;
            
            // 새 그룹에 기본 Condition 추가 (사용자가 누른 버튼에 따라 AND 또는 OR)
            group.AddCondition(groupOperator);
            
            // 새 그룹 추가 (맨 뒤에 추가)
            ConditionGroups.Add(group);
        }

        /// <summary>
        /// Removes a condition group.
        /// </summary>
        public void RemoveGroup(MaskRuleConditionGroup group)
        {
            if (ConditionGroups.Contains(group))
            {
                ConditionGroups.Remove(group);
            }
        }

        public override string ToString()
        {
            if (ConditionGroups.Count == 0)
                return "No conditions";
            return ToMaskRuleString();
        }
    }
}
