using CommunityToolkit.Mvvm.ComponentModel;

namespace FlashHSI.Core
{
    public partial class ClassInfo : ObservableObject
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string ColorHex { get; set; } = "#888888";
        
        [ObservableProperty] private long _count;
        [ObservableProperty] private string _percentage = "0.00%";
    }
}
