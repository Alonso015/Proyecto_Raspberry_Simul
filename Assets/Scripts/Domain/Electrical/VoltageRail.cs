using System.Collections.Generic;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Raíl de alimentación ideal: fija la tensión de su nodo (3,3 V, 5 V o GND = 0 V).
    /// </summary>
    /// <remarks>
    /// Aproximación: se modela como fuente ideal (sin resistencia interna ni límite de
    /// corriente). Los límites reales de la Pi (3,3 V → 0,5 A; 5 V → 1,5 A, hardware.md §1.2)
    /// se comprobarán en la detección de fallos, no aquí.
    /// </remarks>
    public sealed class VoltageRail : IElectricalElement, IVoltageReference
    {
        private readonly Terminal[] _terminals;

        public VoltageRail(string name, double voltage)
        {
            Name = name;
            Voltage = voltage;
            Terminal = new Terminal(name);
            _terminals = new[] { Terminal };
        }

        public static VoltageRail Ground() => new VoltageRail("GND", 0.0);

        public string Name { get; }
        public double Voltage { get; }
        public Terminal Terminal { get; }
        public IReadOnlyList<Terminal> Terminals => _terminals;

        // Su efecto se aplica al construir la netlist (el nodo queda con tensión fija),
        // no estampando nada en la matriz.
        public void Stamp(IStampContext ctx) { }

        public void OnSolved(ISolution solution) { }

        public override string ToString() => $"{Name} ({Voltage} V)";
    }
}
