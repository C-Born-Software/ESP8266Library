using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GHIElectronics.TinyCLR.Devices.UsbClient;
using Hardware.LcdCharacterDisplay;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Rtc;
using GHIElectronics.TinyCLR.Native;
using GHIElectronics.TinyCLR.Devices.Storage;
using GHIElectronics.TinyCLR.IO;
using GHIElectronics.TinyCLR.Update;
using AnodeMeter.Common;
using AnodeMeter;
using PervasiveDigital.Net;
//using PervasiveDigital.Utilities;
using PervasiveDigital.Hardware.ESP8266;
using GHIElectronics.TinyCLR.Cryptography;
using GHIElectronics.TinyCLR.Devices.Adc;

namespace AnodeMeter
{
    public static class IOMap
    {
        // Define IO, with default SC20260 mappings

        // LCD Display
        public const int RS = SC20260.GpioPin.PK0;
        public const int Enable = SC20260.GpioPin.PJ10;
        public const int LCD_Data_4 = SC20260.GpioPin.PH10;
        public const int LCD_Data_5 = SC20260.GpioPin.PH9;
        public const int LCD_Data_6 = SC20260.GpioPin.PF6;
        public const int LCD_Data_7 = SC20260.GpioPin.PF7;

        // 3.3V Power control
        //public static Cpu.Pin PowerLine = GHI.Hardware.EMX.Pin.IO31;
        public const int PowerLine = SC20260.GpioPin.PH12;

        // Buttons
        public const int UpButton = SC20260.GpioPin.PB7;
        public const int DownButton = SC20260.GpioPin.PE3;
        public const int CentreButton = SC20260.GpioPin.PI13;
        public const int LeftButton = SC20260.GpioPin.PF10;
        public const int RightButton = SC20260.GpioPin.PF8;

        // PWM Channels
        public const int BackLight = SC20260.Timer.Pwm.Controller2.PA3;
        public const string LedFaderController = SC20260.Timer.Pwm.Controller3.Id;
        //public const int GLedFader = SC20260.GpioPin.PB0;
        public const int GLedFader = SC20260.Timer.Pwm.Controller3.PB0;
        //public const int RLedFader = SC20260.GpioPin.PB1;
        public const int RLedFader = SC20260.Timer.Pwm.Controller3.PB1;

        // Analog Inputs
        //public const int VBatt = SC20260.GpioPin.PC0;
        public const int VBatt = SC20260.Adc.Controller1.PC0;
        //public const int VRef2p5 = SC20260.GpioPin.PF9;
        public const int VRef2p5 = SC20260.Adc.Controller3.PF9;
        /*public static Cpu.AnalogChannel VBatt = Cpu.AnalogChannel.ANALOG_1;
        public static Cpu.AnalogChannel VRef2p5 = Cpu.AnalogChannel.ANALOG_5;
        */

        // Analog Outputs
        public const int LCDBias = SC20260.GpioPin.PA5;
        //public static Cpu.AnalogOutputChannel LCDBias = Cpu.AnalogOutputChannel.ANALOG_OUTPUT_0;

        // ESP8266 WiFi
        public const int WiFiPowerPin = SC20260.GpioPin.PD4;
        public const int WiFiResetPin = SC20260.GpioPin.PE4;
        public const int WiFiProgramPin = SC20260.GpioPin.PI4;
        public const string WiFiComPort = SC20260.UartPort.Uart6; //"COM3";
    }
}

namespace AnodeMeter.Hardware
{
    public class BoardSetup
    {
        private static Common.DataStore _ds;
        //        private static Common.LcdDisplay _lcd;
        private static LiquidCrystal _lcd;
        private static Hardware.PhysicalButtons _amb;
        //private static Hardware.PhysicalLED _led;
        private static Common.LED _led;
        private static Common.BatteryCharge _bc;
        private static BinaryTransport _gw_usb;
        private static WifiTransport _gw_wifi;
        private static AnodeMeter _am;
        private static GpioPin PowerLine = null;

        private static HardwareButton LeftButton, RightButton, UpButton, DownButton, CentreButton;

        private static bool InSetupMode = false;
        private static bool InShutDownMode = false;
        private static bool StillHeld = false;

        //private static UsbClientController UsbController;

        public enum MenuTypes { Settings = 0, Info, Mode, Support, Wifi, Exit, Last = Exit, First = Settings };
        public enum MenuItems
        {
            setTopLevel = 0, setBackLight, setGreenLED, setRedLED, setLCDBias, setClock, setMeasMode, setLogRawData, setWiFi, setWifiDebug, setWifiVerbose, setRebootToMS, /* setKeepSchedule, */ setSave, setLoad, setAutoScan = 100,
            infoTopLevel = 0, infoBatt, infoInput, infoFirmware, infoBuiltOn, infoSDCard, infoSerial, infoUID, infoTemperature,
            modeTopLevel = 0, modeDiskDrive = 2,
            supportTopLevel = 0, supportPowerOff,supportHibernate, supportBattTest, supportIFU, supportEraseID, supportWifiTest, supporSetSerial = 100,
            wifiTopLevel = 0, wifiStatus, wifiInfo, wifiScan, wifiTop, wifiTest, wifiSpeedTest,
            exitTopLevel = 0
        };

        private static MenuTypes MenuType = MenuTypes.Settings;
        private static MenuItems MenuItem = MenuItems.setTopLevel;
        private static int MenuStep = 0;

        private double Ain0 = 0.0;
        private double AvAin0 = 0.0;
        private double AvIntTemp = -1.0;

        private static Globals.PowerStates LastPowerState = Globals.PowerStates.Normal;

        private static int WaitSecs = 0;

        public void SetAnalogReading(double ain)
        {
            Ain0 = ain;
        }

        struct dtunit { public int val; public byte col; public byte row; public byte width; public int min; public int max; public dtunit(int Val, byte Col, byte Row, byte Width, int Min, int Max) { val = Val; col = Col; row = Row; width = Width; min = Min; max = Max; } };

        dtunit[] dtunits = new dtunit[] {
            new dtunit (0, 6, 0, 4, 2012, 2100 ),   // Year
            new dtunit (0, 11, 0, 2, 1, 12 ),       // Month
            new dtunit (0, 14, 0, 2, 1, 31 ),       // Day
            new dtunit (0, 6, 1, 2, 0, 23 ),        // Hour
            new dtunit (0, 9, 1, 2, 1, 59 ),        // Minute
            new dtunit (0, 12, 1, 2, 1, 59 ),       // Second
        };

        private void BoardSetupWorker()
        {
            try {
#if false
            // Battery Voltage x 0.5 (divider on input) 10 bit (0-1023) ADC, full scale 3.3V 
            AnalogIn VBatt = new AnalogIn(AnalogIn.Pin.Ain1);
            AnalogIn VRef2p5 = new AnalogIn(AnalogIn.Pin.Ain5); 
#endif


#if false
            LeftButton = new Button((Cpu.Pin)GHI.Hardware.EMX.Pin.IO23, 'L');
            RightButton = new Button((Cpu.Pin)GHI.Hardware.EMX.Pin.IO1, 'R');
            UpButton = new Button((Cpu.Pin)GHI.Hardware.EMX.Pin.IO4, 'U');
            DownButton = new Button((Cpu.Pin)GHI.Hardware.EMX.Pin.IO0, 'D');
            CentreButton = new Button((Cpu.Pin)GHI.Hardware.EMX.Pin.IO30, 'S');

            HardwareButton[] Buttons = _amb.GetButtons();
            UpButton = Buttons[0];
            DownButton = Buttons[1];
            LeftButton = Buttons[2];
            RightButton = Buttons[3];
            CentreButton = Buttons[4];
#endif
                float BattVolts = 0.0F;
                float AvBattVolts = 0.0F;
                float vRef = 0.0f;
                float V3p3 = 3.3f; // Calculated 3.3V volt rail based on 2.5V reference
                int HiberSecs = 15;

                // If Left button held on startup, scan LCD Bias until user clicks to say they can see it
                //            LeftButton.Scan();
                if (LeftButton.bootstate)
                {
                    MenuType = MenuTypes.Settings;   // Set Defaults
                    MenuItem = MenuItems.setAutoScan; // AutoScan Bias Mode
                    MenuStep = 0;
                    EnterSetupMode(true);
                }

                CheckRTC(); // Set RTC to build date if invalid
                Int16 mtDir = 1;
                Int16 miDir = 1;
#if false
            // ------------------------- WiFi Test - move elsewhere when happy! -----------------------
            Profile.DebugTime("Start ESP8266WiFi.Init"); //TODO DAV DEBUG
            ESP8266WiFi.Init();
            Profile.DebugTime("Start ESP8266WiFi.PowerOn"); //TODO DAV DEBUG
            ESP8266WiFi.PowerOn();
//            Thread.Sleep(2000);
            Profile.DebugTime("Start ESP8266WiFi.Reset"); //TODO DAV DEBUG
            ESP8266WiFi.Reset();
            Profile.DebugTime("Start ESP8266WiFi.GetDevice"); //TODO DAV DEBUG
            var wifi = ESP8266WiFi.GetDevice();
            Profile.DebugTime("Start wifi.EnableDebugOutput"); //TODO DAV DEBUG
            wifi.EnableDebugOutput = true;
            Profile.DebugTime("Start wifi.SetOperatingMode"); //TODO DAV DEBUG
            wifi.SetOperatingMode(OperatingMode.Station);

            Debug.Print("Access points:");
            var apList = wifi.GetAccessPoints();
            foreach (var ap in apList)
            {
                Debug.Print("ssid:" + ap.Ssid + "  ecn:" + ap.Ecn + " rssi:" + ap.Rssi);
            }
            Debug.Print("-- end of list -------------");

            
            try
            {
                wifi.Connect(Globals.WifiSSID,Globals.WifiPWD);
            }
            catch (Exception e)
            {   // Connect really should look for a "FAIL" response and return the error code in the exception, not just time out!
                //TODO Fix this - DAV
                Debug.Print("Failed to connect");
            }

            Debug.Print("Station IP address : " + wifi.StationIPAddress.ToString());
            Debug.Print("Station MAC address : " + wifi.StationMacAddress);
            Debug.Print("Station Gateway address : " + wifi.StationGateway.ToString());
            Debug.Print("Station netmask : " + wifi.StationNetmask.ToString());
            // -----------------------------------------------------------------------------
#endif
                while (true)
                {

                    try
                    {
                        //string BtnState = "";

                        if (!InSetupMode)
                        {
                            Thread.Sleep(500);
                            continue;
                        }

                        if (UpButton.held && DownButton.held)
                        {
                            if (!StillHeld)
                            {
                                StillHeld = true;
                                ExitSetup(true);
                            }
                        }
                        else
                            StillHeld = false;


                        {
                            if (DownButton.click)
                            {
                                miDir = 1;
                                ++MenuItem;
                                MenuStep = 0;
                            }
                            if (UpButton.click)
                            {
                                miDir = -1;
                                --MenuItem;
                                MenuStep = 0;
                            }
                        }
                        if (MenuItem == 0)
                        {
                            if (RightButton.click)
                            {
                                mtDir = 1;
                                ++MenuType;
                                MenuStep = 0;
                            }
                            else if (LeftButton.click)
                            {
                                mtDir = -1;
                                --MenuType;
                                if (MenuType < MenuTypes.First)
                                    MenuType = MenuTypes.Last;
                                MenuStep = 0;
                            }
                        }

#if true
                        if (_bc != null)
                        {
                            //BattVolts = ((Hardware.PhysicalBatteryCharge)_bc).BattVolts;
                            //AvBattVolts = ((Hardware.PhysicalBatteryCharge)_bc).AvBattVolts;
                            BattVolts = Globals.gBattVolts;
                            AvBattVolts = Globals.gAvBattVolts;
                            vRef = ((Hardware.PhysicalBatteryCharge)_bc).vRef;
                            V3p3 = ((Hardware.PhysicalBatteryCharge)_bc).V3p3;
                        }
#else
                BattVolts = (float)(((float)VBatt.Read()) * 2 * 3.3) / 1024;   // Read from VBatt divider connected ADC (10-bit)
                vRef = (float)(((float)VRef2p5.Read() * 3.3) / 1024);

                if ((vRef > 2) && (vRef < 3))
                {
                    float scale = 2.5f / vRef;
                    V3p3 = 3.3f * scale; // Calculate 3.3V from 2.5V ref reading
                    BattVolts *= scale;
                } 
                else
                    V3p3 = 3.3f; // Assume no 2.5V connected

                Debug.Print("Battery Voltage = " + BattVolts);

                //AvBattVolts = (AvBattVolts - (AvBattVolts/16)) + (BattVolts / 16);
                AvBattVolts += ((BattVolts - AvBattVolts) / 16);
#endif
                        DateTime dtRTC;

                        switch (MenuType)
                        {
                            case MenuTypes.Settings: // Set Defaults ===================================================
                                switch (MenuItem)
                                {
                                    case MenuItems.setTopLevel: //Default screen
                                                                //PrintScreen("AnodeMeter Setup", "Buttons: " + BtnState + " " + i++);
                                                                //dtRTC = RealTimeClock.GetTime();
                                        PrintScreen("AnodeMeter Setup", "  < " + SpecialLCDCharacter.Down + " >");
                                        if (CentreButton.click)
                                        {
                                            ++MenuItem;
                                            MenuStep = 0;
                                        }
                                        break;

                                    case MenuItems.setBackLight: // Adjust backlight
                                        _led.TurnOff();
                                        PrintScreen("Adjust Backlight", Globals.BackLightLevel + "% (L/R)");
                                        AdjustPercent(RightButton, LeftButton, ref Globals.BackLightLevel);
                                        _lcd.SetBacklight(Globals.BackLightLevel);
                                        break;
                                    case MenuItems.setGreenLED: // Adjust Green LED brightness
                                        PrintScreen("Adjust Green LED", Globals.GLedBright + "% (L/R)");
                                        AdjustPercent(RightButton, LeftButton, ref Globals.GLedBright);
                                        _led.Lock(5);
                                        _led.TurnOn(Common.LED.LedColor.Green);
                                        break;
                                    case MenuItems.setRedLED: // Adjust Red LED brightness
                                        PrintScreen("Adjust Red LED", Globals.RLedBright + "% (L/R)");
                                        AdjustPercent(RightButton, LeftButton, ref Globals.RLedBright);
                                        _led.Lock(5);
                                        _led.TurnOn(Common.LED.LedColor.Red);
                                        break;
                                    case MenuItems.setLCDBias: // Adjust LCD Bias
                                        _led.TurnOff();
                                        PrintScreen("Adjust LCD Bias", Globals.LCDBiasPC + "% (L/R)");
                                        AdjustPercent(RightButton, LeftButton, ref Globals.LCDBiasPC);
                                        _lcd.SetBias(Globals.LCDBiasPC);
                                        break;

                                    case MenuItems.setClock:
                                        switch (MenuStep)
                                        {
                                            case 0:
                                                PrintScreen("Set Clock ?", SpecialLCDCharacter.Tick);
                                                if (CentreButton.click)
                                                    ++MenuStep;
                                                break;

                                            case 1:
                                            case 2:
                                                var rtc = RtcController.GetDefault();
                                                dtRTC = rtc.Now; // RealTimeClock.GetDateTime();
                                                PrintScreen("Date: " + dtRTC.ToString("yyyy/MM/dd"), "Time: " + dtRTC.ToString("HH:mm:ss"));
                                                if (CentreButton.click)
                                                {

                                                    int pos = 0;
                                                    // Do cursor position and blink here
                                                    int year = dtRTC.Year;
                                                    if (year < 2020) year = 2020;

                                                    dtunits[0].val = year;
                                                    dtunits[1].val = dtRTC.Month;
                                                    dtunits[2].val = dtRTC.Day;
                                                    dtunits[3].val = dtRTC.Hour;
                                                    dtunits[4].val = dtRTC.Minute;
                                                    dtunits[5].val = dtRTC.Second;

                                                    while (true)
                                                    {
                                                        Thread.Sleep(100);

                                                        if (RightButton.click)
                                                        {
                                                            if (++pos >= dtunits.Length)
                                                                pos = 0;
                                                        }
                                                        else if (LeftButton.click)
                                                        {
                                                            if (--pos < 0)
                                                                pos = dtunits.Length - 1;
                                                        }
                                                        dtunit dt = dtunits[pos];
                                                        if (UpButton.click)
                                                        {
                                                            if (++dt.val > dt.max)
                                                                dt.val = dt.min;
                                                        }
                                                        else if (DownButton.click)
                                                        {
                                                            if (--dt.val < dt.min)
                                                                dt.val = dt.max;
                                                        }
                                                        else if (CentreButton.click)
                                                            break;

                                                        dtunits[pos] = dt; // Why do we need to do this?
                                                                           //PrintScreen("Date: " + year + "/" + mon + "/" + day, "Time: " + hour + ":" + min + ":" + sec);
                                                        PrintScreen("Date: " + dtfmt(dtunits[0]) + "/" + dtfmt(dtunits[1]) + "/" + dtfmt(dtunits[2]), "Time: " + dtfmt(dtunits[3]) + ":" + dtfmt(dtunits[4]) + ":" + dtfmt(dtunits[5]));
                                                        _lcd.SetBlinkCursor((byte)(dt.col + dt.width - 1), dt.row, true);
                                                    }
                                                    _lcd.SetBlinkCursor(0, 0, false);
                                                    DateTime dtNew = new DateTime(dtunits[0].val, dtunits[1].val, dtunits[2].val, dtunits[3].val, dtunits[4].val, dtunits[5].val);
                                                    //RealTimeClock.SetTime(dtNew);
                                                    Program.AM._tm.SyncTime(dtNew);
                                                    Program.AM._tm.SystemTimeIsOK();
                                                    Program.AM.RegisterActivity();
                                                    _ds.LogSyncTime(dtNew);
                                                    if (MenuStep == 2)
                                                    {
                                                        ExitSetup();
                                                    }

                                                }
                                                break;

                                            case 3:     // Confirm Time message - entry point when time is not confirmed
                                                PrintScreen("Confirm Time", SpecialLCDCharacter.Tick);
                                                while (true)
                                                {
                                                    if (CentreButton.click)
                                                    {
                                                        MenuStep = 2;
                                                        break;
                                                    }
                                                    Thread.Sleep(100);
                                                }
                                                break;

                                            default:
                                                MenuStep = 0;
                                                break;
                                        }
                                        break;

                                    case MenuItems.setMeasMode: // Allow either Rod only, ClampOnly, ClampThenRod or RodThenClamp
                                        PrintScreen("Metering Mode? ", Globals.MeasurementModeDesc[Globals.MeasurementModeIndex]);
                                        SelectMeasurementMode(RightButton, LeftButton);
                                        break;

                                    case MenuItems.setLogRawData: // Are we logging all raw data read (default not)
                                        PrintScreen("Log Raw Data? ", Globals.LogRawData == 0 ? "No" : "Yes");
                                        if (LeftButton.click) Globals.LogRawData = 0;
                                        if (RightButton.click) Globals.LogRawData = 1;
                                        break;

                                    case MenuItems.setWiFi: // Will we enable Wifi (will require a reboot to take effect)
                                        PrintScreen("Enable WiFi? ", Globals.WifiDisable == true ? "No" : "Yes");
                                        if (LeftButton.click) Globals.WifiDisable = true;
                                        if (RightButton.click) Globals.WifiDisable = false;
                                        Globals.WifiDisabled = Globals.WifiDisable;
                                        break;

                                    case MenuItems.setWifiDebug: // Will we enable Wifi (will require a reboot to take effect)
                                        PrintScreen("Wifi Debug Mode?", Globals.WifiDebug == true ? "Yes" : "No");
                                        if (LeftButton.click) Globals.WifiDebug = false;
                                        if (RightButton.click) Globals.WifiDebug = true;
                                        break;

                                    case MenuItems.setWifiVerbose: // Will we enable Wifi (will require a reboot to take effect)
                                        PrintScreen("WiFi Verbose?", Globals.WifiVerbose == true ? "Yes" : "No");
                                        if (LeftButton.click) Globals.WifiVerbose = false;
                                        if (RightButton.click) Globals.WifiVerbose = true;
                                        break;

                                    case MenuItems.setRebootToMS: // Need to Reboot into Mass Storage mode?
                                        PrintScreen("RebootToMS? ", Globals.RebootToMS == true ? "Yes" : "No");
                                        if (LeftButton.click) Globals.RebootToMS = false;
                                        if (RightButton.click) Globals.RebootToMS = true;
                                        break;
#if false
                                    case MenuItems.setKeepSchedule: // Keep Schedule until overwritten?
                                        PrintScreen("Keep Old Schedule? ", Globals.KeepSchedule == true ? "Yes" : "No");
                                        if (LeftButton.click) Globals.KeepSchedule = false;
                                        if (RightButton.click) Globals.KeepSchedule = true;
                                        break;
#endif
                                    case MenuItems.setSave: // Save factory defaults
                                        PrintScreen("Save Defaults?", SpecialLCDCharacter.Tick);
                                        if (CentreButton.click)
                                        {
                                            FlashSettings.SaveSettings();
                                            SaveSettingsToSD(_ds);

                                            PrintScreen("", "Settings Saved");
                                            Thread.Sleep(1000);
                                            MenuItem = 0;
                                            MenuStep = 0;
                                        }
                                        break;
                                    case MenuItems.setLoad: // Load factory defaults
                                        PrintScreen("Reload Defaults?", SpecialLCDCharacter.Tick);
                                        if (CentreButton.click)
                                        {
                                            FlashSettings.Reload();

                                            PrintScreen("", "Reloaded");
                                            Thread.Sleep(1000);
                                            MenuItem = 0;
                                            MenuStep = 0;
                                        }
                                        break;

                                    // AutoScan LCD Bias, enter by holding Left-arrow on startup
                                    // (We don't come here via normal menu selection)
                                    case MenuItems.setAutoScan:
                                        PrintScreen("LCD Bias Adjust", "SEL When Visible");
                                        if (++Globals.LCDBiasPC > 100)
                                            Globals.LCDBiasPC = 0; //GlobalConsts.FACTORY_DEFAULT_LCD_BIAS;
                                        _lcd.SetBias(Globals.LCDBiasPC);
                                        if (CentreButton.state)
                                        {
                                            MenuItem = MenuItems.setLCDBias;   // Jump to manual adjust for fine-tuning
                                            MenuStep = 0;
                                        }
                                        break;
                                    default:
                                        MenuItem = 0;
                                        MenuStep = 0;
                                        break;
                                }
                                break;
                            case MenuTypes.Info: // Info ===================================================
                                switch (MenuItem)
                                {
                                    case 0: //Default screen
                                        PrintScreen("Meter Readings", "  < " + SpecialLCDCharacter.Down + " >");
                                        if (CentreButton.click)
                                        {
                                            ++MenuItem;
                                        }
                                        break;

                                    case MenuItems.infoBatt: // Battery State, Vref (2.5V) and 3.3V supply
                                                             // PrintScreen("Input:" + v.ToString("F4"), "Battery " + AvBattVolts.ToString("F2"));
                                        PrintScreen("Batt: " + AvBattVolts.ToString("F2") + " " + BattVolts.ToString("F2"), "VRef: " + vRef.ToString("F2") + " " + V3p3.ToString("F2"));
                                        break;
                                    case MenuItems.infoInput: // Analog In Value
                                        switch (MenuStep)
                                        {
                                            case 0:
                                                //TODO: Change second input to actual input, currently showing battery percent as test - DAV 
                                                PrintScreen("Input 1: " + Ain0.ToString("F2"), "Input 2: " + _bc.GetStateOfChargePercent());
                                                AvAin0 = Ain0;
                                                if (CentreButton.click)
                                                    ++MenuStep;
                                                break;
                                            case 1:
                                                PrintScreen(Ain0.ToString("F13"), AvAin0.ToString("F13"));
                                                AvAin0 = (0.9 * AvAin0) + (Ain0 / 10.0);
                                                if (CentreButton.click)
                                                    ++MenuStep;
                                                break;
                                            default:
                                                MenuStep = 0;
                                                break;
                                        }

                                        break;
                                    case MenuItems.infoFirmware:    // Board type and firmware version
                                        PrintScreen("Board:" + DeviceInformation.DeviceName, "FW:   " + DeviceInformation.Version.ToVersionString(true));
                                        break;
                                    case MenuItems.infoBuiltOn: // Build date from version string
                                                                //DateTime d = GetBuildDate();
                                        PrintScreen("Built:" + Globals.BuildDate.ToString("yyyy-MM-dd"), "      " + Globals.BuildDate.ToString("HH:mm:ss"));
                                        break;
                                    case MenuItems.infoSerial: // Display Meter Serial Number. Should be same as on sticker inside box
                                        PrintScreen("Serial: " + Globals.Serial, "");
                                        break;
                                    case MenuItems.infoUID: // Display device Unique ID
                                        {
                                            string uidString = ((string)BitConverter.ToString(DeviceInformation.GetUniqueId())).Replace("-", "");
                                            string SHi = uidString.Substring(0, 12);
                                            string SLo = uidString.Substring(12, 12);
                                            PrintScreen("ID: " + SHi, "    " + SLo);
                                        }
                                        break;
                                    case MenuItems.infoTemperature: // Display internal temperature
                                        var IntTemp = GetTemperature();
                                        if (AvIntTemp < 0)
                                            AvIntTemp = IntTemp;
                                        else
                                            AvIntTemp = (0.9 * AvIntTemp) + (IntTemp / 10.0);
                                        PrintScreen("Temp: " + AvIntTemp.ToString("F1") + "C", "");
                                        break;
#if false
                            case MenuItems.infoMacAdd: // MAC Address
                                PrintScreen("Mac:" + GetMacAddress(), "");
                                break;
#endif
                                    case MenuItems.infoSDCard: // SD Card Present? (And details, size, etc?)

                                        if (Globals.CfgState != Globals.ConfigState.UpdatingConfig)
                                        {
                                            if (Globals.SDCardPresent)
                                                PrintScreen("SDCard Present", Globals.CfgState == Globals.ConfigState.ConfigOK ? "" : (Globals.SDCardFault ? "HW Fault" : "Not Configured"));
                                            else
                                                PrintScreen("SDCard Missing", "or Empty");
                                        }

                                        break;
                                    default:
                                        MenuItem = 0;
                                        MenuStep = 0;
                                        break;
                                }
                                break;

                            case MenuTypes.Mode: // Modes ===================================================
                                if (Globals.SDCardPresent && Globals.USBAvailable)
                                {
                                    switch (MenuItem)
                                    {
                                        case 0:
                                            PrintScreen("DiskDrive Mode", "Connect ?  " + SpecialLCDCharacter.Tick);
                                            if (CentreButton.click)
                                            {
                                                if (Globals.RebootToMS) {
                                                    PrintScreen("Rebooting to", "DiskDrive Mode");
                                                    Thread.Sleep(1000);
                                                    RebootToMs();   // TODO Fix  - Shouldn't get past here. But hopefully can remove this when GHI fixes firmware!
                                                }
                                                else
                                                { 
                                                    PrintScreen("Switching to", "DiskDrive Mode");
                                                }
                                                Thread.Sleep(1000);

                                                DiskDriveMode(true);
                                                MenuItem = MenuItems.modeDiskDrive;
                                                MenuStep = 0;
                                            }
                                            break;

                                        case MenuItems.modeDiskDrive:
                                            int wtime = 15;
                                            DateTime _dtSettlingTime = DateTime.Now.AddSeconds(15);
                                            DiskDriveMode(true);
                                            while (ms.DeviceState != DeviceState.Configured) // UsbController.PortState.Running)
                                            {
                                                PrintScreen("Connect USB " + wtime, SpecialLCDCharacter.Tick + " to Exit");
                                                if ((CentreButton.click) || (--wtime <= 0)) break;
                                                Thread.Sleep(1000);
                                            }

                                            PrintScreen("DiskDrive Mode", "Disconnect?  " + SpecialLCDCharacter.Tick);
                                            ushort clicks=0;
                                            CentreButton.ClickedSince(ref clicks);
                                            //while (ms.DeviceState == DeviceState.Configured)
                                            DeviceState ds = ms.DeviceState;
                                            while(true)
                                            {
                                                if(ms.DeviceState != ds)
                                                {
                                                    Debug.WriteLine("DeviceState changed from " + GetEnumName(ds) + " to " + GetEnumName(ms.DeviceState));
                                                    ds = ms.DeviceState;
                                                }
                                                // With 64GB uSD state seems to switch between Default and Configured when in use, and goes to Suspended when ejected
                                                // if (ms.DeviceState != DeviceState.Configured && ms.DeviceState != DeviceState.Default)
                                                if ((ms.DeviceState == DeviceState.Suspended) && (DateTime.Now >= _dtSettlingTime))
                                                    break;

                                                //if (CentreButton.click) break;
                                                if (CentreButton.ClickedSince(ref clicks))
                                                    break;
                                                Thread.Sleep(100);
                                            }
                                            Debug.WriteLine("Resuming because state = " + GetEnumName(ms.DeviceState));
                                            Thread.Sleep(1000);

                                            if(Globals.RebootToMS) 
                                                RebootToWinUSB();

                                            DiskDriveMode(false);
                                            _ds.FileSystemChanged();    // Files may have been changed so re-check
                                            PrintScreen("Device Mode", "Resuming...");
                                            Thread.Sleep(1000);

                                            //RebootToNormal(); // TODO Fix once GHI fixed firmware
                                            
                                            MenuItem = 0;
                                            MenuStep = 0;
                                            break;

                                        default:
                                            MenuItem = 0;
                                            MenuStep = 0;
                                            break;
                                    }
                                }
                                else
                                {
                                    // No SD or in USB debug mode, skip this option
                                    MenuType += mtDir;  // Keep going in last direction. Should wrap this in a method!
                                    MenuStep = 0;
                                    //++MenuType;
                                }
                                break;

                            case MenuTypes.Support: // Support ===================================================
                                switch (MenuItem)
                                {
                                    case 0: //Default screen
                                        PrintScreen("Meter Support", "  < " + SpecialLCDCharacter.Down + " >");
                                        if (CentreButton.click)
                                        {
                                            ++MenuItem;
                                            MenuStep = 0;
                                        }
                                        break;
                                    case MenuItems.supportPowerOff: // Test power-off control
                                        PrintScreen("Test Power-off", "Hold <= Button");
                                        if (LeftButton.held)
                                        {
                                            MenuItem = 0;
                                            MenuStep = 0;
                                            PowerOff("", 1);
                                            break;
                                        }
#if false   // DAV - Used for testing Hibernate - seems to work!
                                    if (CentreButton.held)
                                    {
                                        MenuItem = 0;
                                        MenuStep = 0;
                                        Thread.Sleep(1000);
                                        GHIElectronics.TinyCLR.Native.Power.Sleep(DateTime.Now.AddMinutes(1));
                                        break;
                                    }
#endif
                                        if (RightButton.held)
                                        {
                                            MenuItem = MenuItems.supporSetSerial;
                                            MenuStep = 0;
                                        }
                                        break;
                                    case MenuItems.supportHibernate: // Test hibernate
                                        
                                        PrintScreen("Test Hibernate", SpecialLCDCharacter.Tick + " (" + HiberSecs + "s)" );
                                        if (CentreButton.click)
                                        {
                                            PrintScreen("Sleeping...", " (" + HiberSecs + "s)");
                                            Thread.Sleep(1000);
                                            int res = ConfigureSystem.Meter != null ? ConfigureSystem.Meter.DoHibernate(HiberSecs) : 0;
                                            //int res = ConfigureSystem.Meter != null ? ConfigureSystem.Meter.QuickNap(20): 0;
                                            //ConfigureSystem.Meter?.QuickNap(20);
                                            PrintScreen("Woken by:", res == 0 ? "Timer" : "Button");
                                            Thread.Sleep(1000);
                                            break;
                                        }
                                        if(RightButton.click)
                                        {
                                            if ((HiberSecs+=10) > 300)
                                                HiberSecs = 300;
                                        }
                                        if(LeftButton.click)
                                        {
                                            if ((HiberSecs -= 10) < 5)
                                                HiberSecs = 5;
                                        }
                                        break;
                                    case MenuItems.supportBattTest:
                                        switch (MenuStep)
                                        {
                                            case 0:
                                                PrintScreen("Battery Test", SpecialLCDCharacter.Tick + ((Globals.PowerState != Globals.PowerStates.BatteryTest) ? " Start?" : " Stop?"));
                                                ++MenuStep;
                                                break;
                                            case 1:
                                                if (CentreButton.click)
                                                {
                                                    if (Globals.PowerState != Globals.PowerStates.BatteryTest)
                                                    {
                                                        // Start battery test
                                                        LastPowerState = Globals.PowerState;
                                                        Globals.PowerState = Globals.PowerStates.BatteryTest;
                                                        Globals.gBattFile = FileDefs.BattRefFile;
                                                        _ds.WriteBattTestName(Globals.gBattFile + "," + DateTime.Now.ToString("yyyy/MM/dd HH:mm"));
                                                        Globals.BatTimer = 0;
                                                        _ds.ClearBattLog();
                                                    }
                                                    else
                                                    {
                                                        // Stop battery test (normally stopped by power-down)
                                                        Globals.PowerState = LastPowerState;
                                                    }
                                                    ++MenuStep;
                                                }
                                                break;
                                            default:
                                                MenuStep = 0;
                                                break;
                                        }
                                        break;

                                    case MenuItems.supportIFU:  // Support - IFU (In Field Update)
                                        switch (MenuStep)
                                        {
                                            case 0:
                                                PrintScreen("Update Software?", SpecialLCDCharacter.Tick);
                                                ++MenuStep;
                                                break;
                                            case 1:
                                                if (CentreButton.click)
                                                    ++MenuStep;
                                                break;
                                            case 2:
                                                string[] flist = FieldUpdate.GetUpdateFileList();
                                                if (flist.Length <= 0)
                                                {
                                                    MenuStep = 0;
                                                    break;
                                                }
                                                if (flist.Length == 1) {
                                                    FieldUpdate.CheckForUpdate(flist[0]);
                                                    ++MenuStep;
                                                }
                                                else
                                                {
                                                    // If more than one file, allow user to select which one to update
                                                    int ndx = 0;
                                                    while (true)
                                                    {
                                                        PrintScreen("Select Version", ndx + 1 + ": " + FieldUpdate.UserPart(flist[ndx]));
                                                        if (DownButton.click)
                                                        {
                                                            if (++ndx >= flist.Length)
                                                                ndx = 0;
                                                        }
                                                        else if (UpButton.click)
                                                        {
                                                            if (--ndx < 0)
                                                                ndx = flist.Length - 1;
                                                        }
                                                        else if (CentreButton.click)
                                                        {
                                                            FieldUpdate.CheckForUpdate(flist[ndx]);
                                                            ++MenuStep;
                                                            break;
                                                        }
                                                        else if (RightButton.click || LeftButton.click)
                                                        {
                                                            MenuStep = 0;
                                                            break;
                                                        }
                                                        Thread.Sleep(100);
                                                    }
                                                }
                                                break;
                                            case 3:
                                                if (FieldUpdate.HaveUpdate)
                                                {
                                                    if (FieldUpdate.NeedFWUpdate)
                                                        if (FieldUpdate.HaveFW)
                                                            PrintScreen("App. OK, FW OK", "Update ? " + SpecialLCDCharacter.Tick);
                                                        else
                                                            PrintScreen("Require FW", "Can't Update");
                                                    else
                                                        PrintScreen("FW is OK.", "Update ? " + SpecialLCDCharacter.Tick);
                                                }
                                                else
                                                    PrintScreen("No Update Found", "");

                                                ++MenuStep;
                                                break;
                                            case 4:
                                                if (CentreButton.click)
                                                {
                                                    try
                                                    {
                                                        if (FieldUpdate.Update())
                                                        {
                                                            PrintScreen("System Updated", "");   // This should never be reachable, as we should have rebooted!
                                                            Thread.Sleep(500);
                                                            MenuStep = 0;
                                                        }
                                                        else
                                                        {
                                                            PrintScreen("Update Failed", "");
                                                            Thread.Sleep(1000);
                                                            MenuItem = 0;
                                                            MenuStep = 0;
                                                        }
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        PrintScreen("Update Error", "");
                                                        Debug.WriteLine("Update Error: " + e.Message);
                                                        Thread.Sleep(1000);
                                                        MenuItem = 0;
                                                        MenuStep = 0;
                                                    }

                                                }
                                                break;
                                            case 5:

                                                break;
                                            default:
                                                MenuStep = 0;
                                                break;
                                        }
                                        break;

                                    case MenuItems.supportEraseID:
                                        switch (MenuStep)
                                        {
                                            case 0:
                                                PrintScreen("Erase Meter # ?", "[Pre-shipping]");
                                                ++MenuStep;
                                                break;
                                            case 1:
                                                if (CentreButton.held)
                                                {
                                                    ++MenuStep;
                                                    PrintScreen("Last chance", "Wiping Meter ID");
                                                }
                                                break;
                                            case 2:
                                                if (!CentreButton.held)
                                                {
                                                    PrintScreen("Meter # = " + Globals.DeviceID, "Erase Forever? " + SpecialLCDCharacter.Tick);
                                                    ++MenuStep;
                                                }
                                                break;
                                            case 3:
                                                if (CentreButton.held)
                                                {
                                                    _ds.DeleteDevicedID();
                                                    PrintScreen("It is Done", "Ready to Ship");
                                                    Thread.Sleep(1000);
                                                    MenuItem = MenuItems.supportPowerOff;
                                                    MenuStep = 0;
                                                }
                                                break;
                                            default:
                                                MenuStep = 0;
                                                break;
                                        }
                                        break;
                                        
                                        /* -- Not currently required/enabled - DAV
                                    case MenuItems.supportWifiTest:      // Try some Wifi tests - for now requeue config check
                                        PrintScreen("WiFi Test", SpecialLCDCharacter.Tick);
                                        if (CentreButton.click)
                                        {
                                            _am.TriggerConfigCheck();
                                            MenuItem = 0;
                                            MenuStep = 0;
                                        }
                                        break;
                                        */
                                    case MenuItems.supporSetSerial:   // Set serial number. "Hidden" option, to enter hold right arrow instead of left from previous option
                                        PrintScreen("Set Serial No. ?", "");
                                        if (CentreButton.click)
                                        {
                                            int pos = 1;
                                            int t;
                                            UInt16 s = Globals.Serial;
                                            int n = 1;

                                            while (true)
                                            {
                                                Thread.Sleep(100);
                                                if (RightButton.click)
                                                {
                                                    if (--pos < 1)
                                                        pos = 1;
                                                    n = (int)System.Math.Pow(10, (pos - 1));
                                                }
                                                else if (LeftButton.click)
                                                {
                                                    if (++pos > 4)
                                                        pos = 4;
                                                    n = (int)System.Math.Pow(10, (pos - 1));
                                                }

                                                if (UpButton.click)
                                                    s = (UInt16)((t = (int)s + n) < 10000 ? t : 9999);
                                                else if (DownButton.click)
                                                    s = (UInt16)((t = (int)s - n) > 0 ? t : 0);
                                                else if (CentreButton.click)
                                                    break;

                                                string ss = "000" + s.ToString();
                                                ss = ss.Substring(ss.Length - 4);
                                                PrintScreen("Serial: " + ss, "");
                                                _lcd.SetBlinkCursor((byte)(8 + 4 - pos), 0, true);
                                            }
                                            _lcd.SetBlinkCursor(0, 0, false);
                                            PrintScreen("Serial: " + s, SpecialLCDCharacter.Tick + "  Save?");
                                            while (true)
                                            {
                                                Thread.Sleep(100);
                                                if (CentreButton.click)
                                                {
                                                    UInt16 ls = Globals.Serial;
                                                    Globals.Serial = s;
                                                    MenuType = MenuTypes.Settings;
                                                    MenuItem = MenuItems.setSave;
                                                    MenuStep = 0;
                                                    Logging.IssueEvent(Logging.ErrSeverity.Informational, "BoardSetup::", "Serial Number Changing from " + ls + " to " + Globals.Serial, "Serial: " + Globals.Serial);
                                                    break;
                                                }
                                                else if (LeftButton.click || RightButton.click || UpButton.click || DownButton.click)
                                                {
                                                    PrintScreen("Serial not saved", "Keep " + Globals.Serial);
                                                    Thread.Sleep(1000);
                                                    MenuItem = 0;
                                                    MenuStep = 0;
                                                    break;
                                                }
                                            }

                                            //MenuItem = 0;
                                        }
                                        break;

                                    default:
                                        MenuItem = 0;
                                        MenuStep = 0;
                                        break;
                                }
                                break;

                            /* ===================================================
                             * WiFi Status
                             */
                            case MenuTypes.Wifi: // WiFi Settings ===================================================

                                switch (MenuItem)
                                {
                                    case 0: //Default screen
                                        PrintScreen("WiFi settings", "  < " + SpecialLCDCharacter.Down + " >");
                                        if (CentreButton.click)
                                        {
                                            ++MenuItem;
                                            MenuStep = 0;
                                        }
                                        break;
                                    case MenuItems.wifiStatus:
                                        PrintScreen("Wifi:" + WFStat(0) + " IP:" + WFStat(1),
                                                    "GW:  " + WFStat(2) + " DB:" + WFStat(3));
                                        //Thread.Sleep(200);
                                        break;
                                    case MenuItems.wifiInfo:
                                        if (Globals.WifiInfo.Count == 0)
                                            MenuItem += miDir;      // Skip if no info
                                        PrintScreen("Wifi Info", SpecialLCDCharacter.Tick);
                                        if (CentreButton.click)
                                        {
                                            foreach (DictionaryEntry wi in Globals.WifiInfo)
                                            {
                                                PrintWideScreen(wi.Key.ToString(), wi.Value.ToString());
                                                //Thread.Sleep(2000);
                                                if (HoldOrBreak(20)) break;
                                            }
                                        }
                                        break;
                                    case MenuItems.wifiScan:
                                        PrintScreen("AP Scan", SpecialLCDCharacter.Tick);
                                        if (CentreButton.click)
                                        {
                                            PrintScreen("AP Scan", "Starting WiFi");
                                            Globals.WifiTestMode = Globals.WifiScanMode = true;
                                            WaitApList(10);

                                            for (; ; )
                                            {
                                                var apList = _gw_wifi.apList;
                                                if (apList != null)
                                                {
                                                    int n = 1;
                                                    foreach (var ap in apList)
                                                    {
                                                        PrintWideScreen("AP" + n + ":" + ap.Ssid,
                                                            "Signal:" + (130 + ap.Rssi).ToString());
                                                        ++n;
                                                        if (HoldOrBreak(20)) break;
                                                    }
                                                    if (HoldOrBreak(10)) break;
                                                }
                                                else break;
                                            }
                                        }
                                        break;
                                    case MenuItems.wifiTop:
                                        PrintScreen("Strongest AP", SpecialLCDCharacter.Tick);
                                        if (CentreButton.click)
                                        {
                                            PrintScreen("Strongest AP", "Starting WiFi");
                                            Globals.WifiTestMode = Globals.WifiScanMode = true;
                                            WaitApList(10);

                                            for (; ; )
                                            {
                                                var apList = _gw_wifi.apList;
                                                if (apList != null)
                                                {
                                                    foreach (var ap in apList)
                                                    {
                                                        PrintWideScreen(ap.Ssid,
                                                            "Signal:" + (130 + ap.Rssi).ToString());
                                                        break;
                                                    }
                                                    if (HoldOrBreak(20)) break;
                                                }
                                                else break;
                                            }
                                        }
                                        break;

                                    case MenuItems.wifiTest:        // Keep WiFi running so they can ping-test it
                                        PrintScreen("WiFi Test", SpecialLCDCharacter.Tick);
                                        if (CentreButton.click)
                                        {
                                            PrintScreen("WiFi Test", "Running WiFi");
                                            Globals.WifiTestMode = true;
                                            for (; ; )
                                            {
                                                _gw_wifi.UpdateRSSI();
                                                PrintScreen("Running WiFi", $"RSSI : {Globals.CurrentRSSI}");
                                                if (HoldOrBreak(20)) break;
                                            }

#if false
}
                                                WaitApList(10);
                                            var apList = _gw_wifi.apList;

                                            for (; ; )
                                            {
                                                if (apList != null)
                                                {
                                                    foreach (var ap in apList)
                                                    {
                                                        PrintWideScreen(ap.Ssid,
                                                            "Signal:" + (130 + ap.Rssi).ToString());
                                                        break;
                                                    }
                                                    break;
                                                    if (HoldOrBreak(20)) break;
                                                }
                                                else break;
                                            }
                                            for(; ; )
#endif                                                
                                        }
                                        break;
                                    case MenuItems.wifiSpeedTest:        // Keep WiFi running so they can ping-test it
                                        PrintScreen("WiFi Speed Test", SpecialLCDCharacter.Tick);
                                        Globals.WifiTestMode = Globals.WifiScanMode = false;
                                        switch (Globals.WifiSpeedTestMode)
                                        {                                      
                                            case Globals.SpeedTestModes.idle:
                                                if (CentreButton.click)
                                                {
                                                    Globals.WifiSpeedTestMode = Globals.SpeedTestModes.requested;
                                                    WaitSecs = Globals.RefTimer + 15;
                                                }
                                                break;    
                                            case Globals.SpeedTestModes.requested:
                                                if (_gw_wifi != null)
                                                    _gw_wifi.SetWifi(BinaryTransport.WifiStates.Connected);
                                                //PrintScreen("Speed Test", "Starting");
                                                PrintScreen($"Waiting for", $"Server... {WaitSecs - Globals.RefTimer}");
                                                if (Globals.RefTimer >= WaitSecs)
                                                {
                                                    Globals.WifiSpeedTestMode = Globals.SpeedTestModes.idle;
                                                }
                                                break;
                                            case Globals.SpeedTestModes.running:
                                                PrintScreen($"RSSI : {Globals.CurrentRSSI}", "Testing...");
                                                break;
                                                    
                                            case Globals.SpeedTestModes.completed:
                                                string res = Globals.BounceTestPassed ? "Pass" : "Fail";
                                                //PrintScreen("RSSI: " + Globals.CurrentRSSI, "Speed: " + Globals.SpeedTestBPS + "bps");
                                                PrintScreen($"RSSI : {Globals.CurrentRSSI}  {res}", $"Speed: {Globals.SpeedTestBPS} Bps");
                                                if (CentreButton.click)
                                                {
                                                    if (_gw_wifi != null)
                                                        _gw_wifi.SetWifi(BinaryTransport.WifiStates.Connected);
                                                    Globals.WifiSpeedTestMode = Globals.SpeedTestModes.requested;
                                                    WaitSecs = Globals.RefTimer + 15;
                                                }
                                                if (UpButton.click || DownButton.click)
                                                    Globals.WifiSpeedTestMode = Globals.SpeedTestModes.idle;
                                                break;
                                            default:
                                                Globals.WifiSpeedTestMode = Globals.SpeedTestModes.idle;
                                                break;

                                        }                                      
                                        break;
                                    default:
                                        MenuItem = 0;
                                        MenuStep = 0;
                                        break;
                                }
                                break;

                            case MenuTypes.Exit: // Exit Setup ===================================================
                                PrintScreen("Exit Setup?", SpecialLCDCharacter.Tick + "   <  >");
                                if (CentreButton.click)
                                {
                                    ExitSetup(true);
                                }
                                break;
                            default:
                                MenuType = 0;
                                MenuStep = 0;
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("BoardCheck Error: " + e.Message);
                        PrintScreen("BoardCheck Error", "");
                        Thread.Sleep(1000);
                        MenuType = MenuTypes.Settings;
                        MenuItem = MenuItems.setTopLevel;
                        MenuStep = 0;
                    }
                    Thread.Sleep(100);
                }
            }
            catch (Exception e) {
                Debug.WriteLine("BoardCheck Error: " + e.Message);
                PrintScreen("BoardCheck Error", "");
                Thread.Sleep(1000);
                MenuType = MenuTypes.Settings;
                MenuItem = MenuItems.setTopLevel;
                MenuStep = 0;
            };
        }
        /*====== End BoardSetupWorker() =======*/

        /*====== Read device internal temperature =======*/

        public static double GetTemperature()
        {
            var ts_reg1 = (IntPtr)0x1FF1E820;
            var ts_reg2 = (IntPtr)0x1FF1E840;
            var enable_reg = (IntPtr)((0x40000000U + 0x18020000 + 0x6300 + 8));

            var controller = AdcController.FromName(SC20100.Adc.Controller3.Id);
            var channel = controller.OpenChannel(SC20100.Adc.Controller3.InternalTemperatureSensor);
            var enable_val = Marshal.ReadInt32(enable_reg);

            enable_val |= (1 << 23);

            Marshal.WriteInt32(enable_reg, enable_val);

            var v = channel.ReadValue() * 1.0;


            var ts1 = Marshal.ReadInt32(ts_reg1);
            var ts2 = Marshal.ReadInt32(ts_reg2);

            var t1 = (110 - 30) * 1.0;
            var t2 = (ts2 - ts1) * 1.0;
            var t3 = (v - ts1) * 1.0;

            var temperature = t1 / t2 * t3 + 30;

            Debug.WriteLine("T = " + temperature + " Celsius");

            return temperature;
        }

        private static bool WaitApList(int secs)
        {
            //var apList = _gw_wifi.apList;
            if (_gw_wifi != null)
                _gw_wifi.SetWifi(BinaryTransport.WifiStates.Connected);
            //PrintScreen("AP Scan", "Starting WiFi");
            for (int i = 0; i < secs; ++i)
            {
                var apList = _gw_wifi.apList;
                if (apList != null)
                    return true;
                HoldOrBreak(10);
            }
            return false;
        }

        // ================= WiFi Test ========================
#warning //TODO - disable WiFi Test?
#if true
        private static void sock_DataReceived(object sender, SocketReceivedDataEventArgs args)
        {
            var socket = (WifiSocket)sender;
            if (args.Data != null)
            {
                Debug.WriteLine("Data Received : " + args.Data.Length);
                if (args.Data.Length > 0)
                {
                    var body = PervasiveDigital.Utilities.StringUtilities.ConvertToString(args.Data);
                    Debug.WriteLine("Received: " + body);
                    //TODO: Parse the request - here we're just going to reply with a 404
                    // socket.Send("HTTP/1.1 404 NOT FOUND\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                    // socket.Send("Thanks for <" + body + ">!\r\n");
                }
            }
        }

        private static void sock_SocketClosed(object sender, EventArgs args)
        {
            Debug.WriteLine("Socket closed: " + ((WifiSocket)sender).Id);
        }
#endif
        // =================End Wifi Test =====================
        static UsbClientController usbclientController;
        static MassStorage ms  = null;
        static StorageController sd = null;
        static bool LastReqState = false;   // Consider making the DiskDriveMode method static and adding this to it...
        public static void DiskDriveMode(bool on)
        {
            
            if (on == LastReqState) return;
            LastReqState = on;

        if (on)
            {
                try
                {
                    _gw_usb.Suspend();
                    _ds.Lock(true);

                    //ConfigureSystem._ps.UnmountFileSystem();

                    //ms = USBClientController.StandardDevices.StartMassStorage();
                    //ms = new MassStorage(UsbClientController.GetDefault());
                    //ms.DeviceStateChanged += (a, b) => Debug.WriteLine("Mass Storage changed to " + ms.DeviceState);
                    //Controller.ActiveDevice = ms;

                    // DAV - The following makes us a device with the vendor name AnodeMtr, and the serial number as the product
                    // could be useful, however it then "installs" a new driver instance for each serial number (each meter)
                    //ms.AttachLun(0, ConfigureSystem._ps, "AnodeMtr", "#" + Globals.DeviceID);

                    // This way gives us one name, used for all meters
                    // ms.AttachLogicalUnit(ConfigureSystem._ps, 0, "C-Born", "AnodeMeter Drive");
                    //ms.EnableLogicalUnit(0);

                    // For TinyCLR // TODO DAV Pass constructor with C-Born ID next...
                    //ms.Enable();

                    // TODO DAV 11MAR2023 See why lock at next line?
                    //int res = ConfigureSystem.Meter != null ? ConfigureSystem.Meter.QuickNap(1) : 0;
                    StartMs();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception: " + ex.Message);
                }
            }
            else
            {
                try
                {
                    StopMs();
                    //ms.Disable();
                    //ms.RemoveLogicalUnit(ConfigureSystem._ps.Hdc);
                    //ms.Dispose();

                    _ds.Lock(false);
                    _am.TransportInit();    // In case delayed for boot to MassStorage
                    _gw_usb.Resume();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception: " + ex.Message);
                }
            }
        }
        // Start Mass Storage
        static void StartMs()
        {
            Debug.WriteLine("StartMs " + ((ms is null) ? "" : "(Skipped)"));
            if (ms != null) return;
            usbclientController = UsbClientController.GetDefault();
            ms = new MassStorage(usbclientController, new UsbClientSetting()
            {
                VendorId = 7071,
                ProductId = 61442,
                BcdUsb = 512, //528,
                BcdDevice = 256,
                MaxPower = 250,
                ManufactureName = "C-Born Software Systems",
                ProductName = "Anodemeter uSD",
                SerialNumber = "1",
                InterfaceName = "Mass Storage",
                Mode = UsbClientMode.MassStorage
            });
            //ms = new MassStorage(usbclientController);
            sd = StorageController.FromName(SC20260.StorageController.SdCard);
            ms.DeviceStateChanged += Ms_DeviceStateChanged;
            ms.AttachLogicalUnit(sd.Hdc);
            ms.Enable();
            Debug.WriteLine("MassStorage Started");
        }
        // Stop Mass Storage
        static void StopMs()
        {
            Debug.WriteLine("StopMs " + ((ms is null) ? "(Skipped)" : ""));
            if (ms is null) return;
            ms.Disable();
            ms.RemoveLogicalUnit(sd.Hdc);
            ms.DeviceStateChanged -= Ms_DeviceStateChanged;
            ms.Dispose();
            ms = null;
            Thread.Sleep(1000);
            if(usbclientController != null)
            {
                try { usbclientController.Dispose(); }
                catch { }
                usbclientController = null;
            }
            Debug.WriteLine("MassStorage Stopped");
        }
        private static void Ms_DeviceStateChanged(RawDevice sender, DeviceState state)
        {
            Debug.WriteLine("MassStorage changed to " + GetEnumName(state));
        }
        static string GetEnumName(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Detached: return "Detached";
                case DeviceState.Attached: return "Attached";
                case DeviceState.Powered: return "Powered";
                case DeviceState.Default: return "Default";
                case DeviceState.Address: return "Address";
                case DeviceState.Configured: return "Configured";
                case DeviceState.Suspended: return "Suspended";
                default: return state.ToString(); // Fallback to default ToString() behavior
            }
        }
        private static string WFStat(byte n)
        {
            return Globals.WifiStatus[n] ? "Ok" : "--";
        }

        static void SelectMeasurementMode(HardwareButton BInc, HardwareButton BDec)
        {

            if (BInc.click)
                Globals.MeasurementModeIndex = (Globals.MeasurementModeIndex + 1) % Globals.MeasurementModeDesc.Length;

            else if (BDec.click)
                Globals.MeasurementModeIndex = (Globals.MeasurementModeIndex + Globals.MeasurementModeDesc.Length - 1) % Globals.MeasurementModeDesc.Length;
        }

        static void AdjustPercent(HardwareButton BInc, HardwareButton BDec, ref byte v)
        {
            if ((BInc.click || BInc.held) && (v < 100))
                ++v;
            else if ((BDec.click || BDec.held) && (v > 0))
                --v;
        }

        public static void PowerOff()
        {
            Power(false);
            Thread.Sleep(1000);
        }

        public static void Power(bool State)
        {
            try
            {
                if (PowerLine == null)
                {
                    var gpio = GpioController.GetDefault();
                    PowerLine = gpio.OpenPin(IOMap.PowerLine);
                    PowerLine.SetDriveMode(GpioPinDriveMode.InputPullUp);
                    PowerLine.SetDriveMode(GpioPinDriveMode.OutputOpenDrain);
                    PowerLine.Write(GpioPinValue.High);
                    //PowerLine = new TristatePort(IOMap.PowerLine, false, false, Port.ResistorMode.PullUp);
                }

                if (State)
                {
                    PowerLine.Write(GpioPinValue.High);
                    //PowerLine.Active = false;   // Set to input mode with pullup
                }
                else
                {
                    PowerLine.Write(GpioPinValue.Low);
                    //PowerLine.Active = true;    // Set to output mode, low
                    //PowerLine.Write(false);
                }
            }
            catch { }
        }

        public void PowerOff(string msg, int delay)
        {
            DataStore.FlushFileSystem();

            Boolean Mode = InSetupMode;
            int Screen = _lcd.SetScreen(1); // Change to alternate screen
            PrintScreen(msg, "Goodbye...");
            InSetupMode = false;
            Thread.Sleep(delay * 1000);
            PowerOff();

            // In case PowerOff doesn't work...
            PrintScreen("Power Down", "Failed");
            Power(true);
            Thread.Sleep(1000);
            InSetupMode = Mode;
            _lcd.SetScreen(Screen);         // Restore screen
        }

        private void ExitSetup(bool SetToDefault = true)
        {
            if (SetToDefault)
            {
                MenuType = MenuTypes.Settings;
                MenuItem = MenuItems.setTopLevel;
                MenuStep = 0;
            }
            Globals.bReInitDisplay = true;  // Reinit LCD Display (just in case!)
            PrintScreen("", "");
            Thread.Sleep(100);
            _lcd.SetScreen(0);
            InSetupMode = false;
            Globals.WifiTestMode = Globals.WifiScanMode = false;
        }

        private void CheckRTC()
        {
            var rtc = RtcController.GetDefault();
            if (rtc.IsValid)
            {
                Debug.WriteLine("RTC is Valid");
                // RTC is good so let's use it
                long oldticks = DateTime.Now.Ticks;
                SystemTime.SetTime(rtc.Now);
                Profile.Rebase(DateTime.Now.Ticks - oldticks);
            }
            else
            {
                Debug.WriteLine("RTC is Invalid");
                var MyTime = Globals.BuildDate; // new DateTime(Globals.BuildDate);
                rtc.Now = MyTime;
                SystemTime.SetTime(MyTime);
            }
#if false
            try
            {
                DateTime dt = RealTimeClock.GetDateTime();
            }
            catch (Exception e)
            {
                var message = e.Message;
                RealTimeClock.SetDateTime(Globals.BuildDate);
            }
#endif
        }

        private static void UpdateOrAdd(ref string[] Records, string Key, string value, ref ArrayList extras)
        {
            string LKey = Key.ToLower();
            for (int i = 0; i < Records.Length; i++)
            {
                string rp = Records[i].Trim();
                if (rp.Length == 0 || rp.Left(1) == "#") // Skip blank lines and comments
                    continue;
                string[] RecordParts = rp.Split(',', ' ');
                string RKey = RecordParts[0].Trim().ToLower();
                if (LKey == RKey)
                {
                    Records[i] = Key + ',' + value;
                    return;
                }
            }
            extras.Add(Key + ',' + value + '\r');
        }

        public static void SaveSettingsToSD(Common.DataStore ds)
        {
            string[] Records = new string[] { };
            ArrayList extras = new ArrayList();

            if (ds != null)
            {
                try
                {
                    string Result = ds.ReadFactoryDefaults();
                    if (Result != "")
                    {
                        Records = Result.Split('\r');
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception Reading Factory Defaults: " + ex.Message);
                }
                UpdateOrAdd(ref Records, "BackLight", Globals.BackLightLevel.ToString(), ref extras);
                UpdateOrAdd(ref Records, "GreenLed", Globals.GLedBright.ToString(), ref extras);
                UpdateOrAdd(ref Records, "RedLed", Globals.RLedBright.ToString(), ref extras);
                UpdateOrAdd(ref Records, "LcdBias", Globals.LCDBiasPC.ToString(), ref extras);
                UpdateOrAdd(ref Records, "Serial", Globals.Serial.ToString(), ref extras);
                UpdateOrAdd(ref Records, "MeasMode", Globals.MeasurementModeIndex.ToString(), ref extras);
                UpdateOrAdd(ref Records, "LogRawData", Globals.LogRawData.ToString(), ref extras);
                UpdateOrAdd(ref Records, "WifiDisable", (Globals.WifiDisable ? 1 : 0).ToString(), ref extras);
                UpdateOrAdd(ref Records, "WifiDebug", (Globals.WifiDebug ? 1 : 0).ToString(), ref extras);
                UpdateOrAdd(ref Records, "WifiVerbose", (Globals.WifiVerbose ? 1 : 0).ToString(), ref extras);
                UpdateOrAdd(ref Records, "WifiModes", Globals.WifiModes.ToString(), ref extras);
                UpdateOrAdd(ref Records, "RebootToMS", (Globals.RebootToMS ? 1 : 0).ToString(), ref extras);
//                UpdateOrAdd(ref Records, "KeepSchedule", (Globals.KeepSchedule ? 1 : 0).ToString(), ref extras);

                if (Globals.SleepOverride)
                {
                    UpdateOrAdd(ref Records, "SleepDelay", Globals.SleepDelay.ToString() + ',' + Globals.WakeDelay.ToString(), ref extras);
                    UpdateOrAdd(ref Records, "ConnectedSleepDelay", Globals.ConnectedSleepDelay.ToString() + ',' + Globals.ConnectedWakeDelay.ToString(), ref extras);
                }
#if false       // No need to update these unless they can be updated by xml config or other means. Just let them go through unchanged for now
                if (Globals.WifiAPs.Length > 0)
                {
                    UpdateOrAdd(ref Records, "Wifi", Globals.WifiAPs, ref extras);
                }
                if (Globals.Gateway.Length > 0)
                {
                    UpdateOrAdd(ref Records, "Gateway", Globals.Gateway, ref extras);
                }
#endif
                string FactoryDefs = "";


                foreach (string s in Records)
                    FactoryDefs += s + '\r';

                foreach (string s in extras)
                    FactoryDefs += s;

                ds.WriteFactoryDefaults(FactoryDefs);
            }
        }

        private static Common.LcdDisplay.CursorPosition Display1 = new Common.LcdDisplay.CursorPosition(0, 0);
        private static Common.LcdDisplay.CursorPosition Display2 = new Common.LcdDisplay.CursorPosition(1, 0);

        public static void PrintScreen(string line1, string line2)
        {
            _lcd.AltMessages((line1 + "                ").Substring(0, 16), (line2 + "                ").Substring(0, 16));
        }

        public static void PrintWideScreen(string line1, string line2)
        {
            int dlyMs = 2000;
            int l1 = line1.Length - 16;
            int l2 = line2.Length - 16;

            int sc = l2 > l1 ? l2 : l1;
            if (sc < 0) sc = 0;

            for (int s1 = 0, s2 = 0; sc >= 0; sc--)
            {
                _lcd.AltMessages((line1 + "                ").Substring(s1, 16), (line2 + "                ").Substring(s2, 16));
                //Thread.Sleep(dlyMs);
                if (HoldOrBreak(dlyMs / 100)) break;
                dlyMs = 1000;
                if (s1 <= l1) s1++;
                if (s2 <= l2) s2++;
            }
        }

        private static bool HoldOrBreak(int loops)
        {
            while (loops > 0)
            {
                if (UpButton.held || DownButton.held || RightButton.held) return true;
                if (UpButton.click || DownButton.click || RightButton.click) return true;
                while (CentreButton.held)
                    Thread.Sleep(100);
                Thread.Sleep(100);
                --loops;
            }
            return false;
        }

        private string dtfmt(dtunit dt)
        {
            string s = ("000" + dt.val);
            s = s.Substring(s.Length - dt.width, dt.width);
            return s;
        }

        public BoardSetup(Common.LcdDisplay lcd, Common.AnodeMeterButtons amb, Common.LED led, Common.BatteryCharge bc, Common.DataStore ds, BinaryTransport gw_usb, WifiTransport gw_wifi, AnodeMeter am)
        {
            _lcd = (LiquidCrystal)lcd;
            _amb = (PhysicalButtons)amb;
            _led = led;
            _bc = bc;
            _ds = ds;
            _gw_usb = gw_usb;
            _gw_wifi = gw_wifi;
            _am = am;

             //UsbController = UsbClientController.GetDefault();

            HardwareButton[] Buttons = _amb.GetButtons();
            UpButton = Buttons[0];
            DownButton = Buttons[1];
            LeftButton = Buttons[2];
            RightButton = Buttons[3];
            CentreButton = Buttons[4];

            var boardSetupThread = new Thread(this.BoardSetupWorker);
            boardSetupThread.Start();
        }

        public bool EnterSetupMode(bool enter)
        {
            if (!InSetupMode)
            {
                bool held = false;

                if (enter)
                {
                    Globals.bReInitDisplay = true;  // Reinit LCD Display (just in case!)
                    InSetupMode = true;
                    _lcd.SetScreen(1);
                }
                else
                {
                    if (UpButton.held && DownButton.held)
                        held = true;
                    else
                        StillHeld = false;

                    if (held && !StillHeld)
                    {
                        StillHeld = true;
                        InSetupMode = true;
                        Globals.bReInitDisplay = true;  // Reinit LCD Display (just in case!)
                        _lcd.SetScreen(1);
                    }
                }
            }

            return InSetupMode;
        }


        public bool EnterShutDownMode(bool enter)
        {

            if (enter || (LeftButton.held && RightButton.held))
            {
                InShutDownMode = true;
            }

            return InShutDownMode;
        }


        public bool EnterSetupMode(MenuTypes MType, MenuItems MItem, int optMenuStep = 0)
        {
            if (!InSetupMode)
            {
                MenuType = MType;
                MenuItem = MItem;
                MenuStep = optMenuStep;
                InSetupMode = true;
                _lcd.SetScreen(1);
            }
            return InSetupMode;
        }
#if false
        private static string GetMacAddress()
        {
            NetworkInterface ni = NetworkInterface.GetAllNetworkInterfaces()[0];
            //char[] c = new char[17];
            char[] c = new char[12];
            byte b;

            for (byte y = 0, x = 0; y < 6; ++y, ++x)
            {
                b = (byte)(ni.PhysicalAddress[y] >> 4);
                c[x] = (char)(b > 9 ? b + 0x37 : b + 0x30);
                b = (byte)(ni.PhysicalAddress[y] & 0xF);
                c[++x] = (char)(b > 9 ? b + 0x37 : b + 0x30);
                //if (y < 5) c[++x] = '-';
            }

            return new string(c);
        }

        // Now in Globals.BuildDate, set in Main
        private DateTime GetBuildDate()
        {
            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();
            Version ver = assemName.Version;
            DateTime buildDateTime = new DateTime(2000, 1, 1).Add(new TimeSpan(
                TimeSpan.TicksPerDay * ver.Build + // days since 1 January 2000
                TimeSpan.TicksPerSecond * 2 * ver.Revision)); // seconds since midnight, (multiply by 2 to get original)
            return buildDateTime;
        }
#endif
/* ==================== Workarounds for MassStorage needing reset after WinUSB on some PCs =======================
 * 
 *  Hopefully will be fixed in future SDK releases!
 *  
 *  The Battery Backed memory stuff here could be useful anyway...
 */
#if true // Not needed now GHI has fixed firmware (Back in as fix didn't work for all PCs - DAV 14JAN2024)
        // enum for startup flags. 0 = normal (USB), 1 = Mass Storage, 2 = WinUSB
        enum StartFlags : byte
        {
            Normal = 0,
            MassStorage = 1,
            WinUSB = 2
        }
        // Startup flags - use CRC16 protected structure ASAP
        // Save startup flags in BB Ram
        static void SetBBStartFlags(StartFlags flags)
        {
            byte[] data = { (byte)flags };
            WriteBBRam(data);
        }
        // Get startup flags from BB Ram
        static StartFlags GetBBStartFlags()
        {
            byte[] data = ReadBBRam();
            Debug.WriteLine("BB Ram: " + ((data == null) ? "null" : data.Length.ToString()));
            if (data == null)
                return StartFlags.Normal;
            return (StartFlags)data[2]; // 1st byte after header
        }
        const ushort MaxBBData = 100; // Could use  rtc.BackupMemorySize() for this. Later...
        const ushort MinBBData = 5; // 2 bytes for length, 2 bytes for CRC, at least 1 data byte

        // Read BB Ram according to header size, and validate CRC. Return as byte array if valid, null (or zero size array?) if not
        static byte[] ReadBBRam()
        {
            var rtc = RtcController.GetDefault();
            var header = new byte[2];
            rtc.ReadBackupMemory(header, 0);
            ushort len = BitConverter.ToUInt16(header, 0);
            if ((len < MinBBData) || (len > MaxBBData))
                return null;
            var data = new byte[(int)len];
            rtc.ReadBackupMemory(data, 0);
            var crc = new Crc16();
            var crcVal = crc.ComputeHash(data, 0, len - 2);
            ushort dataCrc = BitConverter.ToUInt16(data, len - 2);
            if (crcVal == dataCrc)
                return data;
            return null;
        }
        // Write BB Ram, using existing structure if present, else build a new one
        static void WriteBBRam(byte[] data)
        {
            var rtc = RtcController.GetDefault();
            var crc = new Crc16();
            var oldData = ReadBBRam();
            if (oldData == null)
                oldData = new byte[data.Length + 4];

            var len = (ushort)oldData.Length - 4;
            if (len < data.Length)
            {   // Need to expand BB Ram
                byte[] newArray = new byte[oldData.Length];
                Array.Copy(oldData, newArray, oldData.Length);
                oldData = newArray;
            }
            // Copy data across
            for (int i = 0; i < data.Length; i++)
                oldData[i + 2] = data[i];

            // Add header to old data
            byte[] lenBytes = BitConverter.GetBytes(oldData.Length);
            oldData[0] = lenBytes[0];
            oldData[1] = lenBytes[1];

            // Add CRC to old data
            var crcVal = crc.ComputeHash(oldData, 0, oldData.Length - 2);
            byte[] crcBytes = BitConverter.GetBytes(crcVal);
            oldData[oldData.Length - 2] = crcBytes[0];
            oldData[oldData.Length - 1] = crcBytes[1];

            // Write to BB Ram
            rtc.WriteBackupMemory(oldData, 0);
        }

        static void RebootToMode(StartFlags mode)
        {
            Debug.WriteLine("Rebooting to mode " + mode.ToString());
            SetBBStartFlags(mode);
            //SleepAndFixTime(1);
            Thread.Sleep(100);                            // without this delay the BB flag doesn't work, assume it it written async on another thread
            GHIElectronics.TinyCLR.Native.Power.Reset();
        }
        static void RebootToMs()
        {
            RebootToMode(StartFlags.MassStorage);
        }
        static void RebootToWinUSB()
        {
            RebootToMode(StartFlags.WinUSB);
        }
        static void RebootToNormal()
        {
            RebootToMode(StartFlags.Normal);
        }
#endif
#if false
        private static void SleepAndFixTime(int seconds)
        {
            var rtc = RtcController.GetDefault();
            if(!rtc.IsValid)
            {
                Debug.WriteLine("RTC Invalid");
                return;
            }
            Debug.WriteLine("RTC: " + rtc.Now.ToString());
            var tStart = rtc.Now.Ticks;
            //GHIElectronics.TinyCLR.Native.Power.Sleep(rtc.Now.AddSeconds(seconds));   // This never wakes with the 99.x firmware
            Thread.Sleep(seconds * 1000);                                               // but this works...
            var tEnd = rtc.Now.Ticks;
            Debug.WriteLine("Slept for " + (tEnd - tStart) / TimeSpan.TicksPerMillisecond + " mS");
            SystemTime.SetTime(rtc.Now);
        }
#endif
        private StartFlags LastStartFlags = StartFlags.Normal;
        private StartFlags ReadAndClearStartFlags()
        {
            var ResetSource = GHIElectronics.TinyCLR.Native.Power.GetResetSource();
            if (ResetSource == ResetSource.SystemReset)
            {
                Debug.WriteLine("SystemReset");
                var sflags = GetBBStartFlags();
                Debug.WriteLine("StartFlags = " + sflags);
                SetBBStartFlags(StartFlags.Normal);

                LastStartFlags = sflags;
            }
            return LastStartFlags;
        }
        public bool BootedToMassStorage()
        {
            return (ReadAndClearStartFlags() == StartFlags.MassStorage);
        }
        public bool BootedToWinUSB()
        {
            return (LastStartFlags == StartFlags.WinUSB);
        }
    }

        /* =========== In Field Update Strategy =========
         * 
         * We have a top level directory "Update" (/SD/Update) with a subdirectory for each hardware type (Update\EMX and Update\G120)
         * We require one file for a deployment only update (Same SDK release)
         * We require an additional 3 files if we also need to update the firmware.
         * We will use a naming convention for now. Later perhaps a descriptor file with file names and CRC/SHA checks etc will be a better approach
         * 
         * The Application file name is App_xxxx.hex, where xxxx contains a firmware revision number.
         * For example, an Application built against SDK 4.2.10.1 should be called App_4.2.10.1_.hex,if we want to be able to load it without loading firmware
         * It could also be App_4.2.10.1_1234.5678.hex, etc, the subsequent digits being used to identify the file, but not used by the software
         * 
         * If the App revision doesn't match the firmware revision in use, then firmware subdirectory and files must be present.
         * 
         * Firmware files should be placed in a subdirectory named for the firmware version, eg SDK_4.2.10.1
         * (or the full path \SD\Update\EMX\SDK_4.2.10.1)
         * The three firware files must be named Firmware.hex, Firmware2.hex and Config.hex, as per the GHI convention.
         * These files will normally be copied from the GHI development directory,
         * ie C:\Program Files (x86)\GHI Electronics\GHI Premium NETMF v4.2 SDK\EMX\Firmware
         * 
         * At this stage only one application is supported. At a later date we may allow multiple files and user selection
         * 
         * Old files can be moved to a /old subdirectory, new files put in a /new subdirectory, if desired
         * 
         * DAV  9AUG13
         *      10AUG13 Updated to require subdirectory for firmware files)
        */

        public class FieldUpdate
    {
        static string Path;
        static string AppBase;

        static string AppName = null;
        static string fw = null;

        public static bool HaveUpdate = false;
        public static bool NeedFWUpdate = true;
        public static bool HaveFW = false;

        // Return a list of possible update file names
        public static string[] GetUpdateFileList()
        {
            Path = @"\Updates\" + "SC20" + @"\";
            string Path1 = Path + "Apps" + @"\";

            string[] Files;

            ArrayList FileList = new ArrayList();

            try {
                if (Globals.SDCardPresent)
                {
                    if (ConfigureSystem._ps != null)
                    {
                        if (Directory.Exists(Path))
                        {
                            Files = Directory.GetFiles(Path);
                            foreach (string file in Files) {
                                string fn = file.ExtractFileNameFromFullPath();
                                if (MatchFile(fn, "App_", ".tca"))
                                {
                                    FileList.Add(file);
                                }
                            }
                        }
                        if (Directory.Exists(Path1))
                        {
                            Files = Directory.GetFiles(Path1);
                            foreach (string file in Files)
                            {
                                string fn = file.ExtractFileNameFromFullPath();
                                if (MatchFile(fn, "App_", ".tca"))
                                {
                                    FileList.Add(file);
                                }
                            }
                        }

                    }
                }
            }
            catch (Exception e)
            {
                Debug.Write("Exception checking for Updates: " + e.Message);
                HaveUpdate = false;
            }
            return (string[])FileList.ToArray(typeof(string));
        }

        public static void CheckForUpdate(string file)
        {
            Debug.WriteLine("Version: " + DeviceInformation.Version.ToVersionString());

            //Path = @"SD\Updates\" + (Globals.G120 ? "G120" : "EMX") + @"\";
            Path = @"\Updates\" + "SC20" + @"\";
            //AppBase = "app_" + SystemInfo.Version.ToString();
            //AppBase = "app_" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            AppBase = "app_" + DeviceInformation.Version.ToVersionString();
            int AppBaseLen = AppBase.Length;
            int CurrentSVn = 0;
            _ = int.TryParse(DeviceInformation.Version.ToVersionString(true).Right(4), out CurrentSVn); // Should get the latest build, eg 6200, as int

            HaveFW = false;
            fw = null;

            string fn = file.ExtractFileNameFromFullPath();
            if (MatchFile(fn, AppBase, ".tca"))
            {
                // Found a version matched application
                AppName = file; // fn;
                HaveUpdate = true;
                NeedFWUpdate = false;
                //return;
            } else NeedFWUpdate = true;

            // No Version-Matched App - try for any match, and confirm FW files exist
            if (MatchFile(fn, "App_", ".tca"))
            {
                // Found a non version matched application
                AppName = file; // fn;
                HaveUpdate = true;
                //NeedFWUpdate = true;

                string[] dirs = fn.Split('_');
                if (dirs.Length > 1)
                {
                    string d = dirs[1];
                    if (d.Right(4).ToLower() == ".tca")
                        d = d.Left(d.Length - 4);
                    string sdkpath = Path + "SDK_" + d + @"\";
                    if (Directory.Exists(sdkpath))
                    {
                        int svn = 0;
                        string[] FWFiles = Directory.GetFiles(sdkpath);
                        foreach (string f in FWFiles)
                        {
                            //fw = sdkpath + "Firmware.ghi";
                            if (f.Length > 8 && f.Right(4).ToLower() == ".ghi")
                            //                                                if (MatchFile(f, "", "ghi"))
                            {
                                int vn = 0;
                                _ = int.TryParse(f.Substring(f.Length - 8, 4), out vn);
                                if (File.Exists(f))
                                {
                                    if(vn > svn)
                                    {   // Use the highest matching subversion if more than one file
                                        svn = vn;
                                        fw = f;
                                        HaveFW = true;
                                        if(vn > CurrentSVn)
                                        {
                                            NeedFWUpdate = true;    // Compatible, but a newer build exists so load it
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        static public string UserPart(string fn)
        {
            try
            {
                string sa = fn.ExtractFileNameFromFullPath().Split('_')[2];

                //string s = fn.ExtractFileNameFromFullPath();
                //string[] sa = s.Split('_');
                //string up = sa[2];
                return sa.Left(sa.Length - 4);
            } catch(Exception e)
            {
                return "";
            }
        }
        static bool MatchHexFile(string fn, string head)
        {
            return MatchFile(fn, head, ".hex");
        }
        static bool MatchFile(string fn, string head, string tail)
        {
            if ((fn.Left(head.Length).ToLower() == head.ToLower()) && (fn.Right(4).ToLower() == tail.ToLower()))
                return true;
            else
                return false;
        }
        public static bool Update()
        {
            var appKey = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }; // your key, assumming they are all zero as example.

            try
            {
                if (HaveUpdate && !NeedFWUpdate)
                {
                    // App matches current firmware. Just load new application
                    BoardSetup.PrintScreen("Loading App...", "");
                    Thread.Sleep(400);
                    var filestreamApp = new FStream(AppName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var updater = new ApplicationUpdate(filestreamApp, appKey);
                    var applicationVersion = updater.Verify();
                    //updater.ActivityPin = indicatorPin; // optional
                    updater.FlashAndReset();

                }
                else if (HaveUpdate && HaveFW)
                {
                    // We need to load in new firmware, config (??) and application
                    BoardSetup.PrintScreen("Loading App.", "+ Firmware");
                    Thread.Sleep(400);

                    // Would be nice to flash green LED, but need to wrest control back from PhysicalLED
                    // Maybe later - for now just toggle a random unused Gpio Pin...
                    var indicatorPin = GpioController.GetDefault().OpenPin(SC20260.GpioPin.PJ4);
                    var updater = new InFieldUpdate() { ActivityPin = indicatorPin };

                    var dataChunk = new byte[1 * 1024]; // must be multiple of 1K

                    var filestreamApp = new FStream(AppName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var filestreamFw = new FStream(fw, FileMode.Open, FileAccess.Read, FileShare.Read);

                    // Buffer application
                    var idxApp = 0;

                    while (idxApp < filestreamApp.Length)
                    {
                        var count = filestreamApp.Read(dataChunk, 0, dataChunk.Length);
                        idxApp += updater.LoadApplicationChunk(dataChunk, 0, count);
                    }

                    // Buffer firmware
                    var idxFirmware = 0;
                    while (idxFirmware < filestreamFw.Length)
                    {
                        var count = filestreamFw.Read(dataChunk, 0, dataChunk.Length);

                        idxFirmware += updater.LoadFirmwareChunk(dataChunk, 0, count);
                    }

                    //Load key
                    updater.LoadApplicationKey(appKey);

                    Debug.WriteLine("Verifying application.... ");
                    var vapp = updater.VerifyApplication();
                    Debug.WriteLine("Application version: " + vapp);

                    Debug.WriteLine("Verify firmware.... ");
                    var vfw = updater.VerifyFirmware();
                    Debug.WriteLine("Firmware version: " + vfw);

                    BoardSetup.PrintScreen("App: " + vapp, "FW: " + vfw);
                    Thread.Sleep(400);

                    BoardSetup.PrintScreen("Saving and", "Rebooting...");
                    Thread.Sleep(400);

                    // If we can update, tell the firmware to copy the new files to flash, and restart.
                    // After this restart, your newly loaded firmware files, configuration, and application will be on the board.
                    // Flashing

                    updater.FlashAndReset();
                }
                else
                    // Can't load. Shouldn't have been called!
                    return false;
                }
            catch (Exception e)
            {
                Debug.WriteLine("Fail when updating data " + e.ToString());
                return false;
            }
            return true;
        }
    }
}
