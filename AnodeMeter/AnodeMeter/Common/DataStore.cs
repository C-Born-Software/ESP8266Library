using System;
//using Microsoft.SPOT;
//using Microsoft.SPOT.IO;
using System.IO;
using System.Collections;
using System.Text;
//using System.Security.Cryptography;
//using Microsoft.SPOT.Cryptoki;
using AnodeMeter.Hardware;
using AnodeMeter.Common;
//using System.Xml;
using System.Diagnostics;
using GHIElectronics.TinyCLR.IO;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Storage;
using GHIElectronics.TinyCLR.Data.Xml;
using GHIElectronics.TinyCLR.Cryptography;

namespace AnodeMeter.Common
{
    public class DataStore
    {

        public DataStore()
        {
            try
            {
                foreach (string spath in Folders.GetPaths())
                {
                    MayMakeDir(spath);
                    Profile.DebugTime("Path:" + spath); //TODO DAV DEBUG
                }
            }
            catch
            {
            }
#if false
           if (!Directory.Exists(Folders.ConfigPath))
               Directory.CreateDirectory(Folders.ConfigPath);

           if (!Directory.Exists(Folders.SchedPath))
               Directory.CreateDirectory(Folders.SchedPath);

           if (!Directory.Exists(Folders.LogsPath))
               Directory.CreateDirectory(Folders.LogsPath);

           if (!Directory.Exists(Folders.LogsArchivePath))
               Directory.CreateDirectory(Folders.LogsArchivePath);

           if (!Directory.Exists(Folders.MeasurementsRoot))
               Directory.CreateDirectory(Folders.MeasurementsRoot);

           if (!Directory.Exists(Folders.NewMeasurementsPath))
               Directory.CreateDirectory(Folders.NewMeasurementsPath);

           if (!Directory.Exists(Folders.OldMeasurementsPath))
               Directory.CreateDirectory(Folders.OldMeasurementsPath);
#endif

        }
        private static void MayMakeDir(string DirName)
        {
            try
            {
                if (!Directory.Exists(DirName))
                    Directory.CreateDirectory(DirName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("directory Create Failed: " + ex.Message);
            }
        }

        private bool IsLocked = false;  // Set to true when we go to DiskDriveMode as then our code can't touch the uSD card

        public void Lock(bool State)
        {
            IsLocked = State;
        }

        public bool bSchedFileTouched = true;
        // Called when file system may have been changed (say by going to DiskDriveMode)
        // Set flags for anyone who may have cached files
        public void FileSystemChanged()
        {
            bSchedFileTouched = true;
        }

        public static bool DeleteOldFiles()
        {
            bool bOK = false;
            string currentFile = "";
            DateTime dtTooOld = DateTime.Now - new TimeSpan(TimeSpan.TicksPerDay * GlobalConsts.FILE_RETENTION_DAYS);
            try
            {
                string[] foldersToPurge = new string[] { Folders.OldMeasurementsPath, Folders.LogsPath, Folders.LogsArchivePath, Folders.SchedPath };
                foreach (string folder in foldersToPurge)
                {
                    string[] files = Directory.GetFiles(folder);
                    foreach (string fil in files)
                    {
                        FileSystemInfo fsi = new FileInfo(fil);
                        if (fsi.LastWriteTime < dtTooOld)
                            fsi.Delete();
                    }
                }
                FlushFileSystem();
                bOK = true;
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "DataStore::DeleteOldFiles", "Can't delete file \"" + currentFile + "\". Reason: " + ex.Message, "");
            }
            return bOK;
        }
        public bool ArchiveMeasurements()
        {
            bool bOK = false;
            string fName = Folders.OldMeasurementsPath + "\\" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".csv";
            try
            {
                File.Move(FileDefs.ScheduleResultsNew, fName);
                FlushFileSystem();
                bOK = true;
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::CleanResultsOut", "Can't rename file \"" + FileDefs.ScheduleResultsNew + "\" to \"" + fName + "\". Reason: " + ex.Message, "");
            }

            return bOK;
        }
        public static void FlushFileSystem()
        {
            try
            {
                var sd = StorageController.FromName(SC20260.StorageController.SdCard);
                FileSystem.Flush(sd.Hdc);

                //VolumeInfo vi = new VolumeInfo(Folders.RootFsPath);
                //vi.FlushAll();
            }
            catch { }
        }
        public string GetDirtyResults()
        {
            string res = "";
            try
            {
                FileInfo fi = GetFileInfoProtected(FileDefs.ScheduleResultsNew);
                if (fi != null)
                {
                    byte[] buff = new byte[(int)fi.Length];
                    FileStream fs = new FileStream(FileDefs.ScheduleResultsNew, FileMode.Open, FileAccess.Read);

                    fs.Read(buff, 0, buff.Length);

                    res = new string(Encoding.UTF8.GetChars(buff));
                    fs.Close();
                }
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::GetDirtyResults", "Can't read data from \"" + FileDefs.ScheduleResultsNew + "\". Reason: " + ex.Message, "");
            }

            return res;
        }
        private FileInfo GetFileInfoProtected(string fName)
        {
            FileInfo fi = null;
            try
            {
                fi = new FileInfo(FileDefs.ScheduleResultsNew);
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "DataStore::GetFileInfoProtected", "Can't get file information for \"" + FileDefs.ScheduleResultsNew + "\". Reason: " + ex.Message, "");
            }

            return fi;
        }
        private bool FileExists(string fName)
        {
            bool bExists = false;
            try
            {
                FileInfo fi = new FileInfo(fName);
                bExists = fi.Exists;
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "DataStore::FileExists", "Checking for existence of file \"" + fName + "\". Reason: " + ex.Message, "");
            }

            return bExists;
        }
        /* 
         * Battery Logs
         */
#if false // DAV 5NOV2020 - Removed routine battery state logging - now only when doing battery test
        public void SetBattLogName()
        {
            if (Globals.gBattFile == "")
            {
                // Shuffle files along, keeping BattLog1 (current) to BattLog9
                string src="", dst;
                for (int i = 8; i > 0; --i)
                {
                    src = Folders.BattLogsPath + "\\BattLog" + i + ".txt";
                    dst = Folders.BattLogsPath + "\\BattLog" + (i+1) + ".txt";
                    try
                    {
                        if (File.Exists(src))
                        {
                            File.Delete(dst);
                            File.Move(src, dst);
                        }
                    }
                    catch
                    {
                    }
                }
                Globals.gBattFile = src;
                Profile.DebugTime("SetBattLogName Done"); //TODO DAV DEBUG
            }
            //return Globals.gBattFile;
        }
#endif
        private void AppendFile(string fName, string sLine, bool append = true)
        {
            if (IsLocked) return;
            if (fName == "") return;

#if false
            try
            {
                using (StreamWriter fs = new StreamWriter(fName, append))
                {
                    fs.WriteLine(sLine);
                    fs.Flush();
                    fs.Close();
                    //fs = null;
                    FlushFileSystem();
                }
            }
            catch (Exception e)
            {
                Exception f = e;
            }

#else
            StreamWriter fs = null;
            try
            {
                fs = new StreamWriter(fName, append);

                fs.WriteLine(sLine);
                fs.Flush();
                fs.Close();
                fs = null;
                FlushFileSystem();
            }
            finally
            {
                if (fs != null)
                    fs.Close();
            }
#endif
        }

        public void ClearBattLog()
        { AppendFile(Globals.gBattFile, "", false); }

        public void WriteBattLog(string s)
        { AppendFile(Globals.gBattFile, s); }

        public void WriteBattTestName(string s)
        { AppendFile(FileDefs.BattTestsFile, s); }
        // End Battery Logs

        private void WriteFile(string fName, string textToWrite, bool append = false)
        {
            WriteFile(fName, Encoding.UTF8.GetBytes(textToWrite), append);
        }
        private void WriteFile(string fName, byte[] buff, bool append = false)
        {
            FileStream fs = null;
            try
            {
                fs = new FileStream(fName, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
                fs.Write(buff, 0, buff.Length);
                fs.Flush();
                fs.Close();
                fs = null;
                FlushFileSystem();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in \"DataStore::WriteFile\" while attempting to " + (append ? "append to " : "create ") + "file \"" + fName + "\". Reason: " + ex.Message);
            }
            if (fs != null)
                fs.Close();
        }
        public bool WriteConfigFile(string configData)
        {
            bool bcompletedOK = false;
            try
            {
                WriteFile(FileDefs.SystemConfigFile, configData);
                bcompletedOK = true;
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::WriteConfigFile", "Error writing file \"" + FileDefs.SystemConfigFile + "\". Reason: " + ex.Message, "");
            }
            return bcompletedOK;
        }

        public bool WriteFactoryDefaults(string configData)
        {
            bool bcompletedOK = false;
            try
            {
                WriteFile(FileDefs.FactoryDefaultsFile, configData);
                bcompletedOK = true;
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::WriteFactoryDefaults", "Error writing file \"" + FileDefs.FactoryDefaultsFile + "\". Reason: " + ex.Message, "");
            }
            return bcompletedOK;
        }

        public string ReadFileAsString(string fname, bool FailQuietly = false)
        {
            if (IsLocked) return "";
            string res = "";

            try
            {
                if (FileExists(fname))
                {
                    byte[] buff = File.ReadAllBytes(fname);
                    byte[] buff2 = new byte[buff.Length];

                    // Replace CRLF with CR manually, as String.Replace is way too slow (user complaints!)
                    int j = 0;
                    byte last = 0;
                    for (int i = 0; i < buff.Length; i++)
                    {
                        byte c;
                        if ((c = buff[i]) != 0x0a)
                            buff2[j++] = last = c;
                        else if (last != 0x0d)
                            buff2[j++] = 0x0d;
                    }

                    /* DAV Appears the GetChars and Replace are incredibly slow
                     * Using both on a 1680 byte string with 194 lines took 4.45 seconds!
                     * Using the code above to change the \r\n to \r took 0.005 seconds
                     * The Encoding.UTF8.GetChars alone takes 1.25 seconds
                     */
                    //res = new string(Encoding.UTF8.GetChars(buff2)).Replace("\r\n","\r");
                    res = new string(Encoding.UTF8.GetChars(buff2));
                }
            }
            catch (Exception ex)
            {
                if (!FailQuietly)
                {
                    //Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::ReadFactoryDefaults", "Can't read data from \"" + FileDefs.FactoryDefaultsFile + "\". Reason: " + ex.Message, "");
                    throw ex;
                }
            }
            return res;
        }

        public string ReadFactoryDefaults()
        {
            string res = "";
            try
            {
                res = ReadFileAsString(FileDefs.FactoryDefaultsFile);
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::ReadFactoryDefaults", "Can't read data from \"" + FileDefs.FactoryDefaultsFile + "\". Reason: " + ex.Message, "");
            }
            return res;
        }

        private byte[] ReadFile(string fName)
        {
            if (IsLocked) return null;

            byte[] buff = null;
            FileStream fs = null;

            try
            {
                if (FileExists(fName))
                {
                    fs = new FileStream(fName, FileMode.Open, FileAccess.Read);
                    buff = new byte[(int)fs.Length];
                    fs.Read(buff, 0, buff.Length);
                }
                else
                    Logging.IssueEvent(Logging.ErrSeverity.Warning, "DataStore::ReadFile", "File \"" + fName + "\" does not exist.", "");
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::ReadFile", "Can't read data from \"" + fName + "\". Exception Type=" + ex.GetType().ToString() + ". Reason: " + ex.Message + "~ " + (ex.InnerException == null ? "" : ex.InnerException.Message), "");
            }
            if (fs != null)
                fs.Close();

            return buff;
        }

        public bool UploadErrorLogs(BinaryTransport _gw)
        {
            bool bOK = false;
            string thisLog = "";
            FileStream fs = null;

            try
            {
                StreamReader sr = null;
                StringBuilder logData = new StringBuilder(GlobalConsts.USB_TRANSPORT_RECOMMENDED_MAX_TX_SIZE * 2);
                string res = "";
                bool IoError = false;
                bool endOfFile = true;
                bool lastLogFile = false;
                string thisLine = "";
                string[] fileList = GetDirectoryList(Folders.LogsPath);
                for (int i = 0; i < fileList.Length && !IoError; i++)
                {
                    thisLog = fileList[i];
                    lastLogFile = (i == fileList.Length - 1);

                    FileInfo fi = new FileInfo(thisLog);
                    if (fi.Length != 0)
                    {
                        fs = new FileStream(thisLog, FileMode.Open, FileAccess.Read);
                        sr = new StreamReader(fs);
                        endOfFile = false;

                        while (!endOfFile && !IoError)
                        {
                            bOK = false;

                            // Sometimes ReadLine() Fails with an Index-Out-Of-Range error (maybe for empty files??)
                            // When this happens, rather than abort, just move to the next file;
                            try
                            {
                                thisLine = sr.ReadLine();
                            }
                            catch (Exception ex)
                            {
                                Logging.IssueEvent(Logging.ErrSeverity.Warning, "DataStore::UploadErrorLogs", "Attempting to read log \"" + thisLog + "\". Reason: " + ex.Message, "");
                                endOfFile = true;
                            }

                            // Note: The value of the EndOfStream property is not set until after a Read operation
                            // so can't use it in the "while" test, because it has the wrong initial value after construction
                            endOfFile |= sr.EndOfStream;

                            if (thisLine != null && thisLine.Length > 0)
                                logData.Append(thisLine.Replace("\"", "·") + "\n");

                            if (logData.Length > GlobalConsts.USB_TRANSPORT_RECOMMENDED_MAX_TX_SIZE || (lastLogFile && endOfFile && logData.Length > 0))
                            {
                                string logChunk = logData.ToString();
                                res = _gw.IssueRequest("UploadClientLog", "ErrorLog", "csvData", logChunk, 10000);

                                logData.Clear();

                                if (res != "Log Saved")
                                    IoError = true;
                                else
                                    bOK = true;
                            }
                        }
                        fs.Flush();
                        fs.Close();
                        sr = null;
                        fs = null;

                        if (!IoError)
                            ArchiveErrorLogFile(thisLog);
                    }
                    else
                        DeleteFile(thisLog);
                }
            }
            catch (Exception ex)
            {
                if (fs != null)
                {
                    fs.Flush();
                    fs.Close();
                    fs = null;
                }
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::UploadErrorLogs", "Error processing log file: \"" + thisLog + "\". Reason: " + ex.Message, "");
            }

            return bOK;
        }

        public static void DeleteFile(string fname)
        {
            try
            {
                FileSystemInfo fsi = new FileInfo(fname);
                fsi.Delete();
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "DataStore::DeleteFile", "Can't delete file \"" + fname + "\". Reason: " + ex.Message, "");
            }
        }

        public void ArchiveErrorLogFile(string thisLog)
        {
            // Move the contents of any log files to the archive floder then delete the source file
            try
            {
                string archiveFName = "";
                byte[] logContent = null;

                archiveFName = Folders.LogsArchivePath + "\\" + thisLog.ExtractFileNameFromFullPath();

                // Use an explicit read-source, append-to-dest rather than file-move so that the existing-dest-file case is handled
                if ((logContent = ReadFile(thisLog)) != null)
                {
                    WriteFile(archiveFName, logContent, true);
                    DeleteFile(thisLog);
                }
                FlushFileSystem();
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::ArchiveErrorLogFile", "Error moving the log file \"" + thisLog + "\" to the archive. Reason: " + ex.Message, "");
            }
        }

        public string GetConfigFileSha1HashString()
        {
            return GetFileSHA1HashString(FileDefs.SystemConfigFile);
        }
        public string GetFileSHA1HashString(string fName)
        {
            return GetFileSHA1Hash(fName).ToHexString();
        }
        public byte[] GetFileSHA1Hash(string fName)
        {
            return GetSHA1Hash(ReadFile(fName)); ;
        }
        public string GetSHA1Hash(string dataToHash)
        {
            string hashedValue = "";
            byte[] rawHash = GetSHA1Hash(Encoding.UTF8.GetBytes(dataToHash));

            if (rawHash.Length > 0)
                hashedValue = rawHash.ToHexString();

            return hashedValue;
        }
        public byte[] GetSHA1Hash(byte[] dataToHash)
        {

            byte[] hashSHA = null;

            try
            {
                var sha1 = SHA1.Create();
                hashSHA = sha1.ComputeHash(dataToHash);
//                CryptokiDigest di = new CryptokiDigest("", new Mechanism(MechanismType.SHA_1), 160);
  //              hashSHA = di.Digest(dataToHash, 0, dataToHash.Length);
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::GetSHA1Hash", "Failed to generate SHA1 hash. Reason: " + ex.Message, "");
            }

            return hashSHA;
        }
        public bool IsMeasurementFileAvailable()
        {
            bool bDirty = false;
            FileInfo fi = GetFileInfoProtected(FileDefs.ScheduleResultsNew);
            if (fi != null)
                bDirty = fi.Exists;

            return bDirty;
        }
        public void WritePotMeasurement(string PotData)
        {
            WriteFile(FileDefs.ScheduleResultsNew, PotData, true);
        }
        public string[] GetDirectoryList(string folder)
        {
            string[] files = null;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "DataStore::GetDirectoryList", "Error getting a directory listing for folder \"" + folder + "\". Reason: " + ex.Message, "");
            }

            files = Directory.GetFiles(folder);

            return files;
        }
        public static bool DeleteOldScheduleFiles()
        {
            bool bOK = false;
            string currentFile = "";
            try
            {
                string[] foldersToPurge = new string[] { Folders.SchedPath };

                foreach (string folder in foldersToPurge)
                {
                    string[] files = Directory.GetFiles(folder);
                    foreach (string fil in files)
                    {
                        DeleteFile(fil);
                    }
                }
                FlushFileSystem();
                bOK = true;
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "DataStore::DeleteOldScheduleFiles", "Can't delete file \"" + currentFile + "\". Reason: " + ex.Message, "");
            }
            return bOK;
        }


        public static DateTime ScheduleFileCreationTime()
        {
            DateTime createDateTime = DateTime.MinValue;

            FileSystemInfo fsi = new FileInfo(FileDefs.SchedulesFileName);
            try
            {
                if (fsi.Exists)
                {
                    // the first line of the schedules file has the date of the shift the schedules are for

                    StreamReader sr = new StreamReader(FileDefs.SchedulesFileName);
                    string createTimeString = sr.ReadLine();
                    sr.Close();
                    fsi = null;
                    createDateTime = createTimeString.ParseDateTime();
                }

            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::ScheduleFileCreationTime", "Can't read data from \"" + FileDefs.SchedulesFileName + "\". Reason: " + ex.Message, "");
            }

            return createDateTime;
        }

        public static bool ScheduleFileMissing()
        {
            bool FileMissing = true;
            FileSystemInfo fsi = new FileInfo(FileDefs.SchedulesFileName);

            if (fsi.Exists)
                FileMissing = false;

            fsi = null;
            return FileMissing;
        }


        public void WriteSchedulesToDisk(string _theSchedData)
        {
            try
            {
                WriteFile(FileDefs.SchedulesFileName, _theSchedData);
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::WriteSchedulesToDisk", "Can't Write data to \"" + FileDefs.SchedulesFileName + "\". Reason: " + ex.Message, "");
            }
            bSchedFileTouched = true;
        }

#if false //TODO This was never used. DAV
        public void MarkPotAsComplete(string _thePotName)
        {

            string PotsToMeter = null;

          try
          {

              FileStream fs = new FileStream(FileDefs.SchedulesFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

              byte[] buff = new byte[(int)fs.Length];

              fs.Read(buff, 0, buff.Length);
              fs.Close();

              string result = new string(Encoding.UTF8.GetChars(buff));

              string[] Record = result.Split(new Char[] { '\r' });

              for (int i = 0; i < Record.Length - 1; i++)
              {
                  string[] RecordParts = Record[i].Split(new Char[] { ',' });

                  string Pot = RecordParts[0].ToString();

                  if (Pot == _thePotName)
                  {
                      PotsToMeter += Record[i].ToString() + '\r';

                  }
              }

          }
          catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::MarkScheduleAsComplete", "Can't read data from \"" + Folders.SchedPath + "\\" + _thePotName + ".csv" + "\". Reason: " + ex.Message, "");
            }


        }
#endif

        public string ReadSchedulesFile()
        {

            string result = "";

            try
            {
                if (FileExists(FileDefs.SchedulesFileName))
                {
                    byte[] schedData = ReadFile(FileDefs.SchedulesFileName);
                    if (schedData != null)
                    {
                        result = new string(Encoding.UTF8.GetChars(schedData));
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::ReadSchedulesFile", "Can't process data. Reason: " + ex.Message, "");
            }
            return result;
        }


        public Stream OpenConfiguration()
        {
#if (CREATE_TST_DATA)
                ForTestingCreateConfigFile(FileDefs.SystemConfigFile);
#endif
            FileStream fs = null;
            try
            {
                fs = new FileStream(FileDefs.SystemConfigFile, FileMode.Open);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OpenConfiguration Error: " + ex.Message);
            }
            return fs;
            //return new FileStream(FileDefs.SystemConfigFile, FileMode.Open);
        }

        public Stream OpenDeviceConfiguration()
        {
            FileStream fs = null;
            try
            {
                fs = new FileStream(FileDefs.DeviceConfigFile, FileMode.Open);
            } catch(Exception ex)
            {
                Debug.WriteLine("OpenDeviceConfiguration Error: " + ex.Message);
            }
            return fs;
//            return new FileStream(FileDefs.DeviceConfigFile, FileMode.Open);

        }
        public bool WriteDeviceID(string DeviceIDInfo)
        {
            bool bcompletedOK = false;
            try
            {
                WriteFile(FileDefs.DeviceConfigFile, DeviceIDInfo);
                bcompletedOK = true;
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::WriteDeviceID", "Error writing file \"" + FileDefs.DeviceConfigFile + "\". Reason: " + ex.Message, "");
            }
            return bcompletedOK;
        }

        // We want to set the Meter Number in Device.xml to zero
        // But for now we'll just delete the file, after making a backup copy...
        public void DeleteDevicedID()
        {
            string backup = FileDefs.DeviceConfigFile + ".bak";

            try
            {
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(FileDefs.DeviceConfigFile, backup);
                File.Delete(FileDefs.DeviceConfigFile);
                FlushFileSystem();
            }
            catch { }
        }

        public void LogSyncTime(DateTime dtDbServer)
        {
            WriteFile(FileDefs.LastServerSyncTimeFile, dtDbServer.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        public DateTime GetLastSyncTime()
        {
            DateTime dtLastSync = DateTime.MinValue;

            if (FileExists(FileDefs.LastServerSyncTimeFile))
            {
                try
                {
                    string lastSyncTime = new string(Encoding.UTF8.GetChars(ReadFile(FileDefs.LastServerSyncTimeFile)));
                    dtLastSync = lastSyncTime.ParseDateTime();
                }
                catch (Exception ex)
                {
                    // Badly formed string read in? Default to MinValue above.
                }

            }

            return dtLastSync;
        }
        public bool DeviceConfigExists()
        {
            return FileExists(FileDefs.DeviceConfigFile);
        }

        public static bool UpdateXmlValue(string UniqueTagName, string NewTagValue)
        {
            bool bOK = false;
            FileStream fs = null;
            try
            {
                FileInfo fi = new FileInfo(FileDefs.SystemConfigFile);
                byte[] buff = new byte[(int)fi.Length];
                fi = null;

                fs = new FileStream(FileDefs.SystemConfigFile, FileMode.Open, FileAccess.Read);
                fs.Read(buff, 0, buff.Length);
                string contents = new string(Encoding.UTF8.GetChars(buff));
                fs.Close();
                int Index;

                // Find the location of the associated xml element and then value
                if ((Index = contents.IndexOf(UniqueTagName)) != -1 && (Index = contents.IndexOf('\"', Index + 12)) != -1)
                {
                    int startIndex = Index;
                    // Find the end of the Bias Value
                    int endIndex;
                    if ((endIndex = contents.IndexOf('\"', startIndex + 1)) != -1)
                    {
                        string newContents = contents.Substring(0, startIndex + 1) + NewTagValue + contents.Substring(endIndex, contents.Length - endIndex);
                        fs = new FileStream(FileDefs.SystemConfigFile, FileMode.Open, FileAccess.Write);
                        buff = Encoding.UTF8.GetBytes(newContents);
                        fs.Write(buff, 0, buff.Length);
                        fs.Close();
                        bOK = true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (fs != null)
                {
                    fs.Close();
                    fs.Dispose();
                    fs = null;
                }
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "DataStore::UpdateXmlValue", "Write to SD error: " + ex.Message, "SD write err.");
            }
            return bOK;

        }
    }
}