using System;
using System.Collections.Generic;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Tarjeta de expansión IMAT HAT, cableada según el esquema oficial rev. v1.1
    /// (docs/hardware.md §3, fuente F8). Es un componente compuesto (patrón <b>Composite</b>).
    /// </summary>
    /// <remarks>
    /// <para>Incluye: D1–D8 con R1–R8 (270 Ω) y los puentes JP1/JP2; SW1–SW4; RV1 (10 kΩ) con
    /// el puente JP3; el MCP3008 (U1) y los conectores J1, J2, J3, J7–J12 como pines.</para>
    /// <para>No incluye (todavía): el L293 y las borneras J4–J6 de los motores (ampliación),
    /// los condensadores C1–C10 (circuito abierto en continua) ni la alimentación BackPower.</para>
    /// <para>Pines: "J1.1".."J1.8", "J2.1".."J2.4", "J3.1".."J3.4", "J7.1".."J10.3",
    /// "J11.1".."J11.8", "J12.1".."J12.4" y "TP1". Cada uno es el mismo terminal que el pin
    /// de la Pi o del componente al que va la pista.</para>
    /// </remarks>
    public sealed class ImatHat : CompositeComponent
    {
        /// <summary>LEDs D1..D8 → GPIO20..27 (hardware.md §3.2).</summary>
        public static readonly int[] LedGpio = { 20, 21, 22, 23, 24, 25, 26, 27 };

        /// <summary>Pulsadores SW1..SW4 → GPIO7, 16, 17, 19 (hardware.md §3.3).</summary>
        public static readonly int[] ButtonGpio = { 7, 16, 17, 19 };

        /// <summary>Conectores de servo J7..J10 → GPIO14, 15, 22, 23 (hardware.md §3.6).</summary>
        public static readonly int[] ServoGpio = { 14, 15, 22, 23 };

        public const double LedResistance = 270.0;
        public const double PotResistance = 10_000.0;

        private readonly Led[] _leds = new Led[8];
        private readonly Resistor[] _ledResistors = new Resistor[8];
        private readonly PushButton[] _buttons = new PushButton[4];

        public ImatHat(RaspberryPi pi, string id = "HAT") : base(id, ComponentTypes.ImatHat)
        {
            Pi = pi ?? throw new ArgumentNullException(nameof(pi));
            var gnd = pi.Ground.Terminal;
            var v33 = pi.Rail3V3.Terminal;
            var v5 = pi.Rail5V.Terminal;

            // ---- LEDs: GPIO ── Rn (270 Ω) ── Dn ── JP1 (D1–D4) / JP2 (D5–D8) ── GND
            Jp1 = AddChild(new SolderJumper("JP1"));
            Jp2 = AddChild(new SolderJumper("JP2"));
            Link(Jp1.Pad2, gnd);
            Link(Jp2.Pad2, gnd);
            for (int i = 0; i < 8; i++)
            {
                _ledResistors[i] = AddChild(new Resistor("R" + (i + 1), LedResistance));
                _leds[i] = AddChild(new Led("D" + (i + 1)));
                Link(pi.Gpio(LedGpio[i]).Pin, _ledResistors[i].A);
                Link(_ledResistors[i].B, _leds[i].Anode);
                Link(_leds[i].Cathode, i < 4 ? Jp1.Pad1 : Jp2.Pad1);
                AddPin("J11." + (i + 1), pi.Gpio(LedGpio[i]).Pin);
            }

            // ---- Pulsadores: GPIO ── SWn ── GND (sin pull-up en placa: se usa la interna)
            for (int i = 0; i < 4; i++)
            {
                _buttons[i] = AddChild(new PushButton("SW" + (i + 1)));
                Link(pi.Gpio(ButtonGpio[i]).Pin, _buttons[i].A);
                Link(_buttons[i].B, gnd);
                AddPin("J3." + (i + 1), pi.Gpio(ButtonGpio[i]).Pin);
            }

            // ---- ADC: CH0–CH6 a J1.1–J1.7; CH7 por JP3 a RV1 (de fábrica) o a J1.8
            Adc = AddChild(new Mcp3008("U1"));
            Potentiometer = AddChild(new Potentiometer("RV1", PotResistance));
            Jp3 = AddChild(new ThreeWayJumper("JP3", JumperSelection.Pad1));
            Link(Potentiometer.End1, v33);
            Link(Potentiometer.End3, gnd);
            Link(Potentiometer.Wiper, Jp3.Pad1);
            Link(Jp3.Common, Adc.Channel(7));
            for (int ch = 0; ch < 7; ch++) AddPin("J1." + (ch + 1), Adc.Channel(ch));
            AddPin("J1.8", Jp3.Pad3);

            // ---- Conectores de servo J7–J10: 1 = señal, 2 = +5 V, 3 = GND
            for (int i = 0; i < 4; i++)
            {
                string j = "J" + (7 + i);
                AddPin(j + ".1", pi.Gpio(ServoGpio[i]).Pin);
                AddPin(j + ".2", v5);
                AddPin(j + ".3", gnd);
            }

            // ---- I2C J2, alimentación externa J12 y punto de prueba TP1
            AddPin("J2.1", v33);
            AddPin("J2.2", pi.Gpio(3).Pin);
            AddPin("J2.3", pi.Gpio(2).Pin);
            AddPin("J2.4", gnd);
            AddPin("J12.1", v5);
            AddPin("J12.2", gnd);
            AddPin("J12.3", v33);
            AddPin("J12.4", gnd);
            AddPin("TP1", gnd);
        }

        public RaspberryPi Pi { get; }

        /// <summary>LED Dn (n = 1..8).</summary>
        public Led Led(int n) => _leds[CheckIndex(n, 8)];

        /// <summary>Resistencia Rn (n = 1..8).</summary>
        public Resistor LedResistor(int n) => _ledResistors[CheckIndex(n, 8)];

        /// <summary>Pulsador SWn (n = 1..4).</summary>
        public PushButton Button(int n) => _buttons[CheckIndex(n, 4)];

        public IReadOnlyList<Led> Leds => _leds;
        public IReadOnlyList<PushButton> Buttons => _buttons;
        public Potentiometer Potentiometer { get; }
        public Mcp3008 Adc { get; }
        public SolderJumper Jp1 { get; }
        public SolderJumper Jp2 { get; }
        public ThreeWayJumper Jp3 { get; }

        private static int CheckIndex(int n, int count)
        {
            if (n < 1 || n > count) throw new ArgumentOutOfRangeException(nameof(n), n, $"Va de 1 a {count}.");
            return n - 1;
        }
    }
}
