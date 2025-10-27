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
using GHIElectronics.TinyCLR.Cryptography;

namespace AnodeMeter.Hardware
{
    public class FlashSettings
    {
        [Serializable]
        public sealed class FactoryDefaults
        {
            public char[] key = { 'D', 'F', 'L', 'T' };
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
            }
            catch (Exception Ex)
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

                if (tfs.Exists(WifiFile))
                {
                    WifiHints wifiHints = (WifiHints)Reflection.Deserialize(tfs.ReadAllBytes(WifiFile), typeof(WifiHints));

                    // found our hints
                    Globals.Wifi_AP_Index = wifiHints.AP_Index;
                    Globals.Wifi_Server_Index = wifiHints.Server_Index;
                }
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

    public static class TinyFS
    {
        const int CLUSTER_SIZE = 256;
        private static TinyFileSystem tfs = null;
        public static TinyFileSystem GetTFS()
        {
            if (tfs == null)
            {
                tfs = new TinyFileSystem(new QspiMemory(4 * 1024 * 4), CLUSTER_SIZE);
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

    // enum for startup flags. 0 = normal (USB), 1 = Mass Storage, 2 = WinUSB
    public enum StartFlags : byte
    {
        Normal = 0,
        MassStorage = 1,
        WinUSB = 2,
        // Bit 7 will indicate if a context is saved
        ContextSaved = 0x80
    }

    public static class BBRam
    {
        private const ushort BBRAM_SIGNATURE = 0xACDC;
        private const int HEADER_SIZE = 6; // Signature(2) + Length(2) + Flags(1) + ShutdownCode(1)
        private const int MIN_BLOCK_SIZE = HEADER_SIZE + 2; // Header + CRC(2)
        private const int MAX_BLOCK_SIZE = 1024; // Max total size for the BB RAM block (Allows for 40 anode pots)

        private static bool ReadRawBlock(out byte[] block)
        {
            block = null;
            var rtc = RtcController.GetDefault();
            var header = new byte[4];

            try
            {
                rtc.ReadBackupMemory(header, 0);
                ushort signature = BitConverter.ToUInt16(header, 0);
                ushort totalLength = BitConverter.ToUInt16(header, 2);

                if (signature != BBRAM_SIGNATURE || totalLength < MIN_BLOCK_SIZE || totalLength > MAX_BLOCK_SIZE)
                    return false;

                block = new byte[totalLength];
                rtc.ReadBackupMemory(block, 0);

                var crc = new Crc16();
                ushort computedCrc = crc.ComputeHash(block, 0, totalLength - 2);
                ushort storedCrc = BitConverter.ToUInt16(block, totalLength - 2);

                return computedCrc == storedCrc;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteRawBlock(byte flags, byte shutdownCode, byte[] contextData)
        {
            contextData = contextData ?? new byte[0];
            int totalLength = HEADER_SIZE + contextData.Length + 2; // Header + Payload + CRC

            if (totalLength > MAX_BLOCK_SIZE) return;

            var block = new byte[totalLength];

            // Header
            Array.Copy(BitConverter.GetBytes(BBRAM_SIGNATURE), 0, block, 0, 2);
            Array.Copy(BitConverter.GetBytes((ushort)totalLength), 0, block, 2, 2);
            block[4] = flags;
            block[5] = shutdownCode;

            // Payload
            if (contextData.Length > 0)
                Array.Copy(contextData, 0, block, HEADER_SIZE, contextData.Length);

            // CRC
            var crc = new Crc16();
            ushort crcVal = crc.ComputeHash(block, 0, totalLength - 2);
            Array.Copy(BitConverter.GetBytes(crcVal), 0, block, totalLength - 2, 2);

            RtcController.GetDefault().WriteBackupMemory(block, 0);
        }

        private static byte[] GetContextFromBlock(byte[] block)
        {
            if (block == null) return null;

            int contextLength = block.Length - HEADER_SIZE - 2;
            if (contextLength > 0)
            {
                var context = new byte[contextLength];
                Array.Copy(block, HEADER_SIZE, context, 0, contextLength);
                return context;
            }
            return null;
        }

        public static byte[] ReadContext()
        {
            if (ReadRawBlock(out byte[] block) && (block[4] & (byte)StartFlags.ContextSaved) != 0)
            {
                return GetContextFromBlock(block);
            }
            return null;
        }

        public static void WriteContext(byte[] context)
        {
            ReadRawBlock(out byte[] currentBlock);
            byte flags;
            byte shutdownCode;
            if (currentBlock != null)
            {
                flags = currentBlock[4];
                shutdownCode = currentBlock[5];
            }
            else
            {
                flags = (byte)StartFlags.Normal;
                shutdownCode = 0;
            }

            WriteRawBlock((byte)(flags | (byte)StartFlags.ContextSaved), shutdownCode, context);
        }

        public static void ClearContext()
        {
            if (ReadRawBlock(out byte[] currentBlock))
            {

                byte flags = currentBlock[4];
                if ((flags & (byte)StartFlags.ContextSaved) != 0)
                {
                    byte shutdownCode = currentBlock[5];
                    // Clear context data but preserve flags and shutdown code
                    WriteRawBlock((byte)(flags & ~(byte)StartFlags.ContextSaved), shutdownCode, null);
                }
            }
        }

        public static void SetBBStartFlags(StartFlags flags)
        {
            ReadRawBlock(out byte[] currentBlock);
            byte currentFlags;
            byte shutdownCode;
            if (currentBlock != null)
            {
                currentFlags = currentBlock[4];
                shutdownCode = currentBlock[5];
            }
            else
            {
                currentFlags = (byte)StartFlags.Normal;
                shutdownCode = 0;
            }
            var context = GetContextFromBlock(currentBlock);
            byte contextBit = (byte)(currentFlags & (byte)StartFlags.ContextSaved);

            WriteRawBlock((byte)((byte)flags | contextBit), shutdownCode, context);
        }

        public static StartFlags GetBBStartFlags()
        {
            if (ReadRawBlock(out byte[] block))
            {
                return (StartFlags)(block[4] & ~(byte)StartFlags.ContextSaved);
            }
            return StartFlags.Normal;
        }

        public static uint GetShutdownCode()
        {
            if (ReadRawBlock(out byte[] block))
            {
                return block[5];
            }
            return 0;
        }

        public static void SetShutdownCode(IOMap.ShutdownCode sc) => SetShutdownCode((uint)sc);

        public static void SetShutdownCode(uint code)
        {
            ReadRawBlock(out byte[] currentBlock);
            byte flags;
            if (currentBlock != null)
            {
                flags = currentBlock[4];
            }
            else
            {
                flags = (byte)StartFlags.Normal;
            }
            var context = GetContextFromBlock(currentBlock);

            WriteRawBlock(flags, (byte)code, context);
        }
    }
}
#if false
public static class BBRam
    {
        const ushort MaxBBData = 100; // Could use  rtc.BackupMemorySize() for this. Later...
        const ushort MinBBData = 5; // 2 bytes for length, 2 bytes for CRC, at least 1 data byte

        // Read BB Ram according to header size, and validate CRC. Return as byte array if valid, null (or zero size array?) if not
        public static byte[] ReadBBRam()
        {
            var rtc = RtcController.GetDefault();

            // Read header (2-byte length)
            var header = new byte[2];
            rtc.ReadBackupMemory(header, 0);
            ushort totalLength = BitConverter.ToUInt16(header, 0);

            if (totalLength < MinBBData || totalLength > MaxBBData)
                return null;

            // Read entire block (length + payload + CRC)
            var fullData = new byte[totalLength];
            rtc.ReadBackupMemory(fullData, 0);

            // Validate CRC
            var crc = new Crc16();
            ushort computed = crc.ComputeHash(fullData, 0, totalLength - 2);
            ushort stored = BitConverter.ToUInt16(fullData, totalLength - 2);

            if (computed != stored)
                return null;

            // Strip header and CRC → return just payload
            int payloadLength = totalLength - 4;
            var payload = new byte[payloadLength];
            Array.Copy(fullData, 2, payload, 0, payloadLength);

            return payload;
        }
        // Write BB Ram, wrapping data in header and CRC
        public static void WriteBBRam(byte[] data)
        {
            var rtc = RtcController.GetDefault();
            var crc = new Crc16();

            int totalLength = data.Length + 4; // 2 bytes header + payload + 2 bytes CRC
            var fullData = new byte[totalLength];

            // Header: total length (little-endian)
            var lenBytes = BitConverter.GetBytes((ushort)totalLength);
            fullData[0] = lenBytes[0];
            fullData[1] = lenBytes[1];

            // Copy payload into buffer
            Array.Copy(data, 0, fullData, 2, data.Length);

            // CRC over everything except final 2 bytes
            ushort crcVal = crc.ComputeHash(fullData, 0, totalLength - 2);
            var crcBytes = BitConverter.GetBytes(crcVal);
            fullData[totalLength - 2] = crcBytes[0];
            fullData[totalLength - 1] = crcBytes[1];

            // Write to BB RAM
            rtc.WriteBackupMemory(fullData, 0);
        }

        // Startup flags - use CRC16 protected structure ASAP
        // Save startup flags in BB Ram
        public static void SetBBStartFlags(StartFlags flags)
        {
            var data = ReadBBRam();
            if (data == null || data.Length < 1)
                data = new byte[1];
            data[0] = (byte)flags;
            WriteBBRam(data);
        }
        // Get startup flags from BB Ram
        public static StartFlags GetBBStartFlags()
        {
            byte[] data = ReadBBRam();
            Debug.WriteLine("BB Ram: " + ((data == null) ? "null" : data.Length.ToString()));
            if (data == null)
                return StartFlags.Normal;
            return (StartFlags)data[0]; // 1st byte of data
        }

        public static uint GetShutdownCode()
        {
            var readData = BBRam.ReadBBRam();
            if ((readData == null) || readData.Length < 2)
                return 0;
            return readData[1];
        }

        public static void SetShutdownCode(IOMap.ShutdownCode sc)
        {
            SetShutdownCode((uint)sc);
        }
        public static void SetShutdownCode(uint code)
        {
            var readData = BBRam.ReadBBRam();
            if (readData == null || readData.Length < 2)
            {
                byte startFlags = (readData != null && readData.Length >= 1) ? readData[0] : (byte)0;
                readData = new byte[2];
                readData[0] = startFlags;
            }

            readData[1] = (byte)code;
            BBRam.WriteBBRam(readData);
        }
    }
}
#endif
