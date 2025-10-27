using AnodeMeter.Common;
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
        private readonly ArrayList _readingOrder; // Tracks the order of measurements

        public int MaxAnodeCount => _aDrops != null ? _aDrops.Length : 0;

        public class TypedReading
        {
            public int AnodeIndex { get; }
            public double Value { get; }
            public MeasurementType Type { get; }

            public TypedReading(int anodeIndex, double value, MeasurementType type)
            {
                AnodeIndex = anodeIndex;
                Value = value;
                Type = type;
            }
        }

        public PotMeasurementRecord(DateTime SampleDate, string PotNumber, string MeterNumber, int MaxAnodeCount)
        {
            this.SampleDate = SampleDate;
            this.PotNumber = PotNumber;
            this.MeterNumber = MeterNumber;
            _aDrops = new MeasurementTypeReading[MaxAnodeCount];
            _cDrops = new MeasurementTypeReading[MaxAnodeCount];
            _readingOrder = new ArrayList();
        }

        public void AddMeasuredValue(string AnodeNumber, double MeasuredVolts, MeasurementType measType)
        {
            try
            {
                int anodeIndex = Convert.ToInt32(AnodeNumber) - 1;
                if (anodeIndex >= 0)
                {
                    double value = MeasuredVolts * 1000.0;
                    _readingOrder.Add(new TypedReading(anodeIndex, value, measType)); // Add composite object

                    if (measType == MeasurementType.RodDrop && anodeIndex < _aDrops.Length)
                        _aDrops[anodeIndex] = new MeasurementTypeReading(value);
                    else if (measType == MeasurementType.ClampDrop && anodeIndex < _cDrops.Length)
                        _cDrops[anodeIndex] = new MeasurementTypeReading(value);
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

            _readingOrder.Add(new TypedReading(anodeIndex, value, type)); // Add composite object

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

        /// <summary>
        /// Gets the last 'count' readings that were added to this record.
        /// </summary>
        /// <param name="count">The number of recent readings to retrieve.</param>
        /// <returns>An ArrayList containing the most recent readings.</returns>
        public ArrayList GetLastReadings(int count)
        {
            var recentReadings = new ArrayList();
            int startIndex = _readingOrder.Count - count;
            if (startIndex < 0) startIndex = 0;

            for (int i = startIndex; i < _readingOrder.Count; i++)
            {
                recentReadings.Add(((TypedReading)_readingOrder[i]).Value);
            }
            return recentReadings;
        }

        public ArrayList GetAllReadingsInOrder()
        {
            return new ArrayList(_readingOrder);
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