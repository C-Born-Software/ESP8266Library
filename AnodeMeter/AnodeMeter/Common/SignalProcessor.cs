using System;
//using Microsoft.SPOT;
using AnodeMeter.Common;

namespace AnodeMeter.Common
{
    public class VoltageMeasAquiredEvent : EventArgs
    {
        public double MeasValue { get; set; }
        public VoltageMeasAquiredEvent(double Voltage) { MeasValue = Voltage; }
    }
    public enum AiMeasurementStates
    {
        InputTooNoisy,
        RemovedTooSoon,
        RemovedAfterGoodRead
    }

    public class AiStateChangeEvent : EventArgs
    {
        public AiMeasurementStates Status { get; set; }
        public AiStateChangeEvent(AiMeasurementStates status) { Status = status; }
    }


    class SignalProcessor
    {
        enum MeasAquisitionState
        {
            NotStarted,
            InProgress,
            InProgressStalled,
            ReadingCompleted
        };

        private double[] _circStore;
        private int _storeSize;
        private int _curPos;
        private int _successiveCounts;
        private int _outlierRetry;
        private int _grabRetries;
        private MeasAquisitionState _cs;
        private const int SCANS_PER_HALF_SECOND = 500 / GlobalConsts.HARDWARE_AI_SCAN_MILLI_SECONDS;


        public delegate void VoltDropRead(object sender, VoltageMeasAquiredEvent e);
        public event VoltDropRead VoltDropHandler;

        public delegate void MeteringReadError(object sender, AiStateChangeEvent e);
        public event MeteringReadError MeteringReadingHandler;


        public SignalProcessor()
        {
            _storeSize = GlobalConsts.ROD_DROP_DECISION_WINDOW_WIDTH;
            _circStore = new double[_storeSize];
            ResetStatus();
        }
        public void ResetStatus()
        {
            _outlierRetry = 0;
            _curPos = 0;
            _successiveCounts = 0;
            _cs = MeasAquisitionState.NotStarted;
            _grabRetries = 0;
        }
        public void QueueValue(double voltage, MeasurementType nextExpectedMeasType)
        {
            bool bValueInRange = false;
            double largeOutlierThresh = 0.0;
            double maxRange = 0.0;

            if (nextExpectedMeasType == MeasurementType.RodDrop)
            {
                bValueInRange = voltage < GlobalConsts.MAX_ABS_MEAS_RDROP;
                largeOutlierThresh = GlobalConsts.LARGE_OUTLIER_RODDROP_THRESH;
                maxRange = GlobalConsts.MEAS_RD_MAX_WINDOW_RANGE;
            }
            else
            {
                bValueInRange = voltage.Abs() < GlobalConsts.MAX_ABS_MEAS_CDROP;
                largeOutlierThresh = GlobalConsts.LARGE_OUTLIER_CLAMPDROP_THRESH;
                maxRange = GlobalConsts.MEAS_CD_MAX_WINDOW_RANGE;
            }

            switch (_cs)
            {
                case MeasAquisitionState.NotStarted:
                    if (bValueInRange && ++_successiveCounts >= GlobalConsts.ROD_DROP_SETTLING_SCANS)
                    {
                        _successiveCounts = 0;
                        _outlierRetry = 0;
                        _cs = MeasAquisitionState.InProgress;
                    }
                    break;
                case MeasAquisitionState.InProgress:
                    if (bValueInRange)
                    {
                        _circStore[_curPos] = voltage;
                        _curPos = (_curPos + 1) % _storeSize;

                        // Providing we've got enough successive GOOD readings 
                        if (++_successiveCounts >= _storeSize)
                        {
                            // providing the RANGE of the readings is not too big
                            if (_circStore.Range() < maxRange)
                            {
                                // Calculate the average and activate the call-back delegate
                                double avgVal = 0.0;

                                for (int i = 1; i <= (_storeSize - GlobalConsts.MEAS_IGNORE_WINDOW_WIDTH); i++)
                                    avgVal += _circStore[(_curPos - i + _storeSize) % _storeSize];

                                avgVal /= (_storeSize - GlobalConsts.MEAS_IGNORE_WINDOW_WIDTH);

                                if ((((nextExpectedMeasType == MeasurementType.RodDrop) && (avgVal < -0.01)) || avgVal.Abs() > largeOutlierThresh) && _outlierRetry < 3)
                                    _outlierRetry++;
                                else
                                {
                                    _successiveCounts = 0;
                                    _cs = MeasAquisitionState.ReadingCompleted;
                                    _outlierRetry = 0;

                                    if (VoltDropHandler != null)
                                        VoltDropHandler(this, new VoltageMeasAquiredEvent(avgVal));

                                }
                            }
                        }
                    }
                    else
                    {
                        _grabRetries++;
                        _successiveCounts = 0;
                        _cs = MeasAquisitionState.InProgressStalled;

                        // We've got a bad value after having had good values
                        if (_grabRetries == GlobalConsts.VOLT_DROP_MAX_GRAB_RETRIES)
                        {
                            if (MeteringReadingHandler != null)
                                MeteringReadingHandler(this, new AiStateChangeEvent(AiMeasurementStates.InputTooNoisy));
                        }
                    }
                    break;
                case MeasAquisitionState.InProgressStalled:
                    if (bValueInRange)
                    {
                        _cs = MeasAquisitionState.InProgress;
                        _successiveCounts = 0;
                        _grabRetries = 0;
                    }
                    else
                    {
                        _successiveCounts++;
                        if (_successiveCounts == SCANS_PER_HALF_SECOND)
                        {
                            if (MeteringReadingHandler != null)
                                MeteringReadingHandler(this, new AiStateChangeEvent(AiMeasurementStates.RemovedTooSoon));
                        }
                    }
                    break;
                case MeasAquisitionState.ReadingCompleted:
                    if (!bValueInRange)
                    {
                        _successiveCounts++;
                        if (_successiveCounts > 3)
                        {
                            ResetStatus();

                            if (MeteringReadingHandler != null)
                                MeteringReadingHandler(this, new AiStateChangeEvent(AiMeasurementStates.RemovedAfterGoodRead));

                        }
                    }
                    else
                        _successiveCounts = 0;
                    break;
            }
        }
    }
}
