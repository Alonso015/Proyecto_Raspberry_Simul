using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Interpreter;

namespace ImatPiSim.Domain.Simulation
{
    /// <summary>Resultado de cargar un programa: correcto, o el error con su línea.</summary>
    public sealed class LoadResult
    {
        private LoadResult(ScriptException error) => Error = error;

        public static readonly LoadResult Ok = new LoadResult(null);
        public static LoadResult Failed(ScriptException error) => new LoadResult(error);

        public bool Success => Error == null;
        public ScriptException Error { get; }
    }

    /// <summary>
    /// Punto de entrada único de la interfaz al simulador (patrón <b>Facade</b>).
    /// </summary>
    /// <remarks>
    /// La escena de Unity no necesita conocer el solver, el intérprete, la fábrica de pines ni
    /// el detector de fallos: carga un programa, lo ejecuta, avanza el tiempo, pulsa botones y
    /// pregunta qué LED están encendidos. Si el subsistema cambia por dentro, la interfaz no.
    /// </remarks>
    public sealed class SimulatorFacade
    {
        public SimulatorFacade(Circuit circuit)
        {
            Engine = new SimulationEngine(circuit);
            Engine.StateChanged += s => StateChanged?.Invoke(s);
            Engine.FaultDetected += f => FaultDetected?.Invoke(f);
            Engine.OutputLine += l => OutputLine?.Invoke(l);
            Engine.Ticked += () => Ticked?.Invoke();
        }

        /// <summary>Simulador con una Raspberry Pi y la IMAT HAT.</summary>
        public static SimulatorFacade WithImatHat() => new SimulatorFacade(Circuit.WithImatHat());

        /// <summary>Acceso al subsistema completo, para casos avanzados (retos, protoboard).</summary>
        public SimulationEngine Engine { get; }

        public Circuit Circuit => Engine.Circuit;
        public RaspberryPi Pi => Engine.Circuit.Pi;
        public ImatHat Hat => Engine.Circuit.Hat ?? throw new InvalidOperationException("Este montaje no tiene IMAT HAT.");

        // ------------------------------------------------------------------ programa

        public LoadResult LoadProgram(string source)
        {
            try
            {
                Engine.Load(source ?? "");
                return LoadResult.Ok;
            }
            catch (ScriptException ex)
            {
                return LoadResult.Failed(ex);
            }
        }

        public void Run() => Engine.Start();
        public void Stop() => Engine.Stop();
        public void Reset() => Engine.Reset();

        /// <summary>Avanza <paramref name="seconds"/> de tiempo simulado (llamar en cada frame).</summary>
        public void Tick(double seconds) => Engine.Tick(seconds);

        public SimulationState State => Engine.State;
        public bool IsRunning => Engine.State == SimulationState.Running;
        public double Time => Engine.Time;
        public int CurrentLine => Engine.Runtime.CurrentLine;
        public IReadOnlyList<string> Output => Engine.Runtime.Output;

        /// <summary>Error de Python si <see cref="State"/> es <see cref="SimulationState.ProgramError"/>.</summary>
        public ScriptException ProgramError => Engine.Runtime.Error;

        public IReadOnlyList<Fault> Faults => Engine.Faults;

        // ------------------------------------------------------------------ interacción

        /// <summary>Pulsa SWn (n = 1..4) de la IMAT HAT.</summary>
        public void PressButton(int n) => Interact(() => Hat.Button(n).Press());

        public void ReleaseButton(int n) => Interact(() => Hat.Button(n).Release());

        public bool IsButtonPressed(int n) => Hat.Button(n).IsPressed;

        /// <summary>Pulsa un pulsador cualquiera (p. ej. uno externo pinchado en la protoboard).</summary>
        public void PressButton(PushButton button) => Interact(button.Press);

        public void ReleaseButton(PushButton button) => Interact(button.Release);

        /// <summary>Gira RV1: 0 = extremo de GND, 1 = extremo de 3,3 V.</summary>
        public void SetPotentiometer(double position) => Interact(() => Hat.Potentiometer.Position = position);

        /// <summary>Activa o desactiva el SPI (como <c>raspi-config</c>); necesario para el MCP3008.</summary>
        public void SetSpiEnabled(bool enabled) => Interact(() => Pi.SpiEnabled = enabled);

        private void Interact(Action change)
        {
            change();
            Engine.Invalidate();
        }

        // ------------------------------------------------------------------ lectura

        /// <summary>Brillo medio de Dn (0..1), ya con el factor de servicio si hay PWM.</summary>
        public double LedBrightness(int n) => Hat.Led(n).Brightness;

        public bool IsLedBurnt(int n) => Hat.Led(n).IsBurnt;

        // ------------------------------------------------------------------ eventos (Observer)

        public event Action<SimulationState> StateChanged;
        public event Action<Fault> FaultDetected;
        public event Action<string> OutputLine;
        public event Action Ticked;
    }
}
