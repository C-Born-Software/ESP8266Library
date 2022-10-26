using System;
using System.IO;
using System.Text;
//using Microsoft.SPOT;
using System.Collections;

namespace AnodeMeter.Common
{
    public class Schedule
    {

        public class AnodeSched
        {
            public string Name { get { return _AnodePos; } }
            public string _potName;
            public MeasurementType _measType;
            public string _AnodePos;
            public bool _FirstAnodeForPot;
            public bool _MidAnodeForPot;
            public bool _LastAnodeForPot;
            public AnodeSched(string Pot, MeasurementType MeasType, string Anode, bool FirstAnode, bool MidAnode, bool LastAnode)
            {
                _potName = Pot;
                _measType = MeasType;
                _AnodePos = Anode;
                _FirstAnodeForPot = FirstAnode;
                _MidAnodeForPot = MidAnode;
                _LastAnodeForPot = LastAnode;
            }
        }

        private AnodeSched[] _AnodeSchedCache = null;
        private int _anodeSchedIndex = 0;

        private AnodeSched[] _AnodeSchedCacheSaved = null;
        private int _anodeSchedIndexSaved;

        private PotSched[] _savedPotSched = null;

        public class PotSchedResults
        {
            public string _potName;
            public DateTime MeteredDate;
            public string _MeteringType;
            public double[] _MillivoltValues;


        }


        public class PotSched
        {
            private string _potName;
            private string _meteringType;
            private string[] _anodeList;

            public string PotName { get { return _potName; } }
            public string[] Anodes { get { return _anodeList; } }
            public string MType { get { return _meteringType; } }

            public PotSched(string PotSched)
            {
                string[] schedParts = PotSched.Split(new char[] { ',' });
                if (schedParts.Length > 1)
                {
                    ArrayList anodes = new ArrayList();
                    _potName = schedParts[0];
                    _meteringType = schedParts[1];

                    for (int anode = 6; anode < schedParts.Length; anode++)
                        anodes.Add(schedParts[anode]);

                    _anodeList = (string[])anodes.ToArray(typeof(string));
                }
            }
        }

        public class ScheduleInformation
        {
            public DateTime _ScheduleShift;
            public string _ScheduleName;
            public PotSched[] _Pots = null;

            public ScheduleInformation(string ScheduleName, string sched)
            {
                _ScheduleName = ScheduleName;
                _ScheduleShift = SmelterDetails.GetShiftStartTime(DateTime.Now);

                try
                {
                    ArrayList pots = new ArrayList();
                    string[] potParts = sched.Split(new char[] { '\r' });

                    foreach (string ps in potParts)
                        if (ps.Length > 0)
                            pots.Add(new PotSched(ps));


                    _Pots = (PotSched[])pots.ToArray(typeof(PotSched));
                }
                catch (Exception ex)
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Warning, "Schedule::ctor", "Schedule Parsing Exception: " + ex.Message, "sched Parse err.");
                }
            }
        }

        private Hashtable Schedules;

        public Schedule()
        {
            Schedules = new Hashtable();
        }


        public ArrayList GetScheduleNames()
        {
            ArrayList ScheduleNameList = new ArrayList();
            foreach (DictionaryEntry d in Schedules)
            {
                ScheduleNameList.Add(d.Key.ToString());

                //     ScheduleNameList.Add(((ScheduleInformation)o)._ScheduleName);
            }
            return ScheduleNameList;
        }


        public void AddSchedule(string ScheduleName, string data)
        {
            ScheduleInformation NewSchedule = new ScheduleInformation(ScheduleName, data);
            if (Schedules[ScheduleName] == null)
                Schedules.Add(ScheduleName, NewSchedule);
            else
                Schedules[ScheduleName] = NewSchedule;
        }

        public void RemoveSchedule(string ScheduleName)
        {
            Schedules.Remove(ScheduleName);
        }


        public string PauseScheduleMetering(string theSchedName)
        {
            string _currentPot = "";

            if (_AnodeSchedCache != null)
            {
                _AnodeSchedCacheSaved = _AnodeSchedCache;
                _anodeSchedIndexSaved = _anodeSchedIndex;
                _currentPot = _AnodeSchedCache[_anodeSchedIndex]._potName;
                _savedPotSched = GetSchedule(theSchedName);
                RemoveScheduleFromCache();
            }
            return _currentPot;

        }

        public void ResumeScheduleMetering(string theSchedName)
        {

            _AnodeSchedCache = _AnodeSchedCacheSaved;
            _anodeSchedIndex = _anodeSchedIndexSaved;

        }

        public AnodeSched GenerateSchedule(string ScheduleName)
        {
            AnodeSched FirstAnode = null;
            PotSched[] s = GetSchedule(ScheduleName);

            ArrayList measurements = new ArrayList();
            MeasModeOption mmo = (MeasModeOption)Globals.MeasurementModeIndex;

            if (s != null)
            {
                AnodeSched thisRod = null;
                AnodeSched thisClamp = null;
                bool RodFirst = (mmo == MeasModeOption.RodDrop || mmo == MeasModeOption.RodThenClamp);
                bool OneMeasPerRod = (mmo == MeasModeOption.RodDrop || mmo == MeasModeOption.ClampDrop);

                for (int Pot = 0; Pot < s.Length; Pot++)
                {
                    for (int anod = 0; anod < s[Pot].Anodes.Length; anod++)
                    {
                        thisRod = null;
                        thisClamp = null;

                        if (mmo == MeasModeOption.RodDrop || mmo == MeasModeOption.ClampThenRod || mmo == MeasModeOption.RodThenClamp)
                            thisRod = new AnodeSched(s[Pot].PotName,
                                                     MeasurementType.RodDrop,
                                                     s[Pot].Anodes[anod],
                                                     (anod == 0 && RodFirst),
                                                     (anod == ((s[Pot].Anodes.Length - 1) / 2) && (!RodFirst || OneMeasPerRod)),
                                                     (anod == (s[Pot].Anodes.Length - 1) && (!RodFirst || OneMeasPerRod))
                                                     );

                        if (mmo == MeasModeOption.ClampDrop || mmo == MeasModeOption.ClampThenRod || mmo == MeasModeOption.RodThenClamp)
                            thisClamp = new AnodeSched(s[Pot].PotName,
                                                     MeasurementType.ClampDrop,
                                                     s[Pot].Anodes[anod],
                                                     (anod == 0 && !RodFirst),
                                                     (anod == ((s[Pot].Anodes.Length - 1) / 2) && (RodFirst || OneMeasPerRod)),
                                                     (anod == (s[Pot].Anodes.Length - 1) && (RodFirst || OneMeasPerRod))
                                                     );

                        if (RodFirst)
                        {
                            measurements.Add(thisRod);

                            if (thisClamp != null)
                                measurements.Add(thisClamp);
                        }
                        else
                        {
                            measurements.Add(thisClamp);

                            if (thisRod != null)
                                measurements.Add(thisRod);
                        }

                    }
                }
                if (measurements.Count != 0)
                {
                    _AnodeSchedCache = new AnodeSched[measurements.Count];
                    _AnodeSchedCache = (AnodeSched[])measurements.ToArray(typeof(AnodeSched));
                    FirstAnode = _AnodeSchedCache[0];
                    _anodeSchedIndex = 0;
                }
            }
            return FirstAnode;
        }

        public AnodeSched GetNextMeasurement()
        {
            AnodeSched aSched = null;
            try
            {
                if (_AnodeSchedCache != null && _anodeSchedIndex >= 0 && _anodeSchedIndex < (_AnodeSchedCache.Length - 1))
                {
                    _anodeSchedIndex++;
                    aSched = _AnodeSchedCache[_anodeSchedIndex];
                }
            }
            catch (Exception ex)
            {
                string asl = _AnodeSchedCache == null ? " null" : _AnodeSchedCache.Length.ToString();
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "Schedule::GetNextAnodeFromSched", "Exception thrown: \"_anodeSchedIndex\"= " + _anodeSchedIndex.ToString() + ", \"_AnodeSchedCache.Length\"= " + asl + ". Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Software err!");
            }
            return aSched;
        }

        public AnodeSched GetPreviousAnodeFromSched()
        {
            AnodeSched aSched = null;

            try
            {
                if (_AnodeSchedCache != null && _AnodeSchedCache.Length >= _anodeSchedIndex && _anodeSchedIndex > 0)
                {
                    _anodeSchedIndex--;
                    aSched = _AnodeSchedCache[_anodeSchedIndex];
                }
            }
            catch (Exception ex)
            {
                string asl = (_AnodeSchedCache == null ? "\"_AnodeSchedCache\" is null" : ("\"_AnodeSchedCache.Length\" = " + _AnodeSchedCache.Length.ToString()));
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "Schedule::GetPreviousAnodeFromSched", "Exception thrown: \"_anodeSchedIndex\"= " + _anodeSchedIndex.ToString() + ", " + asl + ". Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Software err!");
            }
            return aSched;
        }

        public AnodeSched GetNextPotFromSched()
        {
            AnodeSched aSched = null;
            try
            {
                int _newindex = _anodeSchedIndex;
                Boolean _found = false;
                //move along array until potname changes from the current one

                if (_AnodeSchedCache != null && _anodeSchedIndex < (_AnodeSchedCache.Length - 1))
                {
                    string _currentpot = _AnodeSchedCache[_anodeSchedIndex]._potName;

                    for (int i = _newindex; i < _AnodeSchedCache.Length - 1; i++)
                    {
                        if (_AnodeSchedCache[i]._potName.ToString() != _currentpot.ToString() && !_found)
                        {
                            _newindex = i;
                            _found = true;
                            break;
                        }
                    }
                    _anodeSchedIndex = _newindex;
                    aSched = _AnodeSchedCache[_newindex];
                }
            }
            catch (Exception ex)
            {
                string asl = _AnodeSchedCache == null ? " null" : _AnodeSchedCache.Length.ToString();
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "Schedule::GetNextPotFromSched", "Exception thrown: \"_anodeSchedIndex\"= " + _anodeSchedIndex.ToString() + ", \"_AnodeSchedCache.Length\"= " + asl + ". Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Software err!");
            }
            return aSched;
        }

        public AnodeSched GetPreviousPotFromSched()
        {
            AnodeSched aSched = null;
            try
            {
                int _index1 = _anodeSchedIndex - 1;
                int _index2 = _anodeSchedIndex;

                Boolean _found = false;

                // Find out if current anode is first anode in pot
                // if yes, wind back to first anode of previous pot
                // if no wind back to first anode of this pot

                if (_AnodeSchedCache != null && _anodeSchedIndex > 0 && _anodeSchedIndex < _AnodeSchedCache.Length)
                {
                    //first anode for this pot found so move back to previous pot's first anode
                    do
                    {
                        if (_AnodeSchedCache[_index1]._potName.ToString() != _AnodeSchedCache[_index2]._potName.ToString())
                        {
                            if (_index1 < 24)
                            {
                                _anodeSchedIndex = 0;
                                _found = true;
                            }
                            else
                            {
                                _index1 -= 1;
                                _index2 -= 1;

                                for (int i = _index1; i >= 0; i--)
                                {
                                    if (_AnodeSchedCache[i]._potName.ToString() != _AnodeSchedCache[_index2]._potName.ToString() && !_found)
                                    {
                                        _anodeSchedIndex = i + 1;
                                        _found = true;
                                    }
                                }
                            }

                        }
                        else
                            //Not the first anode for this pot so move back to this pot's first anode
                            if (_AnodeSchedCache[_index1]._potName.ToString() == _AnodeSchedCache[_index2]._potName.ToString())
                        {
                            if (_index1 < 24)
                            {
                                _found = true;
                                _anodeSchedIndex = 0;
                            }

                            else
                            {
                                for (int i = _index1; i >= 0; i--)
                                {
                                    if (_AnodeSchedCache[i]._potName.ToString() != _AnodeSchedCache[_index2]._potName.ToString() && !_found)
                                    {
                                        _found = true;
                                        _anodeSchedIndex = i + 1;
                                    }

                                }
                            }

                        }

                    } while (!_found);

                }
                aSched = _AnodeSchedCache[_anodeSchedIndex];
            }
            catch (Exception ex)
            {
                string asl = _AnodeSchedCache == null ? " null" : _AnodeSchedCache.Length.ToString();
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "Schedule::GetPreviousPotFromSched", "Exception thrown: \"_anodeSchedIndex\"= " + _anodeSchedIndex.ToString() + ", \"_AnodeSchedCache.Length\"= " + asl + ". Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Software err!");
            }
            return aSched;
        }

        public void RemoveScheduleFromCache()
        {
            _AnodeSchedCache = null;
        }


        public PotSched[] GetSchedule(string ScheduleName)
        {
            PotSched[] s = null;
            ScheduleInformation thisSched = (ScheduleInformation)Schedules[ScheduleName];
            if (thisSched != null)
                s = thisSched._Pots;

            return s;


        }
        public ArrayList GetPotsInSchedule(string ScheduleName)
        {
            PotSched[] ps = GetSchedule(ScheduleName);
            ArrayList potNames = new ArrayList();
            if (ps != null)
            {
                foreach (PotSched s in ps)
                    potNames.Add(s.PotName);
            }
            return potNames;

        }

        public string BuildAdhocSchedule(string Pot, int[] Anodes)
        {
            string Line = string.Empty;

            if (Pot.IsNumbersOnly())
            {
                int temp = (Convert.ToInt32(Pot) + 100) / 200;
                Line = temp.ToString();
            }
            else
            {
            }

            //this string must be the same as what gets generated out of the stored procedure
            string sOut = Pot + "," + "AH" + "," + "9Xh" + "," + "L" + Line + "," + "R" + "," + "N";

            for (int i = 0; i < Anodes.Length; i++)
            {
                sOut += "," + Anodes[i].ToString();
            }

            return sOut;
        }


        public int CurrentScheduleIndex()
        {
            return _anodeSchedIndex;
        }
    }
}
