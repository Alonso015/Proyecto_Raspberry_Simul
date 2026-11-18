using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Conversor A/D MCP3008 (U1 de la IMAT HAT): 8 canales de 10 bits con referencia de 3,3 V,
    /// conectado al SPI0 de la Pi con CE0 (docs/hardware.md §3.4).
    /// </summary>
    /// <remarks>
    /// <para><b>Eléctricamente</b>, cada canal es una entrada de alta impedancia: no carga el
    /// circuito y los canales son independientes (<see cref="ISignalIsolating"/>).</para>
    /// <para><b>Comportamiento</b>: código = ⌊1024 · V / V<sub>ref</sub>⌋, recortado a 0..1023
    /// (conversor ideal, sin ruido ni error de offset: aproximación). Como en gpiozero,
    /// <see cref="ReadValue"/> = código / 1023 y <see cref="ReadVoltage"/> = valor · 3,3.</para>
    /// <para>Solo se puede leer si el SPI de la Pi está activado (práctica 5: <c>raspi-config</c>).</para>
    /// <para>Más de 3,3 V en un canal se considera un daño (mismo margen supuesto que en los GPIO).</para>
    /// </remarks>
    public sealed class Mcp3008 : ElectricalComponent, ISignalIsolating, IFaultSource
    {
        public const int ChannelCount = 8;
        public const int Resolution = 1024;            // 2^10
        public const int MaxCode = Resolution - 1;     // 1023
        public const double ReferenceVoltage = 3.3;
        public const double MaxInputVoltage = ReferenceVoltage + 0.1;

        private readonly Terminal[] _channels = new Terminal[ChannelCount];
        private readonly double[] _voltages = new double[ChannelCount];
        private readonly List<Fault> _faults = new List<Fault>();
        private readonly Func<bool> _spiEnabled;

        /// <param name="spiEnabled">
        /// Indica si el bus SPI está activo. <c>null</c> = siempre disponible (montajes sin Pi).
        /// </param>
        public Mcp3008(string id, Func<bool> spiEnabled = null) : base(id, ComponentTypes.Mcp3008)
        {
            _spiEnabled = spiEnabled;
            for (int i = 0; i < ChannelCount; i++) _channels[i] = CreateTerminal("CH" + i);
        }

        public bool IsAvailable => _spiEnabled == null || _spiEnabled();

        public Terminal Channel(int index) => _channels[CheckChannel(index)];

        /// <summary>Tensión real del canal en la última resolución (V), sin cuantizar.</summary>
        public double ChannelVoltage(int index) => _voltages[CheckChannel(index)];

        /// <summary>Código de 10 bits (0..1023), como el que devuelve el chip por SPI.</summary>
        /// <exception cref="InvalidOperationException">Si el SPI está desactivado.</exception>
        public int Read(int channel)
        {
            CheckChannel(channel);
            if (!IsAvailable)
                throw new InvalidOperationException(
                    $"{Id}: el SPI está desactivado; actívalo para leer el conversor (sudo raspi-config).");
            return Quantize(_voltages[channel]);
        }

        /// <summary>Lectura normalizada 0..1 (propiedad <c>value</c> de gpiozero).</summary>
        public double ReadValue(int channel) => Read(channel) / (double)MaxCode;

        /// <summary>Lectura en voltios (propiedad <c>voltage</c> de gpiozero).</summary>
        public double ReadVoltage(int channel) => ReadValue(channel) * ReferenceVoltage;

        /// <summary>Código para una tensión dada: ⌊1024 · V / V<sub>ref</sub>⌋ en 0..1023.</summary>
        public static int Quantize(double volts)
        {
            if (double.IsNaN(volts)) return 0;
            double code = Math.Floor(Resolution * volts / ReferenceVoltage);
            return (int)Math.Max(0, Math.Min(MaxCode, code));
        }

        // Alta impedancia ideal: no estampa nada.
        public override void Stamp(IStampContext ctx) { }

        public override void OnSolved(ISolution solution)
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                _voltages[i] = solution.VoltageAt(_channels[i].Node);
                string source = Id + ".CH" + i;
                if (_voltages[i] > MaxInputVoltage && !_faults.Exists(f => f.SourceId == source))
                    _faults.Add(new Fault(FaultKind.Overvoltage, source, _voltages[i], ReferenceVoltage, "V"));
            }
        }

        public IEnumerable<Fault> CheckFaults() => _faults;

        public override void Reset()
        {
            _faults.Clear();
            Array.Clear(_voltages, 0, ChannelCount);
        }

        private static int CheckChannel(int index)
        {
            if (index < 0 || index >= ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, "El MCP3008 tiene los canales 0 a 7.");
            return index;
        }
    }
}
