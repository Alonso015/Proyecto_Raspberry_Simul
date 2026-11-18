using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Datos de un <b>tipo</b> de componente, compartidos por todas sus instancias
    /// (patrón <b>Flyweight</b>): nombre, pines y parámetros por defecto. Cada instancia solo
    /// guarda su estado propio (su valor, si está pulsado, si está quemado...).
    /// </summary>
    /// <remarks>
    /// En la capa de presentación, cada tipo tendrá además su sprite (por <see cref="Id"/>), sin
    /// que el dominio dependa de Unity.
    /// </remarks>
    public sealed class ComponentType
    {
        public ComponentType(string id, string displayName, IReadOnlyList<string> pinNames,
                             IReadOnlyDictionary<string, double> defaultParameters = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
            PinNames = pinNames ?? Array.Empty<string>();
            DefaultParameters = defaultParameters ?? new Dictionary<string, double>();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> PinNames { get; }
        public IReadOnlyDictionary<string, double> DefaultParameters { get; }

        public override string ToString() => Id;
    }

    /// <summary>Catálogo de tipos: una única instancia compartida por tipo.</summary>
    public static class ComponentTypes
    {
        public static readonly ComponentType Resistor = new ComponentType(
            "resistor", "Resistencia", new[] { "A", "B" },
            new Dictionary<string, double> { ["resistance"] = 1000 });

        public static readonly ComponentType Led = new ComponentType(
            "led", "LED", new[] { "A", "K" },
            new Dictionary<string, double>
            {
                ["forwardVoltage"] = Components.Led.DefaultForwardVoltage,
                ["seriesResistance"] = Components.Led.DefaultSeriesResistance,
                ["maxCurrent"] = Components.Led.DefaultMaxCurrent
            });

        public static readonly ComponentType PushButton = new ComponentType(
            "pushbutton", "Pulsador", new[] { "A", "B" });

        public static readonly ComponentType Potentiometer = new ComponentType(
            "potentiometer", "Potenciómetro", new[] { "1", "2", "3" },
            new Dictionary<string, double> { ["resistance"] = 10_000, ["position"] = 0.5 });

        public static readonly ComponentType Ldr = new ComponentType(
            "ldr", "Fotorresistencia (LDR)", new[] { "A", "B" },
            new Dictionary<string, double> { ["illuminance"] = 100 });

        public static readonly ComponentType SolderJumper = new ComponentType(
            "solderjumper", "Puente de soldadura", new[] { "1", "2" });

        public static readonly ComponentType ThreeWayJumper = new ComponentType(
            "threewayjumper", "Puente de soldadura de 3 posiciones", new[] { "1", "2", "3" });

        public static readonly ComponentType Mcp3008 = new ComponentType(
            "mcp3008", "Conversor A/D MCP3008",
            new[] { "CH0", "CH1", "CH2", "CH3", "CH4", "CH5", "CH6", "CH7" });

        public static readonly ComponentType Servo = new ComponentType(
            "servo", "Servomotor", new[] { "SIG", "VCC", "GND" },
            new Dictionary<string, double> { ["initialAngle"] = 90, ["maxSpeed"] = Components.Servo.DefaultMaxSpeed });

        public static readonly ComponentType RaspberryPi = new ComponentType(
            "raspberrypi", "Raspberry Pi", Array.Empty<string>());

        public static readonly ComponentType ImatHat = new ComponentType(
            "imathat", "IMAT HAT", Array.Empty<string>());
    }
}
