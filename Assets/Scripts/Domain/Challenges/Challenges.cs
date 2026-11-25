using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Wiring;

namespace ImatPiSim.Domain.Challenges
{
    /// <summary>Los 9 retos del juego, en orden de dificultad (docs/requisitos.md §2.8).</summary>
    public static class ChallengeCatalog
    {
        public static readonly IReadOnlyList<Challenge> All = new Challenge[]
        {
            new HelloLed(), new WhilePressed(), new ToggleSwitch(), new FirstCircuit(), new ExternalButton(),
            new BinaryCounter(), new PotentiometerLevel(), new Barrier(), new AutomaticLight()
        };

        public static Challenge ByNumber(int number) =>
            All.FirstOrDefault(c => c.Number == number) ?? throw new ArgumentOutOfRangeException(nameof(number), $"No hay reto {number}.");

        internal static readonly int[] Even = { 2, 4, 6, 8 };
        internal static readonly int[] Odd = { 1, 3, 5, 7 };
        internal static readonly int[] All8 = { 1, 2, 3, 4, 5, 6, 7, 8 };
        internal static readonly int[] None = Array.Empty<int>();
    }

    // ====================================================================== 1

    /// <summary>Reto 1: encender D1 (GPIO20) y mantenerlo.</summary>
    public sealed class HelloLed : Challenge
    {
        public HelloLed() : base(1, "Hola LED",
            "Enciende el LED D1 de la IMAT HAT (GPIO20) y déjalo encendido. El resto, apagados.",
            "Primer programa con gpiozero: LED.on()", null) { }

        protected override void Script(Verifier v)
        {
            v.Run(1.0);
            v.ExpectLeds(new[] { 1 }, "Al segundo 1");
            v.Run(2.0);
            v.ExpectLeds(new[] { 1 }, "Al segundo 3 (sigue encendido)");
        }
    }

    // ====================================================================== 2

    /// <summary>Reto 2: LED pares encendidos solo mientras se pulsa SW4.</summary>
    public sealed class WhilePressed : Challenge
    {
        public WhilePressed() : base(2, "Mientras pulso",
            "Los LED pares (D2, D4, D6 y D8) deben estar encendidos solo mientras se mantiene pulsado SW4 (GPIO19).",
            "Entrada por nivel: is_pressed", null) { }

        protected override void Script(Verifier v)
        {
            var sw4 = v.Hat.Button(4);
            v.Run(0.5);
            v.ExpectLeds(ChallengeCatalog.None, "Sin pulsar");
            for (int i = 1; i <= 2; i++)
            {
                v.Facade.PressButton(sw4);
                v.Run(0.4);
                v.ExpectLeds(ChallengeCatalog.Even, $"Con SW4 pulsado ({i}.ª vez)");
                v.Facade.ReleaseButton(sw4);
                v.Run(0.4);
                v.ExpectLeds(ChallengeCatalog.None, $"Al soltar SW4 ({i}.ª vez)");
            }
        }
    }

    // ====================================================================== 3

    /// <summary>Reto 3: cada pulsación de SW2 alterna los LED impares (flanco, no nivel).</summary>
    public sealed class ToggleSwitch : Challenge
    {
        public ToggleSwitch() : base(3, "Interruptor",
            "Cada pulsación de SW2 (GPIO16) alterna los LED impares (D1, D3, D5 y D7): una pulsación los enciende, la siguiente los apaga. " +
            "Ojo: aunque mantengas pulsado el botón, debe contar como una sola pulsación.",
            "Flanco frente a nivel: when_pressed", null) { }

        protected override void Script(Verifier v)
        {
            v.Run(0.5);
            v.ExpectLeds(ChallengeCatalog.None, "Al empezar");
            Press(v, 0.15, 1, ChallengeCatalog.Odd, "1.ª pulsación (corta)");
            Press(v, 0.8, 2, ChallengeCatalog.None, "2.ª pulsación (larga, 0,8 s)");
            Press(v, 0.15, 3, ChallengeCatalog.Odd, "3.ª pulsación (corta)");
        }

        private static void Press(Verifier v, double hold, int n, int[] expected, string what)
        {
            // Cada cambio real de nivel de GPIO20 (D1) suma 1 a su versión, aunque ocurra varias
            // veces entre dos pasos del verificador: así se cuentan todas las conmutaciones.
            var gpio = v.Circuit.Pi.Gpio(ImatHat.LedGpio[0]);
            int before = gpio.Version;
            v.Click(2, hold, after: 0.35);
            int changes = gpio.Version - before;
            if (changes > 1)
                v.End(ResultKind.MultipleToggles,
                      $"En la {n}.ª pulsación D1 cambió {changes} veces: cada pulsación debe contar una sola vez " +
                      "(usa when_pressed en lugar de leer is_pressed en un bucle).");
            v.ExpectLeds(expected, what);
        }
    }

    // ====================================================================== 4

    /// <summary>Reto 4: LED externo con resistencia en GPIO14, montado en la protoboard.</summary>
    public sealed class FirstCircuit : Challenge
    {
        public FirstCircuit() : base(4, "Mi primer circuito",
            "Monta en la protoboard un LED con su resistencia, controlado por GPIO14 (pin 1 de J7), y enciéndelo desde el programa. " +
            "Ojo con la corriente: un LED soporta 20 mA y un GPIO, 7 mA.",
            "Montaje, resistencia limitadora y A*", new[] { "resistor", "led" }) { }

        protected override void Script(Verifier v)
        {
            v.Run(1.0);
            var gpio = v.Circuit.Pi.Gpio(14);
            var leds = v.External<Led>().ToList();
            v.Expect(leds.Count > 0, "Hay un LED externo en la protoboard");
            var lit = leds.OrderByDescending(l => l.Current).First();
            v.Expect(lit.Current >= 1e-3, $"El LED externo se enciende (≥ 1 mA; tiene {lit.Current * 1e3:0.00} mA)");
            v.Expect(gpio.Mode == PinMode.Output && gpio.Current >= 0.9 * lit.Current,
                     $"La corriente del LED sale de GPIO14 (GPIO14 da {gpio.Current * 1e3:0.00} mA)");
            v.Run(1.0);
            v.Expect(lit.Current >= 1e-3, "Sigue encendido un segundo después");
        }
    }

    // ====================================================================== 5

    /// <summary>Reto 5: pulsador externo en GPIO14 (pull-up) que conmuta los LED de la HAT.</summary>
    public sealed class ExternalButton : Challenge
    {
        public ExternalButton() : base(5, "Pulsador externo",
            "Monta un pulsador en la protoboard conectado a GPIO14 (pin 1 de J7) con pull-up. " +
            "Cada pulsación conmuta los 8 LED de la HAT: todos encendidos, luego todos apagados.",
            "Pull-up y nivel en reposo", new[] { "pushbutton", "resistor" }) { }

        protected override void Script(Verifier v)
        {
            var buttons = v.External<PushButton>().ToList();
            v.Expect(buttons.Count > 0, "Hay un pulsador externo en la protoboard");
            v.Run(0.5);
            var gpio = v.Circuit.Pi.Gpio(14);
            v.Expect(gpio.Mode == PinMode.Input, "GPIO14 está configurado como entrada");
            v.ExpectLeds(ChallengeCatalog.None, "Sin pulsar");

            var button = buttons[0];
            v.Click(button, hold: 0.2, after: 0.4);
            v.ExpectLeds(ChallengeCatalog.All8, "Tras la 1.ª pulsación del pulsador externo");
            v.Click(button, hold: 0.2, after: 0.4);
            v.ExpectLeds(ChallengeCatalog.None, "Tras la 2.ª pulsación");
        }
    }

    // ====================================================================== 6

    /// <summary>Reto 6: contador binario de 8 bits en D1–D8 (D1 = bit menos significativo).</summary>
    public sealed class BinaryCounter : Challenge
    {
        public BinaryCounter() : base(6, "Contador binario",
            "Muestra un contador de 8 bits en los LED: D1 es el bit menos significativo y D8 el más significativo. " +
            "SW1 suma 1, SW2 resta 1 y SW3 lo pone a 0. Empieza en 0.",
            "Operaciones a nivel de bit, varias entradas", null) { }

        protected override void Script(Verifier v)
        {
            v.Run(0.5);
            Expect(v, 0, "Al empezar");
            for (int i = 0; i < 5; i++) v.Click(1);
            Expect(v, 5, "Tras pulsar SW1 cinco veces");
            v.Click(2);
            v.Click(2);
            Expect(v, 3, "Tras pulsar SW2 dos veces");
            v.Click(3);
            Expect(v, 0, "Tras pulsar SW3");
            v.Click(1);
            Expect(v, 1, "Tras pulsar SW1 otra vez");
        }

        private static void Expect(Verifier v, int value, string when) =>
            v.ExpectLeds(Enumerable.Range(0, 8).Where(bit => (value >> bit & 1) == 1).Select(bit => bit + 1),
                         $"{when} el contador vale {value} ({Convert.ToString(value, 2).PadLeft(8, '0')})");
    }

    // ====================================================================== 7

    /// <summary>Reto 7: barra de LED proporcional al potenciómetro RV1 (CH7 del MCP3008).</summary>
    public sealed class PotentiometerLevel : Challenge
    {
        public PotentiometerLevel() : base(7, "Nivel del potenciómetro",
            "Lee el potenciómetro RV1 (canal 7 del MCP3008) y enciende una barra de LED desde D1: " +
            "ninguno con el mando al mínimo, los 8 al máximo y, en medio, en proporción.",
            "ADC, cuantización y escalado", null, spiEnabled: true) { }

        protected override void Script(Verifier v)
        {
            foreach (var position in new[] { 0.0, 0.5, 1.0, 0.25 })
            {
                v.Facade.SetPotentiometer(position);
                v.Run(0.6);
                int expected = (int)Math.Round(8 * position);
                int lit = Enumerable.Range(1, 8).Count(v.IsOn);
                bool bar = Enumerable.Range(1, lit).All(v.IsOn) && Enumerable.Range(lit + 1, 8 - lit).All(v.IsOff);
                v.Expect(bar && Math.Abs(lit - expected) <= 1,
                         $"Con RV1 al {position:P0}: barra de {expected} LED desde D1, ±1 (hay: {v.LitLeds()})");
            }
        }
    }

    // ====================================================================== 8

    /// <summary>Reto 8: barrera con servo; SW4 la abre y la cierra, y se cierra sola a los 10 s.</summary>
    public sealed class Barrier : Challenge
    {
        public const double OpenAngle = 120;
        public const double ClosedAngle = 60;

        public Barrier() : base(8, "Barrera",
            "El servo de J7 (GPIO14) es una barrera. SW4 (GPIO19) la abre si está cerrada y la cierra si está abierta. " +
            "Si se queda abierta, debe cerrarse sola a los 10 s. Abierta: 120° o más; cerrada: 60° o menos.",
            "PWM de servo y temporización sin bloquear", null) { }

        /// <summary>El servo ya viene enchufado a J7: señal, 5 V y GND.</summary>
        public override AssemblyPlan InitialAssembly => new AssemblyPlan(
            new[] { new AssemblyPlan.Part("M1", "servo", new Dictionary<string, double> { ["initialAngle"] = 90, ["maxSpeed"] = Servo.DefaultMaxSpeed }, null, Direction.Right) },
            new[]
            {
                new AssemblyPlan.Link("M1.SIG", "J7.1", Array.Empty<string>()),
                new AssemblyPlan.Link("M1.VCC", "J7.2", Array.Empty<string>()),
                new AssemblyPlan.Link("M1.GND", "J7.3", Array.Empty<string>())
            }, spiEnabled: false);

        public override IReadOnlyList<string> FixedParts => new[] { "M1" };

        protected override void Script(Verifier v)
        {
            var servo = v.External<Servo>().FirstOrDefault();
            v.Expect(servo != null, "El servo está conectado");
            v.Run(1.0);

            v.Click(4, hold: 0.2, after: 1.3);
            v.Expect(servo.Angle >= OpenAngle, $"SW4 abre la barrera (ángulo {servo.Angle:0}°)");
            v.Click(4, hold: 0.2, after: 1.3);
            v.Expect(servo.Angle <= ClosedAngle, $"SW4 otra vez la cierra (ángulo {servo.Angle:0}°)");

            v.Click(4, hold: 0.2, after: 0);
            double opened = v.Time;
            v.Run(1.3);
            v.Expect(servo.Angle >= OpenAngle, $"SW4 la vuelve a abrir (ángulo {servo.Angle:0}°)");
            v.Run(opened + 8 - v.Time);
            v.Expect(servo.Angle >= OpenAngle, $"A los 8 s sigue abierta: no se cierra antes de tiempo (ángulo {servo.Angle:0}°)");
            v.Run(opened + 11 - v.Time);
            v.Expect(servo.Angle <= ClosedAngle, $"A los 11 s se ha cerrado sola (ángulo {servo.Angle:0}°)", ResultKind.TooSlow);
        }
    }

    // ====================================================================== 9

    /// <summary>Reto 9: luz automática; LDR → divisor → ADC (CH0) → D1 con PWM inverso a la luz.</summary>
    public sealed class AutomaticLight : Challenge
    {
        public AutomaticLight() : base(9, "Luz automática",
            "Monta un divisor de tensión con la LDR y lleva su salida al canal 0 del MCP3008 (J1.1). " +
            "El programa debe regular el brillo de D1 con PWM: más brillo cuanta menos luz. " +
            "Cuidado: el ADC trabaja a 3,3 V.",
            "Divisor de tensión, sensor y PWM", new[] { "ldr", "resistor" }, spiEnabled: true) { }

        protected override void Script(Verifier v)
        {
            var ldr = v.External<Ldr>().FirstOrDefault();
            v.Expect(ldr != null, "Hay una LDR en la protoboard");
            var levels = new Dictionary<double, double>();
            foreach (var lux in new[] { 10.0, 100.0, 1000.0 })
            {
                v.Facade.SetIlluminance(ldr, lux);
                v.Run(1.0);
                levels[lux] = v.LedLevel(1);
                v.Log($"Con {lux} lx, D1 al {levels[lux]:P0}");
            }
            v.Expect(levels[10] >= 0.5, $"A oscuras (10 lx) D1 brilla al menos al 50 % (está al {levels[10]:P0})");
            v.Expect(levels[1000] <= 0.35, $"Con mucha luz (1000 lx) D1 brilla como mucho al 35 % (está al {levels[1000]:P0})");
            v.Expect(levels[10] >= levels[100] - 0.02 && levels[100] >= levels[1000] - 0.02 && levels[10] - levels[1000] >= 0.2,
                     "El brillo baja al subir la luz");
        }
    }
}
