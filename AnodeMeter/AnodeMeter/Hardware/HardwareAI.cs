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
        private int _errorCount = 0;
        private const int MAX_RETRIES_BEFORE_BACKOFF = 5;
        // With a 70ms scan timer, we need about 857 ticks for a ~60-second backoff (60000 / 70)
        private const int BACKOFF_RECOVERY_ATTEMPT_TICKS = 60000 / GlobalConsts.HARDWARE_AI_SCAN_MILLI_SECONDS;

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
                    // If we are in a backoff state, do nothing.
                    if (_errorCount > MAX_RETRIES_BEFORE_BACKOFF)
                    {
                        // Approximately every minute we try to recover.
                        if ((_errorCount++ % BACKOFF_RECOVERY_ATTEMPT_TICKS) != 0)
                        {
                            return; // Skip this cycle
                        }
                        // If we fall through, we are attempting a recovery.
                    }

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
                    // Successful read, reset the error counter.
                    _errorCount = 0;

                    AnalogInputEventArg e1 = new AnalogInputEventArg(ThisValue);
                    base.OnAnalogValueRead(e1);
                }
                catch (System.IO.IOException x)
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "HardwareAI::ScanInput", x.Message, "AI I/O Err");
                    _errorCount++;
                    if (_ai != null)
                    {
                        // Don't destroy the object. Just reset the underlying connection.
                        _ai.Reset();
                    }
                    //Thread.Sleep(1000); // Don't want to flood system with error messages
                }
                catch (Exception x)
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "HardwareAI::ScanInput", x.Message, "AI Critical Err");
                    _errorCount++;
                    // For a critical or unknown error (like OutOfMemory),
                    // it's safer to destroy and recreate the object on the next recovery attempt.
                    if (_ai != null)
                    {
                        _ai.Close();
                        _ai = null;
                    }
                    //Thread.Sleep(1000); // Don't want to flood system with error messages
                }
                finally
                {
                    Interlocked.Exchange(ref _isRunning, 0);
                }
            }
        }
    }
}
