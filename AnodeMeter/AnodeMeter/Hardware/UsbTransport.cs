using System;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.UsbClient;
using System.IO;
using System.Text;
using System.Diagnostics;
using AnodeMeter.Common;
using GHIElectronics.TinyCLR.IO;



namespace AnodeMeter.Hardware
{
    class UsbTransport : BinaryTransport
    {
        private const int HeaderSize = 23;
        private static RawDevice.RawStream _usbStream = null;
        //private static RawDevice _usbDevice = null;
        private Thread _pollUsb = null;
        static bool _bWritePending;

        static UsbClientController UsbController;
        static WinUsb winUsb= null;

        static UsbClientSetting usbClientSetting = new UsbClientSetting()
        {
            Mode = UsbClientMode.WinUsb,
            ManufactureName = "C-Born Software Systems",
            ProductName = "Anode Drop Meter",
            SerialNumber = "2",
            //Guid = "{77C99034-2428-424a-8130-DC481841429B}", // Doesn't work with GHI TinyCLR (yet?)
            Guid = "{a5dcbf10-6530-11d2-901f-00c04fb951ed}",    // Interface GUID GUID_DEVINTERFACE_USB_DEVICE = "Attached to a Hub"
            VendorId = 0x1234,
            ProductId = 0x0002,
            MaxPower = 500,
            InterfaceName = "SitCore Based Anode Meter"
        };

        //UsbController = UsbClientController.GetDefault();

        //WinUsb winUsb = new WinUsb(UsbClientController.GetDefault(), usbClientSetting);
  

        DeviceState _usbState = DeviceState.Default;
        //UsbController.PortState _usbState = UsbController.PortState.Stopped;

        public UsbTransport()
        {
            try
            {
                //UsbController = UsbClientController.GetDefault();
                //winUsb = new WinUsb(UsbController, usbClientSetting);
                //winUsb.DeviceStateChanged += (a, b) => Debug.WriteLine("Connection changed to " + winUsb.DeviceState);

                //StartWinUsb(); (Started in Init() )
            } catch(Exception ex)
            {
                Debug.WriteLine("UsbTransport Exception: " + ex.Message);
            }
        }
        // Start WinUSB
        static void StartWinUsb()
        {
            Debug.WriteLine("StartWinUsb " + ((winUsb is null) ? "" : "(Skipped)"));
            if (winUsb != null) return;
            UsbController = UsbClientController.GetDefault();
            winUsb = new WinUsb(UsbController, usbClientSetting);
            //winUsb.DeviceStateChanged += Usb_DeviceStateChanged;
            //winUsb.DataReceived += Usb_DataReceived;
            winUsb.Enable();
            Debug.WriteLine("WinUsb Started");
        }
        //Stop WinUSB
        static void StopWinUsb()
        {
            Debug.WriteLine("StopWinUsb " + ((winUsb is null) ? "(Skipped)" : ""));
            if (winUsb == null) return;
            winUsb.Disable();
            //winUsb.DeviceStateChanged -= Usb_DeviceStateChanged;
            //winUsb.DataReceived -= Usb_DataReceived;
            winUsb.Dispose();
            winUsb = null;
            Thread.Sleep(1000);
            Debug.WriteLine("WinUsb Stopped");
        }
        public override bool WriteWithTimeout(byte[] Data, int Timeout_ms)
        {
            bool bDone = false;
            if (Data.Length > 0)
            {
                byte[] header = Encoding.UTF8.GetBytes("PayloadSize:0x" + ((UInt32)Data.Length).ToHexString() + "\n");
                _txBuff = new byte[Data.Length + HeaderSize];
                Array.Copy(header, _txBuff, HeaderSize);
                Array.Copy(Data, 0, _txBuff, HeaderSize, Data.Length);

                _txOpCompleted = false;
                _bWritePending = true;
                bDone = WaitForOpToComplete(OpType.WriteOperation, Timeout_ms);
            }
            return bDone;
        }

        public override void Close()
        {
            if (_pollUsb != null)
            {
                _pollUsb.Abort();
                _pollUsb = null;
            }
            StopWinUsb();
//            winUsb.Disable();
//            winUsb.Dispose();
            //Controller.ActiveDevice = null;
            //_usbDevice = null;
            _usbStream = null;
            _usbState = DeviceState.Default; // UsbController.PortState.Stopped;
            base.Close();
        }

        public override TransportType GetTransportType()
        {
            return TransportType.Usb;
        }

        public override bool Init()
        {
            bool bCode = false;

            try
            {
                if (!base.Init())
                    throw new Exception("Method \"BinaryTransport::Init\" not initialised");

                //// Check debug interface
                //if (GHI.Premium.Hardware.Configuration.DebugInterface.GetCurrent() == GHI.Premium.Hardware.Configuration.DebugInterface.Port.USB1)
                //    throw new InvalidOperationException("Current debug interface is USB. It must be changed to something else before proceeding.");

                StartWinUsb();
                _usbStream = winUsb.Stream;
                // All done, you can start the device now
                //winUsb.Enable();

                _pollUsb = new Thread(PollUSB);
                _pollUsb.Priority = ThreadPriority.AboveNormal;
                _pollUsb.Start();

                bCode = true;
            }

            catch (Exception ex)
            {
                Globals.USBAvailable = false;
                Logging.IssueEvent(Logging.ErrSeverity.Fatal, "UsbTransport::Init", ex.Message, "USB Config Err");
            }
            return bCode;
        }

        static private bool bSuspend = false;
        static private AutoResetEvent EventSuspend = new AutoResetEvent(false);
        public override void Suspend()
        {
            Globals.DiskDriveMode = true;
            EventSuspend.Reset();
            bSuspend = true;
            EventSuspend.WaitOne(2000, true);
            //Controller.ActiveDevice = null;
            Logging.LockOutput();

            StopWinUsb();
//            winUsb.Disable();
//            winUsb.Dispose();
            FileSystem.Unmount(ConfigureSystem._ps.Hdc);
            ConfigureSystem._ps.Close();
            ConfigureSystem._ps.Dispose();
        }
        public override void Resume()
        {
            //USBClientController.Start(_usbDevice);
            StartWinUsb();
//            winUsb.Enable();
//            Controller.ActiveDevice = _usbDevice;
            try
            {
                ConfigureSystem.ProtectedFsMount();
                Logging.UnLockOutput();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.Message);
            }
            bSuspend = false;
            EventSuspend.Set();
            Globals.DiskDriveMode = false;
        }

        private enum receiveStates
        {
            WaitForStart,
            WaitingForPacketSize,
            WaitingForPackBody
        }

        private void PollUSB()
        {
            int bytesRead = 0;
            //            int bytesWritten = 0;
            bool usbWasAlreadyRunning = false;
            byte[] ReceiveBuffer = new byte[1024];
            byte[] headerBuffer = new byte[24];
            byte[] bodyBuffer = null;
            int headerIndex = 0;
            int bodyIndex = 0;
            int rxBuffIndex = 0;
            receiveStates rs = receiveStates.WaitForStart;
            int headerSize = ("PayloadSize:0x0123abcd\n").Length;
            UInt32 packetSize = 0;
            bool bGoAgain = false;
            int copySize;
            DateTime _dtTimeOut = DateTime.MinValue;
            TimeSpan tsOneSecond = new TimeSpan(1 * TimeSpan.TicksPerSecond);

            DeviceState previousState = DeviceState.Default; // Is this a thing?

            DateTime usbStateDebounce = DateTime.MinValue;

            //int cnt = 0; //TODO REMOVE DEBUG DAV
            while (true)
            {
                if (bSuspend)
                {
                    EventSuspend.Set();
                    //Debug.Print("PollUSB Suspended");   //TODO REMOVE DEBUG DAV
                    Thread.Sleep(1000);
                    EventSuspend.WaitOne();
                    //Debug.Print("PollUSB Resuming");    //TODO REMOVE DEBUG DAV
                }
                //Debug.Print("PollUSB: " + ++cnt); //TODO REMOVE DEBUG DAV
                try
                {

                    //_usbState = Controller.State;
                    _usbState = winUsb.DeviceState;

                    //Debug.Print("USB State: " + _usbState); ////TODO REMOVE DEBUG DAV 

                    if (previousState != _usbState)
                    {
                        if (usbStateDebounce == DateTime.MinValue)
                            usbStateDebounce = DateTime.Now + new TimeSpan(1 * TimeSpan.TicksPerSecond);

                        //if (_usbState != UsbController.PortState.Running || usbStateDebounce < DateTime.Now)
                        if (_usbState != DeviceState.Configured || usbStateDebounce < DateTime.Now)
                        {
                            // Issue state change event
                            IssueEvent(_usbState == DeviceState.Configured ? ConnectionState.Connected : ConnectionState.Detached);
                            previousState = _usbState;
                        }
                    }
                    else
                        usbStateDebounce = DateTime.MinValue;

                    if (_usbState != DeviceState.Configured)
                    {
                        if (usbWasAlreadyRunning)
                        {
                            //USBClientController.Stop();           // TODO DAV Can we stop by setting ActiveDevice to null or 0?
                            //USBClientController.Start(_usbDevice);
#warning //TODO - Fix next line
//                            Controller.ActiveDevice = _usbDevice;
                            usbWasAlreadyRunning = false;
                        }
                        Thread.Sleep(1200);
                    }
                    else //running
                    {
                        usbWasAlreadyRunning = true;
                        rxBuffIndex = 0;
                        try
                        {
                            bytesRead = _usbStream.Read(ReceiveBuffer, 0, ReceiveBuffer.Length);
                        }
                        catch (Exception ex)
                        {
                            // timed out exception? Set bytesRead to 0. Perhaps add an actual timeout value?
                            bytesRead = 0;
                        }

                        if (bytesRead != 0)
                        {
                            bGoAgain = true;

                            while (bGoAgain)
                            {
                                //Debug.Print("PollUSB::GoAgain: " + ++cnt); //TODO REMOVE DEBUG DAV
                                bGoAgain = false;
                                switch (rs)
                                {
                                    case receiveStates.WaitForStart:
                                        if (bytesRead > 0)
                                        {
                                            rs = receiveStates.WaitingForPacketSize;
                                            headerIndex = 0;
                                            bodyBuffer = null;
                                            bGoAgain = true;
                                            _dtTimeOut = DateTime.Now + tsOneSecond;
                                        }

                                        break;
                                    case receiveStates.WaitingForPacketSize:
                                        if (bytesRead + headerIndex >= headerSize)
                                        {
                                            copySize = headerSize - headerIndex;
                                            Array.Copy(ReceiveBuffer, rxBuffIndex, headerBuffer, headerIndex, copySize);
                                            rxBuffIndex += copySize;

                                            string size = new string(Encoding.UTF8.GetChars(headerBuffer));

                                            if (size.Substring(0, 14) != "PayloadSize:0x")
                                            {
                                                rs = receiveStates.WaitForStart;
                                                Logging.IssueEvent(Logging.ErrSeverity.Warning, "UsbTransport::PollUSB", "Receive sequence error", "");
                                            }
                                            else
                                            {
                                                packetSize = size.Substring(14, 8).HexToUInt32();
                                                _dtTimeOut = DateTime.Now + new TimeSpan((packetSize / 5000 + 1) * TimeSpan.TicksPerSecond);

                                                rs = receiveStates.WaitingForPackBody;
                                                bodyIndex = 0;
                                                bytesRead -= copySize;

                                                bodyBuffer = new byte[packetSize];

                                                if (bytesRead > 0)
                                                    bGoAgain = true;
                                            }
                                        }
                                        else
                                        {
                                            Array.Copy(ReceiveBuffer, rxBuffIndex, headerBuffer, headerIndex, bytesRead);
                                            rxBuffIndex += bytesRead;
                                            headerIndex += bytesRead;
                                        }

                                        break;
                                    case receiveStates.WaitingForPackBody:

                                        if (bodyIndex + bytesRead > bodyBuffer.Length)
                                        {
#if (DEBUG)
                                            string bb = new string(UTF8Encoding.UTF8.GetChars(bodyBuffer));
                                            string rb = new string(UTF8Encoding.UTF8.GetChars(ReceiveBuffer));
#endif
                                            Logging.IssueEvent(Logging.ErrSeverity.Warning, "UsbTransport::PollUSB", "Data received on USB from PC is larger (" +
                                                (bodyIndex + bytesRead).ToString() + ") than negotiated packet size (" + bodyBuffer.Length.ToString() + ").",
                                                "Usb rx err");
                                        }

                                        else
                                        {
                                            Array.Copy(ReceiveBuffer, rxBuffIndex, bodyBuffer, bodyIndex, bytesRead);
                                            rxBuffIndex += bytesRead;
                                            bodyIndex += bytesRead;

                                            if (bodyIndex == packetSize)
                                            {
                                                // Pass completed packet up
                                                _rxBuff = bodyBuffer;
                                                bodyBuffer = null;
                                                _rxOpCompleted = true;
                                                rs = receiveStates.WaitForStart;
                                            }
                                        }
                                        break;
                                }
                            }
                        }
                        else
                        {
                            if (rs == receiveStates.WaitForStart)
                            {
                                if (_bWritePending)
                                {
                                    _bWritePending = false;
#if true
//#if (MF_FRAMEWORK_VERSION_V4_3)
                                    // In 4.3 Write is a void, so must always succeed?? DAV
                                    // GHI says: Write loops internally until all of the bytes have been written or the amount of time specified by WriteTimeout has passed
                                     _usbStream.Write(_txBuff, 0, _txBuff.Length);
                                     _txOpCompleted = true;
#else
                                    bytesWritten = _usbStream.Write(_txBuff, 0, _txBuff.Length);

                                    if (bytesWritten != _txBuff.Length)
                                    {
                                        string currentCallStack = "";
                                        try
                                        {
                                            throw new Exception("USB write incomplete");
                                        }
                                        catch (Exception ex)
                                        {
                                            currentCallStack = ex.StackTrace;
                                        }

                                        Logging.IssueEvent(Logging.ErrSeverity.Warning, "UsbTransport::PollUSB", "USB Data Send incomplete: Requested=" +
                                            _txBuff.Length.ToString() + ", Sent=" + bytesWritten.ToString() + ". Usb tx err. Call Stack: " + currentCallStack, "");

                                        IssueEvent(ConnectionState.Detached);

                                        Thread.Sleep(500);
                                    }
                                    else
                                    {
                                        _txOpCompleted = true;
                                    }
#endif
                                }
                                else
                                    Thread.Sleep(500);
                            }
                            else if (DateTime.Now > _dtTimeOut)
                            {
                                Logging.IssueEvent(Logging.ErrSeverity.Warning, "UsbTransport::PollUSB", "Timed-out waiting for data", "");
                                rs = receiveStates.WaitForStart;
                                Thread.Sleep(500);
                            }
                            else
                                Thread.Sleep(50);
                        }
                    }
                }
                catch (Exception ex)
                {
                    rs = receiveStates.WaitForStart;
                    Logging.IssueEvent(Logging.ErrSeverity.Warning, "UsbTransport::PollUSB", "Exception processing usb data:" + ex.Message, "USB Error");
                }
            }
        }
    }
}
