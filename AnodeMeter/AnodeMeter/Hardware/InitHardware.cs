using System;
using AnodeMeter.Common;
using Hardware.LcdCharacterDisplay;
using GHIElectronics.TinyCLR.Native;
using GHIElectronics.TinyCLR.IO;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Adc;
using GHIElectronics.TinyCLR.Devices.Storage;
using System.Diagnostics;
using GHIElectronics.TinyCLR.Devices.Rtc.Provider;
using GHIElectronics.TinyCLR.Devices.Rtc;

namespace AnodeMeter.Hardware
{
    public class ConfigureSystem : SystemInit
    {
        //public static SDCard _ps = null;
        public static StorageController _ps = null;
        
        //private OutputPort _SELI = null;

        public static AnodeMeter Meter = null;
        public ConfigureSystem(AnodeMeter parent)
        {
            Meter = parent;
        }

        ~ConfigureSystem()
        {
            Close();
        }

        public override void InitOnStart()
        {
#warning //TODO Set up Glitch Filter (now per GPIO, or still CPU? Find out...
//            Cpu.GlitchFilterTime = new TimeSpan(TimeSpan.TicksPerMillisecond * 200);

            Profile.DebugTime("IO Mapped"); //TODO DAV DEBUG
            // We do an early read of the analog input here to give it time to settle
            // Without this our initial battery reads (on EMX at least) come in at 5.1 when it is 4.2, so clearly some analog problems in HW or SDK!
            // DAV 10AUG15 

            AdcChannel VBatt = AdcController.FromName(SC20260.Adc.Controller1.Id).OpenChannel(IOMap.VBatt);
            // Microsoft.SPOT.Hardware.AnalogInput VBatt = new Microsoft.SPOT.Hardware.AnalogInput(IOMap.VBatt);
            VBatt.ReadValue(); //Debug.Print("VBatt Preread= " + VBatt.ReadRaw());
            VBatt.Dispose();

            // Also drop the red LED
            PhysicalLED _led = new PhysicalLED(); //TODO DAV Debug (This may cause problems when the PWMs are allocated again?)
            Meter._led = _led;
            Profile.DebugTime("LED Dimmed"); //TODO DAV DEBUG

            LiquidCrystal _lcd = new LiquidCrystal();
            Meter._lcd = _lcd;
            _lcd.Initialize();
            Profile.DebugTime("LCD Constructed"); //TODO DAV DEBUG

            FlashSettings.OnBoot();
            Profile.DebugTime("FlashSettings.OnBoot Done"); //TODO DAV DEBUG

            FlashWifi.OnBoot();
            Profile.DebugTime("FlashWifi.OnBoot Done"); //TODO DAV DEBUG

            // If not using the Ethernet Port, switch the oscillator off to save ~25mA 
            // At the moment, this glues the system, see: http://www.tinyclr.com/forum/10/3587/#/1/
            // DAV 29MAY13 - Back in as should work under 4.2
            //     01JUN13 - But locks up on G120!
//#if (!MF_FRAMEWORK_VERSION_V4_3)
//            //TODO DAV - Missing function on 4.3??
//            if (!Globals.G120)
//                Power.EthernetOscillatorEnable(false);
//#endif
            if (_ps != null)
                Close();

            try
            {
                _ps = StorageController.FromName(SC20260.StorageController.SdCard);
                var drive = FileSystem.Mount(_ps.Hdc);
//                _ps = FileSystem.Mount(sd.Hdc);
//                _ps = new SDCard();
                Globals.SDCardPresent = true;
                FileSystem.Unmount(_ps.Hdc);
            }
            catch (Exception ex)
            {
                // Logging doesn't really make sense if no SD to log to...
                Logging.IssueEvent(Logging.ErrSeverity.Fatal, "ConfigureSystem::OnInitStart", "Error Allocating Persistent Storage. Reason: " + ex.Message, "No SD Card?");
                Globals.SDCardPresent = false;
            }
            Profile.DebugTime("SD Persistent Created"); //TODO DAV DEBUG
            if (Globals.SDCardPresent)
                ProtectedFsMount();

            Profile.DebugTime("InitOnStart Calling Base"); //TODO DAV DEBUG
            base.InitOnStart();
        }

        public override void Close()
        {
            if (_ps != null)
            {
                FileSystem.Unmount(_ps.Hdc);
//                _ps.Unmount();
//                _ps.Dispose();
                _ps = null;
            }
        }
        public static void ProtectedFsMount()
        {
            int Retries = 0;
            bool Done = false;

            while (!Done)
            {
                try
                {
//                    Debug.WriteLine("ProtectedFsMount 1");
                    _ps = StorageController.FromName(SC20260.StorageController.SdCard);
//                    Debug.WriteLine("ProtectedFsMount 2: " + _ps);
                    var drive = FileSystem.Mount(_ps.Hdc);
//                    Debug.WriteLine("ProtectedFsMount 3: " + drive);
                    //_ps.Mount();
                    //TODO DAV - Don't know why we needed a Sleep here? Will try removing it for now. If things break, replace it or find out why. 18OCT15
                    //System.Threading.Thread.Sleep(1000);
                    Done = true;
                }
                catch (Exception ex)
                {
                    if (++Retries > 5)
                    {
                        Logging.IssueEvent(Logging.ErrSeverity.Fatal, "ConfigureSystem::ProtectedFsMount", "Error Mounting the SD File-System. Reason: " + ex.Message, "SD Card Err");
                        Done = true;
                    }
                    else
                        System.Threading.Thread.Sleep(1000);
                }
            }
        }
        public override int Hibernate(int seconds = 60 * 60)
        {
            // Hibernate, wake up on button push, or RTC alarm
            //Power.Hibernate(Power.WakeUpInterrupt.InterruptInputs); 
            // RTC alarm set for 1 hour. If it is an hour before we wake, assume no activity and power off
            // DAV 11AUG15
            //RealTimeClock.SetAlarm(DateTime.Now + new TimeSpan(60 * TimeSpan.TicksPerMinute));
            //DAV -  Note that the above line works, but as RTC and local time may not be in sync, the following is meant to be safer
            ///RealTimeClock.SetAlarm(RealTimeClock.GetDateTime().AddMinutes(60));
            //Power.Hibernate(Power.WakeUpInterrupt.InterruptInputs | Power.WakeUpInterrupt.RTCAlarm); 
            //TODO DAV Fixed Wakeup for 4.3 and RTC!!
            ///PowerState.WakeupEvents |= (HardwareEvent.OEMReserved1 | HardwareEvent.OEMReserved2);
            ///PowerState.Sleep(SleepLevel.DeepSleep, HardwareEvent.OEMReserved1 | HardwareEvent.OEMReserved2);
            var rtc = RtcController.GetDefault();
            var StartTime = DateTime.Now;
            Globals.ButtonPressed = false;
            const int MaxSleep =  4 * 60;
            while(seconds > 0) 
            {
                // Wake up every 4 minutes to get around bug in GHI firmware
                int SecondsToSleep = (seconds > MaxSleep) ? MaxSleep : seconds;
                Debug.WriteLine("Sleeping for: " + SecondsToSleep + "s of " + seconds);
                DateTime dt = rtc.Now;
                Debug.WriteLine("Sleep from " + dt + " (" + DateTime.Now + ")  to " + rtc.Now.AddSeconds(SecondsToSleep));
                Power.Sleep(rtc.Now.AddSeconds(SecondsToSleep));
                SystemTime.SetTime(rtc.Now);
                if (Globals.ButtonPressed) break;
                seconds -= SecondsToSleep;
                Debug.WriteLine("Sleep to go: " + seconds);
            }
            Debug.WriteLine("Slept for " + (rtc.Now - StartTime).TotalSeconds + " Seconds");

            return seconds; // Zero if timed out, else buttonpush (and equals seconds before scheduled timeout)
        }
    }
}
