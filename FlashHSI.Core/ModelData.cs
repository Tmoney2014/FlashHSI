using Newtonsoft.Json;

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
    }
}
