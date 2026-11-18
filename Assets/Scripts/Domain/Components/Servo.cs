using System;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Components
{
    /// <summary>
    /// Componente con comportamiento que evoluciona en el tiempo (no solo con la solución
    /// eléctrica): un servo que gira hacia su objetivo, un conversor que muestrea...
    /// </summary>
    public interface IBehavioralComponent
    {
        /// <summary>Avanza el comportamiento <paramref name="dt"/> segundos de tiempo simulado.</summary>
        void Update(double dt);
    }

    /// <summary>
    /// Servomotor de posición, como los del laboratorio (práctica 4): la anchura del pulso PWM
    /// fija el ángulo objetivo y el eje gira hacia él a velocidad limitada.
    /// </summary>
    /// <remarks>
    /// <para>Pines (como el cable del servo y el conector J7–J10): "SIG" (blanco o amarillo, señal),
    /// "VCC" (rojo, 5 V) y "GND" (negro).</para>
    /// <para>Confirmado (F3): 0,5 ms → 0° y 2,5 ms → 180°, con periodo habitual de 20 ms (50 Hz).
    /// Entre medias, lineal. Pulsos fuera de rango se recortan a los topes mecánicos.</para>
    /// <para>Supuestos: velocidad máxima <see cref="DefaultMaxSpeed"/>; alimentación mínima para
    /// moverse <see cref="MinSupplyVoltage"/>; no se modela el consumo del motor; sin señal, el servo
    /// mantiene su posición.</para>
    /// <para>Recibir una orden no es estar en la posición: <see cref="TargetAngle"/> cambia al
    /// instante, <see cref="Angle"/> tarda en alcanzarlo.</para>
    /// </remarks>
    public sealed class Servo : ElectricalComponent, IBehavioralComponent
    {
        public const double MinPulse = 0.0005;   // s → 0°
        public const double MaxPulse = 0.0025;   // s → 180°
        public const double MinAngle = 0.0;
        public const double MaxAngle = 180.0;

        /// <summary>Supuesto: velocidad máxima de giro (°/s), del orden de 0,1 s cada 60°.</summary>
        public const double DefaultMaxSpeed = 600.0;

        /// <summary>Supuesto: tensión entre VCC y GND por debajo de la cual el servo no se mueve (V).</summary>
        public const double MinSupplyVoltage = 4.0;

        private double _angle;

        public Servo(string id, double initialAngle = 90.0, double maxSpeed = DefaultMaxSpeed)
            : base(id, ComponentTypes.Servo)
        {
            if (!(maxSpeed > 0)) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            Signal = CreateTerminal("SIG");
            Vcc = CreateTerminal("VCC");
            Gnd = CreateTerminal("GND");
            MaxSpeed = maxSpeed;
            InitialAngle = Clamp(initialAngle);
            _angle = InitialAngle;
            TargetAngle = InitialAngle;
        }

        public Terminal Signal { get; }
        public Terminal Vcc { get; }
        public Terminal Gnd { get; }

        public double MaxSpeed { get; }
        public double InitialAngle { get; }

        /// <summary>Ángulo real del eje (°).</summary>
        public double Angle => _angle;

        /// <summary>Ángulo que pide la señal (°).</summary>
        public double TargetAngle { get; private set; }

        /// <summary>Señal recibida en la última resolución (<c>null</c> si no llega PWM).</summary>
        public PwmSignal LastSignal { get; private set; }

        public double SupplyVoltage { get; private set; }

        public bool IsPowered => SupplyVoltage >= MinSupplyVoltage;

        /// <summary>Ángulo que corresponde a un ancho de pulso (s), recortado a 0–180°.</summary>
        public static double AngleForPulse(double pulseWidth) =>
            Clamp(MinAngle + (pulseWidth - MinPulse) / (MaxPulse - MinPulse) * (MaxAngle - MinAngle));

        // Entrada de señal de alta impedancia y consumo del motor sin modelar: no estampa nada.
        public override void Stamp(IStampContext ctx) { }

        public override void OnSolved(ISolution solution)
        {
            SupplyVoltage = solution.VoltageAt(Vcc.Node) - solution.VoltageAt(Gnd.Node);
            LastSignal = solution.PwmAt(Signal.Node);
            if (LastSignal != null && LastSignal.DutyCycle > 0)
                TargetAngle = AngleForPulse(LastSignal.PulseWidth);
        }

        public void Update(double dt)
        {
            if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (!IsPowered) return;
            double step = MaxSpeed * dt;
            double diff = TargetAngle - _angle;
            if (Math.Abs(diff) < 1e-12) return;
            _angle = Math.Abs(diff) <= step ? TargetAngle : _angle + Math.Sign(diff) * step;
            RaiseStateChanged();
        }

        public override void Reset()
        {
            _angle = InitialAngle;
            TargetAngle = InitialAngle;
            LastSignal = null;
            SupplyVoltage = 0;
            RaiseStateChanged();
        }

        private static double Clamp(double a) => Math.Max(MinAngle, Math.Min(MaxAngle, a));
    }
}
