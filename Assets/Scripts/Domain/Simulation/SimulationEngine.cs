using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Api;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Interpreter.Runtime;

namespace ImatPiSim.Domain.Simulation
{
    /// <summary>Estado de la simulación vista en conjunto (programa + circuito).</summary>
    public enum SimulationState
    {
        /// <summary>Circuito encendido, sin programa en marcha.</summary>
        Idle,
        Running,
        /// <summary>El programa llegó a su final.</summary>
        Finished,
        /// <summary>El jugador pulsó Parar.</summary>
        Stopped,
        /// <summary>El programa terminó por un error de Python no capturado.</summary>
        ProgramError,
        /// <summary>El circuito ha fallado (LED quemado, GPIO dañado, cortocircuito...).</summary>
        Faulted
    }

    /// <summary>
    /// Motor de simulación: en cada <see cref="Tick"/> coordina el programa del jugador y el
    /// circuito en un orden fijo (docs/arquitectura.md §3).
    /// </summary>
    /// <remarks>
    /// <code>
    /// 1. programa:       ScriptRuntime.Tick      (el programa escribe y lee pines)
    /// 2. circuito:       NodalSolver.Solve       (solo si algo ha cambiado)
    /// 3. flancos:        SimulatedPinFactory.Poll (encola when_pressed...)
    /// 4. comportamiento: IBehavioralComponent.Update (el servo gira)
    /// 5. fallos:         FaultDetector.Detect    (si hay uno nuevo, se para todo)
    /// 6. aviso:          eventos para las vistas
    /// </code>
    /// <para>Las entradas que lee el programa en el paso 1 son las de la resolución del tick
    /// anterior, como la muestra de un bucle de scan real.</para>
    /// <para>Cuando el programa termina, se liberan sus pines (gpiozero lo hace al salir de
    /// Python), así que los LED se apagan.</para>
    /// </remarks>
    public sealed class SimulationEngine
    {
        private readonly NodalSolver _solver = new NodalSolver();
        private readonly List<Fault> _faults = new List<Fault>();
        private readonly HashSet<string> _faultKeys = new HashSet<string>();
        private Netlist _netlist;
        private int _netlistVersion = -1;
        private long _solvedSignature = long.MinValue;
        private bool _invalid = true;

        public SimulationEngine(Circuit circuit)
        {
            Circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            Runtime = new ScriptRuntime();
            Pins = new SimulatedPinFactory(circuit.Pi);
            GpioZeroModule.Register(Runtime, Pins, circuit.Hat?.Adc);
            Runtime.LineWritten += line => OutputLine?.Invoke(line);
        }

        public Circuit Circuit { get; }
        public ScriptRuntime Runtime { get; }
        public SimulatedPinFactory Pins { get; }

        public SimulationState State { get; private set; } = SimulationState.Idle;

        /// <summary>Tiempo simulado desde que se arrancó el programa (s).</summary>
        public double Time { get; private set; }

        /// <summary>Fallos detectados desde el último arranque o reinicio.</summary>
        public IReadOnlyList<Fault> Faults => _faults;

        /// <summary>Resoluciones del circuito hechas (para comprobar que solo se resuelve si hay cambios).</summary>
        public int SolveCount { get; private set; }

        /// <summary>La última netlist resuelta (para inspeccionar tensiones en depuración).</summary>
        public Netlist Netlist => _netlist;

        public event Action<SimulationState> StateChanged;
        public event Action<Fault> FaultDetected;
        public event Action<string> OutputLine;

        /// <summary>Se lanza al final de cada tick, con el circuito ya actualizado.</summary>
        public event Action Ticked;

        // ================================================================== programa

        /// <summary>Carga el programa (sin arrancarlo).</summary>
        /// <exception cref="Interpreter.ScriptException">Error léxico o sintáctico, con línea.</exception>
        public void Load(string source)
        {
            Reset();
            Runtime.Load(source);
        }

        /// <summary>Repone el circuito y arranca el programa cargado desde el principio.</summary>
        public void Start()
        {
            Reset();
            Runtime.Start();
            SetState(SimulationState.Running);
        }

        /// <summary>Pide parar el programa (Ctrl+C); una segunda vez lo detiene a la fuerza.</summary>
        public void Stop() => Runtime.Stop();

        /// <summary>
        /// Detiene el programa y devuelve el circuito al estado inicial: pines libres, pulsadores
        /// sueltos, LED repuestos y sin fallos. El programa sigue cargado.
        /// </summary>
        public void Reset()
        {
            Runtime.Abort();
            Pins.ReleaseAll();
            foreach (var c in Circuit.Components) c.Reset();
            _faults.Clear();
            _faultKeys.Clear();
            Time = 0;
            Invalidate();
            SetState(SimulationState.Idle);
        }

        /// <summary>Obliga a volver a resolver el circuito en el próximo tick (tras pulsar un botón...).</summary>
        public void Invalidate() => _invalid = true;

        // ================================================================== tick

        public void Tick(double dt)
        {
            if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (State == SimulationState.Running) Time += dt;

            // 1. Programa
            if (State == SimulationState.Running)
            {
                Runtime.Tick(dt);
                if (Runtime.State != RuntimeState.Running) OnProgramEnded();
            }

            // 2. Circuito (solo si algo ha cambiado)
            if (!SolveIfNeeded()) return;   // cortocircuito: no hay solución que mostrar

            // 3. Flancos → callbacks (se ejecutarán en el próximo tick)
            Pins.Poll();

            // 4. Comportamiento con el tiempo
            foreach (var b in Circuit.AllComponents.OfType<IBehavioralComponent>()) b.Update(dt);

            // 5. Fallos
            DetectFaults(FaultDetector.Detect(_netlist, Circuit.Pi));

            // 6. Aviso
            Ticked?.Invoke();
        }

        private bool SolveIfNeeded()
        {
            if (_netlistVersion != Circuit.Version || _netlist == null)
            {
                _netlist = Circuit.BuildNetlist();
                _netlistVersion = Circuit.Version;
                _invalid = true;
            }
            if (_netlist.HasShortCircuit)
            {
                DetectFaults(FaultDetector.Detect(_netlist));
                Ticked?.Invoke();
                return false;
            }

            long signature = Circuit.Pi.AllGpio.Sum(g => (long)g.Version);
            if (!_invalid && signature == _solvedSignature) return true;

            _solver.Solve(_netlist);
            SolveCount++;
            _solvedSignature = signature;
            _invalid = false;
            return true;
        }

        private void DetectFaults(IReadOnlyList<Fault> detected)
        {
            bool any = false;
            foreach (var f in detected)
            {
                if (!_faultKeys.Add(f.Kind + "|" + f.SourceId)) continue;
                _faults.Add(f);
                any = true;
                FaultDetected?.Invoke(f);
            }
            if (!any) return;

            // Un fallo eléctrico detiene el programa: en la placa real, la Pi se reiniciaría o el
            // componente dejaría de funcionar. El juego decidirá si eso es una derrota (E11).
            Runtime.Abort();
            Invalidate();   // un LED quemado pasa a ser un circuito abierto: hay que volver a resolver
            SetState(SimulationState.Faulted);
        }

        private void OnProgramEnded()
        {
            Pins.ReleaseAll();   // como gpiozero al terminar Python: los pines vuelven a ser entradas
            Invalidate();
            switch (Runtime.State)
            {
                case RuntimeState.Finished: SetState(SimulationState.Finished); break;
                case RuntimeState.Stopped: SetState(SimulationState.Stopped); break;
                case RuntimeState.Error: SetState(SimulationState.ProgramError); break;
            }
        }

        private void SetState(SimulationState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
