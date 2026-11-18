using System;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using NUnit.Framework;

namespace ImatPiSim.Domain.Tests.Components
{
    /// <summary>Etapa E5: PWM (propuesta A), servo, MCP3008 y conflictos de pines.</summary>
    public class PwmServoAdcTests
    {
        private RaspberryPi _pi;
        private ImatHat _hat;
        private NodalSolver _solver;

        [SetUp]
        public void SetUp()
        {
            _pi = new RaspberryPi();
            _hat = new ImatHat(_pi);
            _solver = new NodalSolver();
        }

        private Netlist Build(params Component[] extra)
        {
            var b = new NetlistBuilder().AddComponent(_pi).AddComponent(_hat);
            foreach (var c in extra) b.AddComponent(c);
            return b.Build();
        }

        // ------------------------------------------------------------------ PWM y LED

        [Test]
        public void T1_Pwm25PorCientoEnGpio20_D1BrillaUnCuartoDeLoQueBrillaEnHigh()
        {
            var netlist = Build();

            _pi.Gpio(20).ConfigureAsOutput(true);
            _solver.Solve(netlist);
            double brilloEnHigh = _hat.Led(1).Brightness;

            _pi.Gpio(20).ConfigureAsPwm(100, 0.25);   // 100 Hz, como en la práctica 4
            _solver.Solve(netlist);

            Assert.That(_hat.Led(1).DutyFactor, Is.EqualTo(0.25));
            Assert.That(_hat.Led(1).Brightness, Is.EqualTo(0.25 * brilloEnHigh).Within(1e-12));
            // La corriente que se calcula es la de pico (estado HIGH), la misma que en continua.
            Assert.That(_hat.Led(1).Current, Is.EqualTo(brilloEnHigh * Led.DefaultMaxCurrent).Within(1e-12));
        }

        [Test]
        public void LedSinResistenciaConPwmDel10PorCiento_SeQuemaIgual_PorLaCorrienteDePico()
        {
            // En cada pulso circulan 26 mA aunque la media sea pequeña: el LED se quema.
            var gpio = _pi.Gpio(18);
            var led = new Led("Dext");
            var netlist = new NetlistBuilder().AddComponent(_pi).AddComponent(led)
                .Connect(gpio.Pin, led.Anode).Connect(led.Cathode, _pi.Ground.Terminal).Build();
            gpio.ConfigureAsPwm(1000, 0.10);

            _solver.Solve(netlist);

            Assert.That(led.IsBurnt, Is.True);
            Assert.That(FaultDetector.Detect(netlist).Select(f => f.Kind), Does.Contain(FaultKind.LedOvercurrent));
        }

        [Test]
        public void LaSenalPwmNoAtraviesaLosRailes()
        {
            // D1 con PWM y D5 en continua comparten GND, pero el raíl corta la señal.
            var netlist = Build();
            _pi.Gpio(20).ConfigureAsPwm(100, 0.25);
            _pi.Gpio(24).ConfigureAsOutput(true);

            _solver.Solve(netlist);

            Assert.That(_hat.Led(1).DutyFactor, Is.EqualTo(0.25));
            Assert.That(_hat.Led(5).DutyFactor, Is.EqualTo(1.0));
        }

        [Test]
        public void PwmDelCeroPorCiento_ElPinNoSube()
        {
            var netlist = Build();
            _pi.Gpio(20).ConfigureAsPwm(100, 0.0);

            _solver.Solve(netlist);

            Assert.That(_hat.Led(1).Current, Is.EqualTo(0).Within(1e-9));
            Assert.That(_hat.Led(1).Brightness, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ Servo

        private (Netlist netlist, Servo servo) ServoOnJ7(double initialAngle = 0)
        {
            var servo = new Servo("Servo", initialAngle);
            var netlist = new NetlistBuilder().AddComponent(_pi).AddComponent(_hat).AddComponent(servo)
                .Connect(servo.GetPin("SIG"), _hat.GetPin("J7.1"))    // GPIO14
                .Connect(servo.GetPin("VCC"), _hat.GetPin("J7.2"))    // 5 V
                .Connect(servo.GetPin("GND"), _hat.GetPin("J7.3"))
                .Build();
            return (netlist, servo);
        }

        [Test]
        public void T2_PulsoDe1Coma5ms_PideNoventaGrados_YElEjeLlegaConElTiempo()
        {
            var (netlist, servo) = ServoOnJ7(initialAngle: 0);
            _pi.Gpio(14).ConfigureAsPwm(50, 0.075);   // 50 Hz → periodo 20 ms; 7,5 % → 1,5 ms

            _solver.Solve(netlist);

            Assert.That(servo.LastSignal.PulseWidth, Is.EqualTo(0.0015).Within(1e-12));
            Assert.That(servo.TargetAngle, Is.EqualTo(90).Within(1e-9), "la orden cambia al instante...");
            Assert.That(servo.Angle, Is.EqualTo(0), "...pero el eje aún no se ha movido");

            servo.Update(0.05);   // 600 °/s · 0,05 s = 30°
            Assert.That(servo.Angle, Is.EqualTo(30).Within(1e-9));

            servo.Update(0.2);    // llega y se queda en 90°
            Assert.That(servo.Angle, Is.EqualTo(90).Within(1e-9));
        }

        [TestCase(0.0005, 0)]
        [TestCase(0.0025, 180)]
        [TestCase(0.0030, 180)]   // fuera de rango: tope mecánico
        [TestCase(0.0002, 0)]
        public void AnchoDePulso_SeConvierteEnAngulo(double pulse, double angle)
        {
            Assert.That(Servo.AngleForPulse(pulse), Is.EqualTo(angle).Within(1e-9));
        }

        [Test]
        public void ServoSinAlimentacion_NoSeMueve()
        {
            var servo = new Servo("Servo", 0);
            var netlist = new NetlistBuilder().AddComponent(_pi).AddComponent(servo)
                .Connect(servo.Signal, _pi.Gpio(14).Pin).Build();   // solo la señal, sin 5 V ni GND
            _pi.Gpio(14).ConfigureAsPwm(50, 0.075);

            _solver.Solve(netlist);
            servo.Update(1.0);

            Assert.That(servo.IsPowered, Is.False);
            Assert.That(servo.Angle, Is.EqualTo(0));
        }

        [Test]
        public void ServoSinSenal_MantieneSuPosicion()
        {
            var (netlist, servo) = ServoOnJ7(initialAngle: 45);

            _solver.Solve(netlist);
            servo.Update(1.0);

            Assert.That(servo.LastSignal, Is.Null);
            Assert.That(servo.Angle, Is.EqualTo(45));
        }

        // ------------------------------------------------------------------ MCP3008

        [Test]
        public void T3_Rv1AlCincuentaPorCiento_Ch7LeeUnos512()
        {
            var netlist = Build();
            _pi.SpiEnabled = true;
            _hat.Potentiometer.Position = 0.5;

            _solver.Solve(netlist);

            // 1024 · 1,65 / 3,3 = 512,0 exacto: es justo la frontera entre los códigos 511 y 512.
            // gmin deja el nodo en 1,6499999… V y ⌊…⌋ da 511. Un ADC real también "bailaría"
            // entre los dos por el ruido, así que se aceptan ambos.
            int code = _hat.Adc.Read(7);
            Assert.That(code, Is.InRange(511, 512));
            Assert.That(_hat.Adc.ReadValue(7), Is.EqualTo(code / 1023.0).Within(1e-12));
            Assert.That(_hat.Adc.ReadVoltage(7), Is.EqualTo(1.65).Within(3.3 / 1023));   // ±1 LSB
        }

        [TestCase(0.0, 0)]
        [TestCase(3.3, 1023)]
        [TestCase(5.0, 1023)]   // saturación
        [TestCase(-0.2, 0)]
        public void Cuantizacion_De10Bits(double volts, int code)
        {
            Assert.That(Mcp3008.Quantize(volts), Is.EqualTo(code));
        }

        [Test]
        public void LeerElAdcConElSpiDesactivado_EsUnError()
        {
            var netlist = Build();
            _solver.Solve(netlist);

            var ex = Assert.Throws<InvalidOperationException>(() => _hat.Adc.Read(7));
            Assert.That(ex.Message, Does.Contain("SPI"));
        }

        [Test]
        public void CincoVoltiosEnUnCanalDelAdc_EsSobretension()
        {
            var netlist = new NetlistBuilder().AddComponent(_pi).AddComponent(_hat)
                .Connect(_hat.GetPin("J12.1"), _hat.GetPin("J1.1"))   // 5 V directo a CH0
                .Build();

            _solver.Solve(netlist);

            var fault = FaultDetector.Detect(netlist).Single();
            Assert.That(fault.Kind, Is.EqualTo(FaultKind.Overvoltage));
            Assert.That(fault.SourceId, Is.EqualTo("U1.CH0"));
        }

        // ------------------------------------------------------------------ Conflictos

        [Test]
        public void T4_SpiActivoYPulsadorSw1EnUso_EsConflictoDePines()
        {
            var netlist = Build();
            _pi.SpiEnabled = true;
            _pi.Gpio(7).ConfigureAsInput(PullMode.Up);   // el programa usa SW1

            _solver.Solve(netlist);

            var fault = FaultDetector.Detect(netlist, _pi).Single();
            Assert.That(fault.Kind, Is.EqualTo(FaultKind.PinConflict));
            Assert.That(fault.Message, Does.Contain("GPIO7"));
        }

        [Test]
        public void SpiDesactivado_Sw1SePuedeUsar()
        {
            var netlist = Build();
            _pi.Gpio(7).ConfigureAsInput(PullMode.Up);

            _solver.Solve(netlist);

            Assert.That(FaultDetector.Detect(netlist, _pi), Is.Empty);
        }

        [Test]
        public void SpiActivoSinUsarSusPines_NoHayConflicto()
        {
            var netlist = Build();
            _pi.SpiEnabled = true;
            _pi.Gpio(16).ConfigureAsInput(PullMode.Up);   // SW2: no es del SPI0

            _solver.Solve(netlist);

            Assert.That(FaultDetector.Detect(netlist, _pi), Is.Empty);
        }
    }
}
