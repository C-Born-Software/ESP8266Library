using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using AnodeMeter.Common;
using Hardware.LcdCharacterDisplay;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Rtc;
using GHIElectronics.TinyCLR.Native;
using System.Diagnostics;

namespace AnodeMeter.Hardware
{
    public class FlashSettings
    {
#warning //TODO Refactor ExtendedWeakReference to use other method! DAV
//        private static ExtendedWeakReference s_FlashSettings;
//        private static class TypeUniqueToOurAppV2 { }

        [Serializable]
        public sealed class FactoryDefaults
        {
            public char[] key = { 'D', 'F', 'L','T' };
            public byte LCDBiasPC = GlobalConsts.FACTORY_DEFAULT_LCD_BIAS;         // This needs to be set correctly so we can see the display
            public byte GLedBright = 25;     // Green LED Brightness
            public byte RLedBright = 25;     // Red LED Brighness
            public byte BackLightLevel = 35; // 35 Percent backlight by default 
            public UInt16 Serial = 0;         // Serial number (written on PCB/Box sticker, digits only)
            public byte MeasMode = 0;
        }

        public static void OnBoot()
        {
#warning //TODO Refactor ExtendedWeakReference to use other method! DAV
#if false
            s_FlashSettings = ExtendedWeakReference.RecoverOrCreate(typeof(TypeUniqueToOurAppV2), 0, ExtendedWeakReference.c_SurvivePowerdown);
            //s_FlashSettings.Priority = (Int32)ExtendedWeakReference.PriorityLevel.Important; // Or System??
            FactoryDefaults factoryDefaults = (FactoryDefaults)s_FlashSettings.Target;
            if (factoryDefaults == null)
            {
                // Wasn't found - create new one
                factoryDefaults = new FactoryDefaults();
            }
            else
            {
                // found our defaults
                Globals.LCDBiasPC = factoryDefaults.LCDBiasPC;
                Globals.BackLightLevel = factoryDefaults.BackLightLevel;
                Globals.GLedBright = factoryDefaults.GLedBright;
                Globals.RLedBright = factoryDefaults.RLedBright;
                Globals.Serial = factoryDefaults.Serial;
                Globals.MeasurementModeIndex = factoryDefaults.MeasMode;
            }

            //s_FlashSettings.Target = factoryDefaults; // Writing settings?
#endif
        }

        public static void Reload()
        {
#warning //TODO Refactor ExtendedWeakReference to use other method! DAV
#if false
            FactoryDefaults factoryDefaults = (FactoryDefaults)s_FlashSettings.Target;

            if (factoryDefaults != null)
            {
                // Load our defaults
                Globals.LCDBiasPC = factoryDefaults.LCDBiasPC;
                Globals.BackLightLevel = factoryDefaults.BackLightLevel;
                Globals.GLedBright = factoryDefaults.GLedBright;
                Globals.RLedBright = factoryDefaults.RLedBright;
                Globals.Serial = factoryDefaults.Serial;
                Globals.MeasurementModeIndex = factoryDefaults.MeasMode;
            }
#endif
        }

        public static void SaveSettings()
        {
#warning //TODO Refactor ExtendedWeakReference to use other method! DAV
#if false
            FactoryDefaults factoryDefaults = (FactoryDefaults)s_FlashSettings.Target;

            if (factoryDefaults == null)
            {
                // Wasn't found - create new one
                factoryDefaults = new FactoryDefaults();
            }

            // Install current values

            factoryDefaults.LCDBiasPC = Globals.LCDBiasPC;
            factoryDefaults.BackLightLevel = Globals.BackLightLevel;
            factoryDefaults.GLedBright = Globals.GLedBright;
            factoryDefaults.RLedBright = Globals.RLedBright;
            factoryDefaults.Serial = Globals.Serial;
            factoryDefaults.MeasMode = (byte)Globals.MeasurementModeIndex;

            s_FlashSettings.Target = factoryDefaults; // Write settings
                                                      //GHI.Premium.System.Util.FlushExtendedWeakReferences();
                                                      //ExtendedWeakReference.FlushAll();
#endif
        }
    }

    public class FlashWifi
    {
        #warning //TODO Refactor ExtendedWeakReference to use other method! DAV
//        private static ExtendedWeakReference s_FlashWifi;
//        private static class TypeUniqueToOurAppW1 { }

        [Serializable]
        public sealed class WifiHints
        {
            public char[] key = { 'W', 'I', 'F', 'I' };
            public byte AP_Index = 0;
            public byte Server_Index = 0;
        }

        public static void OnBoot()
        {
#warning //TODO Refactor ExtendedWeakReference to use other method! DAV
#if false
            s_FlashWifi = ExtendedWeakReference.RecoverOrCreate(typeof(TypeUniqueToOurAppW1), 0, ExtendedWeakReference.c_SurvivePowerdown);
            //s_FlashWifi.Priority = (Int32)ExtendedWeakReference.PriorityLevel.Important; // Or System??
            WifiHints WifiHints = (WifiHints)s_FlashWifi.Target;
            if (WifiHints == null)
            {
                // Wasn't found - create new one
                WifiHints = new WifiHints();
            }
            else
            {
                Globals.Wifi_AP_Index = WifiHints.AP_Index;
                Globals.Wifi_Server_Index = WifiHints.Server_Index;
            }
#endif
        }

        public static void Reload()
        {
#warning //TODO Refactor ExtendedWeakReference to use other method! DAV
#if false
            WifiHints WifiHints = (WifiHints)s_FlashWifi.Target;

            if (WifiHints != null)
            {
                Globals.Wifi_AP_Index = WifiHints.AP_Index;
                Globals.Wifi_Server_Index = WifiHints.Server_Index;
            }
#endif
        }

        public static void SaveSettings()
        {
#warning //TODO Refactor ExtendedWeakReference to use other method! DAV
#if false
            WifiHints WifiHints = (WifiHints)s_FlashWifi.Target;

            if (WifiHints == null)
                WifiHints = new WifiHints();

            WifiHints.AP_Index = Globals.Wifi_AP_Index;
            WifiHints.Server_Index = Globals.Wifi_Server_Index;
            s_FlashWifi.Target = WifiHints; // Write settings
            //ExtendedWeakReference.FlushAll();
#endif
        }
     }
 }
