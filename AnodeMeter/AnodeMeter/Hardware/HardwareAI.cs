using System;
using System.Threading;
using AnodeMeter.Common;

namespace AnodeMeter.Hardware
{
    // Define a specific delegate for the StallDetected event.
    public delegate void StallDetectedEventHandler(object sender, EventArgs e);

    class HardwareAI : AnalogInput, IDisposable
    {
        public event StallDetectedEventHandler StallDetected;

        private const double _lowValueThreshold = 0.001;
        private HiResADC _ai = null;
        private Thread _scanInputThread = null;
        private Thread _watchdogThread = null;
        private bool _exitThread = false;
        //private int _successiveCountsInErrorRange;
        private DateTime _dtLastBatteryVoltageCheck;
        private TimeSpan tsBattChkInterval = new TimeSpan(TimeSpan.TicksPerSecond * 30);
        private int _errorCount = 0;
        private const int MAX_RETRIES_BEFORE_BACKOFF = 5;
        // With a 70ms scan timer, we need about 857 ticks for a ~60-second backoff (60000 / 70)
        private const int BACKOFF_RECOVERY_ATTEMPT_TICKS = 60000 / GlobalConsts.HARDWARE_AI_SCAN_MILLI_SECONDS;

        // --- Stuck Reading Detection ---
        private const int MAX_IDENTICAL_READINGS = 42;
        private double _lastReading = double.MinValue;
        private int _identicalReadingCount = 0;
        private bool _ignoreNextReadingAfterReset = false;

        /// <summary>
        /// A simple counter incremented on every successful scan loop. An external watchdog can monitor this for changes.
        /// </summary>
        public int HeartbeatCounter { get; private set; }


        public HardwareAI()
        {
            HeartbeatCounter = 0;
            _scanInputThread = new Thread(ScanInputLoop);
            _scanInputThread.Priority = ThreadPriority.BelowNormal;
            _scanInputThread.Start();

            _watchdogThread = new Thread(WatchdogLoop);
            _watchdogThread.Priority = ThreadPriority.Highest;
            _watchdogThread.Start();

            // _successiveCountsInErrorRange = 0;
            _dtLastBatteryVoltageCheck = DateTime.MinValue;
        }

        /// <summary>
        /// A high-priority watchdog thread that monitors the liveness of the ScanInputLoop.
        /// If the HeartbeatCounter stops incrementing, it means the scan thread is stalled,
        /// and this watchdog will force a system reboot.
        /// </summary>
        private void WatchdogLoop()
        {
            long lastSeenHeartbeat = -1;
            // The number of checks to fail before rebooting. 3 checks * 2s interval = 6s timeout.
            const int failureThreshold = 3;
            int failureCount = 0;

            while (!_exitThread)
            {
                Thread.Sleep(2000); // Check every 2 seconds

                long currentHeartbeat = this.HeartbeatCounter;

                if (currentHeartbeat == lastSeenHeartbeat)
                {
                    failureCount++;
                    if (failureCount >= failureThreshold)
                    {
                        // Raise the stall detected event. The subscriber is responsible for saving state and rebooting.
                        StallDetected?.Invoke(this, EventArgs.Empty);

                        // As a fallback, if no subscriber reboots the device within a few seconds, do it ourselves.
                        Thread.Sleep(4000);
                        //GHIElectronics.TinyCLR.Native.Power.Reset();
                    }
                }
                else
                {
                    // The thread is alive, update our last seen value and reset the failure counter.
                    lastSeenHeartbeat = currentHeartbeat;
                    failureCount = 0;
                }
            }
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
#if false
                    //TODO DAV DEBUG Test - simulate frozen read for debugging
#warning //TODO DAV DEBUG Test - simulate frozen read for debugging
                    if (ThisValue < 0.001) 
                    { 
                        for (int i = 0; i < 10; i++)
                        {
                            Thread.Sleep(1000);
                        }
                    }
#endif
                    // If the ignore flag is set, discard this reading and continue.
                    if (_ignoreNextReadingAfterReset)
                    {
                        _ignoreNextReadingAfterReset = false;
                        _lastReading = ThisValue; // Prime the last reading with this discarded value.
                        continue;
                    }

                    // Successful read, reset the error counter.
                    _errorCount = 0;

                    // --- Stuck Reading Detection Logic ---
                    if (ThisValue == _lastReading)
                    {
                        _identicalReadingCount++;
                    }
                    else
                    {
                        _lastReading = ThisValue;
                        _identicalReadingCount = 0;
                    }

                    if (_identicalReadingCount > MAX_IDENTICAL_READINGS)
                    {
                        Logging.DbgWrite("HardwareAI: Reset ADC");
                        //Logging.IssueEvent(Logging.ErrSeverity.Warning, "HardwareAI::ScanInputLoop", "Stuck ADC reading detected. Resetting ADC.", "AI Stuck");
                        _ai.Reset();
                        _identicalReadingCount = 0; // Reset counter after action
                        _ignoreNextReadingAfterReset = true; // Set flag to ignore the next reading
                        continue; // Skip processing this stuck value
                    }
                    // --- End of Stuck Reading Detection ---

                    AnalogInputEventArg e1 = new AnalogInputEventArg(ThisValue);
                    base.OnAnalogValueRead(e1);

                    // --- Update Heartbeat ---
                    // This indicates a successful, non-stalled loop completion.
                    this.HeartbeatCounter++;
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
            if (_watchdogThread != null)
            {
                _watchdogThread.Join(1000);
                _watchdogThread = null;
            }

            if (_ai != null)
            {
                _ai.Close();
                _ai = null;
            }
        }
    }
}