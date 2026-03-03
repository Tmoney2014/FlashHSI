namespace FlashHSI.Core
{
    public class ModelConfig
    {
        public string ModelType { get; set; } = string.Empty;
        public string OriginalType { get; set; } = string.Empty;
        public List<int> SelectedBands { get; set; } = new();
        public string ExcludeBands { get; set; } = string.Empty;
        public List<List<double>> Weights { get; set; } = new();
        public List<double> Bias { get; set; } = new();
        public bool IsMultiClass { get; set; }
        public PreprocessingConfig Preprocessing { get; set; } = new();
        public Dictionary<string, string> Labels { get; set; } = new();
        public Dictionary<string, string> Colors { get; set; } = new();
    }

    public class PreprocessingConfig
    {
        public bool ApplySG { get; set; }
        public int SGWin { get; set; }
        public int SGPoly { get; set; }
        public bool ApplyDeriv { get; set; }
        public int Gap { get; set; }
        public int DerivOrder { get; set; }
        public bool ApplyL2 { get; set; }
        public bool ApplyMinMax { get; set; }
        public bool ApplySNV { get; set; }
        public bool ApplyCenter { get; set; }
        public bool ApplyAbsorbance { get; set; }
        public string Mode { get; set; } = "Raw";
        public string MaskRules { get; set; } = "Mean";
        public string Threshold { get; set; } = "0.0";

        // C# 앱에서 추가: 배경 마스킹 UI 설정 저장/복원용
        public string? MaskMode { get; set; }
        public int? MaskBandIndex { get; set; }
        public bool? MaskLessThan { get; set; }
        public bool? IsMaskRuleActive { get; set; }
        public double? MaskThreshold { get; set; }
        
        // C# 앱에서 추가: MaskRule 2중 구조 컨디션 저장/복원용
        public List<MaskRuleConditionGroupData>? MaskRuleConditionsData { get; set; }
    }

    /// <summary>
    /// MaskRule 컨디션 그룹 직렬화용 DTO
    /// </summary>
    public class MaskRuleConditionGroupData
    {
        public string GroupOperator { get; set; } = "AND";
        public List<MaskRuleConditionData> Conditions { get; set; } = new();
    }

    /// <summary>
    /// MaskRule 개별 컨디션 직렬화용 DTO
    /// </summary>
    public class MaskRuleConditionData
    {
        public int BandIndex { get; set; } = 80;
        public double Threshold { get; set; } = 35000.0;
        public bool IsLess { get; set; } = true;
        public string NextOperator { get; set; } = "AND";
    }
}
