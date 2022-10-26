using System;
using System.IO;
//using Microsoft.SPOT;
using System.Collections;
using GHIElectronics.TinyCLR.Data.Xml;
//using System.Xml;

namespace AnodeMeter.Common
{
    public class SmelterDetails
    {
        // Enumerate the 8 ways to traverse a pot when metering
        public enum AnodeOrderRule
        {
            CPattern, // 1 to End
            ReverseC, // End to 1
            MirrorC,  // n/2 to 1 then End to (n/2 + 1)   
            ReverseMirrorC, // (n/2 + 1) to End then 1 to n/2
            ZPattern, // n/2 to 1 then (n/2 + 1) to End
            ReverseZ, // End to (n/2 + 1) then 1 to n/2
            SPattern, // 1 to n/2 then End to (n/2 + 1)
            ReverseS // (n/2 + 1) to End then n/2 to 1
        }
        public enum LineMeteringDirection
        {
            ClockWise,
            AntiClockWise
        }
        Hashtable _potnameCharSeq = null;
        public enum UseMode
        {
            AdHoc,
            Prescribed
        }
        private class MeasurementDetails
        {
            //Rod
            public double _scaleFactor = 1.0;
            public double _offset = 0.0;
            public string _displayFormatString = "";
            public string _units = "";
            //Clamp
            public double _ClampscaleFactor = 1.0;
            public string _Clampunits = "";
            //
            public UseMode _useMode = UseMode.AdHoc;
            //
            public string[] _AdhocTypes = { "AH" };
        }
        private MeasurementDetails _measDetails = new MeasurementDetails();
        //public double GetMeasurementScalingFactor() { return _measDetails._scaleFactor; }
        public double GetMeasurementScalingFactor(MeasurementType expectedNow) { return ((expectedNow == MeasurementType.ClampDrop)) ? _measDetails._ClampscaleFactor : _measDetails._scaleFactor; }
        public double GetMeasurementOffset() { return _measDetails._offset; }
        public string GetMeasurementDisplayString() { return _measDetails._displayFormatString; }
        public string GetMeasurementUnits() { return _measDetails._units; }
        //public string GetMeasurementUnits(MeasurementType expectedNow) {return ((expectedNow == MeasurementType.ClampDrop)) ? _measDetails._Clampunits : _measDetails._units;}
        public UseMode GetUseMode() { return _measDetails._useMode; }
        public string[] GetAdhocTypes() { return _measDetails._AdhocTypes; }

        public class PotlineDetails
        {
            public int _AnodesPerPot;
            public string _Name;
            public Hashtable _potsByOrdinal;
            public AnodeBar _ab = new AnodeBar();

            public LineMeteringDirection _MeteringDirection = LineMeteringDirection.ClockWise;

            public class AnodeBar
            {
                private const double AssumedBarTemperature = 100;
                private const double TemperatureCoefficientPerDegreesK = 0.0039; // resistance change factor per degree K
                private const double CuResistivity20C = 0.0000168; // ohms per mm^2 (csa) per mm (length)
                private const double AlResistivity20C = 0.0000282;

                private enum BarMaterial
                {
                    Copper,
                    Aluminium
                }
                private double _nominalBarAmperage;
                private double _crossSectionalAreaMilliMeters;
                private double _measurementDistancemilliMeters;
                private BarMaterial _Material = BarMaterial.Copper;
                public double NominalVoltageDrop { get; set; }

                public AnodeBar()
                {
                    // Setup default values
                    SetParameters(8000, "Copper", 7000, 95);
                }

                private double CalcNominalVoltsDrop()
                {
                    double deltaT = AssumedBarTemperature - 20;
                    double tempCoFactor = 1 + deltaT * TemperatureCoefficientPerDegreesK;
                    double resistance = (_Material == BarMaterial.Copper ? CuResistivity20C : AlResistivity20C) * tempCoFactor * _measurementDistancemilliMeters / (_crossSectionalAreaMilliMeters);

                    return resistance * _nominalBarAmperage;
                }
                public void SetParameters(double NominalAmperage, string Material, double CrossSectionalAreaMilliMeters, double MeasurementDistanceMilliMeters)
                {
                    _nominalBarAmperage = NominalAmperage;
                    _crossSectionalAreaMilliMeters = CrossSectionalAreaMilliMeters;
                    _measurementDistancemilliMeters = MeasurementDistanceMilliMeters;

                    if (Material != null & Material.Length >= 6 && Material.ToUpper().IndexOf("COPPER") != -1)
                        _Material = BarMaterial.Copper;
                    else
                        _Material = BarMaterial.Aluminium;

                    NominalVoltageDrop = CalcNominalVoltsDrop();
                }
            }

            public class Room
            {
                public class MeteringSection
                {
                    public string _Name;
                    public AnodeOrderRule _rule;
                    public ArrayList _Pots;
                    public int LoadPots(string PotList)
                    {
                        string[] Pots = PotList.Split(new char[] { ',' });
                        int potsLoaded = 0;

                        for (int i = 0; i < Pots.Length; i++)
                        {
                            _Pots.Add(Pots[i]);
                            potsLoaded++;
                        }
                        return potsLoaded;
                    }
                    public AnodeOrderRule EncodeRule(string ruleName)
                    {
                        AnodeOrderRule r = AnodeOrderRule.CPattern;
                        switch (ruleName)
                        {
                            case "CPattern": r = AnodeOrderRule.CPattern; break;
                            case "ReverseC": r = AnodeOrderRule.ReverseC; break;
                            case "MirrorC": r = AnodeOrderRule.MirrorC; break;
                            case "ReverseMirrorC": r = AnodeOrderRule.ReverseMirrorC; break;
                            case "ZPattern": r = AnodeOrderRule.ZPattern; break;
                            case "ReverseZ": r = AnodeOrderRule.ReverseZ; break;
                            case "SPattern": r = AnodeOrderRule.SPattern; break;
                            case "ReverseS": r = AnodeOrderRule.ReverseS; break;
                        }
                        return r;
                    }
                }
                public string _Name { get; set; }
                public int _Ordinal { get; set; }
                public ArrayList _Sections { get; set; }
            }
            //public Room[] _rooms = new Room[2];
            public Room[] _rooms;

            public PotlineDetails()
            {
                // Make space for 20 Rooms initially, trim back later after parsing the xml
                int cntWhileParsingXml = 20;

                _rooms = new Room[cntWhileParsingXml];

                for (int i = 0; i < cntWhileParsingXml; i++)
                {
                    _rooms[i] = new Room();
                    _rooms[i]._Sections = new ArrayList();
                }
            }

            public void TrimRoomCount2PlantConfig(int numberOfRooms)
            {
                // Create a temp copy of the initial room array
                Room[] oldRoomArray = _rooms;
                _rooms = new Room[numberOfRooms];

                for (int i = 0; i < numberOfRooms; i++)
                    _rooms[i] = oldRoomArray[i];

                oldRoomArray = null;
            }
        }

        private string _locationTag;
        private PotlineDetails[] _lines;
        private static TimeSpan _tsShiftLen;
        private static TimeSpan _tsFirstShiftOffset;
        private string _defaultLine;
        public string DefaultLine
        {
            get { return _defaultLine; }
            set { if (value != _defaultLine) { _defaultLine = value; DataStore.UpdateXmlValue("DefaultLineName", _defaultLine); } }
        }

        // Provide a container for reverse lookup of Pot details
        public class PotDetails
        {
            public string _name;
            public string _line;
            public string _section;
            public int _potOrdinal;
            public int _roomOrdinal;
            public AnodeOrderRule _rule;
            public int _anodeCount;

            public PotDetails(string PotName, string LineName, string SectionName, AnodeOrderRule Rule, int AnodeCount, int RoomOrdinal, int PotOrdinal)
            {
                _name = PotName;
                _line = LineName;
                _section = SectionName;
                _rule = Rule;
                _anodeCount = AnodeCount;
                _roomOrdinal = RoomOrdinal;
                _potOrdinal = PotOrdinal;
            }
        }
        private Hashtable _potInfo;
        // Initialise the Plant Details object from the xml-text stream
        public bool Init(Stream s)
        {
            XmlReader xml = null;
            Stack stk;
            bool bParsedOK = true;

            try
            {
                xml = XmlReader.Create(s);
                stk = new Stack();
                bool bPlantConfigFound = false;
                ArrayList Lines = new ArrayList();
                PotlineDetails thisLine = null;
                PotlineDetails.Room.MeteringSection thisSectn = null;
                int RoomIndex = 0;
                int PotCount = 0;
                bool bFirstLineFound = true;
                string[] xmlLevel = null;
                int xmlPrevDepth = -1;

                while (xml.Read())
                {
                    // ********* WARNING **** WARNING *************
                    // The line of code that follows does MORE than it should.
                    // The use of various properties including: <LocalName>, <IsEmptyElement>, etc
                    // are not assigned correct values UNTIL after a method like <IsStartElement> is called.
                    // Removing the call to <IsStartElement> here, will cause this code to break ...
                    bool bStartElement = xml.IsStartElement();

                    while (xml.Depth < stk.Count && stk.Count > 0)
                        stk.Pop();

                    if (xml.LocalName != "" && !xml.IsEmptyElement && bStartElement)
                        stk.Push(xml.LocalName);


                    if (!bPlantConfigFound)
                    {
                        if (bStartElement && xml.LocalName == "LocationConfig")
                        {
                            _locationTag = xml.ReadAttributeString("LocationTag");
                            if (_locationTag == "PTD")
                                Globals.MaskT4 = true;
                            _tsShiftLen = new TimeSpan((long)xml.ReadAttributeValue("WorkShiftHours") * TimeSpan.TicksPerHour);
                            _tsFirstShiftOffset = new TimeSpan((long)xml.ReadAttributeValue("FirstShiftStartMinutes") * TimeSpan.TicksPerMinute);
                            _defaultLine = xml.ReadAttributeString("DefaultLineName");
                            bPlantConfigFound = true;
                        }
                    }
                    else
                    {

                        if (xml.AttributeCount >= 1)
                        {
                            if (xmlPrevDepth != xml.Depth)
                            {
                                // Create an array view of the xml Node Hierarchy so that we can be sure about current location
                                object[] xmlNodes = stk.ToArray();
                                xmlLevel = new string[xmlNodes.Length];
                                for (int lvl = 0; lvl < xmlNodes.Length; lvl++)
                                    xmlLevel[xmlNodes.Length - lvl - 1] = xmlNodes[lvl].ToString();

                                xmlPrevDepth = xml.Depth;
                            }

                            switch (xml.Depth)
                            {
                                case 1:
                                    if (xmlLevel[0] == "LocationConfig")
                                    {
                                        if (xml.LocalName == "Line")
                                        {
                                            // Use the start of a new "Line" block as a trigger to tidy up the end of the previous Line block
                                            if (!bFirstLineFound)
                                                thisLine.TrimRoomCount2PlantConfig(RoomIndex + 1);

                                            bFirstLineFound = false;
                                            thisLine = new PotlineDetails();
                                            thisLine._Name = xml.ReadAttributeString("Name");
                                            thisLine._AnodesPerPot = (int)xml.ReadAttributeValue("AnodesPerPot");
                                            thisLine._MeteringDirection = ResolveMeteringDirection(xml.ReadAttributeString("MeteringDirection"));
                                            RoomIndex = -1;
                                            Lines.Add(thisLine);
                                        }
                                        else if (xml.LocalName == "Measurement")
                                        {
                                            // Used for Rod Drop, and default for Clamp if not overridden
                                            _measDetails._ClampscaleFactor =
                                            _measDetails._scaleFactor = xml.ReadAttributeValue("ScaleFactor");
                                            _measDetails._offset = xml.ReadAttributeValue("Offset");
                                            _measDetails._displayFormatString = xml.ReadAttributeString("DisplayFormatString");
                                            _measDetails._Clampunits =
                                            _measDetails._units = xml.ReadAttributeString("Units");
                                            _measDetails._useMode = (xml.ReadAttributeString("DefaultUseMode") == "AdHoc" ? UseMode.AdHoc : UseMode.Prescribed);

                                            // Read Clamp Scale Overrides if present
                                            if (xml.MoveToAttribute("ClampScaleFactor")) //Could also use if(GetAttribute("ClampScaleFactor")!=null)
                                                _measDetails._ClampscaleFactor = xml.ReadAttributeValue("ClampScaleFactor");

                                            if (xml.MoveToAttribute("ClampUnits"))
                                                _measDetails._Clampunits = xml.ReadAttributeString("ClampUnits");

                                            if (xml.MoveToAttribute("AdhocTypes"))
                                                _measDetails._AdhocTypes = ("AH," + xml.ReadAttributeString("AdhocTypes")).Split(',');

                                            // Without this we loop forever if the XML node has an attribute we don't handle.
                                            // It has been like this forever though
                                            // A bug in the MS xmlreader code??
                                            xml.MoveToElement();
                                        }
                                    }
                                    break;
                                case 2:
                                    if (xmlLevel[0] == "LocationConfig" && xmlLevel[1] == "Line")
                                    {
                                        if (xml.LocalName == "Room")
                                        {
                                            RoomIndex++;
                                            thisLine._rooms[RoomIndex]._Name = xml.ReadAttributeString("Name");
                                            thisLine._rooms[RoomIndex]._Ordinal = RoomIndex + 1;
                                        }
                                        else if (xml.LocalName == "Anodebar")
                                        {
                                            thisLine._ab.SetParameters(xml.ReadAttributeValue("NominalAmperage"),
                                                                       xml.ReadAttributeString("Material"),
                                                                       xml.ReadAttributeValue("CrossSectionalAreaMilliMeters"),
                                                                       xml.ReadAttributeValue("MeasurementDistanceMilliMeters"));
                                        }
                                    }
                                    break;
                                case 3:
                                    if (xmlLevel[0] == "LocationConfig" && xmlLevel[1] == "Line" && xmlLevel[2] == "Room" && xml.LocalName == "MeteringSection")
                                    {
                                        thisSectn = new PotlineDetails.Room.MeteringSection();
                                        thisSectn._Pots = new ArrayList();
                                        thisSectn._Name = xml.ReadAttributeString("Name");
                                        thisSectn._rule = thisSectn.EncodeRule(xml.ReadAttributeString("AnodeOrderRule"));
                                        PotCount += thisSectn.LoadPots(xml.ReadAttributeString("Pots"));
                                        thisLine._rooms[RoomIndex]._Sections.Add(thisSectn);
                                    }
                                    break;
                            }
                        }
                    }
                }
                xml.Close();
                if (Lines.Count > 0)
                {
                    // Tidy up the last Line-Block parsed
                    thisLine.TrimRoomCount2PlantConfig(RoomIndex + 1);

                    // Create the lookup-details-by-pot-number hashtable
                    _potInfo = new Hashtable(PotCount);
                    _lines = (PotlineDetails[])Lines.ToArray(typeof(PotlineDetails));
                    PotDetails pot = null;
                    postXmlParseHouseKeeping();

                    int maxLenOfPotName = GetMaxPotNameLength();

                    _potnameCharSeq = new Hashtable(PotCount); // Sizing here is oversized approximation
                    string TenThou = "";
                    string Thous = "";
                    string Hundr = "";
                    string Tens = "";
                    string Units = "";
                    string keyTh = "";
                    string keyH = "";
                    string keyTen = "";
                    string keyU = "";

                    PotlineDetails ln;
                    for (int line = 0; line < Lines.Count; line++)
                    {
                        int iPotOfLine = 0;
                        ln = _lines[line];
                        ln._potsByOrdinal = new Hashtable();

                        for (int room = 0; room < ln._rooms.Length; room++)
                        {
                            foreach (PotlineDetails.Room.MeteringSection sectn in ln._rooms[room]._Sections)
                            {
                                foreach (object oPot in sectn._Pots)
                                {
                                    string sPot = ((string)oPot);
                                    pot = new PotDetails(sPot, ln._Name, sectn._Name, sectn._rule, ln._AnodesPerPot, ln._rooms[room]._Ordinal, ++iPotOfLine);
                                    _potInfo.Add(sPot, pot);
                                    ln._potsByOrdinal.Add(iPotOfLine, pot);

                                    // When a user enters a pot name for ad-hoc metering, the data entery method asks for the potname
                                    // one character at a time from right-to-left. The code below populates a hashtable so that potname entry 
                                    // code can quickly determine the range of characters that are valid for subsequent potname-characters
                                    if (sPot.Length == 5)
                                    {
                                        TenThou = sPot.Left(1);
                                        Thous = sPot.Substring(1, 1);
                                        Hundr = sPot.Substring(2, 1);
                                        Tens = sPot.Substring(3, 1);
                                        Units = sPot.Substring(4, 1);
                                        keyTh = TenThou;
                                        keyH = TenThou + Thous;
                                        keyTen = TenThou + Thous + Hundr;
                                        keyU = TenThou + Thous + Hundr + Tens;
                                        _potnameCharSeq.AddIfNotExists(" ", TenThou);
                                        _potnameCharSeq.AddIfNotExists(keyTh, Thous);
                                    }
                                    else if (sPot.Length == 4)
                                    {
                                        Thous = sPot.Left(1);
                                        Hundr = sPot.Substring(1, 1);
                                        Tens = sPot.Substring(2, 1);
                                        Units = sPot.Substring(3, 1);
                                        keyH = Thous;
                                        keyTen = Thous + Hundr;
                                        keyU = Thous + Hundr + Tens;
                                        _potnameCharSeq.AddIfNotExists(" ", Thous);
                                    }
                                    else if (sPot.Length == 3)
                                    {
                                        Hundr = sPot.Substring(0, 1);
                                        Tens = sPot.Substring(1, 1);
                                        Units = sPot.Substring(2, 1);
                                        keyH = " ";
                                        keyTen = Hundr;
                                        keyU = Hundr + Tens;

                                    }
                                    _potnameCharSeq.AddIfNotExists(keyH, Hundr);
                                    _potnameCharSeq.AddIfNotExists(keyTen, Tens);
                                    _potnameCharSeq.AddIfNotExists(keyU, Units);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (xml != null)
                    xml.Close();

                bParsedOK = false;
                Logging.IssueEvent(Logging.ErrSeverity.Severe, "SmelterDetails::Init", "xml Parsing Exception (Check XML Config-Document for errors): " + ex.Message, "xml Parse err.");
            }
            return bParsedOK;
        }


        private PotDetails _pdCache;
        public PotDetails GetPotDetails(string Pot)
        {
            if (_pdCache == null || _pdCache._name != Pot)
                _pdCache = (PotDetails)_potInfo[Pot];

            return _pdCache;
        }
        public PotlineDetails GetLineDetails(string LineName)
        {
            PotlineDetails pld = null;
            foreach (PotlineDetails l in _lines)
            {
                if (l._Name == LineName)
                {
                    pld = l;
                    break;
                }
            }
            return pld;
        }
        public ArrayList GetRoomNames()
        {
            ArrayList rn = new ArrayList();

            foreach (PotlineDetails ln in _lines)
            {
                for (int room = 0; room < 2; room++)
                    rn.Add(ln._rooms[room]._Name);

            }
            return rn;
        }

        public ArrayList GetLineNames()
        {
            ArrayList ln = new ArrayList();

            foreach (PotlineDetails l in _lines)
            {
                ln.Add("L" + l._Name);
            }
            return ln;
        }

        public bool ImplausibleMeasurement(string Pot, double ADrop, bool isRodDrop)
        {
            bool bOK = false;

            ADrop = ADrop / _measDetails._scaleFactor;
            double nominalVDrop = GetLineDetails(GetPotDetails(Pot)._line)._ab.NominalVoltageDrop;

            if (isRodDrop)
                bOK = ADrop / 2 > nominalVDrop || ADrop * 2.5 < nominalVDrop;
            else
                bOK = ADrop.Abs() > GlobalConsts.LARGE_OUTLIER_CLAMPDROP_THRESH || ADrop.Abs() * 2.5 < nominalVDrop;

            return bOK;
        }

        public string GetLineNumberForPot(string Pot)
        {
            PotDetails pd = GetPotDetails(Pot);
            return pd._line;
        }


        public ArrayList GetPotsInRoom(string RoomName)
        {
            ArrayList pir = new ArrayList();

            foreach (PotlineDetails ln in _lines)
            {
                for (int room = 0; room < 2; room++)
                {
                    if (ln._rooms[room]._Name == RoomName)
                    {
                        foreach (PotlineDetails.Room.MeteringSection sectn in ln._rooms[room]._Sections)
                        {
                            string[] sectPots = (string[])sectn._Pots.ToArray(typeof(string));
                            foreach (string sp in sectPots)
                            {
                                pir.Add(sp);
                            }
                        }
                    }
                }
            }
            return pir;
        }
        public static DateTime GetNextShiftStartTime(DateTime dtIn) { return GetShiftStartTime(dtIn) + _tsShiftLen; }
        public static DateTime GetShiftStartTime(DateTime dtIn)
        {
            DateTime sos = new DateTime();
            DateTime dtBase = new DateTime(2000, 1, 1, 0, 0, 0);

            if (_tsShiftLen.Ticks != 0)
                sos = dtBase.Add(new TimeSpan(_tsShiftLen.Ticks * (((dtIn - dtBase) - _tsFirstShiftOffset).Ticks / _tsShiftLen.Ticks)) + _tsFirstShiftOffset);

            return sos;
        }
        public string[] GetPotNameCharRange(string knownLeftPart)
        {
            string[] range = null;

            knownLeftPart = (knownLeftPart == "" ? " " : knownLeftPart);

            string group = (string)_potnameCharSeq[knownLeftPart];
            if (group != null)
                range = group.Split(new char[] { ',' });

            return range;
        }
        public bool IsValidPotName(string PotName, string requiredLeftPart, ref string ProposedPotName)
        {
            bool bPotnameOK = true;
            ProposedPotName = requiredLeftPart;

            if (_potInfo[PotName] == null)
            {
                bPotnameOK = false;

                while (ProposedPotName.Length < PotName.Length)
                {
                    // PotName is invalid, so determine the best alternative
                    string[] posibilities = GetPotNameCharRange(ProposedPotName);

                    if (posibilities != null && posibilities.Length > 0)
                        ProposedPotName += posibilities[0];
                    else
                    {
                        ProposedPotName = null;
                        break;
                    }
                }
            }

            return bPotnameOK;
        }
        public int[] GetAnodeListForPot(string PotName)
        {
            // For ad-hoc metering, this method returns an ordered array of anodes
            // based on the section specific anode-order-rule
            int[] al = null;
            PotDetails pd = GetPotDetails(PotName);

            if (pd != null)
            {
                al = new int[pd._anodeCount];

                int loopAStart = 0;
                int loopAEnd = 0;
                int loopBStart = 0;
                int loopBEnd = 0;
                int inc = 0;
                int aInd = 0;

                int s1NarrowAisle = 1;
                int s1WideAisle = pd._anodeCount / 2;
                int s2NarrowAisle = pd._anodeCount;
                int s2WideAisle = pd._anodeCount / 2 + 1;

                switch (pd._rule)
                {
                    case AnodeOrderRule.CPattern: loopAStart = s1NarrowAisle; loopAEnd = s1WideAisle; loopBStart = s2WideAisle; loopBEnd = s2NarrowAisle; break;
                    case AnodeOrderRule.ReverseC: loopAStart = s2NarrowAisle; loopAEnd = s2WideAisle; loopBStart = s1WideAisle; loopBEnd = s1NarrowAisle; break;
                    case AnodeOrderRule.MirrorC: loopAStart = s1WideAisle; loopAEnd = s1NarrowAisle; loopBStart = s2NarrowAisle; loopBEnd = s2WideAisle; break;
                    case AnodeOrderRule.ReverseMirrorC: loopAStart = s2WideAisle; loopAEnd = s2NarrowAisle; loopBStart = s1NarrowAisle; loopBEnd = s1WideAisle; break;
                    case AnodeOrderRule.ZPattern: loopAStart = s1WideAisle; loopAEnd = s1NarrowAisle; loopBStart = s2WideAisle; loopBEnd = s2NarrowAisle; break;
                    case AnodeOrderRule.ReverseZ: loopAStart = s2NarrowAisle; loopAEnd = s2WideAisle; loopBStart = s1NarrowAisle; loopBEnd = s1WideAisle; break;
                    case AnodeOrderRule.SPattern: loopAStart = s1NarrowAisle; loopAEnd = s1WideAisle; loopBStart = s2NarrowAisle; loopBEnd = s2WideAisle; break;
                    case AnodeOrderRule.ReverseS: loopAStart = s2WideAisle; loopAEnd = s2NarrowAisle; loopBStart = s1WideAisle; loopBEnd = s1NarrowAisle; break;
                }

                inc = (loopAStart < loopAEnd ? 1 : -1);
                int a = loopAStart;
                do
                {
                    al[aInd++] = a;
                    a += inc;
                } while (a != (loopAEnd + inc));

                inc = (loopBStart < loopBEnd ? 1 : -1);
                int b = loopBStart;
                do
                {
                    al[aInd++] = b;
                    b += inc;
                } while (b != (loopBEnd + inc));

            }

            return al;
        }
        public string GetDefaultPot()
        {
            int defaultLineInd = Convert.ToInt32(_defaultLine) - 1;
            int lineIndex = _lines.Length > defaultLineInd && defaultLineInd >= 0 ? defaultLineInd : 0;
            return (string)((PotlineDetails.Room.MeteringSection)_lines[lineIndex]._rooms[0]._Sections[0])._Pots[0];
        }
        public string GetNextAdhocPot(string thisPot)
        {
            string Nextpot = GetDefaultPot();
            int nextPotOrdinal = 0;
            PotDetails nextPotDetails = null;

            PotDetails pd = GetPotDetails(thisPot);
            if (pd != null)
            {
                PotlineDetails pld = GetLineDetails(pd._line);

                if (pld != null)
                {
                    SmelterDetails.PotlineDetails.Room.MeteringSection sectnOfPot = (SmelterDetails.PotlineDetails.Room.MeteringSection)pld._rooms[pd._roomOrdinal - 1]._Sections[pld._rooms[pd._roomOrdinal - 1]._Sections.Count - 1];
                    bool lastPotInRoom = pd._name == (string)sectnOfPot._Pots[sectnOfPot._Pots.Count - 1];
                    bool firstPotInRoom = pd._name == (string)((SmelterDetails.PotlineDetails.Room.MeteringSection)pld._rooms[pd._roomOrdinal - 1]._Sections[0])._Pots[0];

                    // Determine the next adhoc pot by applying the associated metering-direction rule.
                    if (pld._MeteringDirection == LineMeteringDirection.ClockWise)
                    {
                        if (firstPotInRoom && pd._roomOrdinal % 2 == 0) // First pot in an even room
                        {
                            if (pd._roomOrdinal == pld._rooms.Length) // At last room, so loop back to start
                                Nextpot = (string)((SmelterDetails.PotlineDetails.Room.MeteringSection)pld._rooms[0]._Sections[0])._Pots[0];
                            else
                                Nextpot = (string)((SmelterDetails.PotlineDetails.Room.MeteringSection)pld._rooms[pd._roomOrdinal]._Sections[0])._Pots[0];
                        }
                        else if (lastPotInRoom && pd._roomOrdinal % 2 == 1) // last pot in odd room
                        {
                            sectnOfPot = (SmelterDetails.PotlineDetails.Room.MeteringSection)pld._rooms[pd._roomOrdinal]._Sections[pld._rooms[pd._roomOrdinal]._Sections.Count - 1];
                            Nextpot = (string)sectnOfPot._Pots[sectnOfPot._Pots.Count - 1];
                        }
                        else
                        {
                            nextPotOrdinal = pd._potOrdinal;

                            if (pd._roomOrdinal % 2 == 1)
                                nextPotOrdinal++;
                            else
                                nextPotOrdinal--;

                            nextPotDetails = (PotDetails)pld._potsByOrdinal[nextPotOrdinal];
                        }
                    }
                    else // Counter-Clockwise walking direction arround rooms within line
                    {
                        if (lastPotInRoom && pd._roomOrdinal % 2 == 0) // Last pot in an even room
                        {
                            if (pd._roomOrdinal == pld._rooms.Length) // last pot in last room, so loop back to last pot in first room
                            {
                                sectnOfPot = (SmelterDetails.PotlineDetails.Room.MeteringSection)pld._rooms[1]._Sections[pld._rooms[1]._Sections.Count - 1];
                                Nextpot = (string)sectnOfPot._Pots[sectnOfPot._Pots.Count - 1];
                            }
                            else // last pot in even room, goto last pot in next room
                            {
                                sectnOfPot = (SmelterDetails.PotlineDetails.Room.MeteringSection)pld._rooms[pd._roomOrdinal]._Sections[pld._rooms[pd._roomOrdinal]._Sections.Count - 1];
                                Nextpot = (string)sectnOfPot._Pots[sectnOfPot._Pots.Count - 1];
                            }
                        }
                        else if (firstPotInRoom && pd._roomOrdinal % 2 == 1) // first pot in odd room, next pot is first pot in next room
                            Nextpot = (string)((SmelterDetails.PotlineDetails.Room.MeteringSection)pld._rooms[pd._roomOrdinal]._Sections[0])._Pots[0];

                        else
                        {
                            nextPotOrdinal = pd._potOrdinal;

                            if (pd._roomOrdinal % 2 == 1)
                                nextPotOrdinal--;
                            else
                                nextPotOrdinal++;

                            nextPotDetails = (PotDetails)pld._potsByOrdinal[nextPotOrdinal];
                        }
                    }
                }

                if (nextPotDetails != null)
                    Nextpot = nextPotDetails._name;
            }

            return Nextpot;
        }
        private LineMeteringDirection ResolveMeteringDirection(string Direction)
        {
            LineMeteringDirection d = LineMeteringDirection.ClockWise;
            if (Direction != null && (Direction.ToUpper().IndexOf("ANTI") != -1 || Direction.ToUpper().IndexOf("COUNTER") != -1))
                d = LineMeteringDirection.AntiClockWise;

            return d;
        }
        public int GetMaxPotNameLength()
        {
            int maxPotLength = 0;

            for (int iLn = 0; iLn < _lines.Length; iLn++)
            {
                for (int room = 0; room < _lines[iLn]._rooms.Length; room++)
                {
                    foreach (PotlineDetails.Room.MeteringSection sectn in _lines[iLn]._rooms[room]._Sections)
                    {
                        foreach (object oPot in sectn._Pots)
                        {
                            string thisPot = (string)oPot;

                            if (thisPot.Length > maxPotLength)
                                maxPotLength = thisPot.Length;
                        }
                    }
                }
            }
            return maxPotLength;
        }
        private void postXmlParseHouseKeeping()
        {
            int maxPotNameLength = GetMaxPotNameLength();
            string thisPotname;
            char paddingChar = '0';

            for (int iLn = 0; iLn < _lines.Length; iLn++)
            {
                for (int room = 0; room < _lines[iLn]._rooms.Length; room++)
                {
                    foreach (PotlineDetails.Room.MeteringSection sectn in _lines[iLn]._rooms[room]._Sections)
                    {
                        for (int iPot = 0; iPot < sectn._Pots.Count; iPot++)
                        {
                            thisPotname = (string)sectn._Pots[iPot];
                            if (thisPotname.Length < maxPotNameLength)
                            {
                                thisPotname = new string(paddingChar, maxPotNameLength - thisPotname.Length) + thisPotname;
                                sectn._Pots[iPot] = thisPotname;
                            }
                        }
                    }
                }
            }
        }
    }
}
