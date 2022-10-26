using System;
//using Microsoft.SPOT;
//using Microsoft.SPOT.Hardware;
//using GHI.Premium.Hardware;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Pwm;

namespace AnodeMeter.Hardware
{
    class PhysicalLED : Common.LED
    {
        PwmChannel GLedFader;
        PwmChannel RLedFader;
        protected Timer _tmrOnOff = null;

        public PhysicalLED()
        {
            var controller = PwmController.FromName(SC20260.Timer.Pwm.Controller3.Id);
            controller.SetDesiredFrequency(10000);

            _tmrOnOff = new Timer(ExecuteFlashTransitions, null, 0, GlobalConsts.LED_UPDATE_RATE);

            GLedFader = controller.OpenChannel(SC20260.Timer.Pwm.Controller3.PB0);
            GLedFader.SetActiveDutyCyclePercentage(0.25);
            GLedFader.Start();
            RLedFader = controller.OpenChannel(SC20260.Timer.Pwm.Controller3.PB1);
            RLedFader.SetActiveDutyCyclePercentage(0.0);
            RLedFader.Start();
        }
        protected override void UpdateDevice(LedState state)
        {
            // We can actually run up to 50% duty cycle on both LEDs at once
            // However even 50% is way too bright. And we don't have a use for both LEDS at once..
            // At this stage just implement on-off control at global duty cycle, one LED at a time.
            if (state._greenPercent > 0)
            {
                //GLedFader.DutyCycle = (double)Globals.GLedBright / 100;
                GLedFader.SetActiveDutyCyclePercentage((double)Globals.GLedBright / 100);
                GLedFader.Start();
                RLedFader.Stop();
            }
            else if (state._redPercent > 0)
            {
                //RLedFader.DutyCycle = (double)Globals.RLedBright / 100;
                RLedFader.SetActiveDutyCyclePercentage((double)Globals.RLedBright / 100);
                RLedFader.Start();
                GLedFader.Stop();
            }
            else
            {
                GLedFader.Stop();
                RLedFader.Stop();
            }
        }
    }
}
