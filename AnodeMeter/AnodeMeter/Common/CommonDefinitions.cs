using System;
using System.Collections;
using System.Diagnostics;

namespace AnodeMeter
{
    public enum AnodeMeterType { EMULATOR, TOPAZ, GHI };
    public enum MeasModeOption { RodDrop, ClampDrop, RodThenClamp, ClampThenRod };
    public enum MeasurementType { RodDrop, ClampDrop };
    public enum SpecialLCDCharacters : byte { batteryMt = 1, batteryQuart = 2, batteryHalf = 3, battery3Quart = 4, batteryFull = 5, Tick = 6, Down = 7 };
    public static class SpecialLCDCharacter
    {   // Fix this later, when get time. DAV
        public const string Tick = "\u0006";
        public const string Down = "\u0007";
    }

    public static class Globals
    {
        public static Boolean ExclusiveMenuUse = false;
        public static readonly string[] MeasurementModeDesc = new string[] { "Rod-Drop", "Clamp-Drop", "Rod then Clamp", "Clamp then Rod" };
        public static int MeasurementModeIndex = 0;

        // Board Settings - DAV
        // Load/Save from onboard storage
        public static byte LCDBiasPC = GlobalConsts.FACTORY_DEFAULT_LCD_BIAS;     // This needs to be set correctly so we can see the display
        public static byte GLedBright = 20;     // Green LED Brightness
        public static byte RLedBright = 20;     // Red LED Brighness
        public static byte BackLightLevel = 25; // Default to 25% duty cycle
        public static UInt16 Serial = 0;        // Serial number (as per box internal sticker)

        // Read from SD card
        //TODO DAV These are not yet (fully) implemented and will require some tuning.
        public static int SleepDelay = 5 * 60;  // When disconnected, hibernate after this many seconds without activity
        public static int WakeDelay = 30 * 60;  //  but wake up every this many seconds to check battery (and possibly power-down)
        public static int ConnectedSleepDelay = 1 * 60; // When connected, hibernate after this many seconds to enable faster battery charge (depending on battery level?) 
        public static int ConnectedWakeDelay = 10 * 60; //  but wake up after this time to check for new schedules
        public static byte LogRawData = 0;      // If set we log every reading (<1.0V) to file
        public static bool MaskT4 = false;     // CP masks T4 for Portand

        // New for WiFi
        public static string[] WifiAPs;
        public static byte Wifi_AP_Index;       // Hints (last succesful AP or server connection)
        public static byte Wifi_Server_Index;
        public static string[] Gateways;
        public static bool USBDisable = false;
        public static bool WifiDisable = false;
        public static byte WifiModes = 3;       // Flags, b0 = timed, b1 = end of pot, b2 = on charge
        public static int WifiSyncTime = 5 * 60;  // Intervals between wiFi sync attempts in timed mode (seconds)
        public static Hashtable WifiInfo = new Hashtable(); // Cache info about wifi
        public static bool[] WifiStatus = new bool[4];

        public static bool WifiTestMode = false; // Use when testing so normal operations don't step on us

        public static bool SleepOverride = false;   // Set if values have been overridden from SD card

        public enum ConfigState : byte { Unknown, NotConfigured, ConfigOK, UpdatingConfig };

        // Globals not saved in Flash
        public static bool SDCardPresent = false;   // If no SD card we can't function normally, but still need to run basic functions
        public static bool SDCardFault = false;   // for when Directory.Exists() throws E_NOT_SUPPORTED!
        //public static bool G120 = false;            // Default to EMX
        public static bool DiskDriveMode = false;   // true if we are connected as USB device in Disk Drive mode
        public static bool USBAvailable = true;     // false if in USB debugging mode
        public static ConfigState CfgState = ConfigState.Unknown;
        public static DateTime BuildDate = DateTime.MaxValue;   // Gets set in main
        public static int DeviceID = -1;     // this is MeterNumber, stored in sysinit (GetMeterNumber) and cached in logging, but boardsetup wants to see it...
        public static int RefTimer = 0;     // Seconds since system start
        public static int RunTimer = 0;     // Active seconds since start (not including time in hibernate)
        public static int BatTimer = 0;     // Seconds since battery test start
        public static string gBattFile = "";    // Battery log file name
        public static float gBattVolts = 4.0f;
        public static float gAvBattVolts = 4.0f;

        public enum PowerStates : byte { Normal = 0, BatteryTest, LowPower, VeryLowPower, Critical }; // For use in testing battery, or power-save when battery low
        public static PowerStates PowerState = PowerStates.Normal;

        public static float BattVoltLow = 3.7f; // Low battery
        public static float BattVoltVeryLow = 3.6f; // Very Low battery
        public static float BattVoltCritical = 3.5f; // Critically Low battery - immediate power off

        //TODO WiFi - make more nuanced later. Perhaps track if we have WiFi (on board), if we have ever connected, last SSID, etc?
        public static bool HaveWifi = false;
        //public static int WifiConnectTime = 1; //TODO minutes? Could keep in config. Default probably 5 or 10 or more, need client feedback. 1 for testing
    }

    public static class Profile
    {
        private static long dbgLastUptime = 0;
        private static long dbgBaseTime = 0;
        [Conditional("PROFILE")]
        public static void DebugTime(string sWhere = "")
        {
            //TODO DAV This may not work, but seems Gus doesn't want to implement a proper uptime (GetTickCount() ? )
            long tUp = DateTime.Now.Ticks;  // Microsoft.SPOT.Hardware.PowerState.Uptime.Ticks;
            long tLast = tUp - dbgLastUptime;
            if (dbgBaseTime == 0) dbgBaseTime = tUp;
            long tTotUpTime = tUp - dbgBaseTime;
            dbgLastUptime = tUp;
            //Debug.Print("At " + sWhere + " Time " + tUp/10000 + " Delta " + tLast/10000 + "mS Elapsed " + tTotUpTime/10000);
            Debug.WriteLine("At " + sWhere + " Time " + tTotUpTime / 10000 + "mS Delta " + tLast / 10000 + "mS");
        }
        public static void Rebase(long tickoffset)
        {
            dbgBaseTime += tickoffset;
            DebugTime("Profile::Rebase");
        }
    }

    public static class GlobalConsts
    {
        public const int POT_NAME_LENGTH = 4;
        public const int LED_UPDATE_RATE = 100;
        public const int DISPLAY_UPDATE_RATE = 300;
        public const double MIN_REASONABLE_ROD_DROP = 0.001;
        public const double MAX_ABS_MEAS_RDROP = 0.1;
        public const double MAX_ABS_MEAS_CDROP = 0.17;
        public const double LARGE_OUTLIER_RODDROP_THRESH = 0.015;
        public const double LARGE_OUTLIER_CLAMPDROP_THRESH = 0.045;
        public const int ROD_DROP_DECISION_WINDOW_WIDTH = 6;
        public const int MEAS_IGNORE_WINDOW_WIDTH = 2;
        public const int ROD_DROP_SETTLING_SCANS = 3;
        public const int VOLT_DROP_MAX_GRAB_RETRIES = 4;
        public const double MEAS_RD_MAX_WINDOW_RANGE = 0.0005;
        public const double MEAS_CD_MAX_WINDOW_RANGE = 0.001;
        public const int HARDWARE_AI_SCAN_MILLI_SECONDS = 70;
        public const int MESSAGE_LINGER_SECONDS = 2;
        public const int FILE_RETENTION_DAYS = 31;
        public const uint WATCHDOG_TIMEOUT_MILLISECONDS = 30000;
        public const int HOUSE_KEEPING_CHECK_SECONDS = 3;
        public const char FILENAME_PREPEND_CHAR = '#';
        public const int MINUTES_BEFORE_SHIFT_TO_LOAD_SCHEDULES = 30;
        public const int FACTORY_DEFAULT_LCD_BIAS = 75;
        public const int USB_TRANSPORT_RECOMMENDED_MAX_TX_SIZE = 500;
        public const int WIFI_CONNECT_SECONDS = 60; //TODO Probably 300 (5 min), or set from config file, later...
    }

    public static class Folders
    {
        public const string RootFsPath = ""; // Was "SD" on EMX and G120 (NetMF);

        public const string ConfigPath = RootFsPath + "\\Config";
        public const string SchedPath = RootFsPath + "\\Schedules";
        public const string LogsPath = RootFsPath + "\\Logs";
        public const string LogsArchivePath = LogsPath + "\\Archive";
        public const string MeasurementsRoot = RootFsPath + "\\Measurements";
        public const string NewMeasurementsPath = MeasurementsRoot + "\\New";
        public const string OldMeasurementsPath = MeasurementsRoot + "\\Old";
        public const string BattLogsPath = LogsPath + "\\Battery";

        public static string[] GetPaths()
        {
            string[] Paths = { ConfigPath, SchedPath, LogsPath, LogsArchivePath, MeasurementsRoot, NewMeasurementsPath, OldMeasurementsPath, BattLogsPath };
            return Paths;
        }
    }
    public static class FileDefs
    {
        public const string SystemConfigFile = Folders.ConfigPath + "\\" + "SmelterConfiguration.xml";
        public const string SchedulesFileName = Folders.SchedPath + "\\" + "Schedules.csv";
        //public const string ScheduleTypesFileName = Folders.SchedPath + "\\" + "ScheduleTypes.csv";
        public const string ScheduleResultsNew = Folders.NewMeasurementsPath + "\\" + "AnodeDrops.csv";
        public const string DeviceConfigFile = Folders.ConfigPath + "\\" + "Device.xml";
        public const string FactoryDefaultsFile = Folders.ConfigPath + "\\" + "FactoryDefaults.csv";
        public const string LastServerSyncTimeFile = Folders.ConfigPath + "\\" + "LastServerSync.txt";
        public const string BattTestsFile = Folders.BattLogsPath + "\\" + "BattTests.txt";
        public const string BattRefFile = Folders.BattLogsPath + "\\" + "BattRef.txt";
    }
}
