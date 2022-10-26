using System;
//using Microsoft.SPOT;
//using GHI.Premium.Hardware;
using AnodeMeter.Common;
using GHIElectronics.TinyCLR.Devices.Rtc;

namespace AnodeMeter.Hardware
{
    public class HWTimeManager : TimeManager
    {
        protected override bool GetNonVolatileLocalTime(ref DateTime dtUtcNow)
        {
            try
            {
                var rtc = RtcController.GetDefault();
                dtUtcNow = rtc.Now;
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "HWTimeManager::isRTCTimeValid",
                    "Error fetching time from RTC, check battery. Reason: " + ex.Message, "RTC Bat Low");
                dtUtcNow = DateTime.MinValue;
            }
            return true;
        }
        public override void SetRtcTime(DateTime dtLocal)
        {
            var rtc = RtcController.GetDefault();
            rtc.Now = dtLocal;
        }
    }
}