using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// LED modelado como diodo <b>lineal a trozos</b>, con dos regiones:
    /// <list type="bullet">
    /// <item><b>Corte</b> (Va − Vk &lt; V<sub>F</sub>): no conduce, circuito abierto.</item>
    /// <item><b>Conducción</b>: fuente de V<sub>F</sub> en serie con una resistencia pequeña
    /// R<sub>S</sub>, es decir I = (Va − Vk − V<sub>F</sub>) / R<sub>S</sub>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>Si la corriente supera <see cref="MaxCurrent"/>, el LED se quema: desde entonces es un
    /// circuito abierto (como en la realidad, un LED fundido no vuelve) hasta <see cref="Reset"/>.</para>
    /// <para>V<sub>F</sub>, R<sub>S</sub> e I<sub>máx</sub> son <b>supuestos de la simulación</b>
    /// (valores típicos de un LED rojo de 5 mm; docs/requisitos.md §2.6).</para>
    /// </remarks>
    public sealed class Led : INonlinearElement, IFaultSource
    {
        /// <summary>Supuesto: tensión directa (V).</summary>
        public const double DefaultForwardVoltage = 2.0;

        /// <summary>Supuesto: resistencia serie en conducción (Ω).</summary>
        public const double DefaultSeriesResistance = 10.0;

        /// <summary>Supuesto: corriente máxima antes de quemarse (A).</summary>
        public const double DefaultMaxCurrent = 0.020;

        private readonly Terminal[] _terminals;

        public Led(string id,
                   double forwardVoltage = DefaultForwardVoltage,
                   double seriesResistance = DefaultSeriesResistance,
                   double maxCurrent = DefaultMaxCurrent)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El LED necesita un id.", nameof(id));
            if (!(forwardVoltage > 0)) throw new ArgumentOutOfRangeException(nameof(forwardVoltage));
            if (!(seriesResistance > 0)) throw new ArgumentOutOfRangeException(nameof(seriesResistance));
            if (!(maxCurrent > 0)) throw new ArgumentOutOfRangeException(nameof(maxCurrent));
            Id = id;
            ForwardVoltage = forwardVoltage;
            SeriesResistance = seriesResistance;
            MaxCurrent = maxCurrent;
            Anode = new Terminal(id + ".A");
            Cathode = new Terminal(id + ".K");
            _terminals = new[] { Anode, Cathode };
        }

        public string Id { get; }
        public double ForwardVoltage { get; }
        public double SeriesResistance { get; }
        public double MaxCurrent { get; }

        /// <summary>Ánodo (+): la corriente entra por aquí cuando el LED conduce.</summary>
        public Terminal Anode { get; }

        /// <summary>Cátodo (−): la patilla corta / lado plano del encapsulado.</summary>
        public Terminal Cathode { get; }

        public IReadOnlyList<Terminal> Terminals => _terminals;

        /// <summary>Región supuesta en la resolución actual.</summary>
        public bool IsConducting { get; private set; }

        /// <summary>Corriente ánodo → cátodo en la última resolución (A).</summary>
        public double Current { get; private set; }

        /// <summary>Brillo relativo 0..1, proporcional a la corriente (1 = <see cref="MaxCurrent"/>).</summary>
        public double Brightness => IsBurnt ? 0 : Math.Max(0, Math.Min(1, Current / MaxCurrent));

        public bool IsBurnt { get; private set; }

        /// <summary>Corriente que lo quemó (A); 0 si no está quemado.</summary>
        public double BurnCurrent { get; private set; }

        public void Stamp(IStampContext ctx)
        {
            if (IsBurnt || !IsConducting) return;   // circuito abierto
            // Norton de "V_F en serie con R_S": conductancia g entre A y K y una fuente g·V_F
            // que sale de A y entra en K.  I(A→K) = g·(Va − Vk) − g·V_F
            double g = 1.0 / SeriesResistance;
            ctx.AddConductance(Anode.Node, Cathode.Node, g);
            ctx.AddCurrentSource(Anode.Node, +g * ForwardVoltage);
            ctx.AddCurrentSource(Cathode.Node, -g * ForwardVoltage);
        }

        public bool UpdateRegion(ISolution solution)
        {
            if (IsBurnt) return false;
            double vak = solution.VoltageAt(Anode.Node) - solution.VoltageAt(Cathode.Node);
            // Conduce si la tensión ánodo-cátodo supera V_F. En conducción esto equivale a que la
            // corriente calculada, (vak − V_F)/R_S, sea positiva; si sale negativa, no conducía.
            bool shouldConduct = vak > ForwardVoltage;
            if (shouldConduct == IsConducting) return false;
            IsConducting = shouldConduct;
            return true;
        }

        public void OnSolved(ISolution solution)
        {
            if (IsBurnt || !IsConducting)
            {
                Current = 0;
                return;
            }
            double vak = solution.VoltageAt(Anode.Node) - solution.VoltageAt(Cathode.Node);
            Current = (vak - ForwardVoltage) / SeriesResistance;
            if (Current > MaxCurrent)
            {
                IsBurnt = true;          // queda abierto a partir de la próxima resolución
                BurnCurrent = Current;
            }
        }

        public IEnumerable<Fault> CheckFaults()
        {
            if (IsBurnt)
                yield return new Fault(FaultKind.LedOvercurrent, Id, BurnCurrent, MaxCurrent, "A");
        }

        /// <summary>Repone el LED (nuevo intento del reto).</summary>
        public void Reset()
        {
            IsBurnt = false;
            BurnCurrent = 0;
            IsConducting = false;
            Current = 0;
        }

        public override string ToString() => $"{Id} (LED{(IsBurnt ? ", quemado" : "")})";
    }
}
