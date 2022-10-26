using System;
//using Microsoft.SPOT;
//using Microsoft.SPOT.Hardware;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Pins;

namespace AnodeMeter.Hardware
{
    public class HardwareButton
    {
        public enum ButtonAction { Press, Hold, Release }
        private Thread InputPortSamplingThread;

        private const int SamplingInterval = 100;
        private int HoldCounterThreshold = 2000 / SamplingInterval;
        public bool ActiveLevel { get; private set; }
        private bool previouslyPressed = false;
        private int counter = 0;

        public delegate void ButtonDownEvent();
        public event ButtonDownEvent OnButtonDown;

        public delegate void ButtonUpEvent();
        public event ButtonUpEvent OnButtonUp;

        public delegate void ButtonHoldEvent();
        public event ButtonHoldEvent OnButtonHold;


        private GpioPin ButtonPort;

        //TODO DAV Refactor button handling. Perhaps only one thread for all?
        public bool state;
        public bool bootstate;
        public bool held;
        public bool click;
        public char code;
        byte time;
        //----

        //public HardwareButton(Cpu.Pin port, Port.ResistorMode resistor)
        public HardwareButton(int port)
        {
            // DAV ---
            state = false;
            bootstate = false;
            held = false;
            click = false;
            //code = c;
            time = 0;
            //----
            
            var gpio = GpioController.GetDefault();
            ButtonPort = gpio.OpenPin(port);
            ButtonPort.SetDriveMode(GpioPinDriveMode.InputPullUp);
            ButtonPort.ValueChangedEdge = GpioPinEdge.FallingEdge;
            ButtonPort.ValueChanged += ButtonPort_OnInterrupt;

            //ButtonPort = new InterruptPort(port, false, resistor, Port.InterruptMode.InterruptEdgeHigh);
            //ButtonPort.OnInterrupt += new NativeEventHandler(ButtonPort_OnInterrupt);
            //ButtonPort = new InputPort(port, false, resistor);
            ActiveLevel = true;
            InputPortSamplingThread = new Thread(this.InputPortSamplerWorker);
            InputPortSamplingThread.Start();
        }

        //void ButtonPort_OnInterrupt(uint data1, uint data2, DateTime time)
        void ButtonPort_OnInterrupt(GpioPin sender, GpioPinValueChangedEventArgs e)
        {

        }
        private void InputPortSamplerWorker()
        {
            bool onboot = true; //DAV

            while (true)
            {
                bool State = (ButtonPort.Read() == GpioPinValue.High);
                //Debug.Print(State.ToString());

                // DAV -----
                bool nstate = !State;
                click = false;
                held = false;
                if (state)
                {
                    if (nstate != state)
                        click = true;
                    if (time >= 4)
                        held = true;
                    else
                        ++time;
                }
                else
                    time = 0;

                state = nstate;
                if (onboot)
                {
                    bootstate = state;
                    onboot = false;
                }

                //-----

                if (State != ActiveLevel)
                {
                    if (previouslyPressed)
                    {
                        if (--counter <= 0)
                        {
                            if (OnButtonHold != null)
                                OnButtonHold();
                            counter = HoldCounterThreshold;
                        }
                    }
                    else
                    {
                        if (OnButtonDown != null)
                            OnButtonDown();
                        previouslyPressed = true;
                        counter = HoldCounterThreshold;
                    }
                }
                else if (previouslyPressed)
                {
                    if (OnButtonUp != null)
                        OnButtonUp();
                    counter = 0;
                    previouslyPressed = false;
                }
                Thread.Sleep(SamplingInterval);
            }
        }
    }
}
