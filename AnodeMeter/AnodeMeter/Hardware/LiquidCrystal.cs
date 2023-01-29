using System;
//using System.Text;
using System.Threading;
//using Microsoft.SPOT.Hardware;
using AnodeMeter;
using AnodeMeter.Common;
//using GHI.Premium.Hardware;
//using Microsoft.SPOT;
using System.Diagnostics;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Pwm;
using GHIElectronics.TinyCLR.Devices.Dac;

namespace Hardware.LcdCharacterDisplay
{
    public class LiquidCrystal : LcdDisplay
    {

        private DacChannel LCDBias;
        private GpioPin _rsPort;
        private GpioPin _enablePort;
        private GpioPin[] _dataPorts;
        private const int NumberOfLCDRows = 2;
        private const int NumberOfLCDColumns = 16;
        private const byte LcdMemoryRow1Start = 0x00;
        private const byte LcdMemoryRow2Start = 0x40;
        private PwmChannel _backlightLED = null;
        private double MaximumBiasValue = 0.57; //0.45;
        private double MinimuBiasValue = 0.0; //0.02;

        const GpioPinValue PinHi = GpioPinValue.High;
        const GpioPinValue PinLo = GpioPinValue.Low;

        public delegate void TimerUpdate();
        public TimerUpdate LEDFlashHandler { get; set; }
        //private Timer UpdateDisplayTimer = null;
        private Thread DisplayUpdateThread = null;
        private byte[][] dispBuf = new byte[2][];

        //static readonly byte[] row_offsets = new byte[] { 0x00, 0x40, 0x14, 0x54 };

        [Flags]
        public enum LcdCommand : byte
        {
            LCD_CLEARDISPLAY = 0x01,
            LCD_RETURNHOME = 0x02,
            LCD_ENTRYMODESET = 0x04,
            LCD_DISPLAYCONTROL = 0x08,
            LCD_CURSORSHIFT = 0x10,
            LCD_FUNCTIONSET = 0x20,
            LCD_SETCGRAMADDR = 0x40,
            LCD_SETDDRAMADDR = 0x80,
            LCD_ENTRYRIGHT = 0x00,
            LCD_ENTRYLEFT = 0x02,
            LCD_ENTRYSHIFTINCREMENT = 0x01,
            LCD_ENTRYSHIFTDECREMENT = 0x00,
            LCD_DISPLAYON = 0x04,
            LCD_DISPLAYOFF = 0x00,
            LCD_CURSORON = 0x02,
            LCD_CURSOROFF = 0x00,
            LCD_BLINKON = 0x01,
            LCD_BLINKOFF = 0x00,
            //LCD_DISPLAYMOVE = 0x08,
            //LCD_CURSORMOVE = 0x00,
            //LCD_MOVERIGHT = 0x04,
            //LCD_MOVELEFT = 0x00,
            LCD_4BITMODE = 0x00,
            //LCD_8BITMODE = 0x10,
            //LCD_1LINE = 0x00,
            LCD_2LINE = 0x08,
            LCD_5x8DOTS = 0x00,
            //LCD_5x10DOTS = 0x04,
        }
#if false
        // Define the pins based on the schematic included with this project (Schematic.png)
        GpioController gpio = GpioController.GetDefault();

        GpioPin RS = IOMap.RS;
        GpioPin Enable = IOMap.Enable;
        GpioPin LCD_Data_4 = IOMap.LCD_Data_4;
        GpioPin LCD_Data_5 = IOMap.LCD_Data_5;
        GpioPin LCD_Data_6 = IOMap.LCD_Data_6;
        GpioPin LCD_Data_7 = IOMap.LCD_Data_7;
#endif
        public LiquidCrystal()
            : base(NumberOfLCDRows, NumberOfLCDColumns, LcdMemoryRow1Start, LcdMemoryRow2Start)
        {
            dispBuf[0] = new byte[16];
            dispBuf[1] = new byte[16];

            LEDFlashHandler = null;
        }

        protected override void PreInit()
        {
            var gpio = GpioController.GetDefault();
            var op = GpioPinDriveMode.Output;
            CreateLCDBiasControlChannel();
            CreateBackLightControlChannel();

            _rsPort = gpio.OpenPin(IOMap.RS);
            _rsPort.SetDriveMode(op);
            _enablePort = gpio.OpenPin(IOMap.Enable);
            _enablePort.SetDriveMode(op);

            _dataPorts = new GpioPin[4] { gpio.OpenPin(IOMap.LCD_Data_4), gpio.OpenPin(IOMap.LCD_Data_5), gpio.OpenPin(IOMap.LCD_Data_6), gpio.OpenPin(IOMap.LCD_Data_7) };
            foreach (GpioPin p in _dataPorts)
                p.SetDriveMode(op);


            Thread.Sleep(50);                       // LCD controller needs some warm-up time
            for (int i = 0; i < 3; i++)
            {            // we start in 8bit mode, try to set 4 bit mode
                SendCommand((LcdCommand)0x03);
                Thread.Sleep(5);                    // wait min 4.1ms
            }
            SendCommand((LcdCommand)0x02);          // set to 4-bit interface

            SendCommand(LcdCommand.LCD_FUNCTIONSET | LcdCommand.LCD_4BITMODE | LcdCommand.LCD_2LINE | LcdCommand.LCD_5x8DOTS); // set # lines, font size, etc.
            SendCommand(LcdCommand.LCD_ENTRYMODESET | LcdCommand.LCD_ENTRYLEFT | LcdCommand.LCD_ENTRYSHIFTDECREMENT);
            UpdateCursor();        // turn the display on with no cursor or blinking default
            Clear_Display();        // clear it off
            HomeCursor();
            CreateSpecialCharacters();
            UpdateDisplay();
        }
        //TODO DAV Fix for broken Vn 4.3 SDK that loses some IO config after hibernate.
        // Remove when GHI fixes this
        public override void FixIO()
        {
            var gpio = GpioController.GetDefault();
            _dataPorts[2].Dispose();
            _dataPorts[2] = gpio.OpenPin(IOMap.LCD_Data_6);
            _dataPorts[2].SetDriveMode(GpioPinDriveMode.Output);
        }

        // Called after SD defaults read in
        public override void ReInit()
        {
            SetBias(0);     // See if this helps new displays start. DAV 13DEC21
            Thread.Sleep(5);
            SetBias(Globals.LCDBiasPC);
        }

        public override void Suspend()
        {
            DisplayUpdateThread?.Suspend();
        }
        public override void Resume()
        {
            DisplayUpdateThread?.Resume();
        }
        private void CreateLCDBiasControlChannel()
        {
            try
            {
                var dac = DacController.GetDefault();
                LCDBias = dac.OpenChannel(IOMap.LCDBias);
                //LCDBias = new AnalogOutput(IOMap.LCDBias);
                ReInit();
                //SetBias(Globals.LCDBiasPC);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
        private void CreateBackLightControlChannel()
        {
            var controller2 = PwmController.FromName(SC20260.Timer.Pwm.Controller2.Id);
            double freq = 10000;
            double DutyCycle = 0.1;
            controller2.SetDesiredFrequency(freq);
            _backlightLED = controller2.OpenChannel(IOMap.BackLight);
            _backlightLED.SetActiveDutyCyclePercentage(DutyCycle);
            //_backlightLED = new PWM(IOMap.BackLight, freq, DutyCycle, false);
            _backlightLED.Start();
        }
        public override void SetBacklight(int PercentOn)
        {
            double DutyCycle = ((double)PercentOn / 100);
            _backlightLED.SetActiveDutyCyclePercentage(DutyCycle);
        }

        public override void SetBias(int Percent)
        {
            double BiasRange = MaximumBiasValue - MinimuBiasValue;
            double Bias = MinimuBiasValue + ((double)Percent / 100) * BiasRange;
            LCDBias.WriteValue(Bias);
        }

        public override void SetBlinkCursor(byte col, byte row, bool blink)
        {
            //            int[] row_offsets = { 0x00, 0x40, 0x14, 0x54 };  
            //            SendCommand(LcdCommand.LCD_SETDDRAMADDR | (LcdCommand)(col + row_offsets[row]));
            DisplayBuffer.CursorCol = col;
            DisplayBuffer.CursorRow = row;
            base.BlinkCursor = blink;
            //            _displaycontrol |= LCD_BLINKON;
            //            command(LCD_DISPLAYCONTROL | _displaycontrol);
        }

        private void SetCursor(byte col, byte row)
        {
            int[] row_offsets = { 0x00, 0x40, 0x14, 0x54 };
            SendCommand(LcdCommand.LCD_SETDDRAMADDR | (LcdCommand)(col + row_offsets[row]));
        }

        public void Clear_Display()
        {
            base.ClearDisplay();
#if false
            byte Space = (Encoding.UTF8.GetBytes(" "))[0];
            for(int iRow = 0; iRow < DisplayBuffer.Rows; iRow++) {
                for(int iCol = 0; iCol < DisplayBuffer.Columns; iCol++)
                    DisplayBuffer.Row[iRow].Characters[iCol] = Space;
            }
#endif
            SendCommand(LcdCommand.LCD_CLEARDISPLAY);
            Thread.Sleep(2); // this command takes a long time!
        }
        public override void HomeCursor()
        {
            SendCommand(LcdCommand.LCD_RETURNHOME);
            Thread.Sleep(2); // this command takes a long time!
        }

        protected override void UpdateDisplay()
        {

            base.DisplayNullsAsSpaces();

            if (_messageExpiry != DateTime.MinValue)
            {
                dispBuf[0] = _TimeMessageLine1;
                dispBuf[1] = _TimeMessageLine2;
            }
            else
            {
                if (CurrScreen == 0)
                {
                    dispBuf[0] = DisplayBuffer.Row[0].Characters;
                    dispBuf[1] = DisplayBuffer.Row[1].Characters;
                }
                else
                {
                    dispBuf[0] = AltLine1;
                    dispBuf[1] = AltLine2;
                }
            }

            for (int iRow = 0; iRow < DisplayBuffer.Rows; iRow++)
            {
                SendCommand(LcdCommand.LCD_SETDDRAMADDR | (LcdCommand)DisplayBuffer.RowStartAddress[iRow]);  // 1,0
                for (int iCol = 0; iCol < DisplayBuffer.Columns; iCol++)
                    Send(dispBuf[iRow][iCol], true, Backlight);
            }

            if (DisplayBuffer.CursorCol > 0)
                SetCursor(DisplayBuffer.CursorCol, DisplayBuffer.CursorRow); // DAV

            // If the LED Flash time function has been hooked, then call it
            if (LEDFlashHandler != null)
                LEDFlashHandler();

            base.UpdateDisplay();
        }
        public override void UpdateCursor()
        {
            LcdCommand command = LcdCommand.LCD_DISPLAYCONTROL;
            command |= (Visible) ? LcdCommand.LCD_DISPLAYON : LcdCommand.LCD_DISPLAYOFF;
            command |= (ShowCursor) ? LcdCommand.LCD_CURSORON : LcdCommand.LCD_CURSOROFF;
            command |= (BlinkCursor) ? LcdCommand.LCD_BLINKON : LcdCommand.LCD_BLINKOFF;
            SendCommand((LcdCommand)command);       //NOTE: backlight is updated with each command
        }
        public void WriteByte(byte data)
        {
            Send(data, true, Backlight);
        }
        public void SendCommand(LcdCommand data)
        {
            Send((byte)data, false, Backlight);
        }
        public override void Initialize()
        {
            if (DisplayUpdateThread == null)
            {
                DisplayUpdateThread = new Thread(DisplayUpdateTask);
                DisplayUpdateThread.Start();
            }
        }
        public void Send(byte value, bool mode, bool backlight)
        {
            _rsPort.Write(mode ? PinHi : PinLo);
            Write4Bits((byte)(value >> 4));
            Write4Bits(value);
        }
        private void Write4Bits(byte value)
        {
            for (int i = 0; i < 4; i++)
            {
                _dataPorts[i].Write(((value >> i) & 0x01) == 0x01 ? PinHi : PinLo);
            }
            _enablePort.Write(PinLo);
            _enablePort.Write(PinHi);  // enable pulse must be >450ns
            _enablePort.Write(PinLo); // commands need > 37us to settle
        }
        private void CreateSpecialCharacters()
        {
#if false
            CreateChar(SpecialLCDCharacters.batteryMt, new byte[] { 0x0E, 0x0E, 0x1F, 0x11, 0x11, 0x11, 0x11, 0x1F });
            CreateChar(SpecialLCDCharacters.batteryQuart, new byte[] { 0x0E, 0x0E, 0x1F, 0x11, 0x11, 0x11, 0x1F, 0x1F });
            CreateChar(SpecialLCDCharacters.batteryHalf, new byte[] { 0x0E, 0x0E, 0x1F, 0x11, 0x11, 0x1F, 0x1F, 0x1F });
            CreateChar(SpecialLCDCharacters.battery3Quart, new byte[] { 0x0E, 0x0E, 0x1F, 0x11, 0x1F, 0x1F, 0x1F, 0x1F });
            CreateChar(SpecialLCDCharacters.batteryFull, new byte[] { 0x0E, 0x0E, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F });
#else
            // At present limited to 7 special characters, out of 8 available.
            // We could do some nice arrows, if had more space.
            // Look at implementing a cache system to allow any 8 on display at once, from a larger set of possibles.
            // DAV
            CreateChar(SpecialLCDCharacters.batteryMt, new byte[] { 0xe, 0x1b, 0x11, 0x11, 0x11, 0x11, 0x1f, 0x0 });
            CreateChar(SpecialLCDCharacters.batteryQuart, new byte[] { 0xe, 0x1b, 0x11, 0x11, 0x11, 0x1f, 0x1f, 0x0 });
            CreateChar(SpecialLCDCharacters.batteryHalf, new byte[] { 0xe, 0x1b, 0x11, 0x11, 0x1f, 0x1f, 0x1f, 0x0 });
            CreateChar(SpecialLCDCharacters.battery3Quart, new byte[] { 0xe, 0x1b, 0x11, 0x1f, 0x1f, 0x1f, 0x1f, 0x0 });
            CreateChar(SpecialLCDCharacters.batteryFull, new byte[] { 0xe, 0x1f, 0x1f, 0x1f, 0x1f, 0x1f, 0x1f, 0x0 });
            CreateChar(SpecialLCDCharacters.Tick, new byte[] { 0x0, 0x01, 0x03, 0x16, 0x1c, 0x08, 0x00, 0x0 });
            CreateChar(SpecialLCDCharacters.Down, new byte[] { 0x0, 0x00, 0x00, 0x00, 0x11, 0x0a, 0x04, 0x0 });   // Opposite of carat, for now
#endif
        }
        // Up to eight characters of 5x8 pixels are supported (numbered 0 to 7). 
        // The appearance of each custom character is specified by an array of eight bytes, one for each row.
        // The five least significant bits of each byte determine the pixels in that row. 
        // To display a custom character on the screen, call WriteByte() and pass its number. 

        public void CreateChar(SpecialLCDCharacters Lcdlocation, byte[] charmap)
        {
            int location = (int)Lcdlocation;
            location &= 0x7; // we only have 8 locations 0-7
            SendCommand((LcdCommand.LCD_SETCGRAMADDR | (LcdCommand)(location << 3)));
            Thread.Sleep(20); // this command takes a long time!
            for (int i = 0; i < 8; i++)
            {
                Send(charmap[i], true, Backlight);
            }
        }
        public override void AddNewAnalogReading(double value)
        {
        }

    }
}