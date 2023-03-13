using System;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;
using GHIElectronics.TinyCLR.Devices.Uart;
using PervasiveDigital.Hardware.ESP8266;

using SerialPort = GHIElectronics.TinyCLR.Devices.Uart.UartController;

namespace AnodeMeter.Hardware
{
    public static class ESP8266WiFi
    {
        private static  OutputPort _rfPower;// = new OutputPort(IOMap.WiFiPowerPin, false);
        private static  OutputPort _rfReset; // = new OutputPort(IOMap.WiFiResetPin, true);
        static SerialPort port; // = new SerialPort(IOMap.WiFiComPort, 115200, Parity.None, 8, StopBits.One);

        private static bool bInit = false;
        private static Esp8266WifiDevice _wifi = null;
        public static bool booted = false;

        public static void Init()
        {
            if (!bInit)
            {
                _rfPower = new OutputPort(IOMap.WiFiPowerPin, false);
                _rfReset = new OutputPort(IOMap.WiFiResetPin, true);
                //port = new SerialPort(IOMap.WiFiComPort, 115200, Parity.None, 8, StopBits.One);
                port = SerialPort.FromName(IOMap.WiFiComPort);
                var uartSetting = new UartSetting()
                {
                    BaudRate = 115200,
                    DataBits = 8,
                    Parity = UartParity.None,
                    StopBits = UartStopBitCount.One,
                    Handshaking = UartHandshake.None,
                };
                port.SetActiveSettings(uartSetting);
                Reset();
                bInit = true;
            }
        }

        public static void Reset()
        {
            if (_rfReset != null)
            {
                _rfReset.Write(false); // Reset ESP module (Should make part of class?)
                _rfReset.Write(true);
            }
        }

        public static void PowerOn()
        {
            Init();
            _wifi?.SetPower(true);
//            _rfPower.Write(true); // Power on ESP module
        }
        
        public static void PowerOff(bool Hard = false)
        {
            Init();
            _wifi?.SetPower(false);
            if (Hard)
                _rfPower?.Write(false);
//            _rfPower.Write(false); // Power off ESP module
        }

        public static Esp8266WifiDevice GetDevice()
        {
            Init();
            if (_wifi != null)
                return _wifi;

            _wifi = new Esp8266WifiDevice(port, _rfPower, _rfReset);
            if(_wifi != null)
            {
                _wifi.EnableDebugOutput = Globals.WifiDebug;
                _wifi.EnableVerboseOutput = Globals.WifiVerbose;

                _wifi.Booted += (sender, cause) =>
                {
                    Debug.WriteLine("ESP8266 Device Booted");
                    booted = true;
                    if (Globals.WifiDisable && !Globals.WifiTestMode)
                    {
                        //Debug.Print("SetPower(false)");
                        _wifi.SetPower(false);
                    }
                };
            }
            return _wifi;
        }
    }
}