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
        private bool _bFinishedLastPass = true;
        private TimeSpan tsBattChkInterval = new TimeSpan(TimeSpan.TicksPerSecond * 30);

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
            if (_bFinishedLastPass)
            {
                _bFinishedLastPass = false;
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

#if false           
                    /* Errors suspected to have been caused by rentrant timer calls, and may no longer be a problem
                     * Remove test code so can use absolute readings for clamp-drop measurement
                     * If problems come back, add tests back in or find and fix problem
                     * Note that there is still a small reentrancy window in this function call, fix with a mutex ASAP.
                     * TODO: DAV: 21MAY14
                     * ===== */
                    // If input is in between 65..80mV or negative, then we probably have an instance of an infrequent
                    // failure mode, so wait until certain and then close bounce the AI hardware
                    if ((ThisValue > 0.065 && ThisValue < 0.08) || (ThisValue < -0.015))
                    {
                        if (++_successiveCountsInErrorRange > 20)
                        {
                            _ai.Close();
                            _ai = null;
                        }
                    }
                    else
#endif
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
                _bFinishedLastPass = true;
            }
        }
    }
}
