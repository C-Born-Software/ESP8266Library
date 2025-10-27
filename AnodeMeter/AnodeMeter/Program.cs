using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace AnodeMeter
{
    internal class Program
    {
        public static AnodeMeterType MeterType;
        public static AnodeMeter AM = null;

        static void Main()
        {
#if true
            Profile.DebugTime("We have control"); //TODO DAV DEBUG

            Globals.ShutdownCode = (IOMap.ShutdownCode) IOMap.GetShutdownCode();
            IOMap.SetShutdownCode(IOMap.ShutdownCode.Running);

            System.Version ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Globals.BuildDate = new DateTime(2000, 1, 1) + new TimeSpan(ver.Build * TimeSpan.TicksPerDay + ver.Revision * TimeSpan.TicksPerSecond * 2);
            ver = null;

            AM = new AnodeMeter();
            Profile.DebugTime("new AnodeMeter Done"); //TODO DAV DEBUG
            MeterType = AnodeMeterType.GHI;

            switch (Program.MeterType)
            {
                case AnodeMeterType.GHI:
                    // Extend heap if not already done. System will reset if this is required
                    if (GHIElectronics.TinyCLR.Native.Memory.IsExtendedHeap() == false)
                    {
                        //Debug.WriteLine("Extending heap Disabled for testing, FIX!");// TODO DAV DEBUG
                        IOMap.SetShutdownCode(IOMap.ShutdownCode.MemExtend);
                        GHIElectronics.TinyCLR.Native.Memory.ExtendHeap();
                        GHIElectronics.TinyCLR.Native.Power.Reset();
                    }
                    IOMap.SetShutdownCode(IOMap.ShutdownCode.Running);
                    AM.Run();
                    break;
            }
            while (true)
                System.Threading.Thread.Sleep(10000);
#endif
        }
    }
}
