using System;
//using Microsoft.SPOT;
using AnodeMeter.Common;

namespace AnodeMeter.Common
{
    public class AnalogInputEventArg : EventArgs
    {
        public double AnalogValue { get; set; }
        public AnalogInputEventArg(double AnalogValue)
        {
            this.AnalogValue = AnalogValue;
        }
    }
    public abstract class AnalogInput
    {
        public delegate void EventHandler(object sender, AnalogInputEventArg e);
        public event EventHandler RawDataHandler;

        public AnalogInput()
        {
        }

        protected virtual void OnAnalogValueRead(AnalogInputEventArg e)
        {
            // Make a temporary copy of the event to avoid possibility of
            // a race condition if the last subscriber unsubscribes
            // immediately after the null check and before the event is raised.
            if (RawDataHandler != null)
            {
                RawDataHandler(this, e);
            }
        }
    }
}
