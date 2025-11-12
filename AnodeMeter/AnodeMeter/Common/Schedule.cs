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
            private string _meteringType;
            private string[] _anodeList;
            public string PotName { get { return _potName; } }
            public string[] Anodes { get { return _anodeList; } }
            public string MType { get { return _meteringType; } }

            public PotSched(string PotName, string MeteringType, string[] AnodeList)
            {
                _potName = PotName;
                _meteringType = MeteringType;
                _anodeList = AnodeList;
            }
        }

        private Hashtable _Schedules;
        private string _ScheduleName;
        private int _currentSchedPot;
        private int _currentSchedAnode;

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

                for (int i = 0; i < pots.Length; i++)
                {
                    if (pots[i].Length > 0)
                    {
                        string[] potAnodes = pots[i].Split(comma);
                        string[] anodeList = new string[potAnodes.Length - 6];
                        string potName = potAnodes[0]; // Extract pot name for conversion

                        for (int j = 6; j < potAnodes.Length; j++)
                        {
                            string anodeString = potAnodes[j].Trim();

                            try
                            {
                                int anodeValue = Convert.ToInt32(anodeString);

                                // NEW: Convert anode number to position if site uses anode numbers
                                if (PlantDetails != null && PlantDetails.UsesAnodeNumbers())
                                {
                                    anodeValue = AnodeMapper.GetPosition(potName, anodeValue);
                                }

                                anodeList[j - 6] = anodeValue.ToString();
                            }
                            catch (Exception ex)
                            {
                                if (LogAction != null)
                                    LogAction(2, "Schedule::AddSchedule", "Error converting anode value '" + anodeString + "' for pot " + potName + ". Reason: " + ex.Message, "Anode Parse Err");

                                // Keep original value if conversion fails
                                anodeList[j - 6] = anodeString;
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
        }

        public AnodeSched GenerateSchedule(string theSchedName)
        {
            _ScheduleName = theSchedName;
            _currentSchedPot = 0;
            _currentSchedAnode = 0;
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
                        bool first = _currentSchedAnode == 0;
                        bool mid = _currentSchedAnode == (ps[_currentSchedPot].Anodes.Length / 2);
                        bool last = _currentSchedAnode == (ps[_currentSchedPot].Anodes.Length - 1);
                        MeasurementType mt = ps[_currentSchedPot].MType == "RD" ? MeasurementType.RodDrop : MeasurementType.ClampDrop;
                        AS = new AnodeSched(ps[_currentSchedPot].PotName, ps[_currentSchedPot].Anodes[_currentSchedAnode], mt, first, mid, last);

                        // Increment the anode index for the next call
                        _currentSchedAnode++;
                        if (_currentSchedAnode >= ps[_currentSchedPot].Anodes.Length)
                        {
                            // If we've reached the end of the anodes for this pot, move to the next pot
                            _currentSchedPot++;
                            _currentSchedAnode = 0;
                        }
                    }
                }
            }
            return AS;
        }

        public AnodeSched GetPreviousAnodeFromSched()
        {
            if (_currentSchedAnode > 0)
                _currentSchedAnode--;
            else
            {
                if (_currentSchedPot > 0)
                {
                    _currentSchedPot--;
                    PotSched[] ps = (PotSched[])_Schedules[_ScheduleName];
                    _currentSchedAnode = ps[_currentSchedPot].Anodes.Length - 1;
                }
            }
            return GetNextMeasurement();
        }

        public AnodeSched GetNextPotFromSched()
        {
            if (_currentSchedPot < ((PotSched[])_Schedules[_ScheduleName]).Length - 1)
            {
                _currentSchedPot++;
                _currentSchedAnode = 0;
            }
            return GetNextMeasurement();
        }

        public AnodeSched GetPreviousPotFromSched()
        {
            if (_currentSchedPot > 0)
            {
                _currentSchedPot--;
                _currentSchedAnode = 0;
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
                string AnodeList = "";
                for (int i = 0; i < Anodes.Length; i++)
                    AnodeList += "," + Anodes[i].ToString();
                Sched = Pot + ",RD,AH,L1,R1,S1" + AnodeList + "\r";
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