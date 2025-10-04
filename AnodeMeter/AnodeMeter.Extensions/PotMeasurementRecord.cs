using AnodeMeter.Common;
using PervasiveDigital.Utilities;
using System;
using System.Collections;

namespace AnodeMeter.Common
{
    public enum MeasurementType { RodDrop, ClampDrop };

    public class PotMeasurementRecord
    {
        public DateTime SampleDate;
        private int DateOffset;
        public string PotNumber;
        public string MeterNumber;
        MeasurementTypeReading[] _aDrops;
        MeasurementTypeReading[] _cDrops;

        public int MaxAnodeCount => _aDrops != null ? _aDrops.Length : 0;

        public PotMeasurementRecord(DateTime SampleDate, string PotNumber, string MeterNumber, int MaxAnodeCount)
        {
            this.SampleDate = SampleDate;
            this.PotNumber = PotNumber;
            this.MeterNumber = MeterNumber;
            _aDrops = new MeasurementTypeReading[MaxAnodeCount];
            _cDrops = new MeasurementTypeReading[MaxAnodeCount];
        }

        public void AddMeasuredValue(string AnodeNumber, double MeasuredVolts, MeasurementType measType)
        {
            try
            {
                int anodeIndex = Convert.ToInt32(AnodeNumber) - 1;
                if (anodeIndex >= 0)
                {
                    if (measType == MeasurementType.RodDrop && anodeIndex < _aDrops.Length)
                        _aDrops[anodeIndex] = new MeasurementTypeReading(MeasuredVolts * 1000.0);
                    else if (measType == MeasurementType.ClampDrop && anodeIndex < _cDrops.Length)
                        _cDrops[anodeIndex] = new MeasurementTypeReading(MeasuredVolts * 1000.0);
                }
            }
            catch (Exception)
            {
                // Re-throw a more specific exception to be handled by the caller
                throw new ArgumentOutOfRangeException("AnodeNumber", "Invalid AnodeNumber provided: " + AnodeNumber);
            }
        }

        private string ConcatenateMeasValues(MeasurementTypeReading[] rDrops, ArrayList arl = null)
        {
            string csv = "";
            int nonNullValues = 0;
            int i = 1;
            foreach (MeasurementTypeReading ar in rDrops)
            {
                if ((ar != null) && (arl == null || arl.Contains(i.ToString())))
                {
                    csv += ("," + ar.VoltageDrop.ToString("F2"));
                    nonNullValues++;
                }
                else
                    csv += ",";
                ++i;
            }
            return (nonNullValues == 0 ? "" : csv);
        }

        public string MyToString(String ScheduleName, ArrayList arl = null)
        {
            string sOut = "";
            string rDrops = ConcatenateMeasValues(_aDrops, arl);
            string cDrops = ConcatenateMeasValues(_cDrops, arl);

            if (rDrops != "")
                sOut += (SampleDate + new TimeSpan(0, 0, DateOffset++)).ToString("yyyy-MM-dd HH:mm:ss") + "," + PotNumber.TrimLeadingChar('0') + "," +
                    ScheduleName + ":RodDrops" + "," + MeterNumber.ToString() + rDrops + "\n";

            if (cDrops != "")
                sOut += (SampleDate + new TimeSpan(0, 0, DateOffset++)).ToString("yyyy-MM-dd HH:mm:ss") + "," + PotNumber.TrimLeadingChar('0') + "," +
                    ScheduleName + ":ClampDrops" + "," + MeterNumber.ToString() + cDrops + "\n";

            return sOut;
        }

        public string MaskedPotString(string MaskSched)
        {
            char[] comma = { ',' };
            string[] RecordParts = MaskSched.Split(comma);
            if (RecordParts.Length < 6)
                return "";

            string ScheduleName = RecordParts[2];
            ArrayList arl = new ArrayList();
            for (int i = 6; i < RecordParts.Length; ++i)
                arl.Add(RecordParts[i].Trim());

            return MyToString(ScheduleName, arl);
        }

        public Hashtable GetReadings(MeasurementType type)
        {
            var readings = new Hashtable();
            var source = (type == MeasurementType.RodDrop) ? _aDrops : _cDrops;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != null)
                {
                    readings.Add(i, source[i].VoltageDrop);
                }
            }
            return readings;
        }

        public void SetReading(int anodeIndex, double value, MeasurementType type)
        {
            if (anodeIndex < 0) return;

            if (type == MeasurementType.RodDrop)
            {
                if (anodeIndex < _aDrops.Length)
                    _aDrops[anodeIndex] = new MeasurementTypeReading(value);
            }
            else
            {
                if (anodeIndex < _cDrops.Length)
                    _cDrops[anodeIndex] = new MeasurementTypeReading(value);
            }
        }
    }

    public class MeasurementTypeReading
    {
        internal double _measDrop;
        public double VoltageDrop { get { return _measDrop; } }

        public MeasurementTypeReading(double Measurement)
        {
            _measDrop = Measurement;
        }
    };
}