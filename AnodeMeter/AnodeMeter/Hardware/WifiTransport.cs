using System;
using System.Collections;
using System.Text;
using System.Threading;
//using Microsoft.SPOT;
using AnodeMeter.Common;
//using Microsoft.SPOT.Hardware;
using System.Diagnostics;
using PervasiveDigital.Net;
using PervasiveDigital.Utilities;
using PervasiveDigital.Hardware.ESP8266;

namespace AnodeMeter.Hardware
{
    public class WifiTransport : BinaryTransport
    {
        // TODO  DAV - I really don't like the huge payload header used in usb, could  be so much better
        // 20NOV2020  However I'll go along with it in WiFi for now, and may change to something better later
        private const int minRawPacketSize = 200;   // If packets larger than this we add a packet header
        private const int HeaderSize = 23;          // TODO - DAV Copied from UsbTransport. Maybe we should use a smaller one??
        static bool _bWritePending;                 // TODO - from USB - will we need this?
        static Esp8266WifiDevice wifi;
        static WifiSocket sock;
        private Thread _WifiThread = null;
        static AutoResetEvent wakeEvent = new AutoResetEvent(false);
        public AccessPoint[] apList;


        private static WifiStates WifiState = WifiStates.Init;  // Current state
        private static WifiStates RequestedState = WifiStates.Connected;

        public override WifiStates SetWifi(WifiStates rs)
        {
            if (RequestedState != rs)
            {
                if (rs == WifiStates.Connected)
                {
                    WifiState = WifiStates.Restart;
                    RequestedState = rs;
                }
                else if (rs == WifiStates.Off)
                {
                    RequestedState = rs;
                }
                wakeEvent.Set();
            }
            return WifiState;
        }

        public override TransportType GetTransportType()
        {
            return TransportType.Wifi;
        }

        public override bool Init()
        {
            if (!base.Init())
                throw new Exception("Method \"BinaryTransport::Init\" not initialised");

            if (!Globals.WifiDisable)
            {
                _WifiThread = new Thread(WifiControl);
                _WifiThread.Start();
            }
            return true;
        }

        // WiFi management thread
        /* TODO
         * So what makes sense here would be to initially scan for APs, and get a list in RSSI order
         * Then match from our list of available SSID/PW pairs (if we have a list - set this up?)
         * Then work through and try for a connection, once found cache it
         * Then whenever we need to connect, try the cached one first, reverting to the global search if that fails
         * 
         * Also with the ServerIP/Port pair list (if we have one), similar approach
         * 
         * We also need to add in a light-sleep capability, to save power
         * And turn the WiFi board off when hibernating or powering down
         * 
         * Need to work on the Bakes PLC now, so do the above when get a chance!
         * DAV 23JUL2020
         * 
         *  DAV 26OCT2020 Have an order, so restarted this work.
         */
        private void WifiControl()
        {
            // Initialize - make sure we have a working ESP8266 wifi device
            wifi = ESP8266WiFi.GetDevice();
            //Debug.Print("WiFiControl GetDevice()");
            if (wifi == null)
            {
                Debug.WriteLine("WiFi Device not operational");
                Globals.WifiDisable = true; //So we don't keep trying...
                return;
            }
            Globals.WifiInfo["Meter MAC"] = wifi.StationMacAddress;
            //TODO DAV Add method taking string to library! 19DEC2023
            if (Globals.StaticIP != "") {
                wifi.SetOperatingMode(OperatingMode.Station);
                wifi.EnableDhcp(OperatingMode.Station,false);
                wifi.SetStationIPAddress(Globals.StaticIP);
            }          
            if (Globals.WifiAPs == null || Globals.Gateways == null || Globals.WifiSyncTime == 0)
            {
                Debug.WriteLine("WiFi not configured");
                Globals.WifiDisable = true;
            }
            // === If Wifi is disabled, we only run tests when requested
            if (Globals.WifiDisable)
            {
                //Debug.Print("WiFiControl PowerOff()");
                wifi.SetPower(false);
                for (; ; )
                {
                    if (Globals.WifiTestMode)
                    {
                        wifi.SetPower(true);
                        if (wifi.GetOperatingMode() != OperatingMode.Station)
                            wifi.SetOperatingMode(OperatingMode.Station);
                        if (Globals.StaticIP != "")
                        {
                            wifi.EnableDhcp(OperatingMode.Station, false);
                            wifi.SetStationIPAddress(Globals.StaticIP);
                        } else
                            wifi.EnableDhcp(OperatingMode.Station, true);
                        
                        while (Globals.WifiTestMode)
                        {
                            UpdateApList();
                            Thread.Sleep(1000);
                        }
                        wifi.SetPower(false);
                    }
                    Thread.Sleep(200);
                }
            }

            // === Normal Wifi operating mode
            WifiState = WifiStates.ConnectAP;
            for (; ; )
            {
                if (RequestedState == WifiStates.Connected)
                {
                    // We have a wifi device. Now we try and connect to an AP, and then a server
                    // Once successful, we can flag we have an operation WiFi.
                    // We can keep trying - here, with a retry on fail, or elsewhere...
                    // Once connected we cache the working AP and server index in flash
                    if (WifiState == WifiStates.ConnectAP)
                    {
                        wifi.SetPower(true);
                        wifi.EnableDebugOutput = true;
                        //wifi.EnableVerboseOutput = true;
                        //TODO Remove from final code, or make conditional from setup file or compile option
                        try
                        {
                        if (wifi.GetOperatingMode() != OperatingMode.Station)
                            wifi.SetOperatingMode(OperatingMode.Station);
                        if (Globals.StaticIP != "")
                        {
                            wifi.EnableDhcp(OperatingMode.Station, false);
                            wifi.SetStationIPAddress(Globals.StaticIP);
                        }
                        else
                            wifi.EnableDhcp(OperatingMode.Station, true);
                        }
                        catch (Exception e)
                        {
                        }
                        
                        byte TryCount = 0;
                        byte APNum = Globals.Wifi_AP_Index;
                        string WifiSSID, WifiPWD;

                        while (RequestedState == WifiStates.Connected)
                        {
                            if ((APNum < 1) || (APNum >= Globals.WifiAPs.Length))
                                APNum = 1;
                            WifiSSID = Globals.WifiAPs[APNum];
                            WifiPWD = Globals.WifiAPs[APNum + 1];

                            if (Globals.IpAddress != null)
                                wifi.SetStationIPAddress(Globals.IpAddress);

                            // Quick hardcoded test for FJA
                            //WifiSSID = "StarProbe09";
                            //WifiPWD = "thetta starprobe net er mega secure";

                            try
                            {
                                if (Globals.WifiTestMode || (apList == null))
                                    UpdateApList();
                                wifi.Connect(WifiSSID, WifiPWD);
                                WifiState = WifiStates.ConnectServer;
                                Debug.WriteLine("WifiState => ConnectServer");
                                if (Globals.Wifi_AP_Index != APNum)
                                {
                                    Globals.Wifi_AP_Index = APNum;
                                    FlashWifi.SaveSettings(); // Save as default AP
                                }
                                Globals.WifiInfo["AP SSID"] = WifiSSID;
                                Globals.WifiStatus[0] = true;   //TODO - DAV - Get a fail here even if connect,but no DHCP, so not that useful
                                break;
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine("WiFi AP Connect " + TryCount + " Failed: " + e.Message);
                                //TODO Now we could loop around trying different APs and PWs

                                if (APNum == 1) ++TryCount;
                                APNum += 2;                                                     // Move to next AP in list

                                if (TryCount >= 3)
                                {
                                    RequestedState = WifiStates.Off;
                                    break;
                                }
                                else
                                    wakeEvent.WaitOne(1000 * 5, false);

                            }
                        }
                    }

                    if (WifiState == WifiStates.ConnectServer)
                    {
                        byte ServerNum = Globals.Wifi_Server_Index;

                        int TryCount = 0;

                        while ((RequestedState == WifiStates.Connected) && !Globals.HaveWifi)
                        {
                            if (ServerNum < 1 || ServerNum >= Globals.Gateways.Length)
                                ServerNum = 1;
                            var myadd = Globals.Gateways[ServerNum].Split(new Char[] { ':' });
                            var addr = myadd[0];
                            var port = myadd.Length > 1 ? Convert.ToInt16(myadd[1]) : 80;

                            Debug.WriteLine("Try Server " + Globals.Gateways[ServerNum]);

                            try
                            {
                                if (Globals.WifiTestMode)
                                    UpdateApList();
                                if (!wifi.IsAlive())
                                {   // TODO - DAV - testing
                                    //Debug.Print("WiFi Unresponsive - fall back");
                                    //WifiState = WifiStates.ConnectAP;
                                    Debug.WriteLine("WiFi Unresponsive - Restart");
                                    WifiState = WifiStates.Restart;
                                    wifi.ResetPort();
                                    break;
                                }
                                sock = (WifiSocket)wifi.OpenSocket(addr, port, true);
                                sock.DataReceived += new SocketReceivedDataEventHandler(sock_DataReceived);
                                sock.SocketClosed += new SocketClosedEventHandler(sock_SocketClosed);

                                Globals.WifiInfo["Meter IP"] = wifi.StationIPAddress.ToString();
                                //TODO - DAV - Note, currently don't even get connect if no DHCP, so not useful.
                                // If we do change so can get here, need to determine if the IP is ok. Leave for later, need to ship! (06DEC2020)
                                Globals.WifiStatus[1] = true;
                                Globals.WifiInfo["GW Router IP"] = wifi.StationGateway.ToString();
                                Globals.WifiInfo["Meter MAC"] = wifi.StationMacAddress;
                                // TODO  Quick test - remove/move ASAP
                                string res = IssueRequest("GetGatewayVersion", null, null, null, 6000);
                                //                                Globals.WifiInfo["GW Vn:"] = res;
                                Debug.WriteLine("Connect => " + res);
                                var gwvn = res.Split(new Char[] { ':' });
                                if (gwvn[0] != "Gateway")
                                    throw new Exception("Bad Gateway");
                                Globals.WifiStatus[2] = true;
                                // Else gwvn[1] should be our gateway version, which we may be able to make use of?
                                res = IssueRequest("GetServerLocalTime", null, null, null, 6000);
                                if (res.IsNumbersOnly())
                                {
                                    Globals.WifiInfo["DB Time"] = res.ParseDateTime().ToString();
                                    Globals.WifiStatus[3] = true;
                                }
                                // end test
                                Globals.HaveWifi = true;
                                WifiState = WifiStates.Connected;
                                IssueEvent(ConnectionState.Connected);
                                if (Globals.Wifi_Server_Index != ServerNum)
                                {
                                    Globals.Wifi_Server_Index = ServerNum;
                                    FlashWifi.SaveSettings();
                                }
                                Globals.WifiInfo["Server"] = Globals.Gateways[ServerNum];
                                Debug.WriteLine("WifiState => Connected");
                                TryCount = 0;
                            }
                            catch (Exception e)
                            {
                                //TODO DAV This is expected if server isn't running
                                if (ServerNum++ == 1) ++TryCount;
                                Debug.WriteLine("WiFi Server Connect " + TryCount + " Failure: " + e.Message);
                                if (sock != null)
                                    sock.Dispose();

                                if (TryCount >= 3)
                                {
                                    WifiState = WifiStates.Restart;
                                    break;
                                }
                                else
                                    wakeEvent.WaitOne(1000 * 5, false);
                            }
                        }
                    }
                    if (WifiState == WifiStates.Connected)
                    {
                        if (Globals.WifiTestMode)
                        {
                            UpdateApList();
                            wakeEvent.WaitOne(1000 * 1, false);
                        }
                        else
                            wakeEvent.WaitOne(1000 * 60, false);
                    }
                    if (WifiState == WifiStates.Restart)
                    {
                        Globals.HaveWifi = false;
                        IssueEvent(ConnectionState.Detached);
                        if (sock != null)
                        {
                            sock.Dispose();
                            sock = null;
                        }
                        //wifi.SetPower(false); //TODO - Could just reset?
                        wifi.Sleep(-1);

                        WifiState = WifiStates.ConnectAP;
                        Debug.WriteLine("WifiState => ConnectAP");
                        Thread.Sleep(1000);
                    }
                }
                if (WifiState == WifiStates.Off)
                {
                    if (Globals.WifiTestMode == true) SetWifi(WifiStates.Connected);
                    if (RequestedState != WifiStates.Off)
                        WifiState = WifiStates.Restart;
                }
                if (RequestedState == WifiStates.Off)
                {
                    if (WifiState != WifiStates.Off)
                    {
                        Globals.HaveWifi = false;
                        IssueEvent(ConnectionState.Detached);
                        if (sock != null)
                        {
                            sock.Dispose();
                            sock = null;
                        }
                        Thread.Sleep(1000);     // Allow  ESP time to close socket
                        wifi.SetPower(false);   //TODO - Could just reset?
                        wifi.Sleep(-1);
                        WifiState = WifiStates.Off;
                        Debug.WriteLine("WifiState => Off");
                        wifi.ResetPort();
                    }
                    wakeEvent.WaitOne(1000 * (Globals.WifiTestMode ? 1 : 60), false);
                }
            }
        }

        private void UpdateApList()
        {
            if (wifi != null)
            {
                try
                {
                    apList = wifi.GetAccessPoints(true);
                }
                catch (Exception e)
                {
                }
            }
        }

        //public override bool WriteWithTimeout(byte[] Data, int Timeout_ms) { return true; }
        public override bool WriteWithTimeout(byte[] Data, int Timeout_ms)
        {
            bool bDone = false;
            if (Data.Length > 0)
            {
                if (Data.Length > 0) // 200) // Make this a defined constant. Could be 1000. Could be 0? (Was 200, back to 0 for future reveisions. DAV 5JAN2024)
                {
                    //#if false // USB way
                    byte[] header =
                        Encoding.UTF8.GetBytes("PayloadSize:0x" + ((UInt32)Data.Length).ToHexString() + "\n");
                    _txBuff = new byte[Data.Length + HeaderSize];
                    Array.Copy(header, _txBuff, HeaderSize);
                    Array.Copy(Data, 0, _txBuff, HeaderSize, Data.Length);
                }
                else
                {
                    //#else // wifi way
                    _txBuff = new byte[Data.Length];
                    Array.Copy(Data, _txBuff, Data.Length);
                    //#endif
                }
                _txOpCompleted = false;
                _bWritePending = true;
                //TODO Fix this! DAV =====================
                try
                {
                    sock.Send(_txBuff);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Send Fail: " + e.Message);
                    //TODO - handle failure - probably lost connection. Tear down and start again?
                    WifiState = WifiStates.Restart;
                    Debug.WriteLine("WifiState => Restart");
                }
                _txOpCompleted = true;
                _bWritePending = false;
                //=====================
                bDone = WaitForOpToComplete(OpType.WriteOperation, Timeout_ms);
            }
            return bDone;
        }

        private int bytesExpected;
        private int bytesRead;

        private void sock_DataReceived(object sender, SocketReceivedDataEventArgs args)
        {
            var socket = (WifiSocket)sender;
            if (args.Data != null)
            {
                int rxlen = args.Data.Length;
                Debug.WriteLine("Data Received : " + rxlen);
                if (rxlen > 0)
                {
                    if (bytesExpected > 0)
                    {
                        // Expect additional data to make up packet
                        // Make sure _rxBuff has been allocated
                        if (_rxBuff == null)
                        {
                            Debug.WriteLine("NULL _rxBuff - Allocating (FIX THIS!)");
                            _rxBuff = new byte[bytesExpected];
                        }
                        Array.Copy(args.Data, 0, _rxBuff, bytesRead, rxlen);
                        bytesExpected -= rxlen;
                        bytesRead += rxlen;
                        Debug.WriteLine("Received: Partial");
                    }
                    else if (rxlen >= HeaderSize) // Could be a packet with header?
                    {
                        string size = new string(Encoding.UTF8.GetChars(args.Data));
                        if (size.Substring(0, 14) == "PayloadSize:0x")
                        {
                            // Packet with size header
                            UInt32 packetSize = size.Substring(14, 8).HexToUInt32();
                            bytesRead = rxlen - HeaderSize;
                            bytesExpected = (int)packetSize - bytesRead;
                            _rxBuff = new byte[packetSize];
                            Array.Copy(args.Data, HeaderSize, _rxBuff, 0, bytesRead);
                            Debug.WriteLine("Received: Payload");
                        }
                        else
                        {
                            // Data without header
                            _rxBuff = args.Data;
                            bytesExpected = 0;
                        }
                    }
                    else
                    {
                        // Data without header
                        _rxBuff = args.Data;
                        bytesExpected = 0;
                    }

                    if (bytesExpected <= 0)
                    {
                        bytesExpected = 0;
                        _rxOpCompleted = true;
                    }
                }
                Debug.WriteLine("Received: " + StringUtilities.ConvertToString(args.Data));
            }
        }

        private void sock_SocketClosed(object sender, EventArgs args)
        {
            bytesExpected = bytesRead = 0;
            Debug.WriteLine("Socket closed: " + ((WifiSocket)sender).Id);
        }
    }
}
