using System;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Fotorresistencia (LDR) GL5-05AP02: su resistencia baja al aumentar la luz.
    /// </summary>
    /// <remarks>
    /// <para>La resistencia sale de la curva de calibración de la práctica 5 (docs/hardware.md §4,
    /// fuente F4), interpolando en escala log-log: las LDR siguen aproximadamente R = R0·(L0/L)^γ,
    /// que en log-log es una recta.</para>
    /// <para>Fuera de la tabla (por debajo de 40 lx o por encima de 1200 lx) se extrapola con la
    /// pendiente del tramo extremo. Sin luz se usa <see cref="DarkResistance"/>: el datasheet solo
    /// garantiza "más de 0,5 MΩ", así que el valor concreto es un <b>supuesto</b>.</para>
    /// <para>Cada LDR real es distinta (a 10 lx, entre 3 y 20 kΩ); esta curva es la de un sensor.</para>
    /// </remarks>
    public sealed class Ldr : ElectricalComponent
    {
        /// <summary>Curva de la práctica 5: (intensidad luminosa en lx, resistencia en Ω).</summary>
        private static readonly (double lux, double ohm)[] Curve =
        {
            (40, 6670), (60, 5140), (80, 4270), (100, 3700), (150, 2850), (200, 2370),
            (250, 2050), (300, 1830), (350, 1650), (400, 1520), (500, 1320), (600, 1170),
            (800, 970), (1000, 840), (1200, 750)
        };

        /// <summary>Supuesto: resistencia en oscuridad total (Ω). Datasheet: mínimo 0,5 MΩ.</summary>
        public const double DarkResistance = 1_000_000;

        private double _illuminance;

        public Ldr(string id, double illuminanceLux = 100) : base(id, ComponentTypes.Ldr)
        {
            A = CreateTerminal("A");
            B = CreateTerminal("B");
            Illuminance = illuminanceLux;
        }

        public Terminal A { get; }
        public Terminal B { get; }

        /// <summary>Luz que recibe el sensor (lx). El jugador la controla en el simulador.</summary>
        public double Illuminance
        {
            get => _illuminance;
            set
            {
                if (double.IsNaN(value) || value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "La iluminancia no puede ser negativa.");
                if (value == _illuminance) return;
                _illuminance = value;
                RaiseStateChanged();
            }
        }

        public double Resistance => ResistanceAt(_illuminance);

        public double Current { get; private set; }

        /// <summary>Resistencia para una iluminancia dada (Ω).</summary>
        public static double ResistanceAt(double lux)
        {
            if (lux <= 0) return DarkResistance;
            int last = Curve.Length - 1;
            int i;
            if (lux <= Curve[0].lux) i = 0;                 // extrapola con el primer tramo
            else if (lux >= Curve[last].lux) i = last - 1;  // extrapola con el último tramo
            else
            {
                i = 0;
                while (Curve[i + 1].lux < lux) i++;
            }
            var (l0, r0) = Curve[i];
            var (l1, r1) = Curve[i + 1];
            double slope = Math.Log(r1 / r0) / Math.Log(l1 / l0);   // = −γ del tramo
            double r = r0 * Math.Pow(lux / l0, slope);
            return Math.Min(r, DarkResistance);
        }

        public override void Stamp(IStampContext ctx) => ctx.AddConductance(A.Node, B.Node, 1.0 / Resistance);

        public override void OnSolved(ISolution solution) =>
            Current = (solution.VoltageAt(A.Node) - solution.VoltageAt(B.Node)) / Resistance;
    }
}
