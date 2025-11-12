using System;
using System.Diagnostics;
using System.Threading;
//using Microsoft.SPOT;
//using Microsoft.SPOT.Hardware;
using GHIElectronics.TinyCLR.Native;

namespace AnodeMeter.Common
{
    public abstract class TimeManager
    {
        public enum TimeMode
        {
            Standard,
            Daylight
        }
        enum MonthsOfYear
        {
            January = 1,
            February = 2,
            March = 3,
            April = 4,
            May = 5,
            June = 6,
            July = 7,
            August = 8,
            September = 9,
            October = 10,
            November = 11,
            December = 12
        }

        private bool _rtcTimeOK = false;
        private DataStore _ds = null;

        public TimeManager()
        {
        }
        public virtual void Init()
        {
            RefreshSystemTime();
        }
        public void RegisterDataStore(DataStore ds)
        {
            _ds = ds;
        }
        public void RefreshSystemTime()
        {
            DateTime dtLocalTime = new DateTime();
            DateTime dtTest = Globals.BuildDate;

            if (GetNonVolatileLocalTime(ref dtLocalTime)) // Should always succeed now
            {
                DateTime LastServerSync = DateTime.MinValue;
                // Find out when this meter last sync'd it's time with the Database Server (or had time set/confirmed locally)
                if (_ds != null)
                    LastServerSync = _ds.GetLastSyncTime();

                if (LastServerSync > dtTest)
                    dtTest = LastServerSync;

                // allow a little backward tolerance
                var lowerBound = dtTest - TimeSpan.FromDays(1); // Timezone difference allowance
                var upperBound = dtTest + TimeSpan.FromDays(31);// Could have been off for a while

                // Attempt RTC read
                if (dtLocalTime == DateTime.MinValue)
                {
                    // RTC read failed: set system clock to baseline, require confirmation
                    _rtcTimeOK = false;
                    SystemTime.SetTime(dtTest);
                    return;
                }

                if (dtLocalTime >= lowerBound && dtLocalTime.Year < 2100) {
                    if (dtLocalTime <= upperBound)
                    {
                            // Within 30 days of build or last sync, assume time ok
                            _rtcTimeOK = true;
                            SystemTime.SetTime(dtLocalTime);
                    }
                    else
                    {
                        // Could be ok, but require confirmation
                        _rtcTimeOK = false;
                        SystemTime.SetTime(dtLocalTime);
                    }
                }
                else
                {
                    // Seems wrong, set it to baseline and require confirmation
                    _rtcTimeOK = false;

                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "TimeManager::RefreshSystemTime",
                        "Time set to implausible value of \"" + dtLocalTime.ToString("dd-MM-yyyy HH:mm:ss") + "\".", "RTC Bat Low");

                    dtLocalTime = dtTest;
                    SyncTime(dtLocalTime);
                }
            }
        }
        public bool IsSystemTimeOK()
        {
            return _rtcTimeOK;
        }
        public void SystemTimeIsOK()
        {
            _rtcTimeOK = true;
        }
        protected abstract bool GetNonVolatileLocalTime(ref DateTime dtLocalNow);
        public void SyncTime(DateTime dtLocalTime)
        {
            try
            {
                SetRtcTime(dtLocalTime);
                SystemTime.SetTime(dtLocalTime);
                //Utility.SetLocalTime(dtLocalTime);
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "TimeManager::SyncTime", "Error setting RTC, check battery. Reason: " + ex.Message, "RTC Bat Low");
            }

        }
        public abstract void SetRtcTime(DateTime dtLocalTime);
    }
}
