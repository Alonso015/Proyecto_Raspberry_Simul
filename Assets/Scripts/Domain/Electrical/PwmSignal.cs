using System;

namespace ImatPiSim.Domain.Electrical
{
    /// <summary>
    /// Señal PWM: una onda cuadrada de <see cref="Frequency"/> Hz que pasa una fracción
    /// <see cref="DutyCycle"/> de cada periodo a nivel alto.
    /// </summary>
    /// <remarks>
    /// Qué interesa de ella depende del receptor (docs/arquitectura.md §6.4):
    /// <list type="bullet">
    /// <item>Un LED "ve" la <b>potencia media</b>: su brillo ≈ brillo en HIGH × factor de servicio.</item>
    /// <item>Un servo "lee" el <b>ancho de pulso</b> (<see cref="PulseWidth"/>), no la media.</item>
    /// </list>
    /// </remarks>
    public sealed class PwmSignal
    {
        public PwmSignal(double frequency, double dutyCycle)
        {
            if (!(frequency > 0) || double.IsInfinity(frequency))
                throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "La frecuencia debe ser positiva.");
            if (double.IsNaN(dutyCycle) || dutyCycle < 0 || dutyCycle > 1)
                throw new ArgumentOutOfRangeException(nameof(dutyCycle), dutyCycle, "El factor de servicio va de 0 a 1.");
            Frequency = frequency;
            DutyCycle = dutyCycle;
        }

        /// <summary>Frecuencia en hercios.</summary>
        public double Frequency { get; }

        /// <summary>Fracción del periodo a nivel alto (0..1).</summary>
        public double DutyCycle { get; }

        /// <summary>Periodo en segundos (1 / f).</summary>
        public double Period => 1.0 / Frequency;

        /// <summary>Tiempo a nivel alto en cada periodo, en segundos (factor de servicio · periodo).</summary>
        public double PulseWidth => DutyCycle / Frequency;

        /// <summary>Señal con el ancho de pulso dado (s) a la frecuencia dada (Hz).</summary>
        public static PwmSignal FromPulseWidth(double frequency, double pulseWidth) =>
            new PwmSignal(frequency, pulseWidth * frequency);

        public override string ToString() =>
            $"PWM {Frequency} Hz, {DutyCycle:P1} (pulso {PulseWidth * 1000:0.###} ms)";
    }

    /// <summary>
    /// Elemento con varios terminales que son entradas independientes (p. ej. los 8 canales del
    /// MCP3008): una señal que llega a uno no se "transmite" a los demás.
    /// </summary>
    public interface ISignalIsolating
    {
    }
}
