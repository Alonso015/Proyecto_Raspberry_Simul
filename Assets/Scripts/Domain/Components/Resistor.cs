using System.Collections.Generic;
using System;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Resistencia ideal (lineal): I = (Va − Vb) / R, en cualquier sentido.
    /// </summary>
    public sealed class Resistor : ElectricalComponent, IFaultSource
    {
        public Resistor(string id, double resistanceOhms) : base(id, ComponentTypes.Resistor)
        {
            if (!(resistanceOhms > 0) || double.IsInfinity(resistanceOhms))
                throw new ArgumentOutOfRangeException(nameof(resistanceOhms), resistanceOhms,
                    "La resistencia debe ser un valor finito mayor que 0 Ω.");
            Resistance = resistanceOhms;
            A = CreateTerminal("A");
            B = CreateTerminal("B");
        }

        /// <summary>Resistencia en ohmios.</summary>
        public double Resistance { get; }

        /// <summary>Terminal A. La corriente positiva va de A hacia B.</summary>
        public Terminal A { get; }
        public Terminal B { get; }

        /// <summary>Corriente de A a B en amperios, calculada en la última resolución.</summary>
        public double Current { get; private set; }

        /// <summary>Caída de tensión Va − Vb en voltios.</summary>
        public double VoltageDrop { get; private set; }

        /// <summary>Potencia disipada en vatios (P = V·I).</summary>
        public double Power => VoltageDrop * Current;

        public override void Stamp(IStampContext ctx) => ctx.AddConductance(A.Node, B.Node, 1.0 / Resistance);

        public override void OnSolved(ISolution solution)
        {
            VoltageDrop = solution.VoltageAt(A.Node) - solution.VoltageAt(B.Node);
            Current = VoltageDrop / Resistance;
        }

        public override string ToString() => $"{Id} ({Resistance} Ω)";
    
        /// <summary>Más de 1 A por aquí es un cortocircuito entre raíles (<see cref="ElectricalLimits"/>).</summary>
        public IEnumerable<Fault> CheckFaults() => ElectricalLimits.ShortThrough(Id, Current);
    }
}
