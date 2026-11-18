using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>Configuración de un pin GPIO. PWM llegará en la etapa E5.</summary>
    public enum PinMode
    {
        Input,
        Output
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
    public sealed class GpioPin : IElectricalElement
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

        /// <summary>Nivel que el programa ha escrito (solo cuenta en modo salida).</summary>
        public bool OutputLevel { get; private set; }

        /// <summary>Tensión del pin en la última resolución (V).</summary>
        public double Voltage { get; private set; }

        /// <summary>
        /// Corriente que <b>sale</b> del pin hacia el circuito (A). Negativa si el pin la absorbe.
        /// </summary>
        public double Current { get; private set; }

        /// <summary>Lectura lógica de la tensión del pin en la última resolución.</summary>
        public InputLevel Level { get; private set; } = InputLevel.Low;

        public void ConfigureAsInput(PullMode pull = PullMode.None)
        {
            Mode = PinMode.Input;
            Pull = pull;
        }

        public void ConfigureAsOutput(bool initialLevel = false)
        {
            Mode = PinMode.Output;
            Pull = PullMode.None;   // en salida no tiene sentido la pull
            OutputLevel = initialLevel;
        }

        /// <summary>Escribe un nivel (como <c>GPIO.output</c> o <c>led.on()</c>).</summary>
        /// <exception cref="InvalidOperationException">Si el pin no está configurado como salida.</exception>
        public void Write(bool level)
        {
            if (Mode != PinMode.Output)
                throw new InvalidOperationException($"GPIO{Bcm} no está configurado como salida.");
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
            if (Pull == PullMode.Up) ctx.AddConductanceTo(Pin.Node, 1.0 / PullResistance, SupplyVoltage);
            else if (Pull == PullMode.Down) ctx.AddConductanceTo(Pin.Node, 1.0 / PullResistance, 0.0);
            // PullMode.None: entrada ideal de alta impedancia, no estampa nada.
        }

        public void OnSolved(ISolution solution)
        {
            Voltage = solution.VoltageAt(Pin.Node);

            if (Mode == PinMode.Output)
                Current = ((OutputLevel ? SupplyVoltage : 0.0) - Voltage) / OutputResistance;
            else if (Pull == PullMode.Up)
                Current = (SupplyVoltage - Voltage) / PullResistance;
            else if (Pull == PullMode.Down)
                Current = (0.0 - Voltage) / PullResistance;
            else
                Current = 0.0;

            Level = Voltage <= InputLowMax ? InputLevel.Low
                  : Voltage >= InputHighMin ? InputLevel.High
                  : InputLevel.Undefined;
        }

        public override string ToString() => $"GPIO{Bcm} ({Mode}{(Mode == PinMode.Input ? ", pull " + Pull : "")})";
    }
}
