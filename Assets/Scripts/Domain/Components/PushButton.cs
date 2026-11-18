using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Pulsador normalmente abierto entre los terminales A y B.
    /// </summary>
    /// <remarks>
    /// Pulsado: resistencia de contacto <see cref="ContactResistance"/> (supuesto, muy pequeña).
    /// Suelto: circuito abierto ideal (no estampa nada).
    /// Aproximación: no se simulan los rebotes mecánicos ni el condensador antirrebote de la
    /// IMAT HAT (C1–C4), que en continua es un circuito abierto (docs/arquitectura.md §6.3).
    /// </remarks>
    public sealed class PushButton : IElectricalElement
    {
        /// <summary>Supuesto: resistencia del contacto cerrado (Ω).</summary>
        public const double DefaultContactResistance = 0.1;

        private readonly Terminal[] _terminals;

        public PushButton(string id, double contactResistance = DefaultContactResistance)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El pulsador necesita un id.", nameof(id));
            if (!(contactResistance > 0)) throw new ArgumentOutOfRangeException(nameof(contactResistance));
            Id = id;
            ContactResistance = contactResistance;
            A = new Terminal(id + ".A");
            B = new Terminal(id + ".B");
            _terminals = new[] { A, B };
        }

        public string Id { get; }
        public double ContactResistance { get; }
        public Terminal A { get; }
        public Terminal B { get; }
        public IReadOnlyList<Terminal> Terminals => _terminals;

        public bool IsPressed { get; private set; }

        /// <summary>Corriente de A a B en la última resolución (A).</summary>
        public double Current { get; private set; }

        public void Press() => IsPressed = true;
        public void Release() => IsPressed = false;

        public void Stamp(IStampContext ctx)
        {
            if (IsPressed) ctx.AddConductance(A.Node, B.Node, 1.0 / ContactResistance);
        }

        public void OnSolved(ISolution solution)
        {
            Current = IsPressed
                ? (solution.VoltageAt(A.Node) - solution.VoltageAt(B.Node)) / ContactResistance
                : 0.0;
        }

        public override string ToString() => $"{Id} ({(IsPressed ? "pulsado" : "suelto")})";
    }
}
