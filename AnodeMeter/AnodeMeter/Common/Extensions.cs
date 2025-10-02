using System;
using System.Collections;
using System.IO;
using System.Text;
using GHIElectronics.TinyCLR.Data.Xml;


namespace AnodeMeter.Common
{
    public enum SortDirection
    {
        Ascending,
        Descending
    }
    public static class Extensions
    {
        private static UInt32[] _posMult = new UInt32[] { 0x00000001, 0x00000010, 0x000000100, 0x00001000, 0x00010000, 0x00100000, 0x01000000, 0x10000000 };
        private static UInt32[] _posDev = new UInt32[] { 0x00000001, 0x00000100, 0x00010000, 0x01000000 };
        public static string byte2hexstring(byte by)
        {
            string[] nibs = new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "F" };
            return nibs[by >> 4] + nibs[by & 0x0F];
        }
        public static string ToHexString(this byte[] buff)
        {
            string hex = "";
            if (buff != null)
            {
                for (int i = 0; i < buff.Length; i++)
                    hex += byte2hexstring(buff[i]);
            }

            return hex;
        }
        public static double Abs(this double Arg) { return (Arg < 0 ? 0.0 - Arg : Arg); }
        public static string TrimLeadingChar(this string me, char CharToTrim)
        {
            int i = 0;
            while (me[i] == CharToTrim && i < me.Length)
                i++;

            string trimmed = "";

            if (i < me.Length)
                trimmed = me.Substring(i, me.Length - i);

            return trimmed;
        }
        public static string ReadAttributeString(this XmlReader r, string AttributeName)
        {
            string val = "";
            try
            {
                val = r.GetAttribute(AttributeName);
            }
            catch {; }
            return val;
        }
        public static double ReadAttributeValue(this XmlReader r, string AttributeName)
        {
            return Convert.ToDouble(r.ReadAttributeString(AttributeName));
        }
        public static double MaxVal(this double[] dArr)
        {
            double max = dArr[0];
            for (int i = 1; i < dArr.Length; i++)
                if (dArr[i] > max) max = dArr[i];

            return max;
        }
        public static double MinVal(this double[] dArr)
        {
            double min = dArr[0];
            for (int i = 1; i < dArr.Length; i++)
                if (dArr[i] < min) min = dArr[i];

            return min;
        }
        public static double Range(this double[] dArr)
        {
            return dArr.MaxVal() - dArr.MinVal();
        }
        public static string Right(this string s, int rLen)
        {
            string r = "";
            if (rLen <= s.Length)
            {
                r = s.Substring(s.Length - rLen, rLen);
            }
            return r;
        }
        public static string Left(this string s, int lLen)
        {
            string r = "";
            if (lLen <= s.Length)
            {
                r = s.Substring(0, lLen);
            }
            return r;
        }
        public static void AddIfNotExists(this Hashtable ht, string key, string val)
        {
            if (val != null && val != "" && key != null && key != "")
            {
                string existing = (string)ht[key];
                if (existing == null)
                    ht.Add(key, val);

                else if (existing.IndexOf(val) == -1)
                {
                    existing += ("," + val);
                    ht[key] = existing;
                }
            }
        }
        public static UInt32 HexToUInt32(this string hex8)
        {
            UInt32 val = 0;
            UInt32 c;
            hex8 = hex8.ToUpper();

            if (hex8.Substring(0, 2) == "0X")
                hex8 = hex8.Substring(2, hex8.Length - 2);

            int Ind = hex8.Length - 1;

            for (int i = 0; i < hex8.Length; i++)
            {
                c = (UInt32)hex8[Ind--] - (UInt32)'0';
                val += ((c > 9 ? c - 7 : c) * _posMult[i]);

            }
            return val;
        }
        public static string ToHexString(this UInt32 arg)
        {
            string hex = "";
            for (int i = sizeof(UInt32) - 1; i >= 0; i--)
                hex += byte2hexstring((byte)(arg / _posDev[i]));

            return hex;
        }
        public static bool IsRequestAcknowleged(this string arg)
        {
            return arg.Length == 1 && (int)arg[0] == 6;
        }
        public static DateTime ParseDateTime(this string arg)
        {
            DateTime res = DateTime.MinValue;

            if (arg != null)
            {
                string toParse = arg;

                // Convert Format yyyy-MM-dd HH:mm:ss
                if (toParse.Length == 19)
                    toParse = toParse.Substring(0, 4) + toParse.Substring(5, 2) + toParse.Substring(8, 2) + toParse.Substring(11, 2) + toParse.Substring(14, 2) + toParse.Substring(17, 2);

                // Convert "yyyyMMddHHmmss" format, eg: 20111113231610
                if (toParse.Length == 14 && toParse.IsNumbersOnly())
                {
                    int year = Convert.ToInt32(toParse.Substring(0, 4));
                    int month = Convert.ToInt32(toParse.Substring(4, 2));
                    int day = Convert.ToInt32(toParse.Substring(6, 2));
                    int hour = Convert.ToInt32(toParse.Substring(8, 2));
                    int minute = Convert.ToInt32(toParse.Substring(10, 2));
                    int second = Convert.ToInt32(toParse.Substring(12, 2));

                    if (year > 2010 && year < 2100 &&
                        month >= 1 && month <= 12 &&
                        day >= 1 && day <= DateTime.DaysInMonth(year, month) &&
                        hour >= 0 && hour <= 23 &&
                        minute >= 0 && minute <= 59 &&
                        second >= 0 && second <= 59)

                        res = new DateTime(year, month, day, hour, minute, second);
                }
            }

            return res;
        }
        public static bool IsNumbersOnly(this string arg)
        {
            bool res = false;

            if (arg != null && arg.Length > 0)
            {
                res = true;

                for(int i=0; i< arg.Length; ++i)
                {
                    char c = arg[i];
                    if (c < '0' || c > '9')
                    {
                        res = false;
                        break;
                    }
                }
#if false   // The foreach() was causing the compiler to fail!
                foreach (char c in arg)
                {
                    if (c < '0' || c > '9')
                    {
                        res = false;
                        break;
                    }
                }
#endif
            }
            return res;
        }

        public static bool IsDigit(this char c)
        {
            return c >= '0' && c <= '9';
        }

        public static void Sort(this string[] array)
        {
            Quicksort(array, 0, array.Length - 1);
        }
        public static ArrayList Sort(this ArrayList array, SortDirection dir = SortDirection.Ascending)
        {
            int idx;
            ArrayList retArrayList = new ArrayList();
            IComparable[] temp = (IComparable[])array.ToArray(typeof(IComparable));

            Quicksort(temp, 0, temp.Length - 1);

            if (dir == SortDirection.Ascending)
                for (idx = 0; idx < temp.Length; idx++)
                    retArrayList.Add(temp[idx]);

            else
                for (idx = temp.Length - 1; idx <= 0; idx--)
                    retArrayList.Add(temp[idx]);

            return retArrayList;

        }
        public static string Replace(this string Arg, string Search, string Repl)
        {
            int idx = 0;
            int prevIdx = 0;
            int skipLen = Search.Length;

            string repl = "";

            while ((idx = Arg.IndexOf(Search, idx)) >= 0)
            {
                repl += Arg.Substring(prevIdx, idx - prevIdx) + Repl;
                idx += skipLen;
                prevIdx = idx;
            }
            repl += Arg.Substring(prevIdx, Arg.Length - prevIdx);

            return repl;
        }
        public static string ExtractFileNameFromFullPath(this string Arg)
        {
            string fName = Arg;
            if (Arg != null && Arg.Length > 0)
            {
                int fnIndex = Arg.LastIndexOf('\\');
                if (fnIndex != -1)
                    fName = Arg.Substring(fnIndex + 1, Arg.Length - fnIndex - 1);
            }
            return fName;
        }

#if false
        public static string ToMtString(this MeasurementType Arg)
        {
            string measType = "";
            switch (Arg)
            {
                case MeasurementType.ClampDrop: measType = "C"; break;
                case MeasurementType.RodDrop: measType = "R"; break;
            }
            return measType;
        }
#endif
        public static void Quicksort(IComparable[] elements, int left, int right)
        {
            int i = left, j = right;
            IComparable tmp;
            IComparable pivot = elements[(left + right) / 2];

            while (i <= j)
            {
                while (elements[i].CompareTo(pivot) < 0)
                    i++;

                while (elements[j].CompareTo(pivot) > 0)
                    j--;

                if (i <= j)
                {
                    // Swap
                    tmp = elements[i];
                    elements[i] = elements[j];
                    elements[j] = tmp;

                    i++;
                    j--;
                }
            }

            // Recursive calls
            if (left < j)
                Quicksort(elements, left, j);

            if (i < right)
                Quicksort(elements, i, right);
        }

        /// <summary>
        /// Split CSV (Comma Separated Variable) line into components
        /// <br>Delimiters are comma, space and tab characters</br>
        /// <br>Multiple commas without parameters will be parsed into empty strings, so can use as placeholders</br>
        /// <br>Double-quotes can be used around parameters to allow inclusion of comma,space and tab characters</br>
        /// </summary>
        /// <param name="line"></param>
        /// <returns>String array of items parsed from CSV</returns>
        public static string[] SplitCsv(this string line)
        {
            //List<string> result = new List<string>();
            ArrayList result = new ArrayList { };
            StringBuilder currentStr = new StringBuilder("");
            bool inQuotes = false;
            bool commas = false;
            for (int i = 0; i < line.Length; i++) // For each character
            {
                if (line[i] == '\"') // Quotes are closing or opening
                    inQuotes = !inQuotes;
                else if (line[i] == ',' || line[i] == ' ' || line[i] == '\t') // Comma, space, tab
                {
                    if (!inQuotes) // If not in quotes, end of current string, add it to result
                    {
                        if (currentStr.Length > 0 || (commas && (line[i] == ',')))
                        {
                            result.Add(currentStr.ToString());
                            commas = false;
                        }
                        if (line[i] == ',') commas = true;
                        currentStr.Clear();
                    }
                    else
                        currentStr.Append(line[i]); // If in quotes, just add it 
                }
                else // Add any other character to current string
                    currentStr.Append(line[i]);
            }
            if (currentStr.Length > 0) result.Add(currentStr.ToString());
            //return result.ToArray(); // Return array of all strings
            return (string[])result.ToArray(typeof(string));
        }
        // Note that this is our own local variant based on a ulong, not the InField version based on a uint
        public static string ToVersionString(this ulong vn, bool bLong = false)
        {
            var v1 = vn >> 48;
              var v2 = (vn >> 32) & 0x0ffff;
              var v3 = (vn >> 16) & 0x0ffff;
              var v4 = vn & 0x0ffff;
            string s4 = v4.ToString();
            if (bLong == false)
                s4 = s4.Left(1);

              return v1 + "." + v2 + "." + v3 + "." + s4;
            //return ((vn >> 48) + "." + ((vn >> 32) & 0x0ffff) + "." + ((vn >> 16) & 0x0ffff) + (bLong ? "." + (vn & 0x0ffff) : ""));
        }
    }
}
