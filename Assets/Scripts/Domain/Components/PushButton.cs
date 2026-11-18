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
    public sealed class PushButton : ElectricalComponent
    {
        /// <summary>Supuesto: resistencia del contacto cerrado (Ω).</summary>
        public const double DefaultContactResistance = 0.1;

        public PushButton(string id, double contactResistance = DefaultContactResistance)
            : base(id, ComponentTypes.PushButton)
        {
            if (!(contactResistance > 0)) throw new ArgumentOutOfRangeException(nameof(contactResistance));
            ContactResistance = contactResistance;
            A = CreateTerminal("A");
            B = CreateTerminal("B");
        }

        public double ContactResistance { get; }
        public Terminal A { get; }
        public Terminal B { get; }
        public bool IsPressed { get; private set; }

        /// <summary>Corriente de A a B en la última resolución (A).</summary>
        public double Current { get; private set; }

        public void Press() => SetPressed(true);
        public void Release() => SetPressed(false);

        private void SetPressed(bool pressed)
        {
            if (IsPressed == pressed) return;
            IsPressed = pressed;
            RaiseStateChanged();
        }

        public override void Reset() => Release();

        public override void Stamp(IStampContext ctx)
        {
            if (IsPressed) ctx.AddConductance(A.Node, B.Node, 1.0 / ContactResistance);
        }

        public override void OnSolved(ISolution solution)
        {
            Current = IsPressed
                ? (solution.VoltageAt(A.Node) - solution.VoltageAt(B.Node)) / ContactResistance
                : 0.0;
        }

        public override string ToString() => $"{Id} ({(IsPressed ? "pulsado" : "suelto")})";
    }
}
