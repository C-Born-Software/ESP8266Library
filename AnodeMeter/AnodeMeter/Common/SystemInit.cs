using System;
//using Microsoft.SPOT;
using System.IO;
using GHIElectronics.TinyCLR.Data.Xml;
//using System.Xml;

namespace AnodeMeter.Common
{
    public abstract class SystemInit
    {
        private int _meterNumber = -1;
        protected string _startCause = "Normal Restart, Build Date=" + Globals.BuildDate.ToString("yyyy-MM-dd HH:mm:ss");

        public string GetStartCause()
        {
            return _startCause;
        }

        public int GetMeterNumber
        {
            get
            {
                return _meterNumber;
            }

        }
        public virtual void Hibernate(int seconds = 60 * 60) {; }
        public abstract void Close();

        public virtual void InitOnStart()
        {
            FStream s = null;
            XmlReader xml = null;

            try
            {
                s = new FStream(FileDefs.DeviceConfigFile, FileMode.Open, FileAccess.Read);

                xml = XmlReader.Create(s);
                bool bDeviceConfigFound = false;

                while (xml.Read())
                {
                    // ********* WARNING **** WARNING *************
                    // The line of code that follows does MORE than it should.
                    // The use of various properties including: <LocalName>, <IsEmptyElement>, etc
                    // are not assigned correct values UNTIL after a method like <IsStartElement> is called.
                    // Removing the call to <IsStartElement> here, will cause this code to break ...
                    bool bStartElement = xml.IsStartElement();

                    if (!bDeviceConfigFound)
                    {
                        if (bStartElement && xml.LocalName == "DeviceDetails")
                        {
                            bDeviceConfigFound = true;
                        }
                    }
                    else
                    {

                        if (xml.LocalName == "MeterNumber")
                        {
                            _meterNumber = (int)xml.ReadAttributeValue("Value");

                        }
                    }
                }

                xml.Close();
                s.Close();

            }
            catch (Exception ex)
            {
                if (xml != null)
                    xml.Close();

                if (s != null)
                    s.Close();

                Logging.IssueEvent(Logging.ErrSeverity.Severe, "SystemInit::ctor", "xml Parsing Exception: " + ex.Message, "xml Parse err.");

            }
        }

        // Parse meter number from Device ID xml string
        // Return +ve integer if successful, else -1
        public int ParseMeterNumber(string s)
        {
            int MeterNum = -1;

            byte[] data = System.Text.UTF8Encoding.UTF8.GetBytes(s);
            MemoryStream strm = new MemoryStream(data);
            XmlReader xml = XmlReader.Create(strm);

            try
            {
                while (xml.Read())
                {
                    bool bStartElement = xml.IsStartElement();
                    if (xml.LocalName == "MeterNumber")
                    {
                        MeterNum = (int)xml.ReadAttributeValue("Value");
                        break;
                    }
                }
                xml.Close();
            }
            catch (Exception ex)
            {
                if (xml != null)
                    xml.Close();

                Logging.IssueEvent(Logging.ErrSeverity.Severe, "SystemInit::ParseMeterNumber", "xml Parsing Exception: " + ex.Message, "xml Parse err.");

            }
            strm.Dispose();

            return MeterNum;
        }
    }
}
