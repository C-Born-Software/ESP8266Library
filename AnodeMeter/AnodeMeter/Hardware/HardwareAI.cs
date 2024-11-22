using System;
//using Microsoft.SPOT;
using System.Threading;
using AnodeMeter.Common;

namespace AnodeMeter.Hardware
{
    class HardwareAI : AnalogInput
    {
        private const double _lowValueThreshold = 0.001;
        private HiResADC _ai = null;
        private Timer _tmrAiScan = null;
        //private int _successiveCountsInErrorRange;
        private DateTime _dtLastBatteryVoltageCheck;
        private TimeSpan tsBattChkInterval = new TimeSpan(TimeSpan.TicksPerSecond * 30);
        private int _isRunning = 0;

        public HardwareAI()
        {
            _tmrAiScan = new Timer(ScanInput, null, 0, GlobalConsts.HARDWARE_AI_SCAN_MILLI_SECONDS);
            // _successiveCountsInErrorRange = 0;
            _dtLastBatteryVoltageCheck = DateTime.MinValue;
        }

        private void ScanInput(object state)
        {
            // AI sampling is driven by a timer which will call this routine again,
            // even if it hasn't finish the previous pass. This boolean is used to skip
            // subsequent processing if the previous pass hasn't yet completed.
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0)
            {
                try
                {
                    if (_ai == null)
                    {
                        // _successiveCountsInErrorRange = 0;

                        _ai = new HiResADC();

                        // Configure Channel-One for Anode-Drop input
                        _ai.SetChannelConfig(HiResADC.InputChannel.Ch1,
                                             HiResADC.Resolution.SixteenBits,
                                             HiResADC.ConversionMode.Continuous,
                                             HiResADC.ProgGain.x1);

                    }

                    double ThisValue = _ai.ReadVolts(HiResADC.InputChannel.Ch1);

                    {
                        // Check if this reading is suspiciously low
                        if (ThisValue.Abs() < _lowValueThreshold)
                        {
                            // Reading suspect - reset ADC
                            _ai.Close();
                            _ai = null;
                        }
                        //_successiveCountsInErrorRange = 0;

                        AnalogInputEventArg e1 = new AnalogInputEventArg(ThisValue);
                        base.OnAnalogValueRead(e1);
                    }
                }
                catch (Exception x)
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "HardwareAI::ScanInput", x.Message, "AI Err");
                    if (_ai != null)
                    {
                        _ai.Close();
                        _ai = null;
                    }
                    Thread.Sleep(1000); // Don't want to flood system with error messages
                }
                finally
                {
                    Interlocked.Exchange(ref _isRunning, 0);
                }
            }
        }
    }
}
