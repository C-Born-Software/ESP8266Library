using System;
//using Microsoft.SPOT;
//using Microsoft.SPOT.Presentation.Media;
using System.Threading;

namespace AnodeMeter.Common
{
    public abstract class LED
    {
        protected LedState[] _sequence;
        protected int _subCycle = -2;
        protected FlashRate _fr = FlashRate.Medium;
        protected int _cycEnd = -1;
        protected int _mileStoneCycles;
        protected bool _mileStoneQueued = false;
        private byte _locked = 0;
        protected bool NormalMeasCompletionInd = false;

        public class LedState
        {
            public int _redPercent;
            public int _greenPercent;

            public LedState(int RedPercent, int GreenPercent)
            {
                _redPercent = RedPercent < 0 ? 0 : RedPercent > 100 ? 100 : RedPercent;
                _greenPercent = GreenPercent < 0 ? 0 : GreenPercent > 100 ? 100 : GreenPercent;
            }
        }
        public enum LedColor
        {
            Red,
            Green
        }
        public enum Milestones
        {
            HalfPot,
            EndPot
        }
        public enum FlashRate
        {
            NoFlash,
            Slow,
            Medium,
            Fast
        }
        public void IndicatedNormalCompletion(bool RodDropMeasurement)
        {
            NormalMeasCompletionInd = true;

            if (RodDropMeasurement)
                TurnOn(LedColor.Green);
            else
                FlashFast(LedColor.Green);
        }
        public LED()
        {
        }
        public virtual void IndicateMilestone(Milestones m)
        {
            _mileStoneQueued = true;
            switch (m)
            {
                case Milestones.HalfPot:
                case Milestones.EndPot:
                    _fr = FlashRate.Slow;
                    _mileStoneCycles = 4;
                    break;
            }
        }
        public void TurnOn(LedColor c)
        {
            LedState s;
            if (c == LedColor.Green)
                s = new LedState(0, 100);
            else
                s = new LedState(100, 0);

            TurnOn(s);
        }
        public void TurnOn(LedState state)
        {
            _sequence = null;
            _fr = FlashRate.NoFlash;
            _cycEnd = -1;
            _subCycle = -1;
            UpdateDevice(state);
        }
        public void TurnOff()
        {
            _sequence = null;
            _cycEnd = -1;
            _subCycle = -2;
            UpdateDevice(new LedState(0, 0));

            if (_mileStoneQueued)
            {
                _mileStoneQueued = false;
                _subCycle = -1;
                _cycEnd = _mileStoneCycles;
            }
        }
        public void TurnOffFinishedIndicator()
        {
            if (NormalMeasCompletionInd)
                TurnOff();

            NormalMeasCompletionInd = false;
        }
        public void Flash(LedColor c, int maxSeconds)
        {
            if (maxSeconds != 0)
                _cycEnd = maxSeconds * 1000 / GlobalConsts.LED_UPDATE_RATE + 1;

            Flash(c);
        }
        public void FlashFast(LedColor c)
        {
            LedState[] seq = new LedState[2];
            seq[1] = new LedState(0, 0);

            if (c == LedColor.Green)
                seq[0] = new LedState(0, 100);
            else
                seq[0] = new LedState(100, 0);

            _fr = FlashRate.Fast;
            _subCycle = -1;

            _sequence = seq;

        }
        public void Flash(LedColor c)
        {
            LedState[] seq = new LedState[2];
            seq[1] = new LedState(0, 0);

            if (c == LedColor.Green)
                seq[0] = new LedState(0, 100);
            else
                seq[0] = new LedState(100, 0);

            Flash(seq);
        }
        public void Flash(LedState[] sequence)
        {
            _fr = FlashRate.Medium;
            _subCycle = -1;

            _sequence = sequence;
        }
        public void Flash(LedState[] sequence, int maxSeconds)
        {
            if (maxSeconds != 0)
                _cycEnd = maxSeconds * 1000 / GlobalConsts.LED_UPDATE_RATE + 1;

            Flash(sequence);
        }
        public void Lock(byte locktime)
        {
            _locked = locktime;
        }
        public void FlashAlternateColors(LedColor startColor, int durationSecs)
        {
            LedState[] ledState = new LedState[2];
            if (startColor == LedColor.Green)
            {
                ledState[0] = new LedState(0, 100);
                ledState[1] = new LedState(100, 0);
            }
            else
            {
                ledState[0] = new LedState(100, 0);
                ledState[1] = new LedState(0, 100);
            }
            Flash(ledState, durationSecs);
        }
        protected abstract void UpdateDevice(LedState state);
        protected virtual void ExecuteFlashTransitions(object o)
        {
            if (_locked > 0)
            {
                --_locked;
            }
            else
            {
                if (_subCycle != -2)
                {
                    if (++_subCycle == _cycEnd)
                        TurnOff();

                    else
                    {
                        LedState thisState = null;

                        if (_fr == FlashRate.NoFlash)
                            thisState = new LedState(0, 100);

                        else if (_sequence != null)
                            // We Need to Flash
                            thisState = _sequence[(_subCycle / (_fr == FlashRate.Slow ? 3 : _fr == FlashRate.Medium ? 2 : 1)) % _sequence.Length];

                        else
                        {
                            // this is a timed milestone, manage the LED state 
                            if ((_subCycle / (_fr == FlashRate.Slow ? 3 : _fr == FlashRate.Medium ? 2 : 1)) % 2 == 0)
                                thisState = new LedState(0, 100);
                            else
                                thisState = new LedState(0, 0);
                        }
                        UpdateDevice(thisState);
                    }
                }
            }
        }
    }
}
