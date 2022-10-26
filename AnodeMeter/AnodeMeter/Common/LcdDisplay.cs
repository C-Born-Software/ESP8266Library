using System;
using System.Collections;
//using Microsoft.SPOT;
using System.Text;
using System.Threading;

namespace AnodeMeter.Common
{
    public abstract class LcdDisplay
    {
        private bool _showCursor = false;
        private bool _blinkCursor = false;
        private bool _visible = true;
        private bool _backlight = true;
        private const int NotApplicable = 0;
        protected byte[] _TimeMessageLine1 = null;
        protected byte[] _TimeMessageLine2 = null;
        protected byte[] AltLine1 = null;
        protected byte[] AltLine2 = null;
        protected int CurrScreen = 0;
        protected DateTime _messageExpiry = DateTime.MinValue;
        readonly Queue TimedMessages = new Queue();
        private bool SettingsAvailable = false;

        // Class to hold timed messages so we can queue them
        private class MsgClass
        {
            public readonly string MsgLine1;
            public readonly string MsgLine2;
            public readonly int ShowTime;

            public MsgClass(string line1, string line2, int showtime)
            {
                MsgLine1 = line1;
                MsgLine2 = line2;
                ShowTime = showtime;
            }
        }


        public DisplayBufferStruct DisplayBuffer = new DisplayBufferStruct();
        public virtual void SetBacklight(int PercentOn) {; }
        public virtual void SetBias(int Percent) {; }
        public virtual void FixIO() {; }

        // Call when settings are available from SD
        public void EnableTask()
        {
            SettingsAvailable = true;
        }

        protected virtual void PreInit()
        {
            ClearDisplay();
        }
        public virtual void ReInit() {; }


        public LcdDisplay(int Rows, int Columns, int Row1Start, int Row2Start)
        {
            Configure(Rows, Columns, Row1Start, Row2Start);
        }
        public LcdDisplay(int Rows, int Columns)
        {
            Configure(Rows, Columns, NotApplicable, NotApplicable);
        }
        private void Configure(int Rows, int Columns, int Row1Start, int Row2Start)
        {
            DisplayBuffer.Rows = Rows;
            DisplayBuffer.Columns = Columns;
            DisplayBuffer.Row = new DisplayRow[Rows];
            for (int iRow = 0; iRow < Rows; iRow++)
                DisplayBuffer.Row[iRow].Characters = new byte[Columns];
            DisplayBuffer.RowStartAddress = new int[Rows];
            DisplayBuffer.RowStartAddress[0] = Row1Start;
            DisplayBuffer.RowStartAddress[1] = Row2Start;

            _TimeMessageLine1 = new byte[Columns];
            _TimeMessageLine2 = new byte[Columns];
            for (int i = 0; i < _TimeMessageLine1.Length; i++)
                _TimeMessageLine2[i] = _TimeMessageLine1[i] = (byte)' ';  // Set all to spaces   

            AltLine1 = new byte[Columns];
            AltLine2 = new byte[Columns];
            for (int i = 0; i < AltLine1.Length; i++)
                AltLine2[i] = AltLine1[i] = (byte)' ';  // Set all to spaces   
        }
        public struct CursorPosition
        {
            public int Row;
            public int Column;
            public CursorPosition(int Row, int Column)
            {
                this.Row = Row;
                this.Column = Column;
            }
        }
        public struct DisplayRow
        {
            public byte[] Characters;
        }
        public struct DisplayBufferStruct
        {
            public int Rows;
            public int Columns;
            public DisplayRow[] Row;
            public int[] RowStartAddress;
            public byte CursorCol; // DAV
            public byte CursorRow;
        }
        public bool ShowCursor
        {
            get { return _showCursor; }
            set
            {
                if (_showCursor != value)
                {
                    _showCursor = value;
                    UpdateCursor();
                }
            }
        }
        public abstract void Initialize();

        public void ShowTimedMessage(string MsgLine1)
        {
            ShowTimedMessage(MsgLine1, string.Empty, GlobalConsts.MESSAGE_LINGER_SECONDS);
        }
        public void ShowTimedMessage(string MsgLine1, int HoldSeconds)
        {
            ShowTimedMessage(MsgLine1, string.Empty, HoldSeconds);
        }
        public void ShowTimedMessage(string MsgLine1, string MsgLine2)
        {
            ShowTimedMessage(MsgLine1, MsgLine2, GlobalConsts.MESSAGE_LINGER_SECONDS);
        }

        public void ShowTimedMessage(string MsgLine1, string MsgLine2, int HoldSeconds)
        {
            TimedMessages.Enqueue(new MsgClass(MsgLine1, MsgLine2, HoldSeconds));
        }

        private void UnpackTimedMessage(string MsgLine1, string MsgLine2, int HoldSeconds)
        {
            byte[] enc = Encoding.UTF8.GetBytes((MsgLine1 + "                ").Left(16));
            Array.Copy(enc, _TimeMessageLine1, enc.Length);
            enc = Encoding.UTF8.GetBytes((MsgLine2 + "                ").Left(16));
            Array.Copy(enc, _TimeMessageLine2, enc.Length);

            _messageExpiry = DateTime.Now + new TimeSpan(TimeSpan.TicksPerSecond * HoldSeconds);
        }

        // Cancel current and queued timed messages
        public void CancelTimedMessages()
        {
            TimedMessages.Clear();
            CancelTimedMessage();
        }

        // Cancel current timed message
        public void CancelTimedMessage()
        {
            _messageExpiry = DateTime.MinValue;
        }

        protected void DisplayUpdateTask()
        {
            PreInit();

            while (SettingsAvailable == false)
                Thread.Sleep(100);

            ReInit();
            ShowTimedMessage("Booting", "AnodeMeter", 1);
            ShowTimedMessage("C-Born Software", "Built " + Globals.BuildDate.ToString("yyyy-MM-dd"), 3);
            MoveIntoDisplay("Loading Meter", new LcdDisplay.CursorPosition(0, 0));
            MoveIntoDisplay("Configuration", new LcdDisplay.CursorPosition(1, 0));

            for (; ; )
            {
                //                if (DateTime.Now > _messageExpiry)
                //                    _messageExpiry = DateTime.MinValue;
                if (DateTime.Now > _messageExpiry)
                {
                    bool HasMsg = false;
                    if (TimedMessages.Count >= 1)
                    {
                        MsgClass Msg = TimedMessages.Dequeue() as MsgClass;
                        if (Msg != null)
                        {
                            UnpackTimedMessage(Msg.MsgLine1, Msg.MsgLine2, Msg.ShowTime);
                            HasMsg = true;
                        }

                    }
                    if (!HasMsg) _messageExpiry = DateTime.MinValue;
                }


                UpdateDisplay();

                Thread.Sleep(GlobalConsts.DISPLAY_UPDATE_RATE);
            }
        }

        public int SetScreen(int ScreenNum)
        {
            int old = ScreenNum;
            CurrScreen = ScreenNum;
            return old;
        }

        protected virtual void UpdateDisplay()
        {
        }
        protected virtual void DisplayNullsAsSpaces()
        {
            for (int iRow = 0; iRow < DisplayBuffer.Rows; iRow++)
            {
                for (int iChar = 0; iChar < DisplayBuffer.Columns; iChar++)
                {
                    if (DisplayBuffer.Row[iRow].Characters[iChar] == 0)
                        DisplayBuffer.Row[iRow].Characters[iChar] = 32;   //Space
                }
            }
        }
        public abstract void AddNewAnalogReading(double value);

        public bool BlinkCursor
        {
            get { return _blinkCursor; }
            set
            {
                if (_blinkCursor != value)
                {
                    _blinkCursor = value;
                    UpdateCursor();
                }
            }
        }
        public bool Visible
        {
            get { return _visible; }
            set
            {
                if (_visible != value)
                {
                    _visible = value;
                    UpdateCursor();
                }
            }
        }
        public bool Backlight
        {
            get { return _backlight; }
            set
            {
                if (_backlight != value)
                {
                    _backlight = value;
                    UpdateCursor();
                }
            }
        }
        public virtual void HomeCursor()
        {
        }
        public virtual void UpdateCursor()
        {

        }
        public virtual void SetBlinkCursor(byte col, byte row, bool blink) { }

        public void MoveIntoDisplay(string TextToDisplay, LcdDisplay.CursorPosition Position)
        {
            byte[] LcdDisplayCodes = Encoding.UTF8.GetBytes(TextToDisplay);
            int MaxSize = DisplayBuffer.Columns - Position.Column;
            int copySize = LcdDisplayCodes.Length > MaxSize ? MaxSize : LcdDisplayCodes.Length;

            if (copySize > 0)
                Array.Copy(LcdDisplayCodes, 0, DisplayBuffer.Row[Position.Row].Characters, Position.Column, copySize);
        }
        public void MoveIntoDisplay(SpecialLCDCharacters SpecialCharacter, LcdDisplay.CursorPosition Position)
        {
            DisplayBuffer.Row[Position.Row].Characters[Position.Column] = (byte)SpecialCharacter;
        }

        public void ClearDisplay()
        {
            for (int row = 0; row < DisplayBuffer.Row.Length; ++row)
            {
                for (int col = 0; col < DisplayBuffer.Row[row].Characters.Length; ++col)
                {
                    DisplayBuffer.Row[row].Characters[col] = 32;
                }
            }
        }

        static void AltMessage(string Msg, byte[] AltLine)
        {
            if (Msg.Length > 0 && Msg.Length <= 16)
            {
                byte[] enc = Encoding.UTF8.GetBytes(Msg);
                Array.Copy(enc, AltLine, enc.Length);
            }
        }

        public void AltMessages(string MsgLine1, string MsgLine2)
        {
            AltMessage(MsgLine1, AltLine1);
            AltMessage(MsgLine2, AltLine2);
        }
    }
}