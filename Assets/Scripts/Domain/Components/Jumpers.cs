using System;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Puente de soldadura de dos pads (JP1, JP2 de la IMAT HAT). De fábrica está unido; se
    /// puede cortar para liberar pines y volver a unir con un punto de soldadura
    /// (docs/hardware.md §3.7).
    /// </summary>
    public sealed class SolderJumper : ElectricalComponent
    {
        /// <summary>Supuesto: resistencia del puente unido (Ω).</summary>
        public const double BridgeResistance = 0.001;

        private bool _bridged = true;

        public SolderJumper(string id) : base(id, ComponentTypes.SolderJumper)
        {
            Pad1 = CreateTerminal("1");
            Pad2 = CreateTerminal("2");
        }

        public Terminal Pad1 { get; }
        public Terminal Pad2 { get; }

        public bool IsBridged
        {
            get => _bridged;
            set
            {
                if (value == _bridged) return;
                _bridged = value;
                RaiseStateChanged();
            }
        }

        public void Cut() => IsBridged = false;
        public void Solder() => IsBridged = true;

        public override void Reset() => Solder();

        public override void Stamp(IStampContext ctx)
        {
            if (_bridged) ctx.AddConductance(Pad1.Node, Pad2.Node, 1.0 / BridgeResistance);
        }

        public override void OnSolved(ISolution solution) { }
    }

    /// <summary>Qué pad lateral está unido al central en un <see cref="ThreeWayJumper"/>.</summary>
    public enum JumperSelection
    {
        None,
        Pad1,
        Pad3
    }

    /// <summary>
    /// Puente de soldadura de tres pads (JP3 de la IMAT HAT): el pad central (2) se une al 1 o
    /// al 3. En la HAT, 2 = CH7 del MCP3008, 1 = cursor de RV1 y 3 = pin 8 de J1.
    /// </summary>
    public sealed class ThreeWayJumper : ElectricalComponent
    {
        private JumperSelection _selection;

        public ThreeWayJumper(string id, JumperSelection initial = JumperSelection.Pad1)
            : base(id, ComponentTypes.ThreeWayJumper)
        {
            Pad1 = CreateTerminal("1");
            Common = CreateTerminal("2");
            Pad3 = CreateTerminal("3");
            _selection = initial;
            Initial = initial;
        }

        public Terminal Pad1 { get; }
        public Terminal Common { get; }
        public Terminal Pad3 { get; }
        public JumperSelection Initial { get; }

        public JumperSelection Selection
        {
            get => _selection;
            set
            {
                if (!Enum.IsDefined(typeof(JumperSelection), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _selection) return;
                _selection = value;
                RaiseStateChanged();
            }
        }

        public override void Reset() => Selection = Initial;

        public override void Stamp(IStampContext ctx)
        {
            double g = 1.0 / SolderJumper.BridgeResistance;
            if (_selection == JumperSelection.Pad1) ctx.AddConductance(Common.Node, Pad1.Node, g);
            else if (_selection == JumperSelection.Pad3) ctx.AddConductance(Common.Node, Pad3.Node, g);
        }

        public override void OnSolved(ISolution solution) { }
    }
}
