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
using GHIElectronics.TinyCLR.Devices.Storage.Provider;
using GHIElectronics.TinyCLR.Devices.Storage;
using GHIElectronics.TinyCLR.IO.TinyFileSystem;
using static AnodeMeter.Hardware.FlashSettings;
using static AnodeMeter.Hardware.TinyFS;

namespace AnodeMeter.Hardware
{
    public class FlashSettings
    {
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
        //const int CLUSTER_SIZE = 256;
        const string DefaultsFile = "defaults.dat";
        
        public static void OnBoot()
        {
            Reload();
        }

        public static bool Reload()
        {
            try
            {
                var tfs = GetTFS();

                FactoryDefaults factoryDefaults = (FactoryDefaults)Reflection.Deserialize(tfs.ReadAllBytes(DefaultsFile), typeof(FactoryDefaults));

                // found our defaults
                Globals.LCDBiasPC = factoryDefaults.LCDBiasPC;
                Globals.BackLightLevel = factoryDefaults.BackLightLevel;
                Globals.GLedBright = factoryDefaults.GLedBright;
                Globals.RLedBright = factoryDefaults.RLedBright;
                Globals.Serial = factoryDefaults.Serial;
                Globals.MeasurementModeIndex = factoryDefaults.MeasMode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("FlashSettings::Reload Failed: " + ex.Message);
                return false;
            }
            return true;
        }

        public static void SaveSettings()
        {
            try
            {
                var tfs = GetTFS();

                FactoryDefaults factoryDefaults = new FactoryDefaults
                {
                    // Install current values
                    LCDBiasPC = Globals.LCDBiasPC,
                    BackLightLevel = Globals.BackLightLevel,
                    GLedBright = Globals.GLedBright,
                    RLedBright = Globals.RLedBright,
                    Serial = Globals.Serial,
                    MeasMode = (byte)Globals.MeasurementModeIndex
                };

                tfs.WriteAllBytes(DefaultsFile, Reflection.Serialize(factoryDefaults, typeof(FactoryDefaults)));
            } catch(Exception Ex)
            {
                Debug.WriteLine("FlashSettings::SaveSettings Error: " + Ex.Message);
            }
        }
    }

    public class FlashWifi
    {
#warning //TODO We may be better off storing the WiFi hints (2 bytes) in RTC BB RAM? DAV

        const string WifiFile = "wifi.dat";

        [Serializable]
        public sealed class WifiHints
        {
            public char[] key = { 'W', 'I', 'F', 'I' };
            public byte AP_Index = 0;
            public byte Server_Index = 0;
        }

        public static void OnBoot()
        {
            try
            {
                var tfs = GetTFS();

                WifiHints wifiHints = (WifiHints)Reflection.Deserialize(tfs.ReadAllBytes(WifiFile), typeof(WifiHints));

                // found our hints
                Globals.Wifi_AP_Index = wifiHints.AP_Index;
                Globals.Wifi_Server_Index = wifiHints.Server_Index;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("FlashWiFi::OnBoot Failed: " + ex.Message);
            }
        }

        public static void Reload()
        {
        }

        public static void SaveSettings()
        {
            try
            {
                var tfs = GetTFS();

                WifiHints wifiHints = new WifiHints
                {
                    // Install current values
                    AP_Index = Globals.Wifi_AP_Index,
                    Server_Index = Globals.Wifi_Server_Index
                };

                tfs.WriteAllBytes(WifiFile, Reflection.Serialize(wifiHints, typeof(WifiHints)));
            }
            catch (Exception Ex)
            {
                Debug.WriteLine("FlashWifi::SaveSettings Error: " + Ex.Message);
            }
        }
     }

    // A simple class to avoid repetition in trying to open/format our TFS - DAV
    public static class TinyFS {
        const int CLUSTER_SIZE = 256;
        public static TinyFileSystem GetTFS()
        {
            var tfs = new TinyFileSystem(new QspiMemory(4 * 1024 * 4), CLUSTER_SIZE);
            if (!tfs.CheckIfFormatted())
            {
                //Do Format if necessary 
                tfs.Format();
            }
            else
            {
                // Mount tiny file system
                tfs.Mount();
            }
            return tfs;
        }
    }
    public sealed class QspiMemory : IStorageControllerProvider
    {
        public StorageDescriptor Descriptor => this.descriptor;
        const int SectorSize = 4 * 1024;

        private StorageDescriptor descriptor = new StorageDescriptor()
        {
            CanReadDirect = false,
            CanWriteDirect = false,
            CanExecuteDirect = false,
            EraseBeforeWrite = true,
            Removable = true,
            RegionsContiguous = true,
            RegionsEqualSized = true,
            RegionAddresses = new long[] { 0 },
            RegionSizes = new int[] { SectorSize },
            RegionCount = (2 * 1024 * 1024) / (SectorSize)
        };

        private IStorageControllerProvider qspiDrive;

        public QspiMemory() : this(2 * 1024 * 1024)
        {

        }

        public QspiMemory(uint size)
        {
            var maxSize = Flash.IsEnabledExtendDeployment ? (10 * 1024 * 1024) : (16 * 1024 * 1024);

            if (size > maxSize)
                throw new ArgumentOutOfRangeException("size too large.");

            if (size <= SectorSize)
                throw new ArgumentOutOfRangeException("size too small.");

            if (size != descriptor.RegionCount * SectorSize)
            {
                descriptor.RegionCount = (int)(size / SectorSize);
            }

            qspiDrive = StorageController.FromName(SC20260.StorageController.QuadSpi).Provider;

            this.Open();
        }

        public void Open()
        {
            qspiDrive.Open();
        }

        public void Close()
        {
            qspiDrive.Close();
        }

        public void Dispose()
        {
            qspiDrive.Dispose();
        }

        public int Erase(long address, int count, TimeSpan timeout)
        {
            return qspiDrive.Erase(address, count, timeout);
        }

        public bool IsErased(long address, int count)
        {
            return qspiDrive.IsErased(address, count);
        }

        public int Read(long address, int count, byte[] buffer, int offset, TimeSpan timeout)
        {
            return qspiDrive.Read(address, count, buffer, offset, timeout);
        }

        public int Write(long address, int count, byte[] buffer, int offset, TimeSpan timeout)
        {
            return qspiDrive.Write(address, count, buffer, offset, timeout);
        }

        public void EraseAll(TimeSpan timeout)
        {
            for (var sector = 0; sector < this.Descriptor.RegionCount; sector++)
            {
                qspiDrive.Erase(sector * this.Descriptor.RegionSizes[0], this.Descriptor.RegionSizes[0], timeout);
            }
        }
    }
}
