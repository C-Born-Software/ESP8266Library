using System;
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

                dtTest -= new TimeSpan(1, 0, 0, 0); // Allow time up to 1day behind build/server time for timezone difference & system testings

                if (dtLocalTime >= dtTest && dtLocalTime.Year < 2100)
                {
                    if (dtLocalTime <= (dtTest + new TimeSpan(31, 0, 0, 0, 0)))
                    {
                        // Within 30 days of build or last sync, assume time ok
                        _rtcTimeOK = true;
                        SystemTime.SetTime(dtLocalTime);
                        //Utility.SetLocalTime(dtLocalTime);
                    }
                    else
                    {
                        // Could be ok, but require confirmation
                        _rtcTimeOK = false;
                        SystemTime.SetTime(dtLocalTime);
                        //Utility.SetLocalTime(dtLocalTime);
                    }
                }
                else
                {
                    // Seems wrong, set it 30 days before build date and require confirmation
                    _rtcTimeOK = false;

                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "TimeManager::RefreshSystemTime",
                        "Time set to implausible value of \"" + dtLocalTime.ToString("dd-MM-yyyy HH:mm:ss") + "\".", "RTC Bat Low");

                    dtLocalTime = Globals.BuildDate - new TimeSpan(30, 0, 0, 0, 0);
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
