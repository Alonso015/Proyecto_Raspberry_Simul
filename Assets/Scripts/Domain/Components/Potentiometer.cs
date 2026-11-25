using System.Collections.Generic;
using System;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Potenciómetro lineal: una pista de resistencia <see cref="TotalResistance"/> entre los
    /// pines 1 y 3, con un cursor (pin 2) que la divide en dos.
    /// </summary>
    /// <remarks>
    /// Con <see cref="Position"/> = p: R(1–2) = R·(1 − p) y R(2–3) = R·p. Si el pin 1 está a
    /// 3,3 V y el 3 a GND (como RV1 en la IMAT HAT), el cursor da 3,3·p voltios sin carga.
    /// Aproximación: cada tramo tiene un mínimo de <see cref="MinTrackResistance"/> para que
    /// los extremos (p = 0 o 1) no sean un cortocircuito ideal.
    /// </remarks>
    public sealed class Potentiometer : ElectricalComponent, IFaultSource
    {
        /// <summary>Supuesto: resistencia mínima de cada tramo de la pista (Ω).</summary>
        public const double MinTrackResistance = 0.01;

        private double _position;

        public Potentiometer(string id, double totalResistance, double position = 0.5)
            : base(id, ComponentTypes.Potentiometer)
        {
            if (!(totalResistance > 0)) throw new ArgumentOutOfRangeException(nameof(totalResistance));
            TotalResistance = totalResistance;
            End1 = CreateTerminal("1");
            Wiper = CreateTerminal("2");
            End3 = CreateTerminal("3");
            Position = position;
        }

        public double TotalResistance { get; }
        public Terminal End1 { get; }
        public Terminal Wiper { get; }
        public Terminal End3 { get; }

        /// <summary>Posición del cursor, de 0 (junto al pin 3) a 1 (junto al pin 1).</summary>
        public double Position
        {
            get => _position;
            set
            {
                if (double.IsNaN(value) || value < 0 || value > 1)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "La posición va de 0 a 1.");
                if (value == _position) return;
                _position = value;
                RaiseStateChanged();
            }
        }

        public double ResistanceToEnd1 => Math.Max(MinTrackResistance, TotalResistance * (1 - _position));
        public double ResistanceToEnd3 => Math.Max(MinTrackResistance, TotalResistance * _position);

        public double WiperVoltage { get; private set; }

        public override void Stamp(IStampContext ctx)
        {
            ctx.AddConductance(End1.Node, Wiper.Node, 1.0 / ResistanceToEnd1);
            ctx.AddConductance(Wiper.Node, End3.Node, 1.0 / ResistanceToEnd3);
        }

        public override void OnSolved(ISolution solution)
        {
            WiperVoltage = solution.VoltageAt(Wiper.Node);
            double i1 = (solution.VoltageAt(End1.Node) - WiperVoltage) / ResistanceToEnd1;
            double i3 = (WiperVoltage - solution.VoltageAt(End3.Node)) / ResistanceToEnd3;
            MaxTrackCurrent = Math.Max(Math.Abs(i1), Math.Abs(i3));
        }

        /// <summary>Corriente del tramo de pista más cargado (A), tras la última resolución.</summary>
        public double MaxTrackCurrent { get; private set; }

        /// <summary>Más de 1 A por la pista es un cortocircuito (p. ej. mando a un extremo entre 3,3 V y GND).</summary>
        public IEnumerable<Fault> CheckFaults() => ElectricalLimits.ShortThrough(Id, MaxTrackCurrent);
    }
}
