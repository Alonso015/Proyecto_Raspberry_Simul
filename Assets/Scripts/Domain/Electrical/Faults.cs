using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>Tipos de fallo eléctrico que hacen perder un reto (docs/requisitos.md §2.6).</summary>
    public enum FaultKind
    {
        /// <summary>Un LED supera su corriente máxima y se quema.</summary>
        LedOvercurrent,
        /// <summary>Un GPIO entrega o absorbe más corriente de la que soporta.</summary>
        GpioOvercurrent,
        /// <summary>Un GPIO recibe más de 3,3 V (p. ej., lógica de 5 V).</summary>
        Overvoltage,
        /// <summary>Dos raíles de distinta tensión unidos en el mismo nodo.</summary>
        ShortCircuit
    }

    /// <summary>Un fallo detectado, con la magnitud que lo provoca y el límite superado.</summary>
    public sealed class Fault
    {
        public Fault(FaultKind kind, string sourceId, double value, double limit, string unit)
        {
            Kind = kind;
            SourceId = sourceId;
            Value = value;
            Limit = limit;
            Unit = unit;
        }

        public FaultKind Kind { get; }

        /// <summary>Id del elemento que falla (p. ej. "D1", "GPIO20").</summary>
        public string SourceId { get; }

        /// <summary>Magnitud medida (A o V). En un cortocircuito, la diferencia de tensión.</summary>
        public double Value { get; }

        /// <summary>Límite que se ha superado, en la misma unidad que <see cref="Value"/>.</summary>
        public double Limit { get; }

        public string Unit { get; }

        /// <summary>Mensaje en castellano para mostrar al jugador.</summary>
        public string Message
        {
            get
            {
                var c = CultureInfo.GetCultureInfo("es-ES");
                string Fmt(double v) => Unit == "A"
                    ? (v * 1000).ToString("0.##", c) + " mA"
                    : v.ToString("0.##", c) + " V";
                switch (Kind)
                {
                    case FaultKind.LedOvercurrent:
                        return $"{SourceId} quemado: {Fmt(Value)} > {Fmt(Limit)} máx.";
                    case FaultKind.GpioOvercurrent:
                        return $"{SourceId} dañado: {Fmt(Value)} > {Fmt(Limit)} máx.";
                    case FaultKind.Overvoltage:
                        return $"{SourceId} dañado: {Fmt(Value)} en el pin (máx. {Fmt(Limit)}).";
                    case FaultKind.ShortCircuit:
                        return $"Cortocircuito: {SourceId}.";
                    default:
                        return Kind.ToString();
                }
            }
        }

        public override string ToString() => Message;
    }

    /// <summary>Elemento que puede fallar según su último estado calculado.</summary>
    public interface IFaultSource
    {
        /// <summary>Fallos según la última resolución (vacío si todo está bien).</summary>
        IEnumerable<Fault> CheckFaults();
    }

    /// <summary>
    /// Reúne los fallos de un circuito: cortocircuitos entre raíles (sin resolver) y los que
    /// declaran los propios elementos tras resolver.
    /// </summary>
    public static class FaultDetector
    {
        public static IReadOnlyList<Fault> Detect(Netlist netlist)
        {
            if (netlist == null) throw new ArgumentNullException(nameof(netlist));
            var faults = new List<Fault>();
            foreach (var sc in netlist.ShortCircuits)
                faults.Add(new Fault(FaultKind.ShortCircuit,
                    $"{sc.First.Terminal.Name} ({sc.First.Voltage} V) unido a {sc.Second.Terminal.Name} ({sc.Second.Voltage} V)",
                    Math.Abs(sc.First.Voltage - sc.Second.Voltage), 0, "V"));
            if (netlist.HasShortCircuit) return faults;   // sin solución no hay más que mirar

            foreach (var e in netlist.Elements)
                if (e is IFaultSource source)
                    faults.AddRange(source.CheckFaults());
            return faults;
        }
    }
}
