using System;
using System.Threading;
//using Microsoft.SPOT;
//using Microsoft.SPOT.Hardware.UsbClient;
//using GHI.Premium.USBClient;
//using GHI.Usb.Client;
using GHIElectronics.TinyCLR.Devices.UsbClient;
using System.IO;
using System.Text;
using System.Diagnostics;
using AnodeMeter.Common;

namespace AnodeMeter.Hardware
{
    class UsbTransport : BinaryTransport
    {
        private const int HeaderSize = 23;
        private static RawDevice.RawStream _usbStream = null;
        private static RawDevice _usbDevice = null;
        private Thread _pollUsb = null;
        static bool _bWritePending;

        static UsbClientController UsbController;
        static WinUsb winUsb;



        static UsbClientSetting usbClientSetting = new UsbClientSetting()
        {
            Mode = UsbClientMode.WinUsb,
            ManufactureName = "C-Born Software Systems",
            ProductName = "Anode Drop Meter",
            SerialNumber = "1",
            Guid = "{77C99034-2428-424a-8130-DC481841429B}",
        };

        //UsbController = UsbClientController.GetDefault();

        //WinUsb winUsb = new WinUsb(UsbClientController.GetDefault(), usbClientSetting);
  

        DeviceState _usbState = DeviceState.Default;
        //UsbController.PortState _usbState = UsbController.PortState.Stopped;

        public UsbTransport()
        {
            try
            {
                UsbController = UsbClientController.GetDefault();
                winUsb = new WinUsb(UsbController, usbClientSetting);
            } catch(Exception ex)
            {
                Debug.WriteLine("UsbTransport Exception: " + ex.Message);
            }
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
            winUsb.Disable();
            //Controller.ActiveDevice = null;
            _usbDevice = null;
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

            // We want to set up some device descriptors so that Windows can recognize us as a WinUSB device and load drivers automatically
            // As we can't set our bcdUSB to 0x0200 under GHI SDK4.2 (or possibly even under 4.3) this may not work, as MS may not query us
            // for these, but at least it is a start! -DAV 28AUG17
            // Refer to https://github.com/pbatard/libwdi/wiki/WCID-Devices for info about this - DAV
            // https://msdn.microsoft.com/en-us/library/windows/hardware/ff540283%28v=vs.85%29.aspx?f=255&MSPPError=-2147217396 // Info from MS

            // We'll use 0x33 as our VendorCode for now, for no particular reason...
            const byte VENDORCODE = 0x33;
            // Our String descriptor with the specific MSFT100 followed by our VendorCode
            Configuration.StringDescriptor MSFT = new Configuration.StringDescriptor(0xEE, "MSFT100\x33");

            //Generic descriptor which contains our specific "WINUSB" identification
            byte[] mcidPayload = new byte[]
            {
                0x28, 0x00, 0x00, 0x00,
                0x00, 0x01,
                0x04, 0x00,
                0x01,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00,
                0x01,
                0x57, 0x49, 0x4E, 0x55, 0x53, 0x42, 0x00, 0x00, // WINUSB
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };
            const byte RequestType = Configuration.GenericDescriptor.REQUEST_IN | Configuration.GenericDescriptor.REQUEST_Vendor;
            Configuration.GenericDescriptor mcidDescriptor = new Configuration.GenericDescriptor(RequestType, 0, mcidPayload);
            mcidDescriptor.bRequest = VENDORCODE; //0x33, was 0xA5; // The VendorCode (Only some seem to work - DAV)
            mcidDescriptor.wIndex = 0x04;

            // An OS Extended Property, containing a GUID, the one which our service is looking for
            // Note that we could use a MultiSZ and add multiple GUIDs here 
            byte[] xpropertyPayload = new byte[]
            {
                0x8E,0,0,0,             // Size = 78 bytes guid + 40 property name + 24 other = 142
                0x00, 0x01,             // Version (1.0 bcd)
                0x05, 0x00,             // descriptor index (5)
                0x01, 0x00,             // 1 section
                0x84, 0x00, 0x00, 0x00, // Property section size, 132 bytes
                0x01, 0x00, 0x00, 0x00, // Data type,1 = Unicode REG_SZ
                0x28, 0x00,             // property name length, 40 bytes
                                        // property name (null -terminated unicode string: 'DeviceInterfaceGuid\0') 
                 (byte)'D',(byte)'\0', (byte) (byte) 'e', (byte)'\0', (byte) 'v', (byte)'\0', (byte) 'i', (byte)'\0', (byte) 'c', (byte)'\0', (byte) 'e', (byte)'\0', (byte) 'I', (byte)'\0', (byte) 'n', (byte)'\0', (byte) 't', (byte)'\0', (byte) 'e', (byte)'\0', (byte) 'r', (byte)'\0', (byte) 'f', (byte)'\0', (byte) 'a', (byte)'\0', (byte) 'c', (byte)'\0', (byte) 'e', (byte)'\0', (byte) 'G', (byte)'\0', (byte) 'u', (byte)'\0', (byte) 'i', (byte)'\0', (byte) 'd', (byte)'\0', (byte) '\0', (byte)'\0' ,
                78,                     // data length
                                        // data ({77C99034-2428-424a-8130-DC481841429B}) - nul-terminated Unicode string
                (byte) '{', (byte)'\0', (byte) '7', (byte)'\0', (byte) '7', (byte)'\0', (byte) 'C', (byte)'\0', (byte) '9', (byte)'\0', (byte) '9', (byte)'\0', (byte) '0', (byte)'\0', (byte) '3', (byte)'\0', (byte) '4', (byte)'\0',
                (byte) '-', (byte)'\0', (byte) '2', (byte)'\0', (byte) '4', (byte)'\0', (byte) '2', (byte)'\0', (byte) '8', (byte)'\0', (byte) '-', (byte)'\0', (byte) '4', (byte)'\0', (byte) '2', (byte)'\0', (byte) '4', (byte)'\0', (byte) 'A', (byte)'\0',
                (byte) '-', (byte)'\0', (byte) '8', (byte)'\0', (byte) '1', (byte)'\0', (byte) '3', (byte)'\0', (byte) '0', (byte)'\0', (byte) '-', (byte)'\0', (byte) 'D', (byte)'\0', (byte) 'C', (byte)'\0', (byte) '4', (byte)'\0', (byte) '8', (byte)'\0', (byte) '1', (byte)'\0', (byte) '8', (byte)'\0', (byte) '4', (byte)'\0', (byte) '1', (byte)'\0', (byte) '4', (byte)'\0', (byte) '2', (byte)'\0', (byte) '9', (byte)'\0', (byte) 'B', (byte)'\0', (byte) '}', (byte)'\0',
                (byte) '\0', (byte)'\0'
            };

            Configuration.GenericDescriptor XpropertyDescriptor = new Configuration.GenericDescriptor(RequestType, 0, xpropertyPayload);
            XpropertyDescriptor.bRequest = VENDORCODE;
            XpropertyDescriptor.wIndex = 5;
            // ============= Descriptors set up ==============

            try
            {
                if (!base.Init())
                    throw new Exception("Method \"BinaryTransport::Init\" not initialised");

                //// Check debug interface
                //if (GHI.Premium.Hardware.Configuration.DebugInterface.GetCurrent() == GHI.Premium.Hardware.Configuration.DebugInterface.Port.USB1)
                //    throw new InvalidOperationException("Current debug interface is USB. It must be changed to something else before proceeding.");

                ushort myVID = 0x1234;
                ushort myPID = 0x0001;
                ushort myDeviceVersion = 0x100;
                ushort myDeviceMaxPower = 250; // in milli amps
                string companyName = "C-Born Software Systems";
                string productName = "Anode Drop Meter";
                string myDeviceSerialNumber = "0";

                // Create the device. Assume it just has one read and one write endpoints.
                //_usbDevice = new RawDevice(myVID, myPID, myDeviceVersion, myDeviceMaxPower, companyName, productName, myDeviceSerialNumber);
                _usbDevice = new RawDevice(UsbController, usbClientSetting);

                byte readEPNumber = (byte)_usbDevice.ReserveNewEndpoint();
                byte writeEPNumber = (byte)_usbDevice.ReserveNewEndpoint();

                Configuration.Endpoint[] epDesc = {
                    new Configuration.Endpoint(writeEPNumber, Configuration.Endpoint.ATTRIB_Write | Configuration.Endpoint.ATTRIB_Bulk),
                    new Configuration.Endpoint(readEPNumber, Configuration.Endpoint.ATTRIB_Read | Configuration.Endpoint.ATTRIB_Bulk) };

                epDesc[0].wMaxPacketSize = 64;
                epDesc[1].wMaxPacketSize = 64;

                Configuration.UsbInterface usbInterface = new Configuration.UsbInterface(0, epDesc);

                usbInterface.bInterfaceClass = 0xFF; // vendor defined
                usbInterface.bInterfaceSubClass = 0xFF;
                usbInterface.bInterfaceProtocol = 0xFF;

                byte interfaceIndex = _usbDevice.AddInterface(usbInterface, "EMX USB Client - Anode Meter");

                // This is used for reading and writing
                _usbStream = _usbDevice.CreateStream(writeEPNumber, readEPNumber);
                _usbStream.ReadTimeout = 0;
                _usbStream.WriteTimeout = 100;

                // Now add our descriptors for WINUSB and our DeviceGuid...
                _usbDevice.AddDescriptor(MSFT);
                _usbDevice.AddDescriptor(mcidDescriptor);
                _usbDevice.AddDescriptor(XpropertyDescriptor);

                // All done, you can start the device now
                //USBClientController.Start(_usbDevice);
                winUsb.Enable();
                //Controller.ActiveDevice = _usbDevice;

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
#warning // TODO - Fix next line
//            ConfigureSystem._ps.Unmount();
        }
        public override void Resume()
        {
            //USBClientController.Start(_usbDevice);

            winUsb.Enable();
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
#warning //TODO - Fix next line
            //            UsbController.PortState previousState = UsbController.PortState.Stopped;
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
                        if (_usbState != DeviceState.Attached || usbStateDebounce < DateTime.Now)
                        {
                            // Issue state change event
                            IssueEvent(_usbState == DeviceState.Attached ? ConnectionState.Connected : ConnectionState.Detached);
                            previousState = _usbState;
                        }
                    }
                    else
                        usbStateDebounce = DateTime.MinValue;

                    if (_usbState != DeviceState.Attached)
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
