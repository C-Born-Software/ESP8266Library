using System;
using System.Text;
using System.Collections;
using PervasiveDigital.Utilities;

namespace AnodeMeter.Common
{
    // Define a custom, non-generic delegate for logging.
    public delegate void LogActionDelegate(int severity, string module, string longMsg, string shortMsg);

    public class Schedule
    {
        // Static delegate for logging, to be injected by the main application.
        public static LogActionDelegate LogAction;
        public static SmelterDetails PlantDetails;

        // Injected provider for measurement mode (0=RodDrop,1=ClampDrop,2=RodThenClamp,3=ClampThenRod)
        public delegate int GetMeasModeIndexDelegate();
        public static GetMeasModeIndexDelegate GetMeasModeIndex;

        public class AnodeSched
        {
            public string Name { get; }
            public MeasurementType _measType;
            public bool _FirstAnodeForPot;
            public bool _MidAnodeForPot;
            public bool _LastAnodeForPot;
            public string _potName;
            public string _AnodePos;

            public AnodeSched(string PotName, string AnodePos, MeasurementType measType, bool First, bool Mid, bool Last)
            {
                Name = AnodePos;
                _potName = PotName;
                _measType = measType;
                _FirstAnodeForPot = First;
                _MidAnodeForPot = Mid;
                _LastAnodeForPot = Last;
                _AnodePos = AnodePos;
            }
        }

        public class PotSched
        {
            public class PotSchedResults
            {
                public string _potName;
                public string _MeteringType;
                public DateTime _dt;
                public Hashtable _AnodeDropPairs;
            }

            private string _potName;
            private string _meteringType; // External schedule code; not a rod/clamp indicator
            private string[] _anodeList;
            public string PotName { get { return _potName; } }
            public string[] Anodes { get { return _anodeList; } }
            public string MType { get { return _meteringType; } }

            public PotSched(string PotName, string MeteringType, string[] AnodeList)
            {
                _potName = PotName;
                _meteringType = MeteringType; // keep for external/reporting
                _anodeList = AnodeList;
            }
        }

        private Hashtable _Schedules;
        private string _ScheduleName;
        private int _currentSchedPot;
        private int _currentSchedAnode;
        // Minimal state to support dual-pass modes without large diffs
        private bool _twoPass = false; // true for RodThenClamp or ClampThenRod
        private bool _rodFirst = true; // true for RodThenClamp
        private bool _pendingSecondPass = false; // true when next call should return the second pass for current anode

        public Schedule()
        {
            _Schedules = new Hashtable();
        }

        public void AddSchedule(string ScheduleName, string data)
        {
            if (data != null)
            {
                ArrayList PotList = new ArrayList();
                string[] pots = data.Split('\r');
                char[] comma = { ',' };

                for (int i =0; i < pots.Length; i++)
                {
                    if (pots[i].Length >0)
                    {
                        string[] potAnodes = pots[i].Split(comma);
                        string[] anodeList = new string[potAnodes.Length -6];
                        string potName = potAnodes[0]; // Extract pot name for conversion

                        for (int j =6; j < potAnodes.Length; j++)
                        {
                            string anodeString = potAnodes[j].Trim();

                            try
                            {
                                int anodeValue = Convert.ToInt32(anodeString);

                                // Ad-hoc schedules already contain positions
                                if (ScheduleName != "AH" &&
                                    PlantDetails != null &&
                                    PlantDetails.UsesAnodeNumbers())
                                {
                                    anodeValue = AnodeMapper.GetPosition(potName, anodeValue);
                                }

                                anodeList[j -6] = anodeValue.ToString();
                            }
                            catch (Exception ex)
                            {
                                if (LogAction != null)
                                    LogAction(2, "Schedule::AddSchedule", "Error converting anode value '" + anodeString + "' for pot " + potName + ". Reason: " + ex.Message, "Anode Parse Err");

                                // Keep original value if conversion fails
                                anodeList[j -6] = anodeString;
                            }
                        }

                        PotList.Add(new PotSched(potAnodes[0], potAnodes[1], anodeList));
                    }
                }
                if (_Schedules.Contains(ScheduleName))
                    _Schedules.Remove(ScheduleName);

                _Schedules.Add(ScheduleName, (PotSched[])PotList.ToArray(typeof(PotSched)));
            }
        }

        public void RemoveSchedule(string ScheduleName)
        {
            if (_Schedules.Contains(ScheduleName))
                _Schedules.Remove(ScheduleName);
        }

        public void RemoveScheduleFromCache()
        {
            _currentSchedAnode = -1;
            _currentSchedPot = -1;
            _ScheduleName = null;
            _pendingSecondPass = false;
        }

        public AnodeSched GenerateSchedule(string theSchedName)
        {
            _ScheduleName = theSchedName;
            _currentSchedPot =0;
            _currentSchedAnode =0;
            _pendingSecondPass = false;

            // Determine metering mode once per schedule start
            int idx = (GetMeasModeIndex != null) ? GetMeasModeIndex() :0;
            _twoPass = (idx ==2 || idx ==3); // RodThenClamp or ClampThenRod
            _rodFirst = (idx ==0 || idx ==2); // RodDrop or RodThenClamp

            return GetNextMeasurement();
        }

        public void ResumeScheduleMetering(string theSchedName)
        {
            _ScheduleName = theSchedName;
        }

        public string PauseScheduleMetering(string theSchedName)
        {
            string potName = "";
            if (_Schedules.Contains(theSchedName))
            {
                PotSched[] ps = (PotSched[])_Schedules[theSchedName];
                potName = ps[_currentSchedPot].PotName;
            }
            return potName;
        }

        public AnodeSched GetNextMeasurement()
        {
            AnodeSched AS = null;
            if (_ScheduleName != null && _Schedules.Contains(_ScheduleName))
            {
                PotSched[] ps = (PotSched[])_Schedules[_ScheduleName];
                if (_currentSchedPot < ps.Length)
                {
                    if (_currentSchedAnode < ps[_currentSchedPot].Anodes.Length)
                    {
                        int anodeCount = ps[_currentSchedPot].Anodes.Length;
                        int midIndex = (anodeCount -1) /2;

                        bool first = false, mid = false, last = false;
                        MeasurementType mt = MeasurementType.RodDrop;

                        if (_twoPass)
                        {
                            if (!_pendingSecondPass)
                            {
                                // First pass for this anode
                                mt = _rodFirst ? MeasurementType.RodDrop : MeasurementType.ClampDrop;
                                first = (_currentSchedAnode ==0); // First flag on first pass of first anode
                                // no mid/last on first pass
                                _pendingSecondPass = true; // Next call will return second pass for same anode
                            }
                            else
                            {
                                // Second pass for this anode
                                mt = _rodFirst ? MeasurementType.ClampDrop : MeasurementType.RodDrop;
                                mid = (_currentSchedAnode == midIndex);
                                last = (_currentSchedAnode == (anodeCount -1));
                                _pendingSecondPass = false; // Advance to next anode after returning this
                            }
                        }
                        else
                        {
                            // Single-pass modes
                            int idx = (GetMeasModeIndex != null) ? GetMeasModeIndex() :0;
                            mt = (idx ==1) ? MeasurementType.ClampDrop : MeasurementType.RodDrop;
                            first = (_currentSchedAnode ==0);
                            mid = (_currentSchedAnode == midIndex);
                            last = (_currentSchedAnode == (anodeCount -1));
                        }

                        AS = new AnodeSched(
                            ps[_currentSchedPot].PotName,
                            ps[_currentSchedPot].Anodes[_currentSchedAnode],
                            mt, first, mid, last);

                        // Increment index if we completed the second pass OR in single-pass modes
                        if (!_twoPass || (_twoPass && !_pendingSecondPass))
                        {
                            _currentSchedAnode++;
                            if (_currentSchedAnode >= ps[_currentSchedPot].Anodes.Length)
                            {
                                // Move to next pot and reset per-pot state
                                _currentSchedPot++;
                                _currentSchedAnode =0;
                                _pendingSecondPass = false;
                            }
                        }
                    }
                }
            }
            return AS;
        }

        public AnodeSched GetPreviousAnodeFromSched()
        {
            if (_currentSchedAnode >0)
                _currentSchedAnode--;
            else
            {
                if (_currentSchedPot >0)
                {
                    _currentSchedPot--;
                    PotSched[] ps = (PotSched[])_Schedules[_ScheduleName];
                    _currentSchedAnode = ps[_currentSchedPot].Anodes.Length -1;
                }
            }
            _pendingSecondPass = false; // Align to first pass when moving backward
            return GetNextMeasurement();
        }

        public AnodeSched GetNextPotFromSched()
        {
            if (_currentSchedPot < ((PotSched[])_Schedules[_ScheduleName]).Length -1)
            {
                _currentSchedPot++;
                _currentSchedAnode =0;
                _pendingSecondPass = false;
            }
            return GetNextMeasurement();
        }

        public AnodeSched GetPreviousPotFromSched()
        {
            if (_currentSchedPot >0)
            {
                _currentSchedPot--;
                _currentSchedAnode =0;
                _pendingSecondPass = false;
            }
            return GetNextMeasurement();
        }

        public ArrayList GetScheduleNames()
        {
            ArrayList al = new ArrayList();
            foreach (string s in _Schedules.Keys)
                al.Add(s);
            return al;
        }

        public PotSched[] GetSchedule(string ScheduleName)
        {
            return (PotSched[])_Schedules[ScheduleName];
        }

        public string BuildAdhocSchedule(string Pot, int[] Anodes)
        {
            string Sched = "";
            try
            {
                // Legacy external format: Pot,AH,9Xh,L<line>,R,N,<anodes...>
                string lineToken = "";
                if (Pot.IsNumbersOnly())
                {
                    int temp = (Convert.ToInt32(Pot) +100) /200;
                    lineToken = temp.ToString();
                }

                string header = Pot + ",AH,9Xh,L" + lineToken + ",R,N";

                string AnodeList = "";
                for (int i =0; i < Anodes.Length; i++)
                    AnodeList += "," + Anodes[i].ToString();
                Sched = header + AnodeList + "\r";
            }
            catch (Exception ex)
            {
                if (LogAction != null)
                    // Use '2' for Severe, as per the ErrSeverity enum order.
                    LogAction(2, "Schedule::BuildAdhocSchedule", "Error building Adhoc schedule for pot " + Pot + ". Reason: " + ex.Message, "Sched Build Err");
            }
            return Sched;
        }
    }
}