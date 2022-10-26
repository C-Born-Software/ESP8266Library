using System;
//using Microsoft.SPOT;
//using Microsoft.SPOT.Hardware;
using GHIElectronics.TinyCLR.Devices.I2c;
using GHIElectronics.TinyCLR.Pins;

namespace AnodeMeter.Hardware
{
    /// <summary>
    /// A Class to manage the MicroChip MCP3422/3/4 delta-sigma ADC series</summary>
    /// <remarks>
    /// Provides am 18-bit ADC via the I2C interface.
    /// </remarks>
    class HiResADC
    {
        private struct ChannelConfig
        {
            public Resolution _resolution;
            public ConversionMode _conversionMode;
            public ProgGain _gain;
        }
        private const ushort AddressBase = 0x68;
        private const int EmxI2cClock = 400; // KHz

        /// <summary>Resolution Selection affects convertion time (more bits, slower conversion)</summary>
        public enum Resolution
        {
            TwelveBits,
            FourteenBits,
            SixteenBits,
            EighteenBits
        }
        /// <summary>Use One-Shot if very low power consuption is required</summary>
        public enum ConversionMode
        {
            /// <summary>Requests a value, waits for conversion then returns value</summary
            OneShot,
            Continuous
        }
        public enum InputChannel
        {
            Ch1,
            Ch2,
            Ch3,
            Ch4
        }
        /// <summary>Selects the gain factor for the input signal</summary>
        public enum ProgGain
        {
            x1,
            x2,
            x4,
            x8
        }

        private ChannelConfig[] _chConfig = new ChannelConfig[4];
        private InputChannel _inputChannel = InputChannel.Ch1;
        private ushort _baseAddressOffset = 0;
        private bool _configDirty = true;
        private int _ReadWriteTimeoutMilliSecs = 100;
        private double _lsbVolts = 0.001;
        private Int32 _countsMask;
        private Int32 _maxValue;
        private double _gainDivisor;
        private I2cDevice _McpAdc = null;
//        private I2CDevice.I2CTransaction[] _xConfigAction = null;
//        private I2CDevice.I2CTransaction[] _xReadAction = null;
        private byte[] _configReg = new byte[1];
        private byte[] _dataReg = new byte[5];

        private void ConfigDevice()
        {
            if (_McpAdc == null)
            {
                var settings = new I2cConnectionSettings(0x68, 400_000);
                var controller = I2cController.FromName(SC20260.I2cBus.I2c1);
                _McpAdc = controller.GetDevice(settings);

                //_McpAdc = new I2cDevice(new I2cDevice.Configuration((ushort)(AddressBase + _baseAddressOffset), EmxI2cClock));

                //_xConfigAction = new I2CDevice.I2CTransaction[1];
                //_xReadAction = new I2CDevice.I2CTransaction[1];
                //_xConfigAction[0] = I2CDevice.CreateWriteTransaction(_configReg);
                //_xReadAction[0] = I2CDevice.CreateReadTransaction(_dataReg);

            }

            double[] lsbValues = new double[] { 0.001, 0.00025, 0.0000625, 0.000015625 };

            _countsMask = (1 << (12 + (int)_chConfig[(int)_inputChannel]._resolution * 2)) - 1;
            _maxValue = 1 << (11 + (int)_chConfig[(int)_inputChannel]._resolution * 2);
            _gainDivisor = System.Math.Pow(2.0, (double)_chConfig[(int)_inputChannel]._gain);

            _lsbVolts = lsbValues[(ushort)_chConfig[(int)_inputChannel]._resolution];

            _configReg[0] = (byte)((ushort)_chConfig[(int)_inputChannel]._gain +
                                   ((ushort)_chConfig[(int)_inputChannel]._resolution << 2) +
                                   ((ushort)_chConfig[(int)_inputChannel]._conversionMode << 4) +
                                   ((ushort)_inputChannel << 5));

            WriteConfigReg();

            _configDirty = false;
        }
        private void WriteConfigReg()
        {
            _configReg[0] |= 0x80; // Queue a conversion (for One-Shot Mode)

            _McpAdc.Write(_configReg);
//            if (_McpAdc.Execute(_xConfigAction, _ReadWriteTimeoutMilliSecs) == 0)
//                throw new Exception("Error attempting to configure the HiResADC device, zero bytes transferred");
//            else
            {
                int[] conversionTimeMilliSecs = new int[4] { 5, 17, 67, 267 };
                // Block here until enough time has elapsed for the current conversion to have completed
                System.Threading.Thread.Sleep(conversionTimeMilliSecs[(ushort)_chConfig[(int)_inputChannel]._resolution]);
            }
        }

        /// <summary>Sets the low order 3 bits of the I2C address of the device</summary>
        /// <param name='Offset'>See datasheet, only required for MCP3423/4 or MCP3422 where Address-Option is NOT A0</param>
        public void SetAddressOffset(ushort Offset)
        {
            if (Offset != (Offset & 0x07))
                throw new Exception("Argument error calling \"HiResADC::SetAddressOffset\", argument \"Offset\" must be between 0 And 7");

            _baseAddressOffset = Offset;
            _configDirty = true;
        }
        /// <summary>Specifies the timeout period while waiting for a response for all read/write operations</summary>
        /// <param name='toMilliSeconds'>TimeOut milliseconds (default = 100)</param>
        public void SetReadWriteTimeout(int toMilliSeconds)
        {
            _ReadWriteTimeoutMilliSecs = toMilliSeconds;
        }
        /// <summary>Parameterless Constructor - set up options later</summary>
        public HiResADC() { }
        /// <summary>Preset Channel Configuration</summary>
        /// <param name='Ch'>Channel Number</param>
        /// <param name='res'>Bits of resolution required</param>
        /// <param name='cm'>Conversion Mode</param>
        /// <param name='gain'>Gain multiplier of the pre-ADC amplifier</param>
        public void SetChannelConfig(InputChannel Ch,
                                     Resolution res,
                                     ConversionMode mode,
                                     ProgGain gain)
        {
            _chConfig[(int)Ch]._resolution = res;
            _chConfig[(int)Ch]._conversionMode = mode;
            _chConfig[(int)Ch]._gain = gain;
        }
        // Destructor
        ~HiResADC()
        {
            Close();
        }
        public void Close()
        {
            if (_McpAdc != null)
            {
                //_McpAdc.Config = new I2CDevice.Configuration(0, 400);
                _McpAdc.Dispose();
                _McpAdc = null;
            }
        }
        /// <summary>Configures the ADC to use the prescribed channel and configures it accordingly</summary>
        /// <returns>Voltage representated as a double-precision real</returns>
        private void SwitchInputChannel(InputChannel ch)
        {
            if (ch != _inputChannel)
            {
                _inputChannel = ch;
                _configDirty = true;
            }
        }
        /// <summary>Reads the selected input channel and converts to voltage units</summary>
        /// <returns>Voltage representated as a double-precision real</returns>
        public double ReadVolts(InputChannel ch)
        {
            SwitchInputChannel(ch);

            Int32 Counts = ReadRawCounts();

            if (Counts > _maxValue)
                Counts -= _maxValue * 2;

            return (Counts * _lsbVolts) / _gainDivisor;
        }
        /// <summary>Reads the raw counts from the ADC</summary>
        /// <returns>A 2's complement counts value from the ADC</returns>
        public Int32 ReadRawCounts()
        {
            if (_configDirty)
                ConfigDevice();

            else if (_chConfig[(int)_inputChannel]._conversionMode == ConversionMode.OneShot)
                WriteConfigReg();

            Int32 Counts = 0;
            _McpAdc.Read(_dataReg);
//            if (_McpAdc.Execute(_xReadAction, _ReadWriteTimeoutMilliSecs) == 0)
//                throw new Exception("Error attempting to read HiResADC data, zero bytes transferred");

//            else
            if (_chConfig[(int)_inputChannel]._resolution == Resolution.EighteenBits)
            {
                Counts = (((Int32)_dataReg[0]) << 16) + (((Int32)_dataReg[1]) << 8) + (Int32)_dataReg[2];
            }
            else
            {
                Counts = (((Int32)_dataReg[0]) << 8) + (Int32)_dataReg[1];
            }

            return Counts &= _countsMask;
        }
    }
}
