using System;
using System.Threading;
using AnodeMeter.Common;

namespace AnodeMeter.Hardware
{
    class HardwareAI : AnalogInput, IDisposable
    {
        private const double _lowValueThreshold = 0.001;
        private HiResADC _ai = null;
        private Thread _scanInputThread = null;
        private bool _exitThread = false;
        //private int _successiveCountsInErrorRange;
        private DateTime _dtLastBatteryVoltageCheck;
        private TimeSpan tsBattChkInterval = new TimeSpan(TimeSpan.TicksPerSecond * 30);
        private int _errorCount = 0;
        private const int MAX_RETRIES_BEFORE_BACKOFF = 5;
        // With a 70ms scan timer, we need about 857 ticks for a ~60-second backoff (60000 / 70)
        private const int BACKOFF_RECOVERY_ATTEMPT_TICKS = 60000 / GlobalConsts.HARDWARE_AI_SCAN_MILLI_SECONDS;

        public HardwareAI()
        {
            _scanInputThread = new Thread(ScanInputLoop);
            _scanInputThread.Priority = ThreadPriority.BelowNormal;
            _scanInputThread.Start();
            // _successiveCountsInErrorRange = 0;
            _dtLastBatteryVoltageCheck = DateTime.MinValue;
        }

        private void ScanInputLoop()
        {
            long startTicks;

            while (!_exitThread)
            {
                startTicks = DateTime.Now.Ticks;

                try
                {
                    // If we are in a backoff state, do nothing for this cycle.
                    if (_errorCount > MAX_RETRIES_BEFORE_BACKOFF)
                    {
                        // Approximately every minute we will attempt to recover.
                        if ((_errorCount++ % BACKOFF_RECOVERY_ATTEMPT_TICKS) != 0)
                        {
                            // Continue to the sleep portion of the loop.
                            goto EndOfLoop;
                        }
                        // If we fall through, we are attempting a recovery.
                    }

                    if (_ai == null)
                    {
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
                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "HardwareAI::ScanInputLoop", x.Message, "AI I/O Err");
                    _errorCount++;
                    // The HiResADC class now handles its own internal reset on IOException.
                }
                catch (Exception x)
                {
                    Logging.IssueEvent(Logging.ErrSeverity.Severe, "HardwareAI::ScanInputLoop", x.Message, "AI Critical Err");
                    _errorCount++;
                    // For a critical or unknown error (like OutOfMemory),
                    // it's safer to destroy and recreate the object on the next recovery attempt.
                    if (_ai != null)
                    {
                        _ai.Close();
                        _ai = null;
                    }
                }

            EndOfLoop:
                // Calculate the time remaining in the interval and sleep.
                var elapsed = (int)((DateTime.Now.Ticks - startTicks) / TimeSpan.TicksPerMillisecond);
                var sleepTime = GlobalConsts.HARDWARE_AI_SCAN_MILLI_SECONDS - elapsed;

                if (sleepTime > 0)
                {
                    Thread.Sleep(sleepTime);
                }
            }
        }

        public void Dispose()
        {
            _exitThread = true;
            if (_scanInputThread != null)
            {
                // Wait up to 1 second for the thread to gracefully terminate.
                _scanInputThread.Join(1000);
                _scanInputThread = null;
            }

            if (_ai != null)
            {
                _ai.Close();
                _ai = null;
            }
        }
    }
}
