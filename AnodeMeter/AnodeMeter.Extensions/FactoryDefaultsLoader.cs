using AnodeMeter.Common;
using System;

namespace AnodeMeter.Common
{
    public static class FactoryDefaultsLoader
    {
        public static void Load(DataStore ds)
        {
            string result = "";
            try
            {
                result = ds.ReadFactoryDefaults();
                if (!string.IsNullOrEmpty(result))
                {
                    string[] records = result.Split('\r');

                    for (int i = 0; i < records.Length; i++)
                    {
                        string record = records[i].Trim();
                        if (record.Length == 0 || record.StartsWith("#")) // Skip blank lines and comments
                            continue;

                        string[] recordParts = record.Split(',');
                        if (recordParts.Length < 1)
                            continue;

                        bool hasP1 = recordParts.Length > 1;
                        string p1 = hasP1 ? recordParts[1].Trim() : "";
                        byte p1b = hasP1 ? TryParseByte(p1, 25) : (byte)25;

                        switch (recordParts[0].Trim().ToLower())
                        {
                            case "backlight":
                                if (hasP1) Globals.BackLightLevel = p1b;
                                break;
                            case "greenled":
                                if (hasP1) Globals.GLedBright = p1b;
                                break;
                            case "redled":
                                if (hasP1) Globals.RLedBright = p1b;
                                break;
                            case "lcdbias":
                                if (hasP1) Globals.LCDBiasPC = p1b;
                                break;
                            case "serial":
                                if (hasP1 && Globals.Serial == 0)
                                    Globals.Serial = Convert.ToUInt16(p1);
                                break;
                            case "measmode":
                                if (hasP1) Globals.MeasurementModeIndex = Convert.ToUInt16(p1);
                                break;
                            case "sleepdelay":
                                Globals.SleepOverride = true;
                                if (hasP1) Globals.SleepDelay = Convert.ToInt16(p1);
                                if (recordParts.Length > 2)
                                    Globals.WakeDelay = Convert.ToInt16(recordParts[2].Trim());
                                break;
                            case "connectedsleepdelay":
                                Globals.SleepOverride = true;
                                if (hasP1) Globals.ConnectedSleepDelay = Convert.ToInt16(p1);
                                if (recordParts.Length > 2)
                                    Globals.ConnectedWakeDelay = Convert.ToInt16(recordParts[2].Trim());
                                break;
                            case "lograwdata":
                                if (hasP1) Globals.LogRawData = p1b;
                                break;
                            case "mask4":
                            case "maskt4":
                                if (hasP1) Globals.MaskT4 = (p1b != 0);
                                break;
                            case "wifi":
                                Globals.WifiAPs = recordParts;
                                break;
                            case "gateway":
                                Globals.Gateways = recordParts;
                                break;
                            case "ipaddress":
                                if (hasP1) Globals.IpAddress = IPAddress.Parse(p1);
                                break;
                            case "usbdisable":
                                if (hasP1) Globals.USBDisable = (p1b != 0);
                                break;
                            case "wifidisable":
                                if (hasP1) Globals.WifiDisable = Globals.WifiDisabled = (p1b != 0);
                                break;
                            case "wifidebug":
                                if (hasP1) Globals.WifiDebug = (p1b != 0);
                                break;
                            case "wifiverbose":
                                if (hasP1) Globals.WifiVerbose = (p1b != 0);
                                break;
                            case "reboottoms":
                                if (hasP1) Globals.RebootToMS = (p1b != 0);
                                break;
                            case "wifimodes":
                                if (hasP1) Globals.WifiModes = p1b;
                                break;
                            case "wifisynctime":
                                if (hasP1)
                                {
                                    Globals.WifiSyncTime = Convert.ToInt16(p1);
                                    if (Globals.WifiSyncTime == 0) Globals.WifiDisabled = true;
                                }
                                break;
                            case "staticip":
                                int quoteIndex = records[i].IndexOf('\"');
                                if (quoteIndex != -1)
                                    Globals.StaticIP = records[i].Substring(quoteIndex);
                                break;
                        }
                    }
                }
                else
                {
                    throw new Exception("Factory Defaults file on SD is empty.");
                }
            }
            catch (Exception ex)
            {
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "FactoryDefaultsLoader::Load", "Error loading factory defaults. Reason: " + ex.Message + ". File Contents: " + result, "");
                // On error, we can't call BoardSetup.SaveSettingsToSD directly,
                // so we log the issue. The calling context may need to handle this.
                throw; // Re-throw to let the caller know it failed.
            }
        }

        private static byte TryParseByte(string s, byte b = 0)
        {
            try
            {
                if (int.TryParse(s, out int i) && i >= 0 && i < 256)
                {
                    return (byte)i;
                }
            }
            catch
            {
                // Ignore conversion errors
            }
            return b;
        }
    }
}