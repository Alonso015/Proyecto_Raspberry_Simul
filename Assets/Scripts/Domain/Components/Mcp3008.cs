using System;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Conversor A/D MCP3008 (U1 de la IMAT HAT), de momento solo como 8 entradas analógicas.
    /// </summary>
    /// <remarks>
    /// <para>Etapa E4: cada canal es una entrada ideal de alta impedancia (no carga el circuito)
    /// y se guarda su tensión. La conversión a 10 bits y la comunicación por SPI son un modelo
    /// de comportamiento que llega en la etapa E5.</para>
    /// <para>Alimentación y referencia: 3,3 V (docs/hardware.md §3.4).</para>
    /// </remarks>
    public sealed class Mcp3008 : ElectricalComponent
    {
        public const int ChannelCount = 8;
        public const double ReferenceVoltage = 3.3;

        private readonly Terminal[] _channels = new Terminal[ChannelCount];
        private readonly double[] _voltages = new double[ChannelCount];

        public Mcp3008(string id) : base(id, ComponentTypes.Mcp3008)
        {
            for (int i = 0; i < ChannelCount; i++) _channels[i] = CreateTerminal("CH" + i);
        }

        public Terminal Channel(int index)
        {
            if (index < 0 || index >= ChannelCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _channels[index];
        }

        /// <summary>Tensión del canal en la última resolución (V).</summary>
        public double ChannelVoltage(int index)
        {
            if (index < 0 || index >= ChannelCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _voltages[index];
        }

        // Alta impedancia ideal: no estampa nada.
        public override void Stamp(IStampContext ctx) { }

        public override void OnSolved(ISolution solution)
        {
            for (int i = 0; i < ChannelCount; i++) _voltages[i] = solution.VoltageAt(_channels[i].Node);
        }
    }
}
