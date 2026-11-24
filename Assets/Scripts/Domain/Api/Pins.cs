using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Interpreter.Runtime;

namespace ImatPiSim.Domain.Api
{
    // =====================================================================================
    // Patrón BRIDGE (como la "pin factory" de gpiozero real)
    //
    //   Abstracción:     las clases de la API (LED, Button, AngularServo...), en GpioZero*.cs.
    //   Implementación:  IPin / IPinFactory, que dicen CÓMO se maneja un pin.
    //
    // gpiozero real hace exactamente esto: el mismo LED(20) funciona sobre RPi.GPIO, pigpio o
    // pines simulados según la "pin factory". Aquí la implementación principal es
    // SimulatedPinFactory, que mueve los GpioPin del modelo eléctrico; los tests usan otra
    // (FakePinFactory) para probar la API sin circuito. Las dos jerarquías varían por separado.
    // =====================================================================================

    /// <summary>Función de un pin vista desde la API.</summary>
    public enum PinFunction
    {
        Input,
        Output,
        Pwm
    }

    /// <summary>Resistencia interna de la entrada.</summary>
    public enum PinPull
    {
        None,
        Up,
        Down
    }

    /// <summary>Implementación de un pin (lado "implementación" del Bridge).</summary>
    public interface IPin
    {
        int Number { get; }
        PinFunction Function { get; }

        void SetFunction(PinFunction function);
        void SetPull(PinPull pull);

        /// <summary>Nivel leído (entrada) o escrito (salida).</summary>
        bool Read();

        void Write(bool high);

        /// <summary>Genera PWM (pasa el pin a <see cref="PinFunction.Pwm"/> si no lo estaba).</summary>
        void SetPwm(double frequency, double dutyCycle);

        /// <summary>Se lanza cuando cambia el nivel leído de una entrada (flanco), con el nivel nuevo.</summary>
        event Action<bool> Edge;

        /// <summary>Libera el pin.</summary>
        void Close();
    }

    /// <summary>Reserva pines para los dispositivos (cada pin solo puede tener un dueño).</summary>
    public interface IPinFactory
    {
        /// <exception cref="PyException">PinInvalidPin o GPIOPinInUse.</exception>
        IPin Reserve(int bcm, string owner);

        void Release(IPin pin);
    }

    /// <summary>Utilidades comunes a todas las fábricas de pines.</summary>
    public static class PinErrors
    {
        static PinErrors()
        {
            PyException.RegisterType("GPIOZeroError");
            PyException.RegisterType("GPIOPinInUse", "GPIOZeroError");
            PyException.RegisterType("PinInvalidPin", "GPIOZeroError");
            PyException.RegisterType("OutputDeviceBadValue", "GPIOZeroError");
            PyException.RegisterType("SPIError", "GPIOZeroError");
        }

        /// <summary>Fuerza el registro de los tipos de excepción de gpiozero.</summary>
        public static void EnsureRegistered() { }

        public static PyException InUse(int bcm, string owner) =>
            new PyException("GPIOPinInUse", $"el pin GPIO{bcm} ya lo está usando {owner}");

        public static PyException Invalid(object pin) =>
            new PyException("PinInvalidPin", $"'{PyOps.Str(pin)}' no es un GPIO válido: usa números BCM del 0 al 27 (p. ej. LED(20))");
    }

    /// <summary>
    /// Fábrica que conecta la API con los <see cref="GpioPin"/> de la Raspberry Pi simulada.
    /// </summary>
    /// <remarks>
    /// Las entradas se leen con la tensión calculada en la última resolución del circuito. Tras
    /// cada resolución hay que llamar a <see cref="Poll"/> para detectar flancos y avisar a los
    /// dispositivos (que encolan sus callbacks <c>when_pressed</c>...).
    /// </remarks>
    public sealed class SimulatedPinFactory : IPinFactory
    {
        private readonly RaspberryPi _pi;
        private readonly Dictionary<int, SimulatedPin> _reserved = new Dictionary<int, SimulatedPin>();

        public SimulatedPinFactory(RaspberryPi pi)
        {
            _pi = pi ?? throw new ArgumentNullException(nameof(pi));
            PinErrors.EnsureRegistered();
        }

        public IPin Reserve(int bcm, string owner)
        {
            if (bcm < 0 || bcm >= RaspberryPi.GpioCount) throw PinErrors.Invalid(bcm);
            if (_reserved.TryGetValue(bcm, out var existing)) throw PinErrors.InUse(bcm, existing.Owner);
            var pin = new SimulatedPin(_pi.Gpio(bcm), owner);
            _reserved[bcm] = pin;
            return pin;
        }

        public void Release(IPin pin)
        {
            if (pin is SimulatedPin sp && _reserved.TryGetValue(sp.Number, out var r) && r == sp)
            {
                _reserved.Remove(sp.Number);
                sp.Close();
            }
        }

        /// <summary>Detecta flancos en las entradas reservadas (llamar tras resolver el circuito).</summary>
        public void Poll()
        {
            foreach (var pin in new List<SimulatedPin>(_reserved.Values)) pin.Poll();
        }

        /// <summary>Libera todos los pines (al reiniciar un reto).</summary>
        public void ReleaseAll()
        {
            foreach (var pin in _reserved.Values) pin.Gpio.Release();
            _reserved.Clear();
        }

        private sealed class SimulatedPin : IPin
        {
            private bool _lastLevel;
            private PinPull _pull;

            public SimulatedPin(GpioPin gpio, string owner)
            {
                Gpio = gpio;
                Owner = owner;
            }

            public GpioPin Gpio { get; }
            public string Owner { get; }
            public int Number => Gpio.Bcm;
            public PinFunction Function { get; private set; } = PinFunction.Input;
            public event Action<bool> Edge;

            public void SetFunction(PinFunction function)
            {
                Function = function;
                switch (function)
                {
                    case PinFunction.Input:
                        Gpio.ConfigureAsInput(ToPull(_pull));
                        _lastLevel = Gpio.Read();
                        break;
                    case PinFunction.Output:
                        Gpio.ConfigureAsOutput(false);
                        break;
                    case PinFunction.Pwm:
                        Gpio.ConfigureAsPwm(100, 0);
                        break;
                }
            }

            public void SetPull(PinPull pull)
            {
                _pull = pull;
                if (Function == PinFunction.Input) Gpio.ConfigureAsInput(ToPull(pull));
                // Antes de la primera resolución, el nivel esperado en reposo es el que fija la pull.
                if (pull == PinPull.Up) _lastLevel = true;
                else if (pull == PinPull.Down) _lastLevel = false;
            }

            public bool Read() => Function == PinFunction.Output ? Gpio.OutputLevel : Gpio.Read();

            public void Write(bool high)
            {
                if (Function != PinFunction.Output) SetFunction(PinFunction.Output);
                Gpio.Write(high);
            }

            public void SetPwm(double frequency, double dutyCycle)
            {
                if (Function != PinFunction.Pwm || Gpio.Pwm == null || Math.Abs(Gpio.Pwm.Frequency - frequency) > 1e-12)
                {
                    Function = PinFunction.Pwm;
                    Gpio.ConfigureAsPwm(frequency, dutyCycle);
                }
                else Gpio.SetDutyCycle(dutyCycle);
            }

            public void Poll()
            {
                if (Function != PinFunction.Input) return;
                bool level = Gpio.Read();
                if (level == _lastLevel) return;
                _lastLevel = level;
                Edge?.Invoke(level);
            }

            public void Close()
            {
                Edge = null;
                Gpio.Release();
            }

            private static PullMode ToPull(PinPull p) =>
                p == PinPull.Up ? PullMode.Up : p == PinPull.Down ? PullMode.Down : PullMode.None;
        }
    }
}
