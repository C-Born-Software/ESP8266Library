using System;
using System.Diagnostics;
//using Microsoft.SPOT;
using AnodeMeter.Common;
//using Microsoft.SPOT.Hardware;
//using GHI.Premium.Hardware;

namespace AnodeMeter.Hardware
{
    public class PhysicalButtons : AnodeMeterButtons
    {

        private const int BUTTON_HOLD_MILLISECS = 800;  // Number of milliseconds to detect button hold
        private DateTime _buttonDownTime;

        private HardwareButton UpButton;
        private HardwareButton DownButton;
        private HardwareButton LeftButton;
        private HardwareButton RightButton;
        private HardwareButton CentreButton;

        // Make buttons available to setup code - DAV
        public HardwareButton[] GetButtons()
        {
            HardwareButton[] Buttons = { UpButton, DownButton, LeftButton, RightButton, CentreButton };
            return Buttons;
        }

        public PhysicalButtons()
        {


            UpButton = new HardwareButton(IOMap.UpButton);
            UpButton.OnButtonDown += new HardwareButton.ButtonDownEvent(UpButton_OnButtonDown);
            UpButton.OnButtonUp += new HardwareButton.ButtonUpEvent(UpButton_OnButtonUp);

            DownButton = new HardwareButton(IOMap.DownButton);
            DownButton.OnButtonDown += new HardwareButton.ButtonDownEvent(DownButton_OnButtonDown);
            DownButton.OnButtonUp += new HardwareButton.ButtonUpEvent(DownButton_OnButtonUp);

            CentreButton = new HardwareButton(IOMap.CentreButton);
            CentreButton.OnButtonDown += new HardwareButton.ButtonDownEvent(CentreButton_OnButtonDown);
            CentreButton.OnButtonUp += new HardwareButton.ButtonUpEvent(CentreButton_OnButtonUp);

            LeftButton = new HardwareButton(IOMap.LeftButton);
            LeftButton.OnButtonDown += new HardwareButton.ButtonDownEvent(LeftButton_OnButtonDown);
            LeftButton.OnButtonUp += new HardwareButton.ButtonUpEvent(LeftButton_OnButtonUp);

            RightButton = new HardwareButton(IOMap.RightButton);
            RightButton.OnButtonDown += new HardwareButton.ButtonDownEvent(RightButton_OnButtonDown);
            RightButton.OnButtonUp += new HardwareButton.ButtonUpEvent(RightButton_OnButtonUp);

        }
        private long ElapsedMilliSecs()
        {
            DateTime Now = DateTime.Now;
            long elapsed = (Now - _buttonDownTime).Ticks / TimeSpan.TicksPerMillisecond;
            return elapsed;
        }
        void UpButton_OnButtonUp()
        {
            if (ElapsedMilliSecs() > BUTTON_HOLD_MILLISECS)
                OnButtonChanged(AnodeMeterButtonPress.uphold);
            else
                OnButtonChanged(AnodeMeterButtonPress.up);
        }
        void DownButton_OnButtonUp()
        {
            if (ElapsedMilliSecs() > BUTTON_HOLD_MILLISECS)
                OnButtonChanged(AnodeMeterButtonPress.downhold);
            else
                OnButtonChanged(AnodeMeterButtonPress.down);

        }
        void CentreButton_OnButtonUp()
        {
            if (ElapsedMilliSecs() > BUTTON_HOLD_MILLISECS)
                OnButtonChanged(AnodeMeterButtonPress.centrehold);
            else
                OnButtonChanged(AnodeMeterButtonPress.centre);
        }

        void LeftButton_OnButtonUp()
        {
            if (ElapsedMilliSecs() > BUTTON_HOLD_MILLISECS)
                OnButtonChanged(AnodeMeterButtonPress.lefthold);
            else
                OnButtonChanged(AnodeMeterButtonPress.left);
        }
        void RightButton_OnButtonUp()
        {
            if (ElapsedMilliSecs() > BUTTON_HOLD_MILLISECS)
                OnButtonChanged(AnodeMeterButtonPress.righthold);
            else
                OnButtonChanged(AnodeMeterButtonPress.right);
        }

        void LeftButton_OnButtonDown()
        {
            _buttonDownTime = DateTime.Now;
        }
        void RightButton_OnButtonDown()
        {
            _buttonDownTime = DateTime.Now;
        }

        void UpButton_OnButtonDown()
        {
            _buttonDownTime = DateTime.Now;
        }

        void DownButton_OnButtonDown()
        {
            _buttonDownTime = DateTime.Now;
        }

        void CentreButton_OnButtonDown()
        {
            _buttonDownTime = DateTime.Now;
        }

        private void OnButtonChanged(AnodeMeterButtonPress ambp)
        {
            MeterButtonPressEventArgs e1 = new MeterButtonPressEventArgs(ambp);
            Debug.WriteLine("ButtonPressEvent: " + e1.MeterButton.ToString()); //TODO DAV DEBUG
            base.OnButtonChanged(e1);
        }
    }
}
