using System;
//using Microsoft.SPOT;
using System.Threading;
using System.Text;
using AnodeMeter.Common;
using AnodeMeter.Hardware;

namespace AnodeMeter
{
    public abstract class BinaryTransport
    {
        public enum ConnectionState
        {
            Detached,
            Connected
        }

        public enum TransportType
        {
            None, Usb, Wifi, Simulated
        }

        public enum WifiStates
        {
            Init = 0, ConnectAP, ConnectServer, Connected, Restart, Sleep, Wake, Testing, Off, Query, NotImplemented
        };

        public delegate void ConnectionStateChanged(ConnectionState cs);
        public event ConnectionStateChanged ConnectionStateHandler;
        Timer _tmrManageConnection = null;
        ConnectionState _cs;
        bool _connStateChanged = false;
        object _intraThreadLock = new object();

        protected byte[] _rxBuff = null;
        protected byte[] _txBuff = null;
        protected bool _rxOpCompleted;
        protected bool _txOpCompleted;

        public BinaryTransport()
        {
        }
        public abstract bool WriteWithTimeout(byte[] Data, int Timeout_ms);
        protected enum OpType
        {
            ReadOperation,
            WriteOperation,
        }

        public virtual TransportType GetTransportType() { return TransportType.None; }
        public virtual WifiStates SetWifi(WifiStates rs) { return WifiStates.NotImplemented; }

        protected virtual bool WaitForOpToComplete(OpType Op, int Timeout_ms)
        {
            bool bDone = false;
            DateTime dtEnd = DateTime.Now + new TimeSpan(Timeout_ms * TimeSpan.TicksPerMillisecond);

            if (Op == OpType.ReadOperation)
            {
                while (!_rxOpCompleted && DateTime.Now < dtEnd)
                    Thread.Sleep(200);

                bDone = _rxOpCompleted;
            }
            else if (Op == OpType.WriteOperation)
            {
                while (!_txOpCompleted && DateTime.Now < dtEnd)
                    Thread.Sleep(200);

                bDone = _txOpCompleted;
            }

            return bDone;
        }
        public virtual bool WriteWithTimeout(string Data, int Timeout_ms)
        {
            return WriteWithTimeout(Encoding.UTF8.GetBytes(Data), Timeout_ms);
        }
        public virtual byte[] PromptThenReadWithTimeout(byte[] Prompt, int Timeout_ms)
        {
#if (EMULATOR)
                string req = new string(Encoding.UTF8.GetChars(Prompt));
                Debug.Print("Req: " + req);
#endif
            byte[] data = null;
            _rxBuff = null;
            _rxOpCompleted = false;

            if (WriteWithTimeout(Prompt, Timeout_ms) && WaitForOpToComplete(OpType.ReadOperation, Timeout_ms))
                data = _rxBuff;

            return data;
        }
        public virtual string PromptThenReadWithTimeout(string Prompt, int Timeout_ms)
        {
            string s = "";
            byte[] d = PromptThenReadWithTimeout(Encoding.UTF8.GetBytes(Prompt), Timeout_ms);
            if (d != null)
                s = new string(Encoding.UTF8.GetChars(d));
            return s;
        }
        public string IssueRequest(string RequestType, string NodeName, string AttributeName, string AttributeData, int Timeout_ms)
        {
            string result = "";
            string xmlRequest = "";

            if (NodeName == null || NodeName.Length == 0)
                xmlRequest = "<AnodeMeter><Request rqsttype=\"" + RequestType + "\"></Request></AnodeMeter>";
            else
                xmlRequest = "<AnodeMeter><Request rqsttype=\"" + RequestType + "\"></Request><" + NodeName + " " + AttributeName + "=\"" + AttributeData + "\"/></AnodeMeter>";

            result = PromptThenReadWithTimeout(xmlRequest, Timeout_ms);
            return result;
        }
        public virtual void Close()
        {
            _cs = ConnectionState.Detached;
            _tmrManageConnection = null;

        }

        public virtual bool Init()
        {
            _cs = ConnectionState.Detached;
            _tmrManageConnection = new Timer(QueueConnectionStatusChange, null, 0, 500);
            return _tmrManageConnection != null;

        }

        public virtual void Suspend() { }
        public virtual void Resume() { }

        protected void QueueConnectionStatusChange(object o)
        {
            if (_connStateChanged && ConnectionStateHandler != null)
            {
                ConnectionStateHandler(_cs);

                Monitor.Enter(_intraThreadLock);
                _connStateChanged = false;
                Monitor.Exit(_intraThreadLock);
            }
        }
        protected void IssueEvent(ConnectionState cs)
        {
            Monitor.Enter(_intraThreadLock);
            _cs = cs;
            _connStateChanged = true;
            Monitor.Exit(_intraThreadLock);
        }
    }
}
