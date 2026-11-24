using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Api;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Interpreter.Runtime;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Api
{
    /// <summary>
    /// Arnés mínimo de simulación para las pruebas de la E9. En cada paso: el programa ejecuta su
    /// tick → se resuelve el circuito → se detectan flancos (callbacks) → avanzan los servos.
    /// (En la E10 esto lo hará el motor de simulación.)
    /// </summary>
    internal sealed class Sim
    {
        public const double Dt = 0.1;

        public readonly RaspberryPi Pi = new RaspberryPi();
        public readonly ImatHat Hat;
        public readonly ScriptRuntime Rt = new ScriptRuntime();
        public readonly SimulatedPinFactory Pins;
        public readonly NodalSolver Solver = new NodalSolver();
        private readonly List<Servo> _servos = new List<Servo>();
        private Netlist _netlist;

        public Sim(params Servo[] servosOnJ7)
        {
            Hat = new ImatHat(Pi);
            Pins = new SimulatedPinFactory(Pi);
            GpioZeroModule.Register(Rt, Pins, Hat.Adc);
            var b = new NetlistBuilder().AddComponent(Pi).AddComponent(Hat);
            foreach (var s in servosOnJ7)
            {
                b.AddComponent(s)
                 .Connect(s.GetPin("SIG"), Hat.GetPin("J7.1"))
                 .Connect(s.GetPin("VCC"), Hat.GetPin("J7.2"))
                 .Connect(s.GetPin("GND"), Hat.GetPin("J7.3"));
                _servos.Add(s);
            }
            _netlist = b.Build();
            Solver.Solve(_netlist);   // estado inicial antes de arrancar
        }

        public Sim Run(string program)
        {
            Rt.Load(program);
            Rt.Start();
            return this;
        }

        public void Step(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                Rt.Tick(Dt);
                Solver.Solve(_netlist);
                Pins.Poll();
                foreach (var s in _servos) s.Update(Dt);
            }
            if (Rt.State == RuntimeState.Error) Assert.Fail(Rt.Error.Message);
        }

        /// <summary>Pulsa (o suelta) un pulsador de la HAT y deja pasar dos pasos: uno para que el
        /// circuito lo vea y otro para que el programa (o su callback) reaccione.</summary>
        public void Press(int sw) { Hat.Button(sw).Press(); Step(2); }
        public void Release(int sw) { Hat.Button(sw).Release(); Step(2); }

        public bool Lit(int n) => Hat.Led(n).Brightness > 0;

        /// <summary>LEDs encendidos, de D1 a D8, como "10010000".</summary>
        public string Leds => string.Concat(Enumerable.Range(1, 8).Select(n => Lit(n) ? '1' : '0'));
    }

    /// <summary>Etapa E9: API gpiozero simulada (Bridge) contra la IMAT HAT.</summary>
    public class GpioZeroTests
    {
        // ------------------------------------------------------------------ salidas

        [Test]
        public void Led_On_EnciendeD1_YSoloD1()
        {
            var sim = new Sim().Run("from gpiozero import LED\nled = LED(20)\nled.on()\n");
            sim.Step();

            Assert.That(sim.Leds, Is.EqualTo("10000000"));
            Assert.That(sim.Hat.Led(1).Current, Is.EqualTo(1.3 / 320).Within(1e-6), "≈ 4,06 mA: el mismo circuito de la E3");
        }

        [TestCase("'GPIO20'")]
        [TestCase("'BCM20'")]
        [TestCase("'BOARD38'")]   // pin físico 38 = GPIO20 (no hay que confundirlos)
        public void FormasDeIndicarElPin(string pin)
        {
            var sim = new Sim().Run($"from gpiozero import LED\nLED({pin}).on()\n");
            sim.Step();
            Assert.That(sim.Leds, Is.EqualTo("10000000"));
        }

        [Test]
        public void LedBoard_ValorIndicesYLongitud()
        {
            var sim = new Sim().Run(
                "from gpiozero import LEDBoard\n" +
                "leds = LEDBoard(20, 21, 22, 23, 24, 25, 26, 27)\n" +
                "leds.value = (1, 0, 1, 0, 0, 0, 0, 1)\n" +
                "leds[1].on()\n" +
                "n = len(leds)\n" +
                "v = leds.value\n");
            sim.Step();

            Assert.That(sim.Leds, Is.EqualTo("11100001"));
            Assert.That(sim.Rt.GetGlobal("n"), Is.EqualTo(8L));
            Assert.That(PyOps.Repr(sim.Rt.GetGlobal("v")), Is.EqualTo("(1, 1, 1, 0, 0, 0, 0, 1)"));
        }

        [Test]
        public void PwmLed_ValueEsElFactorDeServicio()
        {
            var sim = new Sim().Run("from gpiozero import PWMLED\nled = PWMLED(20)\nled.value = 0.25\n");
            sim.Step();

            // Brillo en HIGH ≈ (1,3 V / 320 Ω) / 20 mA; tolerancia relativa por los puentes de 1 mΩ y gmin.
            double full = (1.3 / 320) / Led.DefaultMaxCurrent;
            Assert.That(sim.Hat.Led(1).Brightness, Is.EqualTo(0.25 * full).Within(0.01).Percent);
            Assert.That(sim.Pi.Gpio(20).Pwm.Frequency, Is.EqualTo(100), "100 Hz por defecto, como gpiozero");
        }

        [Test]
        public void Blink_EnSegundoPlano_SigueLosTiempos()
        {
            // blink(0.1, 0.9): 0,1 s encendido y 0,9 s apagado → periodo de 1 s
            var sim = new Sim().Run("from gpiozero import LED\nfrom signal import pause\nLED(20).blink(0.1, 0.9)\npause()\n");
            var lit = new List<bool>();
            for (int i = 0; i < 20; i++)
            {
                sim.Step();
                lit.Add(sim.Lit(1));
            }

            Assert.That(lit.Count(x => x), Is.EqualTo(2), "encendido 1 de cada 10 ticks de 0,1 s");
            Assert.That(lit[0] && lit[10], Is.True);
        }

        // ------------------------------------------------------------------ entradas (Tema 3 y práctica 3)

        [Test]
        public void LedMientrasPulso_EjemploGpiozeroDelTema3()
        {
            var sim = new Sim().Run(
                "from gpiozero import LED, Button\n" +
                "def main() -> None:\n" +
                "    puls_led = Button(7)\n" +
                "    puls_fin = Button(19)\n" +
                "    led_0 = LED(20)\n" +
                "    while True:\n" +
                "        if puls_led.is_pressed == True:\n" +
                "            led_0.on()\n" +
                "        else:\n" +
                "            led_0.off()\n" +
                "        if puls_fin.is_pressed == True:\n" +
                "            break\n" +
                "if __name__ == \"__main__\":\n" +
                "    try:\n" +
                "        main()\n" +
                "    except KeyboardInterrupt:\n" +
                "        pass\n");
            sim.Step(2);
            Assert.That(sim.Lit(1), Is.False);

            sim.Press(1);    // SW1 = GPIO7
            Assert.That(sim.Lit(1), Is.True);
            sim.Release(1);
            Assert.That(sim.Lit(1), Is.False);

            sim.Press(4);    // SW4 = GPIO19 → break
            Assert.That(sim.Rt.State, Is.EqualTo(RuntimeState.Finished));
        }

        [Test]
        public void ConmutarPorFlanco_ConWhenPressed_Practica3()
        {
            var sim = new Sim().Run(
                "from gpiozero import LEDBoard, Button\n" +
                "from signal import pause\n" +
                "impares = LEDBoard(21, 23, 25, 27)\n" +
                "def alternar():\n" +
                "    impares.toggle()\n" +
                "boton = Button(16)\n" +
                "boton.when_pressed = alternar\n" +
                "pause()\n");
            sim.Step();

            sim.Press(2);
            Assert.That(sim.Leds, Is.EqualTo("01010101"));
            sim.Release(2);
            Assert.That(sim.Leds, Is.EqualTo("01010101"), "soltar no cambia nada: solo cuenta el flanco de pulsar");
            sim.Press(2);
            Assert.That(sim.Leds, Is.EqualTo("00000000"));
        }

        [Test]
        public void ContadorBinario_Practica3()
        {
            var sim = new Sim().Run(
                "from gpiozero import LEDBoard, Button\n" +
                "from signal import pause\n" +
                "leds = LEDBoard(20, 21, 22, 23, 24, 25, 26, 27)\n" +
                "contador = 0\n" +
                "def mostrar():\n" +
                "    bits = []\n" +
                "    for bit in range(8):\n" +
                "        bits.append((contador >> bit) & 0x1)\n" +
                "    leds.value = bits\n" +
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
                "b_mas = Button(7)\nb_menos = Button(16)\nb_cero = Button(17)\n" +
                "b_mas.when_pressed = sumar\n" +
                "b_menos.when_pressed = restar\n" +
                "b_cero.when_pressed = reiniciar\n" +
                "pause()\n");
            sim.Step();

            for (int i = 0; i < 4; i++) { sim.Press(1); sim.Release(1); }
            sim.Press(2); sim.Release(2);

            Assert.That(sim.Rt.GetGlobal("contador"), Is.EqualTo(3L));
            Assert.That(sim.Leds, Is.EqualTo("11000000"), "3 = 0b11 → GPIO20 y GPIO21");

            sim.Press(2); sim.Release(2);
            sim.Press(2); sim.Release(2);
            sim.Press(2); sim.Release(2);
            sim.Press(2); sim.Release(2);   // −1 → 255
            Assert.That(sim.Leds, Is.EqualTo("11111111"));

            sim.Press(3); sim.Release(3);
            Assert.That(sim.Leds, Is.EqualTo("00000000"));
        }

        [Test]
        public void CallbackConLambdaQueCapturaElLed_Codigo3DeLaPractica3()
        {
            var sim = new Sim().Run(
                "from gpiozero import Button, LED\n" +
                "from signal import pause\n" +
                "def al_soltar(led: LED) -> None:\n" +
                "    led.on()\n" +
                "button = Button(16, pull_up=True)\n" +
                "led = LED(20)\n" +
                "button.when_released = lambda led=led: al_soltar(led=led)\n" +
                "pause()\n");
            sim.Step();

            sim.Press(2);
            Assert.That(sim.Lit(1), Is.False);
            sim.Release(2);
            Assert.That(sim.Lit(1), Is.True);
        }

        [Test]
        public void CallbackConUnParametro_RecibeElDispositivo()
        {
            var sim = new Sim().Run(
                "from gpiozero import Button\nfrom signal import pause\n" +
                "quien = None\n" +
                "def pulsado(b):\n    global quien\n    quien = str(b.pin)\n" +
                "Button(17).when_pressed = pulsado\npause()\n");
            sim.Step();
            sim.Press(3);

            Assert.That(sim.Rt.GetGlobal("quien"), Is.EqualTo("GPIO17"));
        }

        [Test]
        public void WaitForPress_EsperaSinBloquear()
        {
            var sim = new Sim().Run(
                "from gpiozero import Button, LED\n" +
                "led = LED(20)\nb = Button(19)\n" +
                "b.wait_for_press()\n" +
                "led.on()\n");
            sim.Step(5);
            Assert.That(sim.Lit(1), Is.False);
            Assert.That(sim.Rt.State, Is.EqualTo(RuntimeState.Running));

            sim.Press(4);
            Assert.That(sim.Lit(1), Is.True);
        }

        // ------------------------------------------------------------------ servo y ADC

        [Test]
        public void AngularServo_ConLosPulsosDelLaboratorio_ElServoLlegaANoventaGrados()
        {
            var servo = new Servo("Servo", initialAngle: 0);
            var sim = new Sim(servo).Run(
                "from gpiozero import AngularServo\n" +
                "from gpiozero.pins.pigpio import PiGPIOFactory\n" +
                "factory = PiGPIOFactory()\n" +
                "servo = AngularServo(14, min_angle=-90, max_angle=90, min_pulse_width=0.0005,\n" +
                "                     max_pulse_width=0.0025, pin_factory=factory)\n" +
                "servo.angle = 0\n");
            sim.Step();

            Assert.That(servo.LastSignal.PulseWidth, Is.EqualTo(0.0015).Within(1e-12), "0° del programa = 1,5 ms");
            Assert.That(servo.TargetAngle, Is.EqualTo(90).Within(1e-9), "= 90° del servo físico");
            sim.Step(3);
            Assert.That(servo.Angle, Is.EqualTo(90).Within(1e-9));
        }

        [Test]
        public void AngularServo_ConLosValoresPorDefectoDeGpiozero_SoloRecorreDe45a135()
        {
            // gpiozero usa 1–2 ms por defecto; los servos del laboratorio esperan 0,5–2,5 ms.
            var servo = new Servo("Servo", initialAngle: 90);
            var sim = new Sim(servo).Run("from gpiozero import AngularServo\ns = AngularServo(14)\ns.angle = -90\n");
            sim.Step();

            Assert.That(servo.TargetAngle, Is.EqualTo(45).Within(1e-9));
        }

        [Test]
        public void Mcp3008_LeeElPotenciometroDeCh7()
        {
            var sim = new Sim().Run("from gpiozero import MCP3008\npot = MCP3008(channel=7)\nfrom time import sleep\nsleep(0.2)\nv = pot.voltage\n");
            sim.Pi.SpiEnabled = true;
            sim.Hat.Potentiometer.Position = 0.5;
            sim.Step(5);

            Assert.That((double)sim.Rt.GetGlobal("v"), Is.EqualTo(1.65).Within(3.3 / 1023));
        }

        [Test]
        public void Mcp3008_ConElSpiDesactivado_DaSpiError()
        {
            var sim = new Sim().Run("from gpiozero import MCP3008\nv = MCP3008(7).value\n");
            sim.Rt.Tick(Sim.Dt);

            Assert.That(sim.Rt.State, Is.EqualTo(RuntimeState.Error));
            Assert.That(sim.Rt.Error.PythonType, Is.EqualTo("SPIError"));
        }

        // ------------------------------------------------------------------ errores y cierre

        [TestCase("from gpiozero import LED\na = LED(20)\nb = LED(20)", "GPIOPinInUse", 3)]
        [TestCase("from gpiozero import LED\na = LED(40)", "PinInvalidPin", 2)]
        [TestCase("from gpiozero import Motor", "ImportError", 1)]
        [TestCase("from gpiozero import Button\nb = Button(7)\nb.when_pressed = 5", "TypeError", 3)]
        [TestCase("from gpiozero import PWMLED\nPWMLED(20).value = 1.5", "OutputDeviceBadValue", 2)]
        [TestCase("from gpiozero import AngularServo\nAngularServo(14).angle = 120", "OutputDeviceBadValue", 2)]
        [TestCase("from gpiozero import LED\nl = LED(20)\nl.close()\nl.on()", "GPIODeviceClosed", 4)]
        [TestCase("from gpiozero import Button\nb = Button(7)\nb.is_pressed = True", "AttributeError", 3)]
        public void ErroresDeLaApi(string src, string type, int line)
        {
            var rt = new ScriptRuntime();
            GpioZeroModule.Register(rt, new SimulatedPinFactory(new RaspberryPi()));
            rt.Load(src);
            rt.Start();
            rt.Tick(0.1);

            Assert.That(rt.State, Is.EqualTo(RuntimeState.Error));
            Assert.That(rt.Error.PythonType, Is.EqualTo(type));
            Assert.That(rt.Error.Line, Is.EqualTo(line));
            TestContext.WriteLine(rt.Error.Message);
        }

        [Test]
        public void Close_LiberaElPin_YSePuedeVolverAUsar()
        {
            var sim = new Sim().Run("from gpiozero import LED\na = LED(20)\na.close()\nb = LED(20)\nb.on()\n");
            sim.Step();
            Assert.That(sim.Lit(1), Is.True);
        }

        [Test]
        public void ExcepcionesDeGpiozero_SonExceptions_YSePuedenCapturar()
        {
            var sim = new Sim().Run(
                "from gpiozero import LED\nok = False\na = LED(20)\n" +
                "try:\n    b = LED(20)\nexcept Exception:\n    ok = True\n");
            sim.Step();
            Assert.That(sim.Rt.GetGlobal("ok"), Is.EqualTo(true));
        }

        // ------------------------------------------------------------------ Bridge

        /// <summary>Otra implementación de pines, sin circuito: demuestra que la API no depende de ella.</summary>
        private sealed class FakePinFactory : IPinFactory
        {
            public readonly Dictionary<int, FakePin> Pins = new Dictionary<int, FakePin>();
            public IPin Reserve(int bcm, string owner) => Pins[bcm] = new FakePin(bcm);
            public void Release(IPin pin) => Pins.Remove(pin.Number);
        }

        private sealed class FakePin : IPin
        {
            public FakePin(int n) => Number = n;
            public int Number { get; }
            public PinFunction Function { get; private set; }
            public bool Level;
            public double Duty;
            public event Action<bool> Edge;
            public void SetFunction(PinFunction f) => Function = f;
            public void SetPull(PinPull p) { }
            public bool Read() => Level;
            public void Write(bool high) { Function = PinFunction.Output; Level = high; }
            public void SetPwm(double f, double d) { Function = PinFunction.Pwm; Duty = d; }
            public void Close() { }
            public void RaiseEdge(bool level) { Level = level; Edge?.Invoke(level); }
        }

        [Test]
        public void Bridge_LaMismaApiFuncionaConOtraImplementacionDePines()
        {
            var fake = new FakePinFactory();
            var rt = new ScriptRuntime();
            GpioZeroModule.Register(rt, fake);
            rt.Load("from gpiozero import LED, PWMLED\nLED(5, active_high=False).on()\nPWMLED(6).value = 0.4\n");
            rt.Start();
            rt.Tick(0.1);

            Assert.That(fake.Pins[5].Level, Is.False, "active_high=False: encendido = nivel bajo");
            Assert.That(fake.Pins[6].Duty, Is.EqualTo(0.4));
        }

        [Test]
        public void Bridge_LosFlancosDeLaImplementacionDisparanLosCallbacks()
        {
            var fake = new FakePinFactory();
            var rt = new ScriptRuntime();
            GpioZeroModule.Register(rt, fake);
            rt.Load("from gpiozero import Button\nfrom signal import pause\nn = 0\n" +
                    "def f():\n    global n\n    n += 1\nButton(3).when_pressed = f\npause()\n");
            rt.Start();
            rt.Tick(0.1);

            fake.Pins[3].RaiseEdge(false);   // pull-up: pulsar = bajar a LOW
            rt.Tick(0.1);
            fake.Pins[3].RaiseEdge(true);
            rt.Tick(0.1);

            Assert.That(rt.GetGlobal("n"), Is.EqualTo(1L));
        }
    }
}
