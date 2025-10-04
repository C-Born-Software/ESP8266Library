using System;

namespace AnodeMeter.Common
{
    public class SmelterDetailsInitResult
    {
        public bool Success { get; set; }
        public bool MaskT4 { get; set; }
        public TimeSpan ShiftLength { get; set; }
        public TimeSpan FirstShiftOffset { get; set; }
        public string DefaultLine { get; set; }
    }
}