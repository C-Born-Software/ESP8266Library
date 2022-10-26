using System;
//using Microsoft.SPOT;

namespace AnodeMeter.Common
{

    public enum AnodeMeterButtonPress { left, right, centre, lefthold, none, up, down, uphold, righthold, downhold, centrehold }
    public class MeterButtonPressEventArgs : EventArgs
    {
        public AnodeMeterButtonPress MeterButton { get; set; }
        public MeterButtonPressEventArgs(AnodeMeterButtonPress MeterButtonPress)
        {
            MeterButton = MeterButtonPress;
        }
    }
    public abstract class AnodeMeterButtons
    {
        public delegate void EventHandler(object sender, MeterButtonPressEventArgs e);
        public event EventHandler MeterButtonChanged;
        protected virtual void OnButtonChanged(MeterButtonPressEventArgs e)
        {
            // Make a temporary copy of the event to avoid possibility of
            // a race condition if the last subscriber unsubscribes
            // immediately after the null check and before the event is raised.
            EventHandler handler = MeterButtonChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }
    }
}





