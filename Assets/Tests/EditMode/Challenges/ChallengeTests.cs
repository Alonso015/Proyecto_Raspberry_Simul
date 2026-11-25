using System;
using System.Linq;
using ImatPiSim.Domain.Challenges;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Simulation;
using ImatPiSim.Domain.Wiring;
using ImatPiSim.Domain.Wiring.Routing;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Challenges
{
    /// <summary>
    /// Etapa E11: para cada reto, una solución de referencia gana y al menos una solución
    /// equivocada pierde por el motivo esperado.
    /// </summary>
    public class ChallengeTests
    {
        private static Challenge Reto(int n) => ChallengeCatalog.ByNumber(n);

        /// <summary>Monta en un circuito de verdad (con sus reglas) y describe el montaje.</summary>
        private static AssemblyPlan Mount(Action<Circuit> build, bool spi = false)
        {
            var c = Circuit.WithImatHat();
            c.Pi.SpiEnabled = spi;
            build(c);
            return AssemblyPlan.Capture(c);
        }

        private static ChallengeResult Check(int n, string program, AssemblyPlan assembly = null)
        {
            var result = Reto(n).Verify(assembly ?? Reto(n).InitialAssembly, program);
            TestContext.Out.WriteLine(result + "\n" + string.Join("\n", result.Log));
            return result;
        }

        private static void Wins(int n, string program, AssemblyPlan assembly = null)
        {
            var r = Check(n, program, assembly);
            Assert.That(r.Kind, Is.EqualTo(ResultKind.Passed), r.Message);
        }

        private static ChallengeResult Loses(int n, string program, ResultKind why, AssemblyPlan assembly = null)
        {
            var r = Check(n, program, assembly);
            Assert.That(r.Kind, Is.EqualTo(why), r.Message);
            Assert.That(r.IsVictory, Is.False);
            return r;
        }

        [Test]
        public void HayNueveRetosEnOrden() =>
            Assert.That(ChallengeCatalog.All.Select(c => c.Number), Is.EqualTo(Enumerable.Range(1, 9)));

        // ================================================================== 1. Hola LED

        private const string Reto1 =
            "from gpiozero import LED\n" +
            "from signal import pause\n" +
            "led = LED(20)\n" +
            "led.on()\n" +
            "pause()\n";

        [Test] public void Reto1_Gana() => Wins(1, Reto1);

        [Test]
        public void Reto1_SinPause_ElProgramaTerminaYElLedSeApaga()
        {
            var r = Loses(1, "from gpiozero import LED\nLED(20).on()\n", ResultKind.WrongBehaviour);
            Assert.That(r.Message, Does.Contain("terminado"));
        }

        [Test] public void Reto1_OtroLed_Pierde() => Loses(1, Reto1.Replace("LED(20)", "LED(21)"), ResultKind.WrongBehaviour);

        [Test]
        public void ErrorDeSintaxis_NoSePuedeComprobar() =>
            Loses(1, "from gpiozero import LED\nif True\n    pass\n", ResultKind.CannotRun);

        [Test]
        public void ExcepcionDePython_NoSupera()
        {
            var r = Loses(1, "from gpiozero import LED\nx = 1 / 0\n", ResultKind.ProgramError);
            Assert.That(r.Message, Does.Contain("ZeroDivisionError"));
        }

        [Test]
        public void UnaPiezaNoPermitida_NoSePuedeComprobar()
        {
            var plan = Mount(c => c.Place(new Led("LED1"), "a1", Direction.Right));
            var r = Loses(1, Reto1, ResultKind.CannotRun, plan);
            Assert.That(r.Message, Does.Contain("LED1"));
        }

        // ================================================================== 2. Mientras pulso

        private const string Reto2 =
            "from gpiozero import LED, Button\n" +
            "from time import sleep\n" +
            "pares = [LED(21), LED(23), LED(25), LED(27)]\n" +
            "sw4 = Button(19)\n" +
            "while True:\n" +
            "    for led in pares:\n" +
            "        if sw4.is_pressed:\n" +
            "            led.on()\n" +
            "        else:\n" +
            "            led.off()\n" +
            "    sleep(0.01)\n";

        [Test] public void Reto2_Gana() => Wins(2, Reto2);

        [Test]
        public void Reto2_ImparesEnVezDePares_Pierde() =>
            Loses(2, Reto2.Replace("LED(21), LED(23), LED(25), LED(27)", "LED(20), LED(22), LED(24), LED(26)"), ResultKind.WrongBehaviour);

        // ================================================================== 3. Interruptor

        private const string Reto3 =
            "from gpiozero import LED, Button\n" +
            "from signal import pause\n" +
            "impares = [LED(20), LED(22), LED(24), LED(26)]\n" +
            "sw2 = Button(16)\n" +
            "def alternar():\n" +
            "    for led in impares:\n" +
            "        led.toggle()\n" +
            "sw2.when_pressed = alternar\n" +
            "pause()\n";

        /// <summary>Otra solución válida: detectar el flanco a mano, comparando con la lectura anterior.</summary>
        private const string Reto3ConFlancoAMano =
            "from gpiozero import LED, Button\n" +
            "from time import sleep\n" +
            "impares = [LED(20), LED(22), LED(24), LED(26)]\n" +
            "sw2 = Button(16)\n" +
            "anterior = False\n" +
            "while True:\n" +
            "    actual = sw2.is_pressed\n" +
            "    if actual and not anterior:\n" +
            "        for led in impares:\n" +
            "            led.toggle()\n" +
            "    anterior = actual\n" +
            "    sleep(0.01)\n";

        /// <summary>El error típico: leer el nivel en un bucle conmuta mientras el botón sigue pulsado.</summary>
        private const string Reto3PorNivel =
            "from gpiozero import LED, Button\n" +
            "from time import sleep\n" +
            "impares = [LED(20), LED(22), LED(24), LED(26)]\n" +
            "sw2 = Button(16)\n" +
            "while True:\n" +
            "    if sw2.is_pressed:\n" +
            "        for led in impares:\n" +
            "            led.toggle()\n" +
            "    sleep(0.05)\n";

        [Test] public void Reto3_Gana() => Wins(3, Reto3);
        [Test] public void Reto3_ConFlancoAMano_TambienGana() => Wins(3, Reto3ConFlancoAMano);

        [Test]
        public void Reto3_LeyendoElNivel_ConmutaVariasVeces()
        {
            var r = Loses(3, Reto3PorNivel, ResultKind.MultipleToggles);
            Assert.That(r.Message, Does.Contain("when_pressed"));
        }

        // ================================================================== 4. Mi primer circuito

        private const string Gpio14On =
            "from gpiozero import LED\n" +
            "from signal import pause\n" +
            "LED(14).on()\n" +
            "pause()\n";

        /// <summary>J7.1 (GPIO14) → a5 | R b5–b9 | LED c9 (ánodo) – c10 | e10 → T− | J7.3 (GND) → T−.</summary>
        private static AssemblyPlan Reto4Montaje(string supply = "J7.1", bool resistor = true) => Mount(c =>
        {
            c.AddWire(supply, "a5");
            if (resistor)
            {
                c.Place(new Resistor("R1", 330), "b5", Direction.Right);
                c.Place(new Led("LED1"), "c9", Direction.Right);
                c.RouteWire("e10", "T-10", new AStarRouter());
            }
            else
            {
                c.Place(new Led("LED1"), "b5", Direction.Right);
                c.AddWire("e6", "T-6");
            }
            c.AddWire(supply == "J7.1" ? "J7.3" : "J12.4", "T-1");
        });

        [Test] public void Reto4_Gana() => Wins(4, Gpio14On, Reto4Montaje());

        [Test]
        public void Reto4_SinResistencia_QuemaElLed()
        {
            var r = Loses(4, Gpio14On, ResultKind.ElectricalFault, Reto4Montaje(resistor: false));
            Assert.That(new[] { FaultKind.LedOvercurrent, FaultKind.GpioOvercurrent }, Has.Member(r.Fault.Kind));
            Assert.That(r.Message, Does.Contain("mA"));
        }

        [Test]
        public void Reto4_AlimentadoDesde3V3_NoLoControlaElGpio()
        {
            var r = Loses(4, Gpio14On, ResultKind.WrongBehaviour, Reto4Montaje(supply: "J12.3"));
            Assert.That(r.Message, Does.Contain("GPIO14"));
        }

        // ================================================================== 5. Pulsador externo

        private const string Reto5 =
            "from gpiozero import LEDBoard, Button\n" +
            "from signal import pause\n" +
            "leds = LEDBoard(20, 21, 22, 23, 24, 25, 26, 27)\n" +
            "boton = Button(14)\n" +
            "def conmutar():\n" +
            "    leds.toggle()\n" +
            "boton.when_pressed = conmutar\n" +
            "pause()\n";

        /// <summary>Pulsador entre <paramref name="supply"/> y GND: c10–c12, con a10 ← supply y e12 → T−.</summary>
        private static AssemblyPlan Reto5Montaje(string supply) => Mount(c =>
        {
            c.AddWire(supply, "a10");
            c.Place(new PushButton("SW5"), "c10", Direction.Right);
            c.AddWire("e12", "T-12");
            c.AddWire(supply == "J7.1" ? "J7.3" : "J12.4", "T-1");
        });

        [Test] public void Reto5_PulsadorEntreGpio14YGnd_Gana() => Wins(5, Reto5, Reto5Montaje("J7.1"));

        [Test]
        public void Reto5_PulsadorEntre3V3YGnd_Cortocircuito()
        {
            var r = Loses(5, Reto5, ResultKind.ElectricalFault, Reto5Montaje("J12.3"));
            Assert.That(r.Fault.Kind, Is.EqualTo(FaultKind.ShortCircuit));
            Assert.That(r.Message, Does.Contain("SW5"));
        }

        [Test] public void Reto5_SinPulsador_Pierde() => Loses(5, Reto5, ResultKind.WrongBehaviour, AssemblyPlan.Empty());

        // ================================================================== 6. Contador binario

        private const string Reto6 =
            "from gpiozero import LED, Button\n" +
            "from signal import pause\n" +
            "leds = [LED(20), LED(21), LED(22), LED(23), LED(24), LED(25), LED(26), LED(27)]\n" +
            "contador = 0\n" +
            "def mostrar():\n" +
            "    for i in range(8):\n" +
            "        if (contador >> i) & 1:\n" +
            "            leds[i].on()\n" +
            "        else:\n" +
            "            leds[i].off()\n" +
            "def sumar():\n" +
            "    global contador\n" +
            "    contador = (contador + 1) % 256\n" +
            "    mostrar()\n" +
            "def restar():\n" +
            "    global contador\n" +
            "    contador = (contador - 1) % 256\n" +
            "    mostrar()\n" +
            "def reiniciar():\n" +
            "    global contador\n" +
            "    contador = 0\n" +
            "    mostrar()\n" +
            "sw1 = Button(7)\n" +
            "sw2 = Button(16)\n" +
            "sw3 = Button(17)\n" +
            "sw1.when_pressed = sumar\n" +
            "sw2.when_pressed = restar\n" +
            "sw3.when_pressed = reiniciar\n" +
            "mostrar()\n" +
            "pause()\n";

        [Test] public void Reto6_Gana() => Wins(6, Reto6);

        [Test]
        public void Reto6_BitsAlReves_Pierde() =>
            Loses(6, Reto6.Replace("leds[i]", "leds[7 - i]"), ResultKind.WrongBehaviour);

        [Test]
        public void Reto6_ConElSpiActivo_ConflictoEnGpio7()
        {
            var r = Loses(6, Reto6, ResultKind.ElectricalFault, AssemblyPlan.Empty(spiEnabled: true));
            Assert.That(r.Fault.Kind, Is.EqualTo(FaultKind.PinConflict));
        }

        // ================================================================== 7. Nivel del potenciómetro

        private const string Reto7 =
            "from gpiozero import MCP3008, LED\n" +
            "from time import sleep\n" +
            "pot = MCP3008(channel=7)\n" +
            "leds = [LED(20), LED(21), LED(22), LED(23), LED(24), LED(25), LED(26), LED(27)]\n" +
            "while True:\n" +
            "    n = round(pot.value * 8)\n" +
            "    for i in range(8):\n" +
            "        if i < n:\n" +
            "            leds[i].on()\n" +
            "        else:\n" +
            "            leds[i].off()\n" +
            "    sleep(0.05)\n";

        [Test] public void Reto7_Gana() => Wins(7, Reto7);

        [Test]
        public void Reto7_BarraDesdeD8_Pierde() => Loses(7, Reto7.Replace("leds[i]", "leds[7 - i]"), ResultKind.WrongBehaviour);

        [Test]
        public void Reto7_SinSpi_ElAdcNoResponde()
        {
            var r = Loses(7, Reto7, ResultKind.ProgramError, AssemblyPlan.Empty(spiEnabled: false));
            Assert.That(r.Message, Does.Contain("SPI"));
        }

        // ================================================================== 8. Barrera

        private const string Reto8 =
            "from gpiozero import Servo, Button\n" +
            "from time import sleep\n" +
            "servo = Servo(14)\n" +
            "sw4 = Button(19)\n" +
            "abierta = False\n" +
            "desde = 0.0\n" +
            "t = 0.0\n" +
            "servo.min()\n" +
            "def pulsar():\n" +
            "    global abierta, desde\n" +
            "    abierta = not abierta\n" +
            "    if abierta:\n" +
            "        servo.max()\n" +
            "        desde = t\n" +
            "    else:\n" +
            "        servo.min()\n" +
            "sw4.when_pressed = pulsar\n" +
            "while True:\n" +
            "    sleep(0.1)\n" +
            "    t = t + 0.1\n" +
            "    if abierta and t - desde >= 10:\n" +
            "        abierta = False\n" +
            "        servo.min()\n";

        [Test] public void Reto8_Gana() => Wins(8, Reto8);

        [Test]
        public void Reto8_SeCierraA15s_NoLlegaATiempo() =>
            Loses(8, Reto8.Replace(">= 10", ">= 15"), ResultKind.TooSlow);

        [Test]
        public void Reto8_SeCierraA5s_DemasiadoPronto() =>
            Loses(8, Reto8.Replace(">= 10", ">= 5"), ResultKind.WrongBehaviour);

        [Test]
        public void Reto8_ElServoVieneMontado()
        {
            var plan = Reto(8).InitialAssembly;
            Assert.That(plan.Parts.Single().TypeId, Is.EqualTo("servo"));
            Assert.That(plan.Wires.Select(w => w.B), Is.EquivalentTo(new[] { "J7.1", "J7.2", "J7.3" }));
            Assert.That(Reto(8).FixedParts, Has.Member("M1"));
        }

        // ================================================================== 9. Luz automática

        private const string Reto9 =
            "from gpiozero import MCP3008, PWMLED\n" +
            "from time import sleep\n" +
            "ldr = MCP3008(channel=0)\n" +
            "led = PWMLED(20)\n" +
            "while True:\n" +
            "    led.value = 1 - ldr.value\n" +
            "    sleep(0.05)\n";

        /// <summary>
        /// Alimentación → T+ | GND → T− | LDR a20–a21 | T+20 → b20 | R 10 kΩ b21–b25 | c25 → T− | c21 → J1.1 (CH0).
        /// </summary>
        private static AssemblyPlan Reto9Montaje(string supply = "J12.3") => Mount(c =>
        {
            c.AddWire(supply, "T+1");
            c.AddWire("J12.4", "T-1");
            c.Place(new Ldr("LDR1"), "a20", Direction.Right);
            c.AddWire("T+20", "b20");
            c.Place(new Resistor("R1", 10_000), "b21", Direction.Right);
            c.AddWire("c25", "T-25");
            c.AddWire("c21", "J1.1");
        }, spi: true);

        [Test] public void Reto9_Gana() => Wins(9, Reto9, Reto9Montaje());

        [Test]
        public void Reto9_BrilloQueSubeConLaLuz_Pierde() =>
            Loses(9, Reto9.Replace("1 - ldr.value", "ldr.value"), ResultKind.WrongBehaviour, Reto9Montaje());

        [Test]
        public void Reto9_DivisorA5V_DañaElAdc()
        {
            var r = Loses(9, Reto9, ResultKind.ElectricalFault, Reto9Montaje(supply: "J12.1"));
            Assert.That(r.Fault.Kind, Is.EqualTo(FaultKind.Overvoltage));
        }

        // ================================================================== el verificador no toca el montaje del jugador

        [Test]
        public void ComprobarNoCambiaElCircuitoDelJugador()
        {
            var circuit = Circuit.WithImatHat();
            circuit.AddWire("J7.1", "a5");
            circuit.Place(new Led("LED1"), "b5", Direction.Right);   // sin resistencia: en la prueba se quema
            circuit.AddWire("e6", "T-6");
            circuit.AddWire("J7.3", "T-1");
            var led = (Led)circuit.ExternalComponents.Single();

            var r = Reto(4).Verify(AssemblyPlan.Capture(circuit), Gpio14On);
            Assert.That(r.Kind, Is.EqualTo(ResultKind.ElectricalFault));
            Assert.That(led.IsBurnt, Is.False, "se quemó el LED de la copia, no el del jugador");
        }
    }
}
