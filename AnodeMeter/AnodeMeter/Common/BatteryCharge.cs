using System;
//using Microsoft.SPOT;

namespace AnodeMeter.Common
{
    public abstract class BatteryCharge
    {
        private const double MAX_BATT_VOLTS = 4.0;
        private const double MIN_BATT_VOLTS_FOR_USE = 3.6;
        private const double FILTER_ALPHA = 0.8;
        private double _batteryVoltageEstimate = 0.0;

        private const double THREE_QUARTERS_FULL = 3.9;
        private const double HALF_FULL = 3.85;
        private const double QUARTER_FULL = 3.71;


        private DateTime _previousEstimateTime;
        private TimeSpan _tsRecent = new TimeSpan(TimeSpan.TicksPerMinute * 3);

        public int GetStateOfChargePercent()
        {

            double CurrentBattVoltage = GetBatteryVoltage();
            if (_batteryVoltageEstimate != 0.0 && (DateTime.Now - _previousEstimateTime < _tsRecent || CurrentBattVoltage > _batteryVoltageEstimate))
            {
                _batteryVoltageEstimate = _batteryVoltageEstimate * FILTER_ALPHA + (1 - FILTER_ALPHA) * CurrentBattVoltage;
            }
            else
                _batteryVoltageEstimate = CurrentBattVoltage;

            _previousEstimateTime = DateTime.Now;

            return PercentChargeFromNonLinearVoltage(_batteryVoltageEstimate);
        }

        private int PercentChargeFromNonLinearVoltage(double BatteryVoltage)
        {
            //// Assume linear relationship between voltage and charge
            //SOC = 100.0 * (1.0 - (MAX_BATT_VOLTS - _batteryVoltageEstimate) / (MAX_BATT_VOLTS - MIN_BATT_VOLTS_FOR_USE));
            //// Limit Estimation Between 0 and 100 percent
            //return SOC > 100.0 ? 100.0 : SOC < 0.0 ? 0.0 : SOC;

            int PercentCharge;

            if (BatteryVoltage > THREE_QUARTERS_FULL)
                PercentCharge = 100;
            else if (BatteryVoltage > HALF_FULL)
                PercentCharge = 75;
            else if (BatteryVoltage > QUARTER_FULL)
                PercentCharge = 25;
            else
                PercentCharge = 0;

            return PercentCharge;
        }

        abstract protected double GetBatteryVoltage();

        public double BatteryVoltage { get { return GetBatteryVoltage(); } }
    }
}
