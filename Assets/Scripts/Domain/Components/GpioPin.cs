using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>Configuración de un pin GPIO.</summary>
    public enum PinMode
    {
        Input,
        Output,
        /// <summary>Salida PWM (por software o por hardware): alterna entre 3,3 V y 0 V.</summary>
        Pwm
    }

    /// <summary>Resistencia interna del chip conectada a la entrada.</summary>
    public enum PullMode
    {
        None,
        Up,
        Down
    }

    /// <summary>Interpretación lógica de la tensión de una entrada.</summary>
    public enum InputLevel
    {
        Low,
        High,
        /// <summary>Entre los umbrales V<sub>IL</sub> y V<sub>IH</sub>: el chip no garantiza qué leerá.</summary>
        Undefined
    }

    /// <summary>
    /// Pin GPIO de la Raspberry Pi visto desde el circuito.
    /// </summary>
    /// <remarks>
    /// <para><b>Salida:</b> equivalente de Norton de una fuente de 3,3 V (HIGH) o 0 V (LOW) con
    /// resistencia interna <see cref="OutputResistance"/>. Por eso la tensión del pin cae un poco
    /// cuando entrega corriente.</para>
    /// <para><b>Entrada:</b> alta impedancia ideal (no consume corriente) más, si se pide, la
    /// pull-up (hacia 3,3 V) o pull-down (hacia 0 V) interna de <see cref="PullResistance"/>.</para>
    /// <para>Los valores por defecto son <b>supuestos de la simulación</b> (no aparecen en las
    /// fuentes del proyecto); ver docs/requisitos.md §2.6.</para>
    /// </remarks>
    public sealed class GpioPin : IElectricalElement, IFaultSource
    {
        public const double SupplyVoltage = 3.3;

        /// <summary>Supuesto: resistencia de salida del pin (Ω).</summary>
        public const double DefaultOutputResistance = 40.0;

        /// <summary>Supuesto: pull-up / pull-down interna (Ω).</summary>
        public const double DefaultPullResistance = 50_000.0;

        /// <summary>Supuesto: por debajo de esta tensión la entrada se lee LOW (V).</summary>
        public const double DefaultInputLowMax = 0.8;

        /// <summary>Supuesto: por encima de esta tensión la entrada se lee HIGH (V).</summary>
        public const double DefaultInputHighMin = 2.0;

        /// <summary>
        /// Límite de corriente por pin (A). Decidido el 17/11/2026 a partir de las diapositivas
        /// (docs/hardware.md §1.2): por encima, el GPIO se daña.
        /// </summary>
        public const double MaxCurrent = 0.007;

        /// <summary>
        /// Tensión máxima admisible en el pin (V). La lógica es de 3,3 V; el margen de 0,1 V es
        /// un supuesto para no confundir el redondeo numérico con un fallo.
        /// </summary>
        public const double MaxVoltage = SupplyVoltage + 0.1;

        private readonly Terminal[] _terminals;
        private bool _lastReadLevel;

        public GpioPin(int bcm,
                       double outputResistance = DefaultOutputResistance,
                       double pullResistance = DefaultPullResistance,
                       double inputLowMax = DefaultInputLowMax,
                       double inputHighMin = DefaultInputHighMin)
        {
            if (bcm < 0) throw new ArgumentOutOfRangeException(nameof(bcm));
            if (!(outputResistance > 0)) throw new ArgumentOutOfRangeException(nameof(outputResistance));
            if (!(pullResistance > 0)) throw new ArgumentOutOfRangeException(nameof(pullResistance));
            if (!(inputLowMax < inputHighMin)) throw new ArgumentException("Debe cumplirse V_IL < V_IH.");

            Bcm = bcm;
            OutputResistance = outputResistance;
            PullResistance = pullResistance;
            InputLowMax = inputLowMax;
            InputHighMin = inputHighMin;
            Pin = new Terminal("GPIO" + bcm);
            _terminals = new[] { Pin };
        }

        public int Bcm { get; }
        public Terminal Pin { get; }
        public IReadOnlyList<Terminal> Terminals => _terminals;

        public double OutputResistance { get; }
        public double PullResistance { get; }
        public double InputLowMax { get; }
        public double InputHighMin { get; }

        /// <summary>Al arrancar, un GPIO de la Pi es una entrada sin pull (como tras <c>GPIO.cleanup()</c>).</summary>
        public PinMode Mode { get; private set; } = PinMode.Input;
        public PullMode Pull { get; private set; } = PullMode.None;

        /// <summary>
        /// El programa ha configurado este pin (como entrada, salida o PWM). Sirve para detectar
        /// conflictos con funciones como SPI (docs/hardware.md §3.9).
        /// </summary>
        public bool IsInUse { get; private set; }

        /// <summary>
        /// Se incrementa cada vez que el programa cambia la configuración o la salida del pin. El
        /// motor de simulación lo usa para saber si hay que volver a resolver el circuito.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>Señal que genera el pin en modo <see cref="PinMode.Pwm"/>; <c>null</c> en otro caso.</summary>
        public PwmSignal Pwm { get; private set; }

        /// <summary>Nivel que el programa ha escrito (solo cuenta en modo salida).</summary>
        public bool OutputLevel { get; private set; }

        /// <summary>Tensión del pin en la última resolución (V).</summary>
        public double Voltage { get; private set; }

        /// <summary>
        /// Corriente que <b>sale</b> del pin hacia el circuito (A). Negativa si el pin la absorbe.
        /// </summary>
        public double Current { get; private set; }

        /// <summary>El pin se ha dañado por sobrecorriente o sobretensión (hasta <see cref="Reset"/>).</summary>
        public bool IsDamaged => _faults.Count > 0;

        private readonly List<Fault> _faults = new List<Fault>();

        /// <summary>Lectura lógica de la tensión del pin en la última resolución.</summary>
        public InputLevel Level { get; private set; } = InputLevel.Low;

        public void ConfigureAsInput(PullMode pull = PullMode.None)
        {
            bool pullChanged = Mode != PinMode.Input || Pull != pull;
            Mode = PinMode.Input;
            Pull = pull;
            Pwm = null;
            IsInUse = true;
            Version++;

            // En la placa real la pull actúa al instante. Aquí la tensión del pin no se conoce hasta
            // la siguiente resolución del circuito, así que, mientras tanto, se lee el nivel que
            // impone la pull (si un pulsador ya pulsado lo contradice, la próxima resolución lo
            // corrige). Sin esto, un Button recién creado leería "pulsado" con la tensión vieja.
            if (pullChanged && pull != PullMode.None)
            {
                Level = pull == PullMode.Up ? InputLevel.High : InputLevel.Low;
                _lastReadLevel = pull == PullMode.Up;
            }
        }

        public void ConfigureAsOutput(bool initialLevel = false)
        {
            Mode = PinMode.Output;
            Pull = PullMode.None;   // en salida no tiene sentido la pull
            OutputLevel = initialLevel;
            Pwm = null;
            IsInUse = true;
            Version++;
        }

        /// <summary>Configura el pin como salida PWM (como <c>PWMLED</c> o <c>AngularServo</c>).</summary>
        public void ConfigureAsPwm(double frequency, double dutyCycle)
        {
            Mode = PinMode.Pwm;
            Pull = PullMode.None;
            Pwm = new PwmSignal(frequency, dutyCycle);
            IsInUse = true;
            Version++;
        }

        /// <summary>Cambia el factor de servicio manteniendo la frecuencia.</summary>
        public void SetDutyCycle(double dutyCycle)
        {
            if (Mode != PinMode.Pwm) throw new InvalidOperationException($"GPIO{Bcm} no está en modo PWM.");
            Pwm = new PwmSignal(Pwm.Frequency, dutyCycle);
            Version++;
        }

        /// <summary>Escribe un nivel (como <c>GPIO.output</c> o <c>led.on()</c>).</summary>
        /// <exception cref="InvalidOperationException">Si el pin no está configurado como salida.</exception>
        public void Write(bool level)
        {
            if (Mode != PinMode.Output)
                throw new InvalidOperationException($"GPIO{Bcm} no está configurado como salida.");
            if (OutputLevel != level) Version++;
            OutputLevel = level;
        }

        /// <summary>
        /// Lee el nivel lógico (como <c>GPIO.input</c> o <c>button.value</c>). En la zona indefinida
        /// devuelve la última lectura válida: aproximación de la histéresis de la entrada.
        /// </summary>
        public bool Read()
        {
            if (Level == InputLevel.High) _lastReadLevel = true;
            else if (Level == InputLevel.Low) _lastReadLevel = false;
            return _lastReadLevel;
        }

        public void Stamp(IStampContext ctx)
        {
            if (Mode == PinMode.Output)
            {
                ctx.AddConductanceTo(Pin.Node, 1.0 / OutputResistance, OutputLevel ? SupplyVoltage : 0.0);
                return;
            }
            if (Mode == PinMode.Pwm)
            {
                // Propuesta A (docs/arquitectura.md §6.4): se resuelve en HIGH para comprobar las
                // corrientes de pico; LEDs y servos interpretan después la señal declarada.
                // Con un factor de servicio 0 el pin nunca sube: se resuelve en LOW.
                bool high = Pwm.DutyCycle > 0;
                ctx.AddConductanceTo(Pin.Node, 1.0 / OutputResistance, high ? SupplyVoltage : 0.0);
                ctx.DeclarePwm(Pin.Node, Pwm);
                return;
            }
            if (Pull == PullMode.Up) ctx.AddConductanceTo(Pin.Node, 1.0 / PullResistance, SupplyVoltage);
            else if (Pull == PullMode.Down) ctx.AddConductanceTo(Pin.Node, 1.0 / PullResistance, 0.0);
            // PullMode.None: entrada ideal de alta impedancia, no estampa nada.
        }

        public void OnSolved(ISolution solution)
        {
            Voltage = solution.VoltageAt(Pin.Node);

            if (Mode == PinMode.Output)
                Current = ((OutputLevel ? SupplyVoltage : 0.0) - Voltage) / OutputResistance;
            else if (Mode == PinMode.Pwm)
                Current = ((Pwm.DutyCycle > 0 ? SupplyVoltage : 0.0) - Voltage) / OutputResistance;   // pico
            else if (Pull == PullMode.Up)
                Current = (SupplyVoltage - Voltage) / PullResistance;
            else if (Pull == PullMode.Down)
                Current = (0.0 - Voltage) / PullResistance;
            else
                Current = 0.0;

            Level = Voltage <= InputLowMax ? InputLevel.Low
                  : Voltage >= InputHighMin ? InputLevel.High
                  : InputLevel.Undefined;

            // Un daño no se "cura" solo: se registra la primera vez que ocurre cada tipo.
            if (Math.Abs(Current) > MaxCurrent && !_faults.Exists(f => f.Kind == FaultKind.GpioOvercurrent))
                _faults.Add(new Fault(FaultKind.GpioOvercurrent, "GPIO" + Bcm, Math.Abs(Current), MaxCurrent, "A"));
            if (Voltage > MaxVoltage && !_faults.Exists(f => f.Kind == FaultKind.Overvoltage))
                _faults.Add(new Fault(FaultKind.Overvoltage, "GPIO" + Bcm, Voltage, SupplyVoltage, "V"));
        }

        public IEnumerable<Fault> CheckFaults() => _faults;

        /// <summary>
        /// Libera el pin (como <c>close()</c> en gpiozero): vuelve a ser una entrada sin pull y deja
        /// de estar en uso. A diferencia de <see cref="Reset"/>, no borra los daños.
        /// </summary>
        public void Release()
        {
            Version++;
            Mode = PinMode.Input;
            Pull = PullMode.None;
            OutputLevel = false;
            Pwm = null;
            IsInUse = false;
        }

        /// <summary>Repone el pin: entrada sin pull y sin daños (nuevo intento del reto).</summary>
        public void Reset()
        {
            Version++;
            _faults.Clear();
            Mode = PinMode.Input;
            Pull = PullMode.None;
            OutputLevel = false;
            Pwm = null;
            IsInUse = false;
            Voltage = 0;
            Current = 0;
            Level = InputLevel.Low;
            _lastReadLevel = false;
        }

        public override string ToString() =>
            $"GPIO{Bcm} ({Mode}{(Mode == PinMode.Input ? ", pull " + Pull : "")}{(Mode == PinMode.Pwm ? ", " + Pwm : "")})";
    }
}
