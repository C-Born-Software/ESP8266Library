using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Diagnostics;
using Hardware.LcdCharacterDisplay;
using System.Threading;
//using GHIElectronics.TinyCLR.Data.Xml;
using GHIElectronics.TinyCLR.Native;
//using GHIElectronics.TinyCLR.Update;
//using GHIElectronics.TinyCLR.Devices.Watchdog;
using System.Reflection;
using AnodeMeter.Common;
using AnodeMeter.Hardware;
using PervasiveDigital.Utilities;
using AnalogInput = AnodeMeter.Common.AnalogInput;
using System.Net;

namespace AnodeMeter
{
    public class AnodeMeter
    {
        public TimeManager _tm;
        private MenuNavigation Navigation;
        private Logging Log;
        private DataStore _ds;
        private Schedule AnodeMeterSchedules;
        private Schedule _previousAdHocSchedule = null;
        private enum DigitPosition { Ones, Tens, Hundreds, Thousands, Canceled };
        private SmelterDetails _plant;
        private SignalProcessor _sp;
        private BinaryTransport.ConnectionState _cs = BinaryTransport.ConnectionState.Detached;
        private DateTime _fileDateTime;
        private DateTime dtCheckForDirtyData = DateTime.MinValue;
        private int MeterNumber;
        private Boolean NoPotsToMeter;
        private double _currentMeasVolts;
        private Timer _hkTimer = null;
        private Int32 TimeLock = 0;
        private DateTime _dtNextTimeSync = DateTime.MinValue;
        private string CurrentChoice = string.Empty;
        private string AdhocPotSelected = string.Empty;
        private SelectPotStruct SelectPot = new SelectPotStruct();
        private ArrayList _currentPotList = new ArrayList();
        private PotMeasurementRecord _PotAnodeResults = null;
        private Schedule.AnodeSched _CurrentAnode = null;
        private Schedule.AnodeSched _prevAdHocAnode = null;
        private ArrayList LastThreeReadings = new ArrayList();
        private string _currentlyExecutingSchedule;
        private LcdDisplay.CursorPosition LastThreeReadingsPosition = new LcdDisplay.CursorPosition(1, 1);
        private LcdDisplay.CursorPosition MillivoltPosition = new LcdDisplay.CursorPosition(0, 11);
        private LcdDisplay.CursorPosition CurrentAnodeNumberPositionIcon = new LcdDisplay.CursorPosition(0, 13);
        private LcdDisplay.CursorPosition CurrentAnodeNumberPosition = new LcdDisplay.CursorPosition(0, GlobalConsts.POT_NAME_LENGTH + 2);
        private LcdDisplay.CursorPosition CurrentPotPositionIcon = new LcdDisplay.CursorPosition(0, GlobalConsts.POT_NAME_LENGTH + 5);
        private LcdDisplay.CursorPosition CurrentPotPosition = new LcdDisplay.CursorPosition(0, 0);
        private LcdDisplay.CursorPosition CurrentPotAnodeSeparatorPosition = new LcdDisplay.CursorPosition(0, GlobalConsts.POT_NAME_LENGTH + 1);
        private LcdDisplay.CursorPosition CurrentMeteringTypePosition = new LcdDisplay.CursorPosition(0, 8);
        private LcdDisplay.CursorPosition RotarySwitchCounterPosition = new LcdDisplay.CursorPosition(1, 10);
        private LcdDisplay.CursorPosition MenuChoicePosition = new LcdDisplay.CursorPosition(1, 1);
        private LcdDisplay.CursorPosition SubMenuChoicePosition = new LcdDisplay.CursorPosition(1, 15);
        private LcdDisplay.CursorPosition MessageChoicePosition = new LcdDisplay.CursorPosition(0, 0);
        private LcdDisplay.CursorPosition PotListPosition = new LcdDisplay.CursorPosition(1, 0);
        private LcdDisplay.CursorPosition SmileyPosition = new LcdDisplay.CursorPosition(0, 15);
        private LcdDisplay.CursorPosition PotPosition = new LcdDisplay.CursorPosition(0, 15);
        private LcdDisplay.CursorPosition CancelPosition = new LcdDisplay.CursorPosition(1, 8);
        private LcdDisplay.CursorPosition AdhocPotPosition = new LcdDisplay.CursorPosition(1, 1);
        private LcdDisplay.CursorPosition BatteryPosition = new LcdDisplay.CursorPosition(1, 0);

        private LcdDisplay.CursorPosition Display1 = new LcdDisplay.CursorPosition(0, 0);
        private LcdDisplay.CursorPosition Display2 = new LcdDisplay.CursorPosition(1, 1);
        private AnodeMeterButtons _amb;
        public LcdDisplay _lcd;
        private BoardSetup _bsp;
        private AnalogInput _ai;
        public Common.LED _led;
        private Common.SystemInit _sys;
        //private BinaryTransport _gw = null; // Create a reference for Gateway communications 
        private WifiTransport _gw_wifi = null;
        private BinaryTransport _gw_usb = null;
        private DateTime dtLastUserActivity = DateTime.Now;
        private DateTime dtIgnoreButtonsUntil = DateTime.MinValue;
        private DateTime _dtShiftOfLoadedSchedules = DateTime.MinValue;
        private BatteryCharge _bc = null;
        private DateTime _dtShiftWanted;
        private DateTime _dtSchedAttemptFetchTime;
        private DateTime _dtUploadLogs;
        private DateTime _dtWaitaFewSeconds = DateTime.MinValue;
        private bool _bWaitingToHibernate = false;
        private string _systemConfigFileHash = null;
        private DateTime _dtQueueConfigCheck = DateTime.MaxValue;
        private bool _bRebootNextPass = false;
        private bool _previousHouseKeepingPassFinished = true;
        private bool _buttonEventInProgress = false;
        private bool _AiEventInProgress = false;
        private BinaryTransport.ConnectionState _previousConnectionState = BinaryTransport.ConnectionState.Detached;
        private TimeSpan INACTIVITYTIME = new TimeSpan(TimeSpan.TicksPerMinute * 20);
        private double BATTERY_TOO_LOW_PERCENT = 10;  // volts
        private const string NO_METERING_SCHEDULED = "No Pots Scheduled";
        private Int16 HouseKeepDivider = 0;
        private Int16 WifiCheckDivider = 0;
        private static bool bForceScheduleReload = false;   // Use for testing, set on down button hold in WiFi mode. DAV 17JAN2024

        public BinaryTransport ActiveGW()
        {
            return Globals.HaveWifi ? _gw_wifi : _gw_usb; //TODO Fix with conditionals, USB connected etc checks later
        }
        public void RegisterActivity()
        {
            dtLastUserActivity = DateTime.Now;
            _bWaitingToHibernate = false;
            _lcd.CancelTimedMessages();
        }
        
        public void TriggerConfigCheck()
        {
            _dtQueueConfigCheck = DateTime.Now;
        }
        
        private class SelectPotStruct
        {
            public bool _bCanceled;
            public string _sPot;
            private int _posIndex;
            private int _posVerticalIndex;
            private string[] _thisDigitSelectionRange;
            public delegate bool PotNameCheck(string Potname, string requiredLeftPart, ref string ProposedPotName);
            private PotNameCheck _potNameCheck = null;
            public delegate string[] potDigitsInfo(string rightPartOfPotname);
            private potDigitsInfo _piProvider = null;

            public void SetPotInfoProvider(potDigitsInfo pi)
            {
                _piProvider = pi;
            }
            public void SetPotNameCheck(PotNameCheck pnc)
            {
                _potNameCheck = pnc;
            }
            public void UsePot(string PotTag)
            {
                _sPot = PotTag;
                _posIndex = 0;
                GetPotSelectionOptions();
                _bCanceled = false;
            }
            public void StartFresh()
            {
                UsePot(_sPot);
            }
            public void GetPotSelectionOptions()
            {
                if (_piProvider != null)
                {
                    try
                    {
                        _posVerticalIndex = 0;
                        _thisDigitSelectionRange = _piProvider(_posIndex == 0 ? "" : _sPot.Left(_posIndex));

                        string thisChar = new string(new char[] { _sPot[_posIndex] });

                        for (int v = 0; v < _thisDigitSelectionRange.Length; v++)
                        {
                            if (_thisDigitSelectionRange[v] == thisChar)
                            {
                                _posVerticalIndex = v;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.IssueEvent(Logging.ErrSeverity.Severe, "SelectPotStruct::GetPotSelectionOptions", "_sPot =  " + (_sPot == null ? "null" : _sPot) + ", _posIndex = " + _posIndex.ToString() + ". Reason: " + ex.Message, "Software err");
                    }
                }
            }
            public void DigitRotateUp()
            {
                if (_posIndex < _sPot.Length)
                {
                    _posVerticalIndex = (_posVerticalIndex + 1) % _thisDigitSelectionRange.Length;
                    DigitRotate();
                }
            }
            public void DigitRotateDown()
            {
                if (_posIndex < _sPot.Length)
                {
                    _posVerticalIndex = (_posVerticalIndex + _thisDigitSelectionRange.Length - 1) % _thisDigitSelectionRange.Length;
                    DigitRotate();
                }
            }
            private void DigitRotate()
            {
                string oldPotName = _sPot;
                string requiredLeftPart = _sPot.Left(_posIndex) + _thisDigitSelectionRange[_posVerticalIndex];

                _sPot = requiredLeftPart + _sPot.Right(_sPot.Length - _posIndex - 1);
                if (_potNameCheck != null)
                {
                    string sProposedPot = "";
                    // Check if the selected pot name is legal
                    if (!_potNameCheck(_sPot, requiredLeftPart, ref sProposedPot))
                    {
                        // if illegal, use the proposed pot name instead
                        if (sProposedPot != null && sProposedPot.Length > 0)
                        {
                            _sPot = sProposedPot;
                        }
                        else
                            _sPot = oldPotName;
                    }

                    GetPotSelectionOptions();
                }
            }

            public SelectPotStruct()
            {
                _sPot = "";
                _posIndex = 0;
            }
            public void MoveLeft()
            {
                if (_posIndex == 0)
                {
                    _posIndex = _sPot.Length;
                    _bCanceled = true;
                }
                else
                {
                    if (_posIndex == _sPot.Length)
                        _posIndex = _sPot.Length - 1;
                    else
                        _posIndex = _posIndex == 0 ? 0 : _posIndex - 1;

                    GetPotSelectionOptions();
                    _bCanceled = false;
                }
            }
            public bool MoveRight()
            {
                bool bMoved = false;
                int oldIndex = _posIndex;

                if (_posIndex == _sPot.Length - 1)
                {
                    _posIndex = _sPot.Length;
                    _bCanceled = true;
                }
                else
                {
                    if (_posIndex == _sPot.Length)
                        _posIndex = 0;
                    else
                        _posIndex = _posIndex == (_sPot.Length - 1) ? _posIndex : _posIndex + 1;

                    if (oldIndex != _posIndex)
                    {
                        GetPotSelectionOptions();
                        bMoved = true;
                    }
                    _bCanceled = false;
                }
                return bMoved;
            }
            public string GetEditString()
            {
                string editString = "";
                if (_sPot.Length > 0)
                {
                    if (_bCanceled)
                        editString = _sPot + "  ";

                    else
                    {
                        string leftPart = _posIndex == 0 ? "" : _sPot.Left(_posIndex);
                        string midPart = "[" + _sPot[_posIndex] + "]";
                        string rightPart = _posIndex == (_sPot.Length - 1) ? "" : _sPot.Right(_sPot.Length - _posIndex - 1);
                        editString = leftPart + midPart + rightPart;
                    }
                }
                return editString;
            }
            public string GetPot()
            {
                return _sPot;
            }
            public bool IsCancelled()
            {
                return _bCanceled;
            }

        }

        public class PotMeasurementRecord
        {
            public class MeasurementTypeReading
            {
                public double VoltageDrop { get { return _measDrop; } }
                internal double _measDrop;

                public MeasurementTypeReading(double Measurement)
                {
                    _measDrop = Measurement;
                }
            }

            DateTime SampleDate;
            private int DateOffset = 0; // Date offset (seconds) to make unique in DB, which only uses date and pot number ar promary key
            internal string PotNumber;
            string MeterNumber;
            MeasurementTypeReading[] _aDrops;
            MeasurementTypeReading[] _cDrops;

            public PotMeasurementRecord(DateTime SampleDate, string PotNumber, string MeterNumber, int MaxAnodeCount)
            {
                this.SampleDate = SampleDate;
                this.PotNumber = PotNumber;
                this.MeterNumber = MeterNumber;
                _aDrops = new MeasurementTypeReading[MaxAnodeCount];
                _cDrops = new MeasurementTypeReading[MaxAnodeCount];
            }

            public void AddMeasuredValue(string AnodeNumber, double MeasuredVolts, MeasurementType measType)
            {
                try
                {
                    int anodeIndex = Convert.ToInt32(AnodeNumber) - 1;
                    if (measType == MeasurementType.RodDrop)
                        _aDrops[anodeIndex] = new MeasurementTypeReading(MeasuredVolts * 1000.0);
                    else if (measType == MeasurementType.ClampDrop)
                        _cDrops[anodeIndex] = new MeasurementTypeReading(MeasuredVolts * 1000.0);
                }
                catch (Exception ex)
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "PotMeasurementRecord::AddMeasuredValue", "Attempted to add a reading for anode= " + ((AnodeNumber == null) ? "null" : AnodeNumber) + ", MeasuredVolts=" + MeasuredVolts.ToString() + ". Reason: " + ex.Message, "Software err");
                }
            }

            private string ConcatenateMeasValues(MeasurementTypeReading[] rDrops, ArrayList arl = null)
            {
                string csv = "";
                int nonNullValues = 0;
                int i = 1;
                foreach (MeasurementTypeReading ar in rDrops)
                {
                    if ((ar != null) && (arl == null || arl.Contains(i.ToString())))
                    {
                        csv += ("," + ar.VoltageDrop.ToString("F2"));
                        nonNullValues++;
                    }
                    else
                        csv += ",";
                    ++i;
                }
                return (nonNullValues == 0 ? "" : csv);
            }

            public string MyToString(String ScheduleName, ArrayList arl = null)
            {
                string sOut = "";
                string rDrops = ConcatenateMeasValues(_aDrops, arl);
                string cDrops = ConcatenateMeasValues(_cDrops, arl);

                if (rDrops != "")
                    sOut += (SampleDate + new TimeSpan(0, 0, DateOffset++)).ToString("yyyy-MM-dd HH:mm:ss") + "," + PotNumber.TrimLeadingChar('0') + "," +
                        ScheduleName + ":RodDrops" + "," + MeterNumber.ToString() + rDrops + "\n";

                if (cDrops != "")
                    sOut += (SampleDate + new TimeSpan(0, 0, DateOffset++)).ToString("yyyy-MM-dd HH:mm:ss") + "," + PotNumber.TrimLeadingChar('0') + "," +
                        ScheduleName + ":ClampDrops" + "," + MeterNumber.ToString() + cDrops + "\n";

                return sOut;
            }

            // Create output data for a masked schedule
            public string MaskedPotString(string MaskSched)
            {
                char[] comma = { ',' };
                string[] RecordParts = MaskSched.Split(comma);
                if (RecordParts.Length < 6)
                    return "";

                string ScheduleName = RecordParts[2];
                ArrayList arl = new ArrayList();
                for (int i = 6; i < RecordParts.Length; ++i)
                    arl.Add(RecordParts[i].Trim());

                return MyToString(ScheduleName, arl);
            }
        }

        public AnodeMeter()
        {


#if (EMULATOR)
            _sys = new InitEmulator();
            _bc = new EmuBatteryCharge();
            Globals.SDCardPresent = true;
#else
            _sys = new Hardware.ConfigureSystem(this);
#endif
            Profile.DebugTime("HW Config Done"); //TODO DAV DEBUG
            _sys.InitOnStart();
            Profile.DebugTime("InitOnStart Done"); //TODO DAV DEBUG

            if (Globals.SDCardPresent)
            {
                try
                {
                    _ds = new DataStore();
                    Profile.DebugTime("DataStore Constructed"); //TODO DAV DEBUG
                    LoadFactoryDefaultsFromSD(_ds);
                    Globals.SDCardFault = false;
                }
                catch (Exception ex)
                {
                    //_ds=null;
                    Globals.SDCardFault = true;
                    Logging.IssueEvent(Logging.ErrSeverity.Fatal, "Read SD", ex.Message.ToString(), "Error");
                }
            }
            Profile.DebugTime("SD Settings Loaded"); //TODO DAV DEBUG
            _lcd.EnableTask();

            _plant = new SmelterDetails();

            Logging.RegisterMeterID(_sys.GetMeterNumber);
            Globals.DeviceID = _sys.GetMeterNumber;     // Duplication for visibility, clean up later...

            NoPotsToMeter = true;
            _sp = new SignalProcessor();
        }



        internal void Run()
        {
            try
            {
                SetupMeterType();
                Log = new Logging(_lcd);

                RegisterActivity();
                _dtWaitaFewSeconds = DateTime.Now.AddSeconds(3);
                //_hkTimer = new Timer(DoPeriodicHouseKeeping, null, 0, GlobalConsts.HOUSE_KEEPING_CHECK_SECONDS * 1000);
                _hkTimer = new Timer(EverySecond, null, 0, 1000);

                if (Globals.CfgState == Globals.ConfigState.ConfigOK)
                {
                    CreateNavigation();
                }

            }
            catch (Exception ex)
            {
                try
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Fatal, "Run Meter", ex.Message.ToString(), "Error");
                }
                catch
                {
                }
                GHIElectronics.TinyCLR.Native.Power.Reset();
//                Microsoft.SPOT.Hardware.PowerState.RebootDevice(true);
            }
        }

        /*
         * Called by timer every second
         * Increment our RefTimer, and call DoPeriodicHouseKeeping at its specified interval (currently 3 seconds)
         */

        private static short Sec = 0;
        private int Minute = 0;

        private void EverySecond(object o)
        {
            ++Globals.RefTimer;     // Housekeeping task may run longer than second, so this section is reentrant
            ++Globals.RunTimer;
            ++Globals.BatTimer;

            //Debug.WriteLine("EverySecond: RefTimer=" + Globals.RefTimer.ToString());

            if (--Sec <= 0)
                Sec = 60;

            if ((Sec == 56) && (_ds != null)) // Allow 5 seconds for system  to get going before shuffling log files
            {
                Minute = ((Globals.PowerState == Globals.PowerStates.BatteryTest) ? Globals.BatTimer : Globals.RefTimer) / 60;

                if(!_ds.IsLocked())
                    ParseBattLog();

                Debug.WriteLine("DateTimeMinute=" + DateTime.Now.Minute.ToString() + "  Minute=" + Minute.ToString());
                /*
                var freeRam = GHIElectronics.TinyCLR.Native.Memory.ManagedMemory.FreeBytes;
                var usedRam = GHIElectronics.TinyCLR.Native.Memory.ManagedMemory.UsedBytes;
                Debug.WriteLine("Free: " + freeRam.ToString());
                Debug.WriteLine("Used: " + usedRam.ToString());
                */
            }
            
            if (Globals.WifiSyncTime > 0)
            {
                if (++WifiCheckDivider >= Globals.WifiSyncTime)
                {
                    WifiCheckDivider = 0;
                    if (_gw_wifi != null)
                        _gw_wifi.SetWifi(BinaryTransport.WifiStates.Connected);
                }
            }

            if (0 == Interlocked.Exchange(ref TimeLock, 1))
            { // Interlock - no TryEnter in microframewok

                // DAV 05NOV2020 - Datastore (StreamWriter etc) not thread safe!
                // Protect Battery level log writes in here, but also change so only write during battery test
                if ((Globals.PowerState == Globals.PowerStates.BatteryTest) && (_ds != null) &&
                    (Globals.gBattFile != "") && (Sec == 56))
                {
                    string s = Minute + " " + Globals.gBattVolts.ToString("F2");
                    _ds.WriteBattLog(s);
                }

                // Housekeeping
                try
                {
                    if (++HouseKeepDivider >= GlobalConsts.HOUSE_KEEPING_CHECK_SECONDS)
                    {
                        HouseKeepDivider = 0;
                        DoPeriodicHouseKeeping(o);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref TimeLock, 0);
                }
            }
        }

        private static byte TryParseByte(string s, byte b = 0)
        {
            try
            {
                if (s.IsNumbersOnly())
                {
                    int i = Convert.ToUInt16(s);
                    if (i < 256)
                        b = (byte) i;
                }
            }
            catch (Exception ex)
            {
                //We return 0 if conversion fails
            }
            return b;
        }


        private static void LoadFactoryDefaultsFromSD(Common.DataStore ds)
        {
            Profile.DebugTime("Start Flash Read"); //TODO DAV DEBUG
            string Result = "";
            bool FlashLoaded = FlashSettings.Reload();
            /*
            FlashSettings.FactoryDefaults fd = new FlashSettings.FactoryDefaults();
            Globals.BackLightLevel = fd.BackLightLevel;
            Globals.GLedBright = fd.GLedBright;
            Globals.RLedBright = fd.RLedBright;
            Globals.LCDBiasPC = fd.LCDBiasPC;
            */

            Profile.DebugTime("Flash Settings Loaded"); //TODO DAV DEBUG

            try
            {
                Result = ds.ReadFactoryDefaults();
                if (Result != "")
                {
                    string[] Record = Result.Split('\r');
                    char[] seps = { ',', ' ' };

                    for (int i = 0; i < Record.Length; i++)
                    {
                        string rp = Record[i].Trim();
                        if (rp.Length == 0 || rp.Left(1) == "#") // Skip blank lines and comments
                            continue;
                        string[] RecordParts = rp.SplitCsv();
                        bool HasP1 = RecordParts.Length > 1;
                        string p1 = "";
                        byte p1b = 25;
                        if (HasP1)
                        {
                            p1 = RecordParts[1].Trim();
                            p1b = TryParseByte(p1, p1b);
                        }

                        switch (RecordParts[0].Trim().ToLower())
                        {
                            case "backlight":
                                Globals.BackLightLevel = p1b;
                                break;
                            case "greenled":
                                Globals.GLedBright = p1b;
                                break;
                            case "redled":
                                Globals.RLedBright = p1b;
                                break;
                            case "lcdbias":
                                Globals.LCDBiasPC = p1b;
                                break;
                            case "serial":
                                // only use serial from SD if we don't already have one from Flash
                                if (Globals.Serial == 0)
                                    Globals.Serial = Convert.ToUInt16(RecordParts[1].ToString());
                                break;
                            case "measmode":
                                Globals.MeasurementModeIndex = Convert.ToUInt16(RecordParts[1].ToString());
                                break;
                            case "sleepdelay":
                                Globals.SleepOverride = true;
                                Globals.SleepDelay = Convert.ToInt16(RecordParts[1].ToString());
                                if (RecordParts.Length > 2)
                                    Globals.WakeDelay = Convert.ToInt16(RecordParts[2].ToString());
                                break;
                            case "connectedsleepdelay":
                                Globals.SleepOverride = true;
                                Globals.ConnectedSleepDelay = Convert.ToInt16(RecordParts[1].ToString());
                                if (RecordParts.Length > 2)
                                    Globals.ConnectedWakeDelay = Convert.ToInt16(RecordParts[2].ToString());
                                break;
                            case "lograwdata":
                                Globals.LogRawData = p1b;
                                break;
                            case "mask4":
                            case "maskt4":
                                if (HasP1) Globals.MaskT4 = (p1b != 0);
                                break;
                            case "wifi":
                                Globals.WifiAPs = RecordParts;
                                break;
                            case "gateway":
                                Globals.Gateways = RecordParts;
                                break;
                            case "ipaddress":
                                if (HasP1) Globals.IpAddress = IPAddress.Parse(p1);
                                break;
                            case "usbdisable":
                                if (HasP1) Globals.USBDisable = (p1b != 0);
                                break;
                            case "wifidisable":
                                if (HasP1) Globals.WifiDisable = Globals.WifiDisabled = (p1b != 0);
                                break;
                            case "wifidebug":
                                if (HasP1) Globals.WifiDebug = (p1b != 0);
                                break;
                            case "wifiverbose":
                                if (HasP1) Globals.WifiVerbose = (p1b != 0);
                                break;
                            case "reboottoms":
                                if (HasP1) Globals.RebootToMS = (p1b != 0);
                                break;
#if false
                            case "keepschedule":
                                if (HasP1) Globals.KeepSchedule = (p1b != 0);
                                break;
#endif
                            case "wifimodes":
                                Globals.WifiModes = p1b;
                                break;
                            case "wifisynctime":
                                Globals.WifiSyncTime = Convert.ToInt16(RecordParts[1].ToString());
                                if (Globals.WifiSyncTime == 0) Globals.WifiDisabled = true;
                                break;
                            case "staticip":
                                int quoteIndex = Record[i].IndexOf('\"');
                                if (quoteIndex != -1)
                                    Globals.StaticIP = Record[i].Substring(quoteIndex);
                                break;
                        }
                    }
                    if(!FlashLoaded)
                        FlashSettings.SaveSettings();
                }
                else
                    throw new Exception("Factory Defaults file on SD is empty.");
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "AnodeMeter::LoadFactoryDefaultsFromSD", "Error loading factory defaults. Reason: " + ex.Message + ". File Contents: " + Result, "");

                // Error Reading FactoryDefaults, attempt to re-write them
                BoardSetup.SaveSettingsToSD(ds);
            }
        }

        private void CreateNavigation()
        {
            string[] theLevels = new string[] { "Lines", "Sections", "Metering" };
            int WindowWidth = 16;
            Navigation = new MenuNavigation(theLevels, WindowWidth);

            if (_plant.GetUseMode() == SmelterDetails.UseMode.Prescribed)
            {
                Navigation.SetChoices("Lines", _plant.GetLineNames());
                Navigation.AppendChoice("Lines", "AH");
            }
            else
            {
                ArrayList ahOnly = new ArrayList();
                ahOnly.Add("AH");
                Navigation.SetChoices("Lines", ahOnly);
            }
            Navigation.AHSubtypes = _plant.GetAdhocTypes();

            _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
        }

        // Most of the methods in this class are driven by user-events (button presses, measurements etc)
        // This method is called every few seconds by a timer and is intended to drive non-critical activities 
        private void DoPeriodicHouseKeeping(object o)
        {
            bool bConnectionStateChanged = false;

            //Globals.bReInitDisplay = true;

            if (Globals.DiskDriveMode || _ds.IsLocked())
            {
                //Debug.Print("HouseKeeping Deferred"); //TODO DEBUG Delete DAV
                return;
            }
            //else
            //Debug.Print("Housekeeping..."); //TODO DEBUG Delete DAV

            UpdateBatterySymbol();

            // Force power off if voltage level critical
            if (Globals.gAvBattVolts <= Globals.BattVoltCritical)
            //if (BatteryTooLow())
            {
                _ds.WriteBattLog("Poweroff gAvBattVolts = " + Globals.gAvBattVolts);
                _bsp.PowerOff("Charge Battery", 15);
            }
            if (_bRebootNextPass)
            {
                // A reboot has been queued, so clean up and exit
                _gw_usb.Close();
                _gw_wifi.Close();
                _sys.Close();
                GHIElectronics.TinyCLR.Native.Power.Reset();
//                Microsoft.SPOT.Hardware.PowerState.RebootDevice(true);
            }
            //TODO =============== WiFi Test =================
            if (Globals.HaveWifi && !Globals.WifiTestMode)
            {
                if (_ds != null)

                {
                    //string res = _gw.IssueRequest("GetServerUTC", null, null, null, 6000);
                    //string res = _gw.IssueRequest("ListAllRequests", null, null, null, 6000);
                    //Debug.Print("Rcv: " + res);
                    if (ActiveGW().GetTransportType() == BinaryTransport.TransportType.Wifi)
                    {
                        HousekeepingWhileConnected();

                        //string res = _gw_wifi.IssueRequest("GetServerUTC", null, null, null, 6000); //TODO Remove - just for testing!
                        //                        string res = _gw_wifi.IssueRequest("GetGatewayVersion", null, null, null, 6000);  //TODO Remove - just for testing
                        //                        Debug.Print("Rcv: " + res);

                        //TODO DAVTEST We may need to disable _noStreamRead before this?
                        if(Globals.WifiSpeedTestMode == Globals.SpeedTestModes.idle)   
                            _gw_wifi.SetWifi(BinaryTransport.WifiStates.Off);
                    }
                }
            }
            //     ===========================================
            //     Diagnostic "heartbeat" - only for testing!
            //string res = ActiveGW().IssueRequest("GetServerUTC", null, null, null, 6000); //TODO Remove - just for testing!
            //Debug.Print("Rcv: " + res);
            //     ===========================================

            // If we've just been connected ...
            if (_previousConnectionState != _cs)
            {
                bConnectionStateChanged = true;
                _previousConnectionState = _cs;
            }

            if (_cs == BinaryTransport.ConnectionState.Connected)
            {
                if (bConnectionStateChanged)
                {
                    if (Globals.CfgState == Globals.ConfigState.ConfigOK)
                    {
                        _dtQueueConfigCheck = DateTime.Now + new TimeSpan(TimeSpan.TicksPerSecond * 30);
                        _dtUploadLogs = DateTime.Now + new TimeSpan(TimeSpan.TicksPerMinute * 3);
                    }
                    else
                    {
                        _dtQueueConfigCheck = DateTime.MinValue;
                        _dtUploadLogs = DateTime.MaxValue;
                    }
                }
                else
                    if (_ds != null) HousekeepingWhileConnected();
            }
            else
            {
                if (DateTime.Now > _dtWaitaFewSeconds && _dtShiftOfLoadedSchedules == DateTime.MinValue && !DataStore.ScheduleFileMissing())
                    _dtShiftOfLoadedSchedules = DataStore.ScheduleFileCreationTime();

                // Not emulating and connected to USB
                if (_bsp != null
                    && Globals.PowerState != Globals.PowerStates.BatteryTest
                    && (DateTime.Now - dtLastUserActivity) > INACTIVITYTIME)
                {
                    _bsp.PowerOff("Auto Off", 15);
                }

                // Hibernate added back in - DAV 31MAY13
                if ((Globals.SleepDelay != 0)
                    && Globals.PowerState != Globals.PowerStates.BatteryTest
                    && ((DateTime.Now - dtLastUserActivity) > new TimeSpan(TimeSpan.TicksPerSecond * Globals.SleepDelay)))
                {
                    // No USB connection to Gateway
                    if (!_bWaitingToHibernate)
                    {
                        //TODO DAVTEST May need to disable _noStreamRead or otherwise prepare lower-levels before this
                        if (_gw_wifi != null)
                            _gw_wifi.SetWifi(BinaryTransport.WifiStates.Off);   //TODO DAV We could/should Suspend() or Sleep() here?

                        //_lcd.ShowTimedMessage("In Stand-By", GlobalConsts.HOUSE_KEEPING_CHECK_SECONDS * 2);
                        _bWaitingToHibernate = true;
                    }
                    else
                    {
                        DoHibernate();
                    }
                }
            }
        }

        public int QuickNap(int seconds)
        {
            return _sys.Hibernate(seconds);
        }
        public int DoHibernate(int SecondsToHibernate = 59 * 60)
        {
            int res = 0;
            // Time to Sleep
            _lcd.ShowTimedMessage("In Stand-By", 2);
            Thread.Sleep(500);
            DateTime SleepStart = DateTime.Now;
            _lcd.SetBacklight(0);
            Debug.WriteLine("Hibernate");

            ESP8266WiFi.PowerOff(true);

            _lcd.Suspend();
            res = _sys.Hibernate(SecondsToHibernate);
            _lcd.Resume();

            // TODO DAV - Remove this when GHI fixes SDK4.3
            _lcd.FixIO();
            Debug.WriteLine("Awaken");
            _tm.RefreshSystemTime();

            TimeSpan tmDiff = DateTime.Now - SleepStart;
            long secs = (DateTime.Now - SleepStart).Ticks / TimeSpan.TicksPerSecond;
            Globals.RefTimer += (int)secs; // Adjust RefTimer but not RunTimer

            //dtIgnoreButtonsUntil = DateTime.Now + new TimeSpan(TimeSpan.TicksPerSecond * 2);
            dtIgnoreButtonsUntil = DateTime.Now.AddSeconds(2);
            RegisterActivity();
            _lcd.SetBacklight(Globals.BackLightLevel);
            _lcd.ReInit();

            // If we have been hibernating for >x (default  59) minutes (selectable later?) then may as well power off
            if ((Globals.ShutDownAfterMinutes > 0) &&  (secs > (60 * Globals.ShutDownAfterMinutes)) && (_bsp != null))
                _bsp.PowerOff("Power Off", 15);
            return res;
        }
        private void HousekeepingWhileConnected()
        {
            if (_previousHouseKeepingPassFinished)
            {
                // Prevent the HouseKeeping timer attempting to launch this code again if it's still running from the previous pass
                _previousHouseKeepingPassFinished = false;

                // run speed test on this thread
                if (Globals.WifiSpeedTestMode == Globals.SpeedTestModes.requested)
                    RunSpeedTest();

                if (DateTime.Now >= dtCheckForDirtyData && DateTime.Now > _dtWaitaFewSeconds)
                {
                    dtCheckForDirtyData = DateTime.Now + new TimeSpan(TimeSpan.TicksPerMinute * GlobalConsts.MINUTES_BEFORE_SHIFT_TO_LOAD_SCHEDULES);
                    if (_ds.IsMeasurementFileAvailable() && _cs == BinaryTransport.ConnectionState.Connected)
                    {
                        string csvData = _ds.GetDirtyResults();

                        if (csvData != "")
                        {
                            _lcd.ShowTimedMessage("Uploading Data", 5);
                            string res = ActiveGW().IssueRequest("SaveCsvResults", "MeasurementRecs", "csvData", csvData, 10000);

                            if (res.IsRequestAcknowleged() && _ds.ArchiveMeasurements())
                            {
                                _lcd.ShowTimedMessage("Save Complete!", 5);///////
                                _dtWaitaFewSeconds = DateTime.Now.AddSeconds(5);
                            }

                            else
                            {
                                dtCheckForDirtyData = DateTime.Now + new TimeSpan(TimeSpan.TicksPerMinute * 5);
                                _lcd.ShowTimedMessage("Save Error");
                                _dtWaitaFewSeconds = DateTime.Now.AddSeconds(5);
                            }
                        }
                    }
                }
                if (DateTime.Now > _dtNextTimeSync)
                {
                    DateTime dtDbServer = ActiveGW().IssueRequest("GetServerLocalTime", null, null, null, 6000).ParseDateTime();
                    if (dtDbServer != DateTime.MinValue)
                    {
                        _tm.SyncTime(dtDbServer);
                        _dtNextTimeSync = DateTime.Now + new TimeSpan(TimeSpan.TicksPerHour * 12);
                        _ds.LogSyncTime(dtDbServer);
                    }
                    else
                        _dtNextTimeSync = DateTime.Now + new TimeSpan(TimeSpan.TicksPerMinute * 10);
                }
                // Once every hour, force the Garbage collector to run
                if (DateTime.Now.Minute == 0 && DateTime.Now.Second < GlobalConsts.HOUSE_KEEPING_CHECK_SECONDS * 2)
                    System.GC.GetTotalMemory(true);
//                    Microsoft.SPOT.Debug.GC(true);

                // Every Few minutes, upload any logged errors to the Database-Server
                if (DateTime.Now > _dtUploadLogs)
                {
                    // Cache logged messages while dealing with loggfiles
                    // to avoid concurrency problems
                    Logging.LockOutput();

                    // string errorLogName = _ds.GetErrorLogName();

                    //if (errorLogName != "")
                    _ds.UploadErrorLogs(ActiveGW());

                    // Resume normal file-based logging
                    Logging.UnLockOutput();
                    _dtUploadLogs = DateTime.Now + new TimeSpan(TimeSpan.TicksPerMinute * 10);
                }

                if (Globals.CfgState == Globals.ConfigState.ConfigOK)
                {
                    int lastHourOfShift = (SmelterDetails.GetNextShiftStartTime(DateTime.Now) - new TimeSpan(TimeSpan.TicksPerHour)).Hour;
                    if (DateTime.Now.Hour == lastHourOfShift && DateTime.Now.Minute == 1 && DateTime.Now.Second < GlobalConsts.HOUSE_KEEPING_CHECK_SECONDS * 2)
                        // Near the end of this shift, which is likely to be a quiet time for anode-meters, so it's a good time to delete old files
                        DataStore.DeleteOldFiles();

                    // Clean out old schedules.
                    // The _dtShiftWanted uses the current date/time and adds 30 minutes so if we are within 30 minutes of change
                    // of shift
                    //retrieve the schedules for the next shift and write them to disk

                    _dtShiftWanted = (SmelterDetails.GetShiftStartTime(DateTime.Now.AddMinutes(GlobalConsts.MINUTES_BEFORE_SHIFT_TO_LOAD_SCHEDULES)));
                    _dtSchedAttemptFetchTime = _dtShiftWanted - new TimeSpan(TimeSpan.TicksPerMinute * GlobalConsts.MINUTES_BEFORE_SHIFT_TO_LOAD_SCHEDULES);

                    if ((_dtShiftOfLoadedSchedules < _dtShiftWanted && DateTime.Now > _dtWaitaFewSeconds) || bForceScheduleReload)
                    {
                        if ((_dtShiftOfLoadedSchedules == DateTime.MinValue || DataStore.ScheduleFileCreationTime() < _dtSchedAttemptFetchTime) || bForceScheduleReload)
                        {
                            DataStore.DeleteOldScheduleFiles();
                            if (LoadSchedulesFileToDisk())
                                _dtShiftOfLoadedSchedules = _dtShiftWanted;
                            bForceScheduleReload = false;
                        }
                        else
                        {
                            _lcd.ShowTimedMessage("Schedules Ready", 3);
                            _dtShiftOfLoadedSchedules = _dtShiftWanted;
                        }
                        _dtWaitaFewSeconds = DateTime.Now.AddSeconds(3);
                    }
                }
                // Check if we've got the most recent Site-Config info
                if (DateTime.Now > _dtQueueConfigCheck)
                {
                    int OldMeterNum = _sys.GetMeterNumber;

                    // Run this once per reconnect event
                    _dtQueueConfigCheck = DateTime.MaxValue;

                    // == This section checks Device.xml, currently only used for the Meter Number, generated by the site server
                    // == Once loaded on a meter, it shouldn't change. Factory default is either no file, for meter number set to zero
                    if ((OldMeterNum <= 0) || !_ds.DeviceConfigExists())
                    {
                        string deviceConfig = ActiveGW().IssueRequest("GenerateDeviceID", null, null, null, 6000);
                        if (deviceConfig != null)
                        {
                            if (deviceConfig == "Server Error")
                            {
                                Logging.IssueEvent(Logging.ErrSeverity.Warning, "AnodeMeter::HousekeepingWhileConnected", "Server side error fetching the Device-ID-Info", "Server Cfg Err");
                                deviceConfig = null;
                            }
                            else if (deviceConfig.Left(5).ToLower() == "<?xml")
                            {
                                int NewMeterNum = _sys.ParseMeterNumber(deviceConfig);
                                if ((NewMeterNum >= 0) && (NewMeterNum != OldMeterNum))
                                {
                                    Globals.CfgState = Globals.ConfigState.UpdatingConfig;

                                    //_lcd.ForceIntoDisplay("Wait ...        ", Display1);
                                    //_lcd.ForceIntoDisplay("Loading Config! ", Display2);

                                    _lcd.ShowTimedMessage("Wait ...        ", "Loading Config! ", 10);
                                    if (_ds.WriteDeviceID(deviceConfig))
                                    {
                                        _lcd.CancelTimedMessages();
                                        _lcd.ShowTimedMessage("Update Meter #", "Rebooting...");
                                        //_sys.InitOnStart();  // reload configuration with new device id
                                        _bRebootNextPass = true;
                                    }
                                    else
                                        _lcd.CancelTimedMessages();
                                }
                            }
                        }
                    }

                    // == This section checks SmelterConfiguration.xml,  the site-specific config. from the database, which may change occasionally
                    // == A SHA1 Hash is used to confirm integrity and that file is identical on meter and database

                    if (_systemConfigFileHash == null)
                    {
                        // Generate a SHA1 hash of the system config file 
                        _systemConfigFileHash = _ds.GetConfigFileSha1HashString();
                    }
                    if (_systemConfigFileHash == null)
                        _systemConfigFileHash = "";

                    // Send the hash of the local config file to the DB server for comparison.
                    // If not the same, server will send complete config back.
                    string configFileServerVersion = ActiveGW().IssueRequest("GetConfigFromServerIfDirty", "ClientConfig", "SHA1Hash", _systemConfigFileHash, 6000);
                    if (configFileServerVersion != null)
                    {
                        if (configFileServerVersion == "Server Error")
                        {
                            Logging.IssueEvent(Logging.ErrSeverity.Warning, "AnodeMeter::HousekeepingWhileConnected", "Server side error fetching the Site-Configuration-File", "Server Cfg Err");
                            configFileServerVersion = null;
                        }
                        //else if (configFileServerVersion.Left(5).ToLower() == "<?xml")
                        else if (configFileServerVersion.Length >= 40) // 40 characters to cover minimum xml declaration length
                        {
                            string xmlHeader = configFileServerVersion.Substring(0, 40).ToLower(); // get the first 40 characters and convert to lower case

                            // Check the header starts with "<?xml" and contains "utf-8"
                            if (xmlHeader.StartsWith("<?xml") && xmlHeader.Contains("utf-8"))
                            {
                                // Check that the new config string is correct (no Transmission errors...)
                                string hashedProposedConfig = _ds.GetSHA1Hash(configFileServerVersion);
                                string serverConfigCheck = ActiveGW().IssueRequest("GetConfigFromServerIfDirty", "ClientConfig", "SHA1Hash", hashedProposedConfig, 6000);

                                if (serverConfigCheck != null && serverConfigCheck.ToUpper().IndexOf("CURRENT") != -1)
                                {
                                    Logging.IssueEvent(Logging.ErrSeverity.Informational, "AnodeMeter::HousekeepingWhileConnected", "Site-Configuration-File updated from Server", "Cfg Updt:Rebootg");

                                    if (_ds.WriteConfigFile(configFileServerVersion))
                                    {
                                        //_sys.InitOnStart();  // reload configuration with new device id
                                        _lcd.CancelTimedMessages();
                                        _lcd.ShowTimedMessage("Config Update", "Rebooting...");

                                        _bRebootNextPass = true;
                                    }
                                }
                            }
                        }
                    }
                }
                _previousHouseKeepingPassFinished = true;
            }
        }

        private void RunSpeedTest()
        {
            // Do Speed Test
            Globals.WifiSpeedTestMode = Globals.SpeedTestModes.running;

            if(ActiveGW().GetTransportType() == BinaryTransport.TransportType.Wifi)
                _gw_wifi.UpdateRSSI();

            string data = BuildTestString(); // "This is the data for testing link speed";
            Profile.DebugTime("Send Bounce Packet");
            // Mark begin time here
            DateTime startTime = DateTime.Now;

            string res = ActiveGW().IssueRequest("BouncePacket", "SpeedTest", "TestData", data, 6000);
            // Mark end time here
            DateTime endTime = DateTime.Now;

            Globals.BounceTestPassed = data.Equals(res);

            Profile.DebugTime("Receive Bounced Packet");
            Debug.WriteLine("Bounce => " + res);
            double durationInSeconds = (endTime - startTime).TotalSeconds;
            Globals.SpeedTestBPS = (int)((2 * data.Length) / durationInSeconds);  // Bytes/Second
            // Calculate the data length in bits
            //int TestLength = data.Length * 8; // 8 bits per character

            // Calculate the speed in bits per second (bps)
            //Globals.SpeedTestBPS = (int)(TestLength / durationInSeconds); // Fill data rate in bps here!
            Globals.WifiSpeedTestMode = Globals.SpeedTestModes.completed;
        }

        private string BuildTestString()
        {
            int maxLines = 312; // About 25kB (80 bytes/line,  1250 lines => 100kB)
            int numbersPerLine = 16;
            int currentValue = 0;
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < maxLines; i++)
            {
                for (int j = 0; j < numbersPerLine; j++)
                {
                    sb.Append(currentValue.ToString("X4")); // Convert to 4-digit hex
                    if (j < numbersPerLine - 1)
                    {
                        sb.Append(","); // Add comma between numbers
                    }
                    currentValue++;
                }
                sb.AppendLine(); // Add line feed at the end of each line
            }

            return sb.ToString();
        }

        private bool MeteringAPot()
        {
            if (Navigation != null && (Navigation.CurrentMenu().ToString() == "2"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void UpdateBatterySymbol()
        {
            int ChargePercent = _bc.GetStateOfChargePercent();
            if (ChargePercent >= 90.0)
            {
                _lcd.MoveIntoDisplay(SpecialLCDCharacters.batteryFull, BatteryPosition);
            }
            else if (ChargePercent >= 75.00)
            {
                _lcd.MoveIntoDisplay(SpecialLCDCharacters.battery3Quart, BatteryPosition);
            }
            else if (ChargePercent >= 50.0)
            {
                _lcd.MoveIntoDisplay(SpecialLCDCharacters.batteryHalf, BatteryPosition);
            }
            else if (ChargePercent >= 25.0)
            {
                _lcd.MoveIntoDisplay(SpecialLCDCharacters.batteryQuart, BatteryPosition);
            }
            else if (ChargePercent < 25.0)
            {
                _lcd.MoveIntoDisplay(SpecialLCDCharacters.batteryMt, BatteryPosition);
            }
        }

        private void SetupMeterType()
        {
            switch (Program.MeterType)
            {
#if EMULATOR
                case AnodeMeterType.EMULATOR:
                    _lcd = new LiquidCrystalDisplayEmulation();
                    _amb = new EmulatorButtons();
                    _ai = new EmulatorAnalogInput();
                    _tm = new Emulator.EmuTimeManager();
                    _gw = new MySocketClient();
                    _led = ((LiquidCrystalDisplayEmulation)_lcd).GetLED();
                    ((LiquidCrystalDisplayEmulation)_lcd).ConStateHandler += new LiquidCrystalDisplayEmulation.ConnectionStateChanged(OnCommsStateChanged);
                    break;
#endif
                case AnodeMeterType.GHI:
                    Profile.DebugTime("Setup Start"); //TODO DAV DEBUG
                    if (_lcd == null) _lcd = new LiquidCrystal();
                    Profile.DebugTime("Setup lcd"); //TODO DAV DEBUG
                    _amb = new PhysicalButtons();
                    Profile.DebugTime("Setup amb"); //TODO DAV DEBUG
                    _ai = new HardwareAI();
                    Profile.DebugTime("Setup ai "); //TODO DAV DEBUG
                    _tm = new HWTimeManager();
                    Profile.DebugTime("Setup tm "); //TODO DAV DEBUG
                    _gw_usb = new UsbTransport();
                    Profile.DebugTime("Setup usb gw"); //TODO DAV DEBUG
                    _gw_wifi = new WifiTransport();
                    Profile.DebugTime("Setup wifi gw"); //TODO DAV DEBUG
                    if (_led == null) _led = new PhysicalLED();
                    Profile.DebugTime("Setup led"); //TODO DAV DEBUG
                    _bc = new PhysicalBatteryCharge();
                    Profile.DebugTime("Setup bc "); //TODO DAV DEBUG
                    //              ((HardwareAI)_ai).BatteryVoltageHandler += new HardwareAI.BattVoltageConsumer(((PhysicalBatteryCharge)_bc).OnBattVoltageReceived);
                    _bsp = new BoardSetup(_lcd, _amb, _led, _bc, _ds, _gw_usb, _gw_wifi, this);
                    Profile.DebugTime("Setup bsp"); //TODO DAV DEBUG
                    break;
            }
            //Profile.DebugTime("pre lcd.Initialize"); //TODO DAV DEBUG

            // Setup display first
            //_lcd.Initialize();
            //Profile.DebugTime("lcd.Initialize"); //TODO DAV DEBUG

            // Don't allow startup if Battery too low
            //if (BatteryTooLow())
            if (Globals.gAvBattVolts <= Globals.BattVoltLow)
            {
                _bsp.PowerOff("Charge Battery", 5);
            }
            else
            {
                //_lcd.MoveIntoDisplay("Loading Meter", new LcdDisplay.CursorPosition(0, 0));
                //_lcd.MoveIntoDisplay("Configuration", new LcdDisplay.CursorPosition(1, 0));
                //_lcd.ShowTimedMessage("Loading Meter", "Configuration",1);
            }

            Profile.DebugTime("Loading"); //TODO DAV DEBUG

            _lcd.SetBacklight(Globals.BackLightLevel);
            _led.TurnOff();

            if (_ds != null)
            {
                FStream fs = null;
                try
                {
                    
                    _tm.RegisterDataStore(_ds);

                    if (_plant.Init(fs = (FStream) _ds.OpenConfiguration()))
                    {
                        _tm.Init();
                        Globals.CfgState = Globals.ConfigState.ConfigOK;

                    }
                    else
                        Globals.CfgState = Globals.ConfigState.NotConfigured;

                    //if(fs != null) { fs.Close(); fs.Dispose(); fs = null; }
                    SelectPot.SetPotInfoProvider(_plant.GetPotNameCharRange);
                    SelectPot.SetPotNameCheck(_plant.IsValidPotName);
                    SelectPot.UsePot(_plant.GetDefaultPot());

                }
                catch
                {
                    //if (fs != null) { fs.Close(); fs.Dispose(); fs = null; }
                } finally {
                    if (fs != null) { fs.Close(); fs.Dispose(); fs = null; }
                }
            }
            if (Globals.CfgState != Globals.ConfigState.ConfigOK)
            {
                _tm.Init();
                if (_bsp != null)
                    _bsp.EnterSetupMode(BoardSetup.MenuTypes.Info, BoardSetup.MenuItems.infoSDCard);
            }

            Logging.IssueEvent(Logging.ErrSeverity.Informational, "AnodeMeter::SetupMeterType",
                "System Starting. Cause: \"" + _sys.GetStartCause() + "\"", "");
            Logging.IssueEvent(Logging.ErrSeverity.Informational, "AnodeMeter::SetupMeterType",
                "Startup Serial: " + Globals.Serial + " Name: " + Assembly.GetExecutingAssembly().FullName + " FW: " +
                DeviceInformation.Version.ToVersionString() + " Board: SC20260",
                "Serial: " + Globals.Serial);


            MeterNumber = _sys.GetMeterNumber;


            _amb.MeterButtonChanged += new AnodeMeterButtons.EventHandler(amb_MeterButtonChanged);
            _ai.RawDataHandler += new AnalogInput.EventHandler(OnRawAiDataReceived);
            _sp.MeteringReadingHandler += new SignalProcessor.MeteringReadError(OnMeasureStateChange);
            _sp.VoltDropHandler += new SignalProcessor.VoltDropRead(OnNewMeasurement);

            if (Globals.CfgState == Globals.ConfigState.ConfigOK)
                AnodeMeterSchedules = new Schedule();

            _lcd.WaitReady();
            _lcd.ClearDisplay();
            _lcd.MoveIntoDisplay("       ", new LcdDisplay.CursorPosition(0, 0));

            // Boot to MassStorage mode? (There may well be better locations for this!)
            if (_bsp != null)
            {
                if (_bsp.BootedToMassStorage())   // Check flag in battery-backed memory
                {
                    //_bsp.DiskDriveMode(true);
                    _bsp.EnterSetupMode(BoardSetup.MenuTypes.Mode, BoardSetup.MenuItems.modeDiskDrive, 0);
                }
                else
                    TransportInit();    // Initialize USB and WiFi transports

                if(_bsp.BootedToWinUSB())
                    _bsp.EnterSetupMode(BoardSetup.MenuTypes.Mode, 0, 0);

                if (!_tm.IsSystemTimeOK())
                    _bsp.EnterSetupMode(BoardSetup.MenuTypes.Settings, BoardSetup.MenuItems.setClock, 3);

            }
        }

        bool bTransportInitDone = false;       // So we can hold off init for boot into MassStorage mode
        public void TransportInit()
        {
            if (!bTransportInitDone)
            {
                _gw_usb.Init();
                _gw_wifi.Init();
                _gw_usb.ConnectionStateHandler += new BinaryTransport.ConnectionStateChanged(OnCommsStateChanged);
                _gw_wifi.ConnectionStateHandler += new BinaryTransport.ConnectionStateChanged(OnWifiStateChanged);
                bTransportInitDone = true;
            }
        }

        private bool BatteryTooLow()
        {
            int ChargePercent = _bc.GetStateOfChargePercent();
            Debug.WriteLine("ChargePercent = " + ChargePercent); //TODO DAV DEBUG
            Debug.WriteLine(("AvBattVolts= " + Globals.gAvBattVolts));//TODO DAV DEBUG
            Debug.WriteLine(("BattVolts= " + Globals.gBattVolts));//TODO DAV DEBUG

            return (ChargePercent < BATTERY_TOO_LOW_PERCENT) ? true : false;
        }

        // Called when USB device is attached/detached
        public void OnCommsStateChanged(BinaryTransport.ConnectionState cs)
        {
            try
            {
                if (cs == BinaryTransport.ConnectionState.Connected && _cs != BinaryTransport.ConnectionState.Connected)
                {
                    _lcd.ShowTimedMessage("USB Attached", 1);
                    Debug.WriteLine("USB Attached");    //TODO REMOVE DEBUG DAV
                    dtCheckForDirtyData = DateTime.Now;
                    _cs = BinaryTransport.ConnectionState.Connected;
                }
                else if (cs == BinaryTransport.ConnectionState.Detached && _cs != BinaryTransport.ConnectionState.Detached)
                {
                    // Bump the inactivity timer so that we don't immediately go to sleep on USB disconnect 
                    RegisterActivity();
                    _cs = BinaryTransport.ConnectionState.Detached;
                    Debug.WriteLine("USB Detached");    //TODO REMOVE DEBUG DAV
                    _lcd.ShowTimedMessage("USB Detached", 1);
                }

            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::OnCommsStateChanged", "Error handling measurement comms state change - Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Comms Hndlr err!");
            }
        }

        // Called when Wifi device is connected/disconnected
        //TODO - Cloned from USB, but will need to change _cs variable to avoid conflicts!
        //TODO As WiFi use is different, need to adjust timeout. And Connect/disconnect via timer and/or pot completion
        public void OnWifiStateChanged(BinaryTransport.ConnectionState cs)
        {
            if (Globals.WifiTestMode) return;
            try
            {
                if (cs == BinaryTransport.ConnectionState.Connected && _cs != BinaryTransport.ConnectionState.Connected)
                {
                    if(Globals.WifiDebug)
                        _lcd.ShowTimedMessage("Wifi Attached", 1);
                    Debug.WriteLine("Wifi Connected");    //TODO REMOVE DEBUG DAV
                    dtCheckForDirtyData = DateTime.Now;
                    _cs = BinaryTransport.ConnectionState.Connected;
                }
                else if (cs == BinaryTransport.ConnectionState.Detached && _cs != BinaryTransport.ConnectionState.Detached)
                {
                    // Bump the inactivity timer so that we don't immediately go to sleep on Wifi disconnect 
                    RegisterActivity();
                    _cs = BinaryTransport.ConnectionState.Detached;
                    Debug.WriteLine("Wifi Disconnected");    //TODO REMOVE DEBUG DAV
                    if(Globals.WifiDebug)
                        _lcd.ShowTimedMessage("Wifi Detached", 1);
                }

            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::OnCommsStateChanged", "Error handling measurement comms state change - Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Comms Hndlr err!");
            }
        }

        // This method will be called when a new Anode-Rod-Drop value is available
        void OnNewMeasurement(object sender, VoltageMeasAquiredEvent e)
        {
            try
            {
                RegisterActivity();

                bool bFlash = false;
                bool bRodMeasured = true;

                if (_CurrentAnode != null)
                {
                    bRodMeasured = (_CurrentAnode._measType == MeasurementType.RodDrop);

                    double thisVDrop = e.MeasValue;
                    if (_plant.ImplausibleMeasurement(_CurrentAnode._potName, thisVDrop, bRodMeasured))
                        bFlash = true;
                }

                if (bFlash)
                    _led.FlashAlternateColors(LED.LedColor.Green, 1);
                else
                    _led.IndicatedNormalCompletion(bRodMeasured);

                _currentMeasVolts = e.MeasValue;
                ProcessAnode(false);
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::OnMeasureStateChange", "Error handling measurement sequence change - Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Meas Hndlr err!");
            }
        }
        // This method will be called when an Operator Error has resulted in a bad or incomplete measurement
        // Measurement state handling is done elsewhere, but alarming/notification needs to be done here
        void OnMeasureStateChange(object sender, AiStateChangeEvent e)
        {
            try
            {
                if (e.Status == AiMeasurementStates.RemovedTooSoon)
                    _led.Flash(LED.LedColor.Red, 2);
                else
                    _led.TurnOffFinishedIndicator();
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::OnMeasureStateChange", "Error handling measurement sequence change - Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Meas Hndlr err!");
            }
        }
        // This method is called when a raw AI value has been read
        void OnRawAiDataReceived(object sender, AnalogInputEventArg e)
        {
            if (Globals.LogRawData != 0)
                Logging.LogRawReading(e.AnalogValue);

            if (!_AiEventInProgress)
            {
                _AiEventInProgress = true;
                try
                {
                    // Are we expecting a Rod Drop or Clamp Drop reading?
                    MeasurementType expectedNow = MeasurementType.RodDrop;
                    if (_CurrentAnode != null)
                        expectedNow = _CurrentAnode._measType;
                    else
                    {
                        MeasModeOption mmo = (MeasModeOption)Globals.MeasurementModeIndex;
                        if (mmo == MeasModeOption.RodDrop || mmo == MeasModeOption.RodThenClamp)
                            expectedNow = MeasurementType.RodDrop;
                        else
                            expectedNow = MeasurementType.ClampDrop;
                    }

                    // Scale reading according to expected type
                    // Use absolute value for Clamp but not Rod, for now.
                    // Need more information for plants as to if this is the best way. DAV 30JUL14
                    double scaledAiVal;
                    if (expectedNow == MeasurementType.RodDrop)
                        scaledAiVal = e.AnalogValue * _plant.GetMeasurementScalingFactor(expectedNow) + _plant.GetMeasurementOffset();
                    else
                        scaledAiVal = e.AnalogValue.Abs() * _plant.GetMeasurementScalingFactor(expectedNow);

                    string FormattedValue;
                    if (e.AnalogValue > 1.0F)     // Fix position, as of v4.1 micro framework has limited formatting
                        FormattedValue = "-----";
                    else
                    {
                        if (scaledAiVal >= 0.1F)
                            FormattedValue = FormatMeasurement(scaledAiVal * 100);
                        else if (scaledAiVal <= -0.1F)
                            FormattedValue = FormatMeasurement(scaledAiVal * 100);
                        else
                            FormattedValue = FormatMeasurement(scaledAiVal * 1000);
                    }

                    if (FormattedValue.Length == 3)
                        FormattedValue = "  " + FormattedValue;
                    else if (FormattedValue.Length == 4)
                        FormattedValue = " " + FormattedValue;

                    _lcd.MoveIntoDisplay(FormattedValue, MillivoltPosition);
                    _lcd.AddNewAnalogReading(scaledAiVal);

                    _sp.QueueValue(scaledAiVal, expectedNow);

                    if (_bsp != null)
                        _bsp.SetAnalogReading(e.AnalogValue);

                }
                catch (Exception ex)
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::OnRawAiDataReceived", "Error handling raw-AI data - Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Meas Hndlr err!");
                }
                _AiEventInProgress = false;
            }
        }


        void amb_MeterButtonChanged(object sender, MeterButtonPressEventArgs e)
        {
            // Ignore this button-press if still processing the previous one
            if (!_buttonEventInProgress)
            {
                try
                {
                    _buttonEventInProgress = true;

                    RegisterActivity();

                    if (_bsp != null)
                    {
                        if (_bsp.EnterShutDownMode(false))
                            _bsp.PowerOff("Power Off", 5);

                        if (_bsp.EnterSetupMode(false))
                            return;
                    }

                    // If the RTC Battery is too low to allow the system time to be set,
                    // then prevent normal operation because any readings collected would be attributed to
                    // the wrong datetime and hence not visible in reports / APG

                    if (!_tm.IsSystemTimeOK())
                        _lcd.ShowTimedMessage("Date Incorrect", "Update via USB", 5);
                    else if(Navigation == null)
                        _lcd.ShowTimedMessage("Meter Settings", "Unavailable", 5);
                    else
                    {
                        if (dtIgnoreButtonsUntil < DateTime.Now)
                        {
                            switch (e.MeterButton)
                            {
                                case AnodeMeterButtonPress.centre:
                                    switch (CurrentChoice)
                                    {
                                        case "AH":
                                            if (!MeteringAPot())
                                                ProcessSelection();
                                            break;

                                        default:
                                            CurrentChoice = Navigation.GetCurrentChoice();
                                            ProcessSelection();
                                            break;
                                    }
                                    break;

                                case AnodeMeterButtonPress.centrehold:
                                    {
                                        Globals.bReInitDisplay = true;  // Reinit LCD Display (just in case!)
                                        DisplayMeterInformation();
                                    }
                                    break;

                                case AnodeMeterButtonPress.left:

                                    switch (Navigation.CurrentMenu())
                                    {
                                        case 0: //lines
                                            switch (CurrentChoice)
                                            {
                                                //case "AH":
                                                //    Adhoc(e.MeterButton);
                                                //    break;
                                                default:
                                                    Navigation.MoveLeft();
                                                    _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                                                    break;
                                            }
                                            break;

                                        case 1: //sections
                                            switch (CurrentChoice)
                                            {
                                                case "AH":
                                                    Adhoc(e.MeterButton);
                                                    break;
                                                default:
                                                    Navigation.MoveLeft();
                                                    _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                                                    break;
                                            }

                                            break;

                                        case 2: // in metering a pot

                                            bool bBackToPreviousPot = false;

                                            if (CurrentChoice == "AH")
                                            {
                                                if (_CurrentAnode._FirstAnodeForPot && _previousAdHocSchedule != null && _prevAdHocAnode != null)
                                                {
                                                    // The operator is attempting to move back to the previous anode
                                                    // after that pot has been completed and we're waiting at the first anode of the subsequent pot.
                                                    // This is a reasonably common scenario when we're in As-Hoc mode
                                                    // and the last anode of a pot had a suspect reading
                                                    AnodeMeterSchedules = _previousAdHocSchedule;
                                                    _previousAdHocSchedule = null;

                                                    _CurrentAnode = _prevAdHocAnode;
                                                    _prevAdHocAnode = null;
                                                    bBackToPreviousPot = true;
                                                }
                                            }
                                            if (!bBackToPreviousPot)
                                            {
                                                // Don't believe that we need to call ProcessAnode on left-button press
                                                // ProcessAnode(true);

                                                // Move to the previous schedule if one exists
                                                Schedule.AnodeSched prev = AnodeMeterSchedules.GetPreviousAnodeFromSched();
                                                if (prev != null)
                                                    _CurrentAnode = prev;
                                            }

                                            DisplayMeteringInfo(_CurrentAnode);
                                            break;
                                    }
                                    break;


                                case AnodeMeterButtonPress.right:

                                    switch (Navigation.CurrentMenu())
                                    {
                                        case 0: //lines
                                            switch (CurrentChoice)
                                            {
                                                //case "AH":
                                                //    Adhoc(e.MeterButton);
                                                //    break;
                                                default:
                                                    Navigation.MoveRight();
                                                    _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                                                    break;
                                            }
                                            break;

                                        case 1: //sections
                                            switch (CurrentChoice)
                                            {
                                                case "AH":
                                                    Adhoc(e.MeterButton);
                                                    break;
                                                default:
                                                    Navigation.MoveRight();
                                                    _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                                                    break;
                                            }

                                            break;

                                        case 2: // in metering a pot
                                            ProcessAnode(true);
                                            DisplayMeteringInfo(_CurrentAnode);
                                            _CurrentAnode = AnodeMeterSchedules.GetNextMeasurement();
                                            if (_CurrentAnode == null)
                                            {
                                                ClearMenuDisplay(1);
                                                ClearMenuDisplay(2);
                                                Navigation.MoveToTopLevelMenu();
                                                Navigation.SetChoiceZero();
                                                CurrentChoice = Navigation.GetCurrentChoice();
                                                _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                                            }
                                            else
                                                DisplayMeteringInfo(_CurrentAnode);
                                            break;
                                    }
                                    break;


                                case AnodeMeterButtonPress.up:

                                    switch (Navigation.CurrentMenu())
                                    {
                                        case 0: //lines
                                            //TODO - DAV Select JA mode here?
                                            string s = Navigation.GetCurrentChoice();
                                            Debug.WriteLine("UP - Nav: " + s + " Cur: " + CurrentChoice);
                                            Navigation.MoveAHSubtype(-1);
                                            _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);

                                            switch (CurrentChoice)
                                            {
                                                case "AH":
                                                    Adhoc(e.MeterButton);
                                                    break;
                                                default:
                                                    break;
                                            }
                                            break;

                                        case 1:
                                            switch (CurrentChoice)
                                            {
                                                case "AH":
                                                    Adhoc(e.MeterButton);
                                                    break;
                                                default:
                                                    break;
                                            }

                                            break;

                                        case 2:
                                            break;
                                    }
                                    break;


                                case AnodeMeterButtonPress.down:
                                    //TODO - DAV Select JA mode here?
                                    if (Navigation.CurrentMenu() == 0)
                                    {
                                        string sd = Navigation.GetCurrentChoice();
                                        Debug.WriteLine("DOWN - Nav: " + sd + " Cur: " + CurrentChoice);
                                        Navigation.MoveAHSubtype(1);
                                        _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                                    }

                                    if (MeteringAPot())
                                    {
                                        // If we're at the start of an Ad-Hoc Pot, use a momentary "down arrow" press
                                        // to drive directly to into pot-selection 
                                        if (_CurrentAnode != null && _CurrentAnode._FirstAnodeForPot && CurrentChoice == "AH")
                                        {
                                            int PotNameLength = _CurrentAnode._potName.Length;
                                            AnodeMeterSchedules.RemoveSchedule("AH");
                                            _CurrentAnode = null;
                                            NoPotsToMeter = true;
                                            AnodeMeterSchedules.RemoveScheduleFromCache();
                                            ClearMenuDisplay(1);
                                            Navigation.MoveToTopLevelMenu(); // move
                                            Navigation.SetChoiceZero();
                                            ProcessSelection();

                                            // Move to the right-most pot digit - most likely to be changed
                                            for (int cursorPot = 1; cursorPot < PotNameLength; cursorPot++)
                                                Adhoc(AnodeMeterButtonPress.right);

                                        }
                                    }
                                    else
                                    {
                                        switch (CurrentChoice)
                                        {
                                            case "AH":
                                                Adhoc(e.MeterButton);
                                                break;
                                            default:
                                                //Navigation.MoveNextMenu();
                                                break;
                                        }
                                    }
                                    break;
                                case AnodeMeterButtonPress.lefthold:

                                    switch (Navigation.CurrentMenu())
                                    {
                                        case 0:
                                            _lcd.ShowTimedMessage(DateTime.Now.ToString("dd-MM-yyyy HH:mm"));
                                            break;

                                        case 1:

                                            break;

                                        case 2:
                                            _CurrentAnode = AnodeMeterSchedules.GetPreviousPotFromSched();
                                            LastThreeReadings.Clear();
                                            DisplayMeteringInfo(_CurrentAnode);
                                            break;

                                    }

                                    break;

                                case AnodeMeterButtonPress.righthold:

                                    switch (Navigation.CurrentMenu())
                                    {
                                        case 0:
                                            //TODO DAV Could make a general systems information display call, and a operational data call? 
                                            //_lcd.ShowTimedMessage("Current Time", DateTime.Now.ToString("ddMMMyy HH:mm:ss"),3);
                                            DateTime dt = DataStore.ScheduleFileCreationTime();
                                            string dts = (dt > DateTime.MinValue)
                                                ? dt.ToString("ddMMMyy HH:mm:ss")
                                                : "None";
                                            _lcd.ShowTimedMessage("Schedule Date", dts, 3);
                                            ShowBattInfo();
                                            break;

                                        case 1:

                                            break;

                                        case 2:
                                            _CurrentAnode = AnodeMeterSchedules.GetNextPotFromSched();
                                            LastThreeReadings.Clear();
                                            DisplayMeteringInfo(_CurrentAnode);
                                            break;
                                    }

                                    break;

                                case AnodeMeterButtonPress.uphold:

                                    switch (Navigation.CurrentMenu())
                                    {
                                        case 0: //lines

                                            Globals.bReInitDisplay = true;  // Reinit LCD Display (just in case!)

                                            ClearMenuDisplay(1);
                                            ClearMenuDisplay(2);
                                            Navigation.MoveToTopLevelMenu();
                                            Navigation.SetChoiceZero();
                                            _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                                            break;

                                        case 1: // sections menu level
                                            ClearMenuDisplay(1);
                                            ClearMenuDisplay(2);
                                            Navigation.MovePreviousMenu();
                                            Navigation.SetChoiceZero();
                                            CurrentChoice = Navigation.GetCurrentChoice();
                                            _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);

                                            break;

                                        case 2: // metering a schedule

                                            switch (CurrentChoice)
                                            {
                                                case "AH":

                                                    AnodeMeterSchedules.RemoveSchedule("AH");
                                                    _CurrentAnode = null;
                                                    NoPotsToMeter = true;
                                                    AnodeMeterSchedules.RemoveScheduleFromCache();
                                                    ClearMenuDisplay(1);
                                                    Navigation.MoveToTopLevelMenu(); // move
                                                    Navigation.SetChoiceZero();
                                                    CurrentChoice = Navigation.GetCurrentChoice();
                                                    _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                                                    _lcd.ShowTimedMessage("Actn Cancelled", 1);
                                                    break;

                                                default:
                                                    AnodeMeterSchedules.RemoveSchedule(CurrentChoice);
                                                    _CurrentAnode = null;
                                                    NoPotsToMeter = true;
                                                    AnodeMeterSchedules.RemoveScheduleFromCache();
                                                    ClearMenuDisplay(1);
                                                    Navigation.MovePreviousMenu();
                                                    Navigation.SetChoiceZero();
                                                    CurrentChoice = Navigation.GetCurrentChoice();
                                                    _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);

                                                    break;
                                            }



                                            break;

                                    }
                                    break;


                                case AnodeMeterButtonPress.downhold:
                                    switch (Navigation.CurrentMenu())
                                    {
                                        case 0: //lines

                                            if (_currentlyExecutingSchedule != null)
                                            {
                                                CurrentChoice = _currentlyExecutingSchedule;
                                                AnodeMeterSchedules.ResumeScheduleMetering(_currentlyExecutingSchedule);
                                                _CurrentAnode = AnodeMeterSchedules.GetNextPotFromSched();
                                                // move us back to the metering menu level
                                                Navigation.MoveToBottomLevelMenu();
                                                DisplayMeteringInfo(_CurrentAnode);
                                            }
                                            else
                                            {
                                                _lcd.ShowTimedMessage("SWBD: " + Globals.BuildDate.ToString("dd-MMM-yy"));
                                                // Try forcing WiFi connection...
                                                if (_gw_wifi != null)
                                                {
                                                    _dtQueueConfigCheck = DateTime.Now; // Also force a config check (for testing)
                                                    bForceScheduleReload = true;        // and Schedule reload  DAV 17JAN2024
                                                    _gw_wifi.SetWifi(BinaryTransport.WifiStates.Connected);
                                                }
                                            }
                                            break;

                                        case 1: // sections menu level

                                            break;

                                        case 2: // metering a schedule

                                            switch (CurrentChoice)
                                            {
                                                // A Down-Hold in AdHoc first-anode-for-pot requests a jump back to the pot-selection menu. 
                                                case "AH":
                                                    if (_CurrentAnode != null && _CurrentAnode._FirstAnodeForPot)
                                                    {
                                                        _currentlyExecutingSchedule = CurrentChoice;
                                                        string _currentPot = AnodeMeterSchedules.PauseScheduleMetering(_currentlyExecutingSchedule);
                                                        AnodeMeterSchedules.RemoveSchedule(CurrentChoice);
                                                        _CurrentAnode = null;
                                                        NoPotsToMeter = true;
                                                        ClearMenuDisplay(1);
                                                        ClearMenuDisplay(2);
                                                        CurrentChoice = "AH";
                                                        Navigation.MoveToTopLevelMenu();
                                                        SelectPot.UsePot(_currentPot);
                                                        ProcessSelection();
                                                        // Move to the Right-Most pot digit by rotating left through 'Cancel'
                                                        Adhoc(AnodeMeterButtonPress.left);
                                                        Adhoc(AnodeMeterButtonPress.left);
                                                    }
                                                    //else  if (_currentlyExecutingSchedule != null)
                                                    //{
                                                    //    CurrentChoice = _currentlyExecutingSchedule;
                                                    //    AnodeMeterSchedules.ResumeScheduleMetering(_currentlyExecutingSchedule);
                                                    //    _CurrentAnode = AnodeMeterSchedules.GetNextPotFromSched();
                                                    //    DisplayMeteringInfo(_CurrentAnode);
                                                    //}
                                                    break;
                                            }

                                            break;
                                    }

                                    break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::amb_MeterButtonChanged", "Error handling button press - Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Button Hndlr err!");
                }
                finally
                {
                    _buttonEventInProgress = false;
                }
            }
        }

        private void DisplayMeterInformation()
        {
            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();
            Version ver = assemName.Version;

            string topLineofDisplay = "#" + _sys.GetMeterNumber.ToString();
            topLineofDisplay += " ";
            string AnodeMeterVersion = "Vn" + ver.Major.ToString() + "." + ver.Minor.ToString() + "." + ver.Revision.ToString();
            topLineofDisplay += AnodeMeterVersion;
            //topLineofDisplay += " " + _bc.BatteryVoltage.ToString("F1") + "v";
            string bottomLineofDisplay;

            if (_dtShiftOfLoadedSchedules > DateTime.MinValue)
            {
                string Year = _dtShiftOfLoadedSchedules.Year.ToString();
                string Month = _dtShiftOfLoadedSchedules.Month.ToString();
                string Day = _dtShiftOfLoadedSchedules.Day.ToString();
                string shift = _dtShiftOfLoadedSchedules.Hour.ToString();
                if (Month.Length == 1)
                    Month = "0" + Month;
                if (Day.Length == 1)
                    Day = "0" + Day;

                if (shift == "7")
                    shift = "DS";
                else
                    shift = "NS";

                //string bottomLineofDisplay = shift + Day + "/" + Month; 
                 bottomLineofDisplay = Day + "/" + Month + "/" + Year + " - " + shift;
            }
            else
                 bottomLineofDisplay = "No Schedules";

            //bottomLineofDisplay += " ";
            //bottomLineofDisplay += DateTime.Now.ToString("HH:mm");
            _lcd.ShowTimedMessage(topLineofDisplay, bottomLineofDisplay, 4);

            DisplayBatteryState();
        }

        /********************************
         * Battery charge state analysis
         */
        private string LastBattTest = ""; // Side Effect here!
        private int LastTestDuration = 0;
        private Int16[] BattmV;
        private bool ParseLogAttempted;

        private void DisplayBatteryState(bool show = true)
        {
            if (BattmV == null)
            {
                if (ParseLogAttempted)
                    _lcd.ShowTimedMessage("Please Run", "Battery Test", 2);
                else
                    _lcd.ShowTimedMessage("Still Checking", "Battery Life", 2);
            }
            else
                DisplayBatteryLife(show);
        }

        private void ParseBattLog()
        {
            if (ParseLogAttempted) return;

            if (BattmV != null) return;

            Profile.DebugTime("ReadBattLog Start");

            ArrayList BattVoltRefs = new ArrayList();

            string Result = ReadBattLog();
            Profile.DebugTime("ReadBattLog Done");
            if (Result != "")
            {
                Profile.DebugTime("Result.Split Start");
                string[] Record = Result.Split('\r');
                Profile.DebugTime("Result.Split Done");

                for (int i = 0; i < Record.Length; i++)
                {
                    string[] RecordParts = Record[i].Split();
                    if (RecordParts.Length >= 2)
                    {
                        string sVolts = RecordParts[1].Trim();
                        try
                        {
                            if (sVolts[0].IsDigit())
                            {
                                double Volts;
                                if (double.TryParse(sVolts, out Volts))
                                {
                                    int mVolts = (int)(Volts * 1000);
                                    BattVoltRefs.Add((Int16)mVolts);
                                }
                            }
                        }
                        catch
                        {
                            // Probably the Average volts line at the bottom of the file
                        }
                    }
                }
                BattmV = (Int16[])BattVoltRefs.ToArray(typeof(Int16));

                Profile.DebugTime("ReadBattLog Loop Done");
            }
            ParseLogAttempted = true;
        }

        private void DisplayBatteryLife(bool show = true)
        {
            int TimeAtCurrentVolts = 0;
            int TimeAt3V7 = 0;
            int TimeAtPoweroff = 0;

            Profile.DebugTime("Battery State Loop Start");

            if ((BattmV != null) && BattmV.Length > 5)
            {
                Int16 mVAvBattVolts = (Int16)(Globals.gAvBattVolts * 1000);
                for (int Minutes = 0; Minutes < BattmV.Length; ++Minutes)
                {
                    Int16 mVolts = BattmV[Minutes];
                    if (mVolts >= (mVAvBattVolts))
                        TimeAtCurrentVolts = Minutes;
                    if (mVolts >= (3700))
                        TimeAt3V7 = Minutes;
                    else
                        TimeAtPoweroff = Minutes;
                }
                LastTestDuration = TimeAtPoweroff;
            }

            Profile.DebugTime("Battery State Loop Done");

            if (show)
            {
                if (TimeAt3V7 != 0)
                {
                    int ExpectedLife;
                    string s = "Remain";
                    if ((ExpectedLife = TimeAt3V7 - TimeAtCurrentVolts) <= 0)
                    {
                        ExpectedLife = TimeAtPoweroff - TimeAtCurrentVolts;
                        s = "Reserve";
                    }
                    _lcd.ShowTimedMessage("Battery " + Globals.gAvBattVolts.ToString("F2") + "V",
                        ">" + ExpectedLife + " Min " + s, 5);
                }
                else
                {
                    _lcd.ShowTimedMessage("Battery Test", "Required", 4);
                }
            }
        }

        private void ShowBattInfo()
        {
            DisplayBatteryState();
            if (LastBattTest != "")
            {
                _lcd.ShowTimedMessage("Last Test " + LastTestDuration + "min", LastBattTest, 4);
            }
            _lcd.ShowTimedMessage("Runtime (min)", "Act:" + Globals.RunTimer / 60 + " Tot:" + Globals.RefTimer / 60, 5);
        }

        private string ReadBattLog()
        {
            var sName = ReadBattTestName();
            if (sName != "")
                return _ds.ReadFileAsString(sName, true);

            return "";
        }

        // File is list of file names, possibly empty
        // Return last name in file
        private string ReadBattTestName()
        {
            string Name = "";
            string Time = "";
            try
            {
                string Result = _ds.ReadFileAsString(FileDefs.BattTestsFile);
                if (Result != "")
                {
                    string[] Record = Result.Split('\r');
                    for (int i = 0; i < Record.Length; i++)
                    {
                        string[] RecordParts = Record[i].Split(',');
                        if (RecordParts.Length >= 1 && RecordParts[0].Length > 3)
                        {
                            Name = RecordParts[0];
                            Time = RecordParts[1].Trim().Left(16);
                            if (Name.Left(2).ToUpper() == Folders.OldRootFsPath)
                                Name = Name.Right(Name.Length - 2);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore for now
                Debug.WriteLine("ReadBattTestName Exception: " + ex.Message);
            }
            if (Time != "") LastBattTest = Time;
            return Name;
        }

        /*
         * End Battery Charge Section
         * *****************************
         */

        private void Adhoc(AnodeMeterButtonPress ButtonPressed)
        {
            switch (ButtonPressed)
            {
                case AnodeMeterButtonPress.up:
                    SelectPot.DigitRotateUp();
                    break;

                case AnodeMeterButtonPress.down:
                    SelectPot.DigitRotateDown();
                    break;

                case AnodeMeterButtonPress.right:
                    SelectPot.MoveRight();
                    break;

                case AnodeMeterButtonPress.left:
                    SelectPot.MoveLeft();
                    break;
            }
            string MenuToDisplay = SelectPot.GetEditString();
            _lcd.MoveIntoDisplay(MenuToDisplay, AdhocPotPosition);
            MenuToDisplay = SelectPot.IsCancelled() ? "[Cancel]" : " Cancel ";
            _lcd.MoveIntoDisplay(MenuToDisplay, CancelPosition);

        }

        private void HighLightSelectedDigit(DigitPosition digit)
        {
            string MenuToDisplay = SelectPot.GetEditString();
            _lcd.MoveIntoDisplay(MenuToDisplay, AdhocPotPosition);
            MenuToDisplay = " Cancel ";
            _lcd.MoveIntoDisplay(MenuToDisplay, CancelPosition);
        }

        private void ProcessSelection()
        {
            string MenuToDisplay = "";

            switch (Navigation.CurrentMenu())
            {
                case 0: // Lines
                    switch (CurrentChoice)
                    {
                        case "AH":
                            ClearMenuDisplay(1);
                            ClearMenuDisplay(2);
                            MenuToDisplay = "Select Pot";
                            _lcd.MoveIntoDisplay(MenuToDisplay, Display1);

                            SelectPot.StartFresh();
                            MenuToDisplay = SelectPot.GetEditString();
                            _lcd.MoveIntoDisplay(MenuToDisplay, AdhocPotPosition);

                            MenuToDisplay = " Cancel ";
                            _lcd.MoveIntoDisplay(MenuToDisplay, CancelPosition);
                            Navigation.MoveNextMenu();
                            break;

                        default:
                            if (LoadSectionsAvailable(CurrentChoice))
                            {
                                ClearMenuDisplay(1);
                                ClearMenuDisplay(2);
                                Navigation.MoveNextMenu();
                                _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                            }
                            else
                            {
                                _lcd.ShowTimedMessage("No Schedules!", 2);
                            }
                            break;
                    }
                    break;

                case 1: //sections
                    switch (CurrentChoice)
                    {
                        case "AH":
                            if (!SelectPot.IsCancelled())
                            {
                                AdhocPotSelected = SelectPot.GetPot();
                                ClearMenuDisplay(1);
                                ClearMenuDisplay(2);
                                string Result = AnodeMeterSchedules.BuildAdhocSchedule(AdhocPotSelected, _plant.GetAnodeListForPot(AdhocPotSelected));
                                LoadSelectedSchedule("AH", Result);
                                InitialiseSelectedSchedule("AH");
                                Navigation.MoveNextMenu();
                            }
                            else
                            {
                                ClearMenuDisplay(1);
                                ClearMenuDisplay(2);
                                Navigation.MoveToTopLevelMenu();
                                Navigation.SetChoiceZero();
                                CurrentChoice = Navigation.GetCurrentChoice();
                                _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                            }
                            break;

                        default:
                            GetSelectedSectionFromFile(CurrentChoice);
                            InitialiseSelectedSchedule(CurrentChoice);
                            Navigation.MoveNextMenu(); //move to metering menu although it is empty
                            break;
                    }
                    break;

                case 2: //metering
                    switch (CurrentChoice)
                    {
                        case "AH":

                            AdhocPotSelected = SelectPot.GetPot();
                            ClearMenuDisplay(1);
                            ClearMenuDisplay(2);
                            string Result = AnodeMeterSchedules.BuildAdhocSchedule(AdhocPotSelected, _plant.GetAnodeListForPot(AdhocPotSelected));
                            LoadSelectedSchedule("AH", Result);
                            InitialiseSelectedSchedule("AH");
                            if (Navigation.CurrentMenu() != 2)
                                Navigation.MoveNextMenu();
                            break;

                        default:

                            break;
                    }

                    break;
            }
        }

        private bool GetSelectedSectionFromFile(string SectionName)
        {
            bool bOK = false;
            try
            {
                string thisSched = ReadSchedulesFromFile(SectionName);

                if (thisSched != null && thisSched != NO_METERING_SCHEDULED)
                {
                    LoadSelectedSchedule(SectionName, thisSched);
                    bOK = true;
                }
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::GetSelectedScheduleFromFile", "Error loading schedule from SD. Reason: " + ex.Message, "Schd Load Err.");
            }
            return bOK;
        }

        private bool LoadSchedulesFileToDisk()
        {
            bool bOK = false;
            bool bAttemptFetch = true;

            int iRetryCnt = 0;
            string thisSched = "";

            while (iRetryCnt++ < 3 && bAttemptFetch)
            {
                //thisSched = ActiveGW().IssueRequest("GetSelectedScheduleRqstAll", null, null, null, 20000);
                thisSched = ActiveGW().IssueRequest("GetSelectedScheduleRqstAll", "ScheduleFor", "MeterNumber", _sys.GetMeterNumber.ToString(), 60000);

                if (thisSched != "")
                {
                    int EosPot;
                    if (thisSched.Length < 30 && thisSched.IndexOf(NO_METERING_SCHEDULED) != -1)
                    {
                        _ds.WriteSchedulesToDisk(thisSched);
                        bOK = true;
                        bAttemptFetch = false;
                    }
                    else if ((EosPot = thisSched.LastIndexOf("End-Of-Schedule")) != -1)
                    {
                        // Strip off the end-of-schedule tag before parsing
                        thisSched = thisSched.Left(EosPot);
                        _ds.WriteSchedulesToDisk(thisSched);
                        bOK = true;
                        bAttemptFetch = false;

                        _lcd.ShowTimedMessage("Schedules Loaded", 3);
                    }
                    else
                        _lcd.ShowTimedMessage("Sched Load Err", 2);
                }
                else
                {
                    bAttemptFetch = false;
                    bOK = false;
                    _lcd.ShowTimedMessage("No Service", 3);
                }
            }
            if (iRetryCnt >= 3 && bOK == false)
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "AnodeMeter::LoadSchedulesFileToDisk", "Exceeded retry count while attempting to fetch schedules from server. Last string returned: " + thisSched, "Schd Load Err.");

            return bOK;
        }

        private bool LoadSectionsAvailable(string Line)
        {
            bool bOK = false;
            try
            {
                ArrayList scheduleList = ReadSectionAvailable(Line);

                if (scheduleList.Count > 0)
                {
                    ArrayList al = scheduleList.Sort();

                    Navigation.SetChoices("Sections", al);

                    bOK = true;
                }
            }

            catch (Exception x)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "LoadSectionsAvailable", x.Message, "Load Sections error");
            }
            return bOK;
        }

        private ArrayList ReadSectionAvailable(string Line)
        {
            string result = "";
            ArrayList Sections = new ArrayList();

            try
            {
                result = ReadSchedulesFile();
                if (result != "")
                {
                    string[] Record = result.Split('\r');

                    // we start at i=1 as the first record now has the date of the schedules

                    Char[] comma = { ',' };
                    for (int i = 1; i < Record.Length - 1; i++)
                    {
                        string[] RecordParts = Record[i].Split(comma);

                        if (RecordParts.Length >= 4)
                        {   // We were tripping up on empty lines in the file - DAV

                            string key = RecordParts[2].ToString();

                            if (RecordParts[3].ToString() == Line)
                            {
                                // add an entry to handle all metering
                                var RoomAll = RecordParts[2].Substring(0, 1).ToString() + "Am";
                                if (Sections.IndexOf(RoomAll) == -1)
                                {
                                    Sections.Add(RoomAll);
                                }

                                //this ensures we only get one instance of each schedule type
                                if (Sections.IndexOf(key) == -1)
                                {
                                    Sections.Add(key);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::ReadSectionAvailable", "Error in \"" + FileDefs.SchedulesFileName + "\". Reason: " + ex.Message, "");
            }
            return Sections;
        }

        private string SchedCache = "";
        private string ReadSchedulesFile()
        {
            if (_ds.bSchedFileTouched)
            {
                SchedCache = _ds.ReadSchedulesFile();
                _ds.bSchedFileTouched = false;

                if (Globals.MaskT4)  // Should be set for Portland, for others must be in FactorySettings file for now
                    MaskSchedules(SchedCache);
            }
            return SchedCache;
        }

        // Mask schedules for Portland
        // Make configurable after testing
        private Hashtable MaskedPots = new Hashtable();
        private void MaskSchedules(string sSched)
        {
            {
                if (sSched != null && sSched != "")
                {
                    var record = sSched.Split('\r');
                    string[] Record = record;

                    Hashtable Masks = new Hashtable();
                    MaskedPots.Clear();

                    // we start at i=1 as the first record now has the date of the schedules
                    Char[] comma = { ',' };
                    for (int i = 1; i < Record.Length - 1; i++)
                    {
                        string[] RecordParts = Record[i].Split(comma);
                        if (RecordParts.Length >= 4) // We were tripping up on empty lines in the file - DAV
                        {
                            string pot = RecordParts[0].Trim();
                            string code = RecordParts[1].Trim();

                            if (code == "CP")
                            {
                                if (!Masks.Contains(pot))
                                    Masks.Add(pot, null);
                            }
                            else if (code == "T4")
                            {
                                if (Masks.Contains(pot) && !MaskedPots.Contains(pot))
                                {
                                    MaskedPots.Add(pot, Record[i]);
                                    //Debug.Print("Masking: " + Record[i]);
                                }
                            }
                        }
                    }
                }
            }
        }

        private string ReadSchedulesFromFile(string _theSectionName)
        {
            string PotsToMeter = null;

            try
            {
                var result = ReadSchedulesFile();
                if (result != "")
                {
                    string[] Record = result.Split('\r');

                    // we start at i=1 as the first record now has the date of the schedules
                    Char[] comma = { ',' };
                    for (int i = 1; i < Record.Length - 1; i++)
                    {
                        string[] RecordParts = Record[i].Split(comma);

                        if (RecordParts.Length >= 4) // We were tripping up on empty lines in the file - DAV
                        {
                            string pot = RecordParts[0].Trim();
                            string code = RecordParts[1].Trim();
                            string section = RecordParts[2].Trim();

                            if (section == _theSectionName)
                            {
                                if (code != "T4" || ((code == "T4" && !MaskedPots.Contains(pot))))
                                    PotsToMeter += Record[i].ToString() + '\r';
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::ReadSchedulesFromFile", "Can't process data. Reason: " + ex.Message, "");
            }
            return PotsToMeter;
        }

        private void LoadSelectedSchedule(string ScheduleName, string SchedData)
        {
            NoPotsToMeter = true;

            if (SchedData != null && SchedData != NO_METERING_SCHEDULED)
            {
                AnodeMeterSchedules.AddSchedule(ScheduleName, SchedData);
                NoPotsToMeter = false;
            }
        }


        private void InitialiseSelectedSchedule(string ScheduleName)
        {
            ClearMenuDisplay(1);
            ClearMenuDisplay(2);

            _fileDateTime = DateTime.Now;

            NoPotsToMeter = false;

            _CurrentAnode = AnodeMeterSchedules.GenerateSchedule(ScheduleName);

            if (_CurrentAnode != null)
            {
                if (LastThreeReadings.Count != 0)
                    LastThreeReadings.Clear();

                DisplayMeteringInfo(_CurrentAnode);
                NoPotsToMeter = false;
            }
            else
            {
                _lcd.ShowTimedMessage("Sched Empty");
                NoPotsToMeter = true;
            }
        }


        private void ProcessAnode(Boolean AnodeSkipped)
        {
            try
            {
                if (_CurrentAnode != null)
                {
                    bool bRewoundToLastPot = false;

                    if (_CurrentAnode._FirstAnodeForPot)
                    {
                        //TODO DAV Fixing exceptions. Default to 32 or 0?
                        int ac = _plant.GetPotDetails(_CurrentAnode._potName) == null ? 0 : _plant.GetPotDetails(_CurrentAnode._potName)._anodeCount;
                        _PotAnodeResults = new PotMeasurementRecord(DateTime.Now, _CurrentAnode._potName, MeterNumber.ToString(), ac);
                    }
                    else
                    {
                        _previousAdHocSchedule = null;
                        _prevAdHocAnode = null;
                    }

                    if (!AnodeSkipped)
                        _PotAnodeResults.AddMeasuredValue(_CurrentAnode.Name, _currentMeasVolts, _CurrentAnode._measType);


                    //this is a special case where there is only one anode
                    //if (_CurrentAnode._FirstAnodeForPot && _CurrentAnode._MidAnodeForPot && _CurrentAnode._LastAnodeForPot)
                    //{

                    //    _PotAnodeResults = new PotMeasurementRecord(DateTime.Now, _CurrentAnode._potName, MeterNumber.ToString(), _plant.GetPotDetails(_CurrentAnode._potName)._anodeCount);

                    //    if (!AnodeSkipped)
                    //    {
                    //        _PotAnodeResults.AddMeasuredValue(_CurrentAnode.Name, _currentMeasVolts, _mtExpectedNext);
                    //    }

                    //    _ds.WritePotMeasurement(_PotAnodeResults.MyToString());

                    //}
                    //else
                    //{

                    //    if (_CurrentAnode._FirstAnodeForPot)
                    //    {
                    //        _PotAnodeResults = new PotMeasurementRecord(DateTime.Now, _CurrentAnode._potName, MeterNumber.ToString(), _plant.GetPotDetails(_CurrentAnode._potName)._anodeCount);

                    //        if (!AnodeSkipped)
                    //        {
                    //            _PotAnodeResults.AddMeasuredValue(_CurrentAnode.Name, _currentMeasVolts, _mtExpectedNext);
                    //        }
                    //    }
                    //    else
                    //    {
                    //        _previousAdHocSchedule = null;
                    //        _prevAdHocAnode = null;
                    //    }


                    if (_CurrentAnode._MidAnodeForPot && !AnodeSkipped)
                    {
                        _led.IndicateMilestone(LED.Milestones.HalfPot);
                        // _PotAnodeResults.AddMeasuredValue(_CurrentAnode.Name, _currentMeasVolts, _mtExpectedNext);
                    }



                    if (_CurrentAnode._LastAnodeForPot)
                    {
                        if (!AnodeSkipped)
                        {
                            _led.IndicateMilestone(LED.Milestones.EndPot);
                            //_PotAnodeResults.AddMeasuredValue(_CurrentAnode.Name, _currentMeasVolts, _mtExpectedNext);

                            if (CurrentChoice == "AH")
                            {
                                _previousAdHocSchedule = AnodeMeterSchedules;
                                _prevAdHocAnode = _CurrentAnode;
                                SelectPot.UsePot(_prevAdHocAnode._potName);
                                bRewoundToLastPot = true;
                            }
                        }

                        _ds.WritePotMeasurement(_PotAnodeResults.MyToString(Navigation.SubChoice(CurrentChoice)));
                        if (MaskedPots.Contains(_PotAnodeResults.PotNumber))
                        {
                            // Now we just have to build an output string from the schedule info and the results data!
                            string sched = MaskedPots[_PotAnodeResults.PotNumber].ToString();
                            var sData = _PotAnodeResults.MaskedPotString(sched);
                            _ds.WritePotMeasurement(sData);
                        }

                        if (LastThreeReadings.Count != 0)
                            LastThreeReadings.Clear();

                    }


                    //if (!AnodeSkipped && (!_CurrentAnode._LastAnodeForPot && !_CurrentAnode._FirstAnodeForPot && !_CurrentAnode._MidAnodeForPot))
                    //{
                    //    _PotAnodeResults.AddMeasuredValue(_CurrentAnode.Name, _currentMeasVolts, _mtExpectedNext);
                    //}




                    if (!AnodeSkipped)
                    {
                        if (LastThreeReadings.Count == 0)
                        {
                            LastThreeReadings.Add(FormatMeasurement(_currentMeasVolts * 1000));
                        }
                        else if (LastThreeReadings.Count == 1)
                        {
                            LastThreeReadings.Add(LastThreeReadings[0].ToString());
                            LastThreeReadings[0] = (FormatMeasurement(_currentMeasVolts * 1000));
                        }
                        else if (LastThreeReadings.Count == 2)
                        {
                            LastThreeReadings.Add(LastThreeReadings[1].ToString());
                            LastThreeReadings[1] = LastThreeReadings[0];
                            LastThreeReadings[0] = (FormatMeasurement(_currentMeasVolts * 1000));
                        }
                        else
                        {
                            LastThreeReadings[2] = LastThreeReadings[1];
                            LastThreeReadings[1] = LastThreeReadings[0];
                            LastThreeReadings[0] = (FormatMeasurement(_currentMeasVolts * 1000));
                        }
                    }

                    if (!bRewoundToLastPot)
                    {
                        if (!AnodeSkipped)
                            _CurrentAnode = AnodeMeterSchedules.GetNextMeasurement();
                    }
                    else
                        _CurrentAnode = null;
                }

                if (_CurrentAnode != null)
                    DisplayMeteringInfo(_CurrentAnode);
                else
                {
                    if (!NoPotsToMeter)
                    {
                        _lcd.ShowTimedMessage("Sched Complete", 2);
                        ArrayList al = AnodeMeterSchedules.GetScheduleNames();
                        if (al.Count > 0)
                            AnodeMeterSchedules.RemoveSchedule(al[0].ToString());

                        if (CurrentChoice == "AH")
                        {
                            string nextPot = null;
                            nextPot = _plant.GetNextAdhocPot(SelectPot.GetPot());
                            SelectPot.UsePot(nextPot);
                            ProcessSelection();
                        }
                        else
                        {
                            NoPotsToMeter = true;
                            LastThreeReadings.Clear();
                            ClearMenuDisplay(1);
                            ClearMenuDisplay(2);
                            Navigation.MovePreviousMenu();
                            Navigation.SetChoiceZero();
                            CurrentChoice = Navigation.GetCurrentChoice();
                            _lcd.MoveIntoDisplay(Navigation.Display(), MenuChoicePosition);
                        }

                    }

                }
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "AnodeMeter::ProcessAnode", "Error Processing Anode. Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Software Err!");
            }

        }


        private void DisplayMeteringInfo(Schedule.AnodeSched ASchedule)
        {

            ClearMenuDisplay(1);

            if (ASchedule != null)
            {
                _lcd.MoveIntoDisplay(_CurrentAnode._potName, CurrentPotPosition);
                _lcd.MoveIntoDisplay(":", CurrentPotAnodeSeparatorPosition);
                _lcd.MoveIntoDisplay(_CurrentAnode._AnodePos.ToString(), CurrentAnodeNumberPosition);
                _lcd.MoveIntoDisplay(_CurrentAnode._measType.ToMtString(), CurrentMeteringTypePosition);

                DisplayLastThreeReadings();
            }

        }

        private void DisplayLastThreeReadings()
        {
            string ReadingsToDisplay = null;

            for (int i = 0; i < LastThreeReadings.Count; i++)
                ReadingsToDisplay += LastThreeReadings[i].ToString() + " ";

            if (ReadingsToDisplay != null)
                _lcd.MoveIntoDisplay(ReadingsToDisplay.ToString(), LastThreeReadingsPosition);
            else
                _lcd.MoveIntoDisplay("                ", LastThreeReadingsPosition);

        }

        private void ClearMenuDisplay(int DisplayToClear)
        {
            string MenuToDisplay;
            if (DisplayToClear == 1)
            {
                MenuToDisplay = "                ";
                _lcd.MoveIntoDisplay(MenuToDisplay, Display1);
            }

            if (DisplayToClear == 2)
            {
                MenuToDisplay = "                ";
                _lcd.MoveIntoDisplay(MenuToDisplay, Display2);
            }

        }
        private string FormatMeasurement(double Measurement) { return Measurement.ToString(_plant.GetMeasurementDisplayString()); }

    }
 }