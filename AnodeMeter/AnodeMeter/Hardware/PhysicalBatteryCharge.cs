using System;
using System.Threading;
//using Microsoft.SPOT;
using AnodeMeter.Common;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Adc;
//using GHI.Premium.Hardware;
//using Microsoft.SPOT.Hardware;

namespace AnodeMeter.Hardware
{
    public class PhysicalBatteryCharge : BatteryCharge
    {
        private Thread PowerMonitorThread;
        private double _battVolts = 0;

        private float BattVolts, AvBattVolts = 4.0f;
        public float vRef, V3p3;
        public bool PowerDownRequired = false;
        private int PowerDownCount = 0;
        //Microsoft.SPOT.Hardware.AnalogInput VBatt;
        //Microsoft.SPOT.Hardware.AnalogInput VRef2p5;

        public PhysicalBatteryCharge()
        {

            //VBatt = new Microsoft.SPOT.Hardware.AnalogInput(Microsoft.SPOT.Hardware.Cpu.AnalogChannel.ANALOG_1);
            //VRef2p5 = new Microsoft.SPOT.Hardware.AnalogInput(Microsoft.SPOT.Hardware.Cpu.AnalogChannel.ANALOG_5);

            PowerMonitorThread = new Thread(this.PowerMonitor);
            PowerMonitorThread.Start();
        }

        override protected double GetBatteryVoltage()
        {
            //return _battVolts;
            return AvBattVolts;
            //return PowerMonitor();
        }
        // This method is called when a raw AI value has been read
        public void OnBattVoltageReceived(object sender, AnalogInputEventArg e)
        {
            _battVolts = e.AnalogValue;
        }

        private void PowerMonitor()
        {
            // Battery Voltage x 0.5 (divider on input) 10 bit (0-1023) ADC (0-4096 on G120), full scale 3.3V 
            //Microsoft.SPOT.Hardware.AnalogInput VBatt = new Microsoft.SPOT.Hardware.AnalogInput(IOMap.VBatt);
            //Microsoft.SPOT.Hardware.AnalogInput VRef2p5 = new Microsoft.SPOT.Hardware.AnalogInput(IOMap.VRef2p5);
            
            var adc = AdcController.FromName(SC20260.Adc.Controller1.Id);
            
            AdcChannel VBatt = adc.OpenChannel(IOMap.VBatt);
            var adc3 = AdcController.FromName(SC20260.Adc.Controller3.Id);
            AdcChannel VRef2p5 = adc3.OpenChannel(IOMap.VRef2p5);

            // ADC is very noisy on the SC20260 - don't know if this does anything to help though...
            VRef2p5.SamplingTime =  VBatt.SamplingTime = TimeSpan.FromTicks(126); // 126 ticks = 12664nS according to GI specs

            // On EMX default precision was 10 bits (1024)
            // On G120 it is 12 bits (4096)
            const double VBatt_Scale = 2 * 3.3;
            const double VRef2p5_Scale = 3.3;

            AvBattVolts = BattVolts = (float) (VBatt.ReadRatio() * VBatt_Scale);

            while (true)
            {
                BattVolts = (float)(VBatt.ReadRatio() * VBatt_Scale);
                vRef = (float)(VRef2p5.ReadRatio() * VRef2p5_Scale);

                if ((vRef > 2) && (vRef < 3))
                {
                    float scale = 2.5f / vRef;
                    V3p3 = 3.3f * scale; // Calculate 3.3V from 2.5V ref reading
                    BattVolts *= scale;
                }
                else
                    V3p3 = 3.3f; // Assume no 2.5V connected

                if (V3p3 >= 3.2)
                {
                    PowerDownCount = 0;
                    PowerDownRequired = false;
                }
                else
                {
                    if (PowerDownCount++ > 50)
                    {
                        // Signal we need Power Down. Close files, etc first
                        // Could be an event.
                        // Also should wake up every hour or so (perhaps depending on voltage) when in Hibernate,
                        // to check power level, and sleep once becomes critical
                        PowerDownRequired = true;
                    }
                }

                //AvBattVolts += ((BattVolts - AvBattVolts) / 8);
                AvBattVolts += ((BattVolts - AvBattVolts) / 32); // Increased filtering to reduce noise

                Globals.gBattVolts = BattVolts;
                Globals.gAvBattVolts = AvBattVolts;

                if (AvBattVolts <= Globals.BattVoltCritical)
                    Globals.PowerState = Globals.PowerStates.Critical;
                else if (Globals.PowerState != Globals.PowerStates.BatteryTest)
                {
                    if (AvBattVolts >= Globals.BattVoltLow)
                        Globals.PowerState = Globals.PowerStates.Normal;
                    else if (AvBattVolts >= Globals.BattVoltVeryLow)
                        Globals.PowerState = Globals.PowerStates.LowPower;
                    else if (AvBattVolts >= Globals.BattVoltCritical)
                        Globals.PowerState = Globals.PowerStates.VeryLowPower;
                    else Globals.PowerState = Globals.PowerStates.Critical;
                }

                Thread.Sleep(100);
                //return AvBattVolts;
            }
        }
    }
}
