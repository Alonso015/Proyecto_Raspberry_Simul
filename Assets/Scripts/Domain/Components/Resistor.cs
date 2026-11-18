using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Resistencia ideal (lineal): I = (Va − Vb) / R, en cualquier sentido.
    /// </summary>
    /// <remarks>
    /// Etapa 1: implementa directamente <see cref="IElectricalElement"/>. En la etapa 4 pasará
    /// a heredar de <c>Component</c> (pines, tipo, eventos), sin cambiar su modelo eléctrico.
    /// </remarks>
    public sealed class Resistor : IElectricalElement
    {
        private readonly Terminal[] _terminals;

        public Resistor(string id, double resistanceOhms)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("La resistencia necesita un id.", nameof(id));
            if (!(resistanceOhms > 0) || double.IsInfinity(resistanceOhms))
                throw new ArgumentOutOfRangeException(nameof(resistanceOhms), resistanceOhms,
                    "La resistencia debe ser un valor finito mayor que 0 Ω.");
            Id = id;
            Resistance = resistanceOhms;
            A = new Terminal(id + ".A");
            B = new Terminal(id + ".B");
            _terminals = new[] { A, B };
        }

        public string Id { get; }

        /// <summary>Resistencia en ohmios.</summary>
        public double Resistance { get; }

        /// <summary>Terminal A. La corriente positiva va de A hacia B.</summary>
        public Terminal A { get; }
        public Terminal B { get; }
        public IReadOnlyList<Terminal> Terminals => _terminals;

        /// <summary>Corriente de A a B en amperios, calculada en la última resolución.</summary>
        public double Current { get; private set; }

        /// <summary>Caída de tensión Va − Vb en voltios.</summary>
        public double VoltageDrop { get; private set; }

        /// <summary>Potencia disipada en vatios (P = V·I).</summary>
        public double Power => VoltageDrop * Current;

        public void Stamp(IStampContext ctx) => ctx.AddConductance(A.Node, B.Node, 1.0 / Resistance);

        public void OnSolved(ISolution solution)
        {
            VoltageDrop = solution.VoltageAt(A.Node) - solution.VoltageAt(B.Node);
            Current = VoltageDrop / Resistance;
        }

        public override string ToString() => $"{Id} ({Resistance} Ω)";
    }
}
