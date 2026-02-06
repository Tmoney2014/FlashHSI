using System;

namespace FlashHSI.Core.Control
{
    public class EjectionLogItem
    {
        public DateTime Timestamp { get; set; }
        public int BlobId { get; set; }
        public int ClassId { get; set; }
        public int ValveId { get; set; }
        public int Delay { get; set; }
        public string HitType { get; set; } = "";
        
        public string TimestampShort => Timestamp.ToString("HH:mm:ss.fff");
    }
}
