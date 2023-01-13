using System;
using System.IO;
//using Microsoft.SPOT;
using System.Text;
using System.Diagnostics;

namespace AnodeMeter.Common
{
    public class Logging
    {
        private static LcdDisplay _lcd = null;
        private static int _meterID = -1;
        static Stream _outPut = null;
        static Stream rawData = null;

        public enum ErrSeverity
        {
            Informational,
            Warning,
            Severe,
            Fatal
        }
        public static void RegisterMeterID(int MeterID)
        {
            _meterID = MeterID;
        }
        public static void LockOutput()
        {
            if (_outPut == null)
                _outPut = new MemoryStream();

        }
        public static void UnLockOutput()
        {
            if (_outPut != null)
            {
                byte[] buff = new byte[(int)_outPut.Length];
                _outPut.Seek(0, SeekOrigin.Begin);
                _outPut.Read(buff, 0, (int)_outPut.Length);

                try
                {
                    FStream log = new FStream(Folders.LogsPath + "\\" + DateTime.Now.ToString("yyyy-MM-dd") + "SystemLog.txt", FileMode.Append, FileAccess.Write);
                    WriteOutput(log, buff);
                    log.Close();
                }
                catch
                {
                    // Can get an exception here if problem with SD card
                    // Not much we can do. We could hold the logged data in memory and retry later? But how likely is it that the SD card will come good?
                    // For now, lets just trap the exception and dump the buffer
                }
                _outPut.Close();
                _outPut.Dispose();
                _outPut = null;
            }
        }

        public Logging(LcdDisplay LCD)
        {
            _lcd = LCD;
        }
        public static void IssueEvent(ErrSeverity Level, string CallingModule, string LongMsg, string ShortMsg)
        {
            // Since we log all of the details here as a csv,
            // then get rid of any commas in the source to prevent parsing problems
            LongMsg = LongMsg.Replace(",", ";");

            string Severity = "Severity: Undefined";
            switch (Level)
            {
                case ErrSeverity.Informational: Severity = "Severity: Informational"; break;
                case ErrSeverity.Warning: Severity = "Severity: Warning"; break;
                case ErrSeverity.Severe: Severity = "Severity: Severe"; break;
                case ErrSeverity.Fatal: Severity = "Severity: Fatal"; break;
            }

            string combined = DateTime.Now.ToString("dd-MMM-yyyy HH:mm:ss") + ", " + "MeterID: " + _meterID.ToString() +
                    ", " + Severity + ", Module=\"" + CallingModule + "\", Details: " + LongMsg;

            // Since we log all of the details here as a csv,
            // then get rid of any new-line characters in the source to prevent parsing problems
            combined = combined.Replace("\r", "");
            combined = combined.Replace("\n", "~");

            byte[] buff = Encoding.UTF8.GetBytes(combined + "\n");
            try
            {
                if (_outPut == null)
                {
                    if (Globals.SDCardPresent)
                    {
                        FStream log = new FStream(Folders.LogsPath + "\\" + DateTime.Now.ToString("yyyy-MM-dd") + "SystemLog.txt", FileMode.Append, FileAccess.Write);
                        WriteOutput(log, buff);
                        log.Close();
                    }
                }
                else
                    WriteOutput(_outPut, buff);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception in \"Logging::IssueEvent\" while attempting to write to the Logfile (on SD). Reason: " + ex.Message);
            }

            if (_lcd != null && Level != ErrSeverity.Informational && ShortMsg.Length > 0)
                // Write the short-message to the LCD
                _lcd.ShowTimedMessage(ShortMsg);
        }
        private static void WriteOutput(Stream output, byte[] buff)
        {
            output.Write(buff, 0, buff.Length);
            output.Flush();
        }

        // Log raw readings to file for diagnostic purposes
        // For now we'll only log data below nominal value (1.0V)
        // Buffer readings in memory until goes above 1.0V again, then flush to file
        public static void LogRawReading(double val)
        {
            try
            {
                if (val > 1.0)
                {
                    // Write any collected data to file
                    if (rawData != null)
                    {
                        byte[] buff = new byte[(int)rawData.Length];
                        rawData.Seek(0, SeekOrigin.Begin);
                        rawData.Read(buff, 0, (int)rawData.Length);

                        try
                        {
                            FStream log = new FStream(Folders.LogsPath + "\\" + DateTime.Now.ToString("yyyy-MM-dd") + "_RawData.txt", FileMode.Append, FileAccess.Write);
                            WriteOutput(log, buff);
                            log.Close();
                        }
                        catch
                        {
                        }
                        rawData.Close();
                        rawData.Dispose();
                        rawData = null;
                    }
                }
                else
                {
                    if (rawData == null)
                    {
                        rawData = new MemoryStream();
                    }
                    string rdg = DateTime.Now.ToString("HH:mm:ss.fff") + " " + val.ToString("F6");
                    byte[] buff = Encoding.UTF8.GetBytes(rdg + "\n");
                    WriteOutput(rawData, buff);
                }
            }
            catch { }
        }
    }
}
