using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Interpreter.Ast;

namespace ImatPiSim.Domain.Interpreter.Runtime
{
    /// <summary>Estado del programa del jugador.</summary>
    public enum RuntimeState
    {
        /// <summary>Sin programa cargado.</summary>
        Empty,
        /// <summary>Cargado y comprobado, sin arrancar.</summary>
        Loaded,
        Running,
        /// <summary>Terminó normalmente (llegó al final del programa).</summary>
        Finished,
        /// <summary>Detenido con Parar (KeyboardInterrupt no capturado, o forzado).</summary>
        Stopped,
        /// <summary>Terminó por un error de ejecución no capturado (ver <see cref="ScriptRuntime.Error"/>).</summary>
        Error
    }

    /// <summary>
    /// Ejecuta el programa del jugador de forma <b>cooperativa</b>: en cada <see cref="Tick"/> avanza
    /// el tiempo simulado y ejecuta, como mucho, <see cref="StepsPerTick"/> pasos.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué cooperativo.</b> Unity llama al simulador unas 60 veces por segundo desde un
    /// único hilo. Si el intérprete no devolviera el control, un <c>while True</c> congelaría el
    /// juego; y un <c>sleep</c> real pararía todo. Aquí el programa se trocea: cada tick ejecuta
    /// unos pocos pasos y, si el programa duerme, se aparca hasta que el reloj simulado llegue a
    /// su hora de despertar.</para>
    /// <para><b>Tareas.</b> El programa principal es una tarea; cada callback encolado (p. ej.
    /// <c>when_pressed</c>, etapa E9) es otra, que se ejecuta intercalada con la principal.</para>
    /// <para><b>Parar.</b> <see cref="Stop"/> se comporta como Ctrl+C: lanza <c>KeyboardInterrupt</c>
    /// en el programa principal, que puede atraparlo con <c>try / except / finally</c>. Si tras
    /// pedirlo una vez se vuelve a pulsar, el programa se termina a la fuerza.</para>
    /// </remarks>
    public sealed class ScriptRuntime
    {
        private const double TimeEpsilon = 1e-9;

        private readonly Dictionary<string, PyModule> _modules = new Dictionary<string, PyModule>();
        private readonly Dictionary<string, object> _builtins;
        private readonly List<ScriptTask> _tasks = new List<ScriptTask>();
        private readonly Queue<(IPyCallable fn, object[] args)> _pendingCalls = new Queue<(IPyCallable, object[])>();
        private readonly List<string> _output = new List<string>();
        private string _partialLine = "";

        private Executable _program;
        private Dictionary<string, object> _globals;
        private ScriptTask _current;
        private bool _interruptRequested;
        private bool _interruptDelivered;
        private int _depth;
        private long _tickId;

        public ScriptRuntime()
        {
            _builtins = Builtins.Create(this);
            RegisterModule(Builtins.TimeModule(this));
            RegisterModule(Builtins.SignalModule());
        }

        /// <summary>Pasos máximos por tick (sentencias, vueltas de bucle, llamadas).</summary>
        public int StepsPerTick { get; set; } = 1000;

        /// <summary>Profundidad máxima de llamadas anidadas (protege contra recursión infinita).</summary>
        public int MaxCallDepth { get; set; } = 100;

        public RuntimeState State { get; private set; } = RuntimeState.Empty;

        /// <summary>Tiempo simulado desde el arranque (s). Es lo que devuelve <c>time.time()</c>.</summary>
        public double Time { get; private set; }

        /// <summary>Error de ejecución si <see cref="State"/> es <see cref="RuntimeState.Error"/>.</summary>
        public ScriptRuntimeException Error { get; private set; }

        /// <summary>Línea que se está ejecutando (para resaltarla en el editor).</summary>
        public int CurrentLine { get; private set; }

        /// <summary>Líneas completas escritas con <c>print</c>.</summary>
        public IReadOnlyList<string> Output => _output;

        /// <summary>Se lanza con cada línea completa de <c>print</c>.</summary>
        public event Action<string> LineWritten;

        /// <summary>Pasos ejecutados en el último tick (para pruebas y para mostrar la carga).</summary>
        public int StepsLastTick { get; private set; }

        public bool IsRunning => State == RuntimeState.Running;

        /// <summary>Hace importable un módulo (<c>gpiozero</c> se registra en la etapa E9).</summary>
        public void RegisterModule(PyModule module) => _modules[module.Name] = module;

        // ================================================================== ciclo de vida

        /// <summary>Analiza y compila el programa. Los errores léxicos y sintácticos salen aquí.</summary>
        /// <exception cref="ScriptException">Error en el programa (con línea y columna).</exception>
        public void Load(string source)
        {
            ResetState();
            var module = Parser.Parse(source);
            _program = new Compiler(this).Compile(module);
            State = RuntimeState.Loaded;
        }

        /// <summary>Arranca el programa cargado (el tiempo simulado vuelve a 0).</summary>
        public void Start()
        {
            if (_program == null) throw new InvalidOperationException("No hay ningún programa cargado.");
            var program = _program;
            ResetState();
            _program = program;
            _globals = new Dictionary<string, object> { ["__name__"] = "__main__" };
            var frame = new Frame(Scope.Module, null, _globals);
            _tasks.Add(new ScriptTask("principal", true, program(frame, null).GetEnumerator()));
            State = RuntimeState.Running;
        }

        /// <summary>
        /// Pide parar (como Ctrl+C). La primera vez lanza <c>KeyboardInterrupt</c> en el programa
        /// principal; si ya se había pedido, lo termina a la fuerza.
        /// </summary>
        public void Stop()
        {
            if (State != RuntimeState.Running) return;
            if (_interruptRequested || _interruptDelivered)
            {
                Kill(RuntimeState.Stopped);
                return;
            }
            _interruptRequested = true;
            _pendingCalls.Clear();
            _tasks.RemoveAll(t => !t.IsMain);       // los callbacks no siguen tras Ctrl+C
            foreach (var t in _tasks)
            {
                t.WakeAt = Time;        // despierta al principal si estaba dormido...
                t.SleptAtTick = -1;     // ...también si se durmió en el tick actual
            }
        }

        /// <summary>
        /// Encola una llamada a una función del programa (p. ej. un callback <c>when_pressed</c>). Se
        /// ejecutará como una tarea aparte, intercalada con el programa principal.
        /// </summary>
        public void EnqueueCall(IPyCallable function, params object[] args)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (State == RuntimeState.Running && !_interruptRequested) _pendingCalls.Enqueue((function, args ?? new object[0]));
        }

        /// <summary>
        /// Arranca una tarea de fondo del propio simulador (p. ej. el parpadeo de <c>led.blink()</c>),
        /// que se ejecuta intercalada con el programa como un callback más.
        /// </summary>
        public BackgroundTask StartBackground(string name, IEnumerable<Signal> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            var task = new ScriptTask(name, false, steps.GetEnumerator());
            if (State == RuntimeState.Running) _tasks.Add(task);
            else task.Done = true;
            return new BackgroundTask(task);
        }

        /// <summary>Avanza <paramref name="dt"/> segundos de tiempo simulado y ejecuta lo que toque.</summary>
        public void Tick(double dt)
        {
            if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            StepsLastTick = 0;
            if (State != RuntimeState.Running) return;
            Time += dt;
            _tickId++;

            while (_pendingCalls.Count > 0)
            {
                var (fn, args) = _pendingCalls.Dequeue();
                var result = new Ref();
                _tasks.Add(new ScriptTask("callback " + fn.Name, false,
                    Invoke(fn, args, new Dictionary<string, object>(), result, 0, 0).GetEnumerator()));
            }

            int budget = StepsPerTick;
            bool progressed = true;
            while (budget > 0 && progressed && State == RuntimeState.Running)
            {
                progressed = false;
                // Los callbacks van antes que el principal, como si saltaran en cuanto ocurre el evento.
                foreach (var task in _tasks.ToArray())
                {
                    if (State != RuntimeState.Running || budget <= 0) break;
                    // Una tarea que se ha dormido en este tick no vuelve a ejecutarse hasta el siguiente,
                    // aunque su espera sea 0: así sleep(0) o esperar a un pulsador no acaparan el tick.
                    if (task.Done || task.WakeAt > Time + TimeEpsilon || task.SleptAtTick == _tickId) continue;
                    budget -= RunTask(task, budget, ref progressed);
                }
                _tasks.RemoveAll(t => t.Done);
            }
            StepsLastTick = StepsPerTick - budget;
        }

        /// <summary>Ejecuta una tarea hasta que duerma, termine o agote el presupuesto. Devuelve los pasos usados.</summary>
        private int RunTask(ScriptTask task, int budget, ref bool progressed)
        {
            int used = 0;
            _current = task;
            try
            {
                while (used < budget)
                {
                    bool has;
                    try
                    {
                        has = task.Steps.MoveNext();
                    }
                    catch (PyException ex)
                    {
                        OnUncaught(task, ex);
                        progressed = true;
                        return used;
                    }
                    if (!has)
                    {
                        task.Done = true;
                        if (task.IsMain) Finish(RuntimeState.Finished);
                        progressed = true;
                        return used;
                    }
                    var signal = task.Steps.Current;
                    if (signal.IsSleep)
                    {
                        task.WakeAt = Time + signal.Seconds;
                        task.SleptAtTick = _tickId;
                        progressed = true;
                        return used;
                    }
                    used++;
                    progressed = true;
                }
                return used;
            }
            finally
            {
                _current = null;
            }
        }

        private void OnUncaught(ScriptTask task, PyException ex)
        {
            task.Done = true;
            if (ex.Line == 0) ex.At(CurrentLine, 1);
            if (ex.PythonType == "KeyboardInterrupt" && task.IsMain)
            {
                Kill(RuntimeState.Stopped);
                return;
            }
            Error = new ScriptRuntimeException(ex);
            Kill(RuntimeState.Error);
        }

        private void Finish(RuntimeState state) => Kill(state);

        private void Kill(RuntimeState state)
        {
            foreach (var t in _tasks) t.Steps.Dispose();
            _tasks.Clear();
            _pendingCalls.Clear();
            FlushPartialLine();
            State = state;
        }

        private void ResetState()
        {
            foreach (var t in _tasks) t.Steps.Dispose();
            _tasks.Clear();
            _pendingCalls.Clear();
            _output.Clear();
            _partialLine = "";
            _interruptRequested = false;
            _interruptDelivered = false;
            _depth = 0;
            Time = 0;
            Error = null;
            CurrentLine = 0;
            State = _program == null ? RuntimeState.Empty : RuntimeState.Loaded;
        }

        // ================================================================== servicios para el compilador

        /// <summary>Al empezar una sentencia: actualiza la línea y entrega la petición de parada.</summary>
        internal void BeginStatement(int line, int column)
        {
            CurrentLine = line;
            if (_interruptRequested && _current != null && _current.IsMain)
            {
                _interruptRequested = false;
                _interruptDelivered = true;
                throw PyException.KeyboardInterrupt().At(line, column);
            }
        }

        /// <summary>Para funciones nativas que esperan (sleep, pause): entrega la petición de parada.</summary>
        public void CheckInterrupt() => BeginStatement(CurrentLine, 1);

        internal void EnterCall(string name)
        {
            if (++_depth > MaxCallDepth)
            {
                _depth--;
                throw new PyException("RecursionError", $"demasiadas llamadas anidadas (más de {MaxCallDepth}) en {name}()");
            }
        }

        internal void ExitCall() => _depth--;

        /// <summary>Llama a una función y pone la posición de la llamada a cualquier error que salga de ella.</summary>
        internal IEnumerable<Signal> Invoke(IPyCallable fn, object[] args, IReadOnlyDictionary<string, object> kwargs,
                                            Ref result, int line, int column)
        {
            using (var it = fn.Call(this, args, kwargs, result).GetEnumerator())
            {
                while (true)
                {
                    bool has;
                    try
                    {
                        has = it.MoveNext();
                    }
                    catch (PyException ex)
                    {
                        if (line > 0) ex.At(line, column);
                        throw;
                    }
                    if (!has) yield break;
                    yield return it.Current;
                }
            }
        }

        internal object LoadName(Frame frame, string name)
        {
            for (var f = frame; f != null; f = f.Enclosing)
            {
                if (f.Scope.IsModule || f.Scope.Globals.Contains(name)) break;
                if (f.Scope.Locals.Contains(name))
                {
                    if (f.Vars.TryGetValue(name, out var v)) return v;
                    throw PyException.UnboundLocal(name);
                }
            }
            if (frame.Globals.TryGetValue(name, out var g)) return g;
            if (_builtins.TryGetValue(name, out var b)) return b;
            throw PyException.NameError(name);
        }

        internal void StoreName(Frame frame, string name, object value)
        {
            if (frame.Scope.IsModule || frame.Scope.Globals.Contains(name))
            {
                frame.Globals[name] = value;
                return;
            }
            if (frame.Scope.Nonlocals.Contains(name))
            {
                for (var f = frame.Enclosing; f != null && !f.Scope.IsModule; f = f.Enclosing)
                    if (f.Scope.Locals.Contains(name))
                    {
                        f.Vars[name] = value;
                        return;
                    }
                throw new PyException("NameError", $"'nonlocal {name}': no hay una variable '{name}' en una función que contenga a esta");
            }
            frame.Vars[name] = value;
        }

        internal PyModule ImportModule(string name)
        {
            if (_modules.TryGetValue(name, out var m)) return m;
            if (name == "RPi.GPIO" || name == "RPi")
                throw new PyException("ImportError", "RPi.GPIO no está disponible en el simulador: usa gpiozero");
            throw new PyException("ImportError",
                $"el módulo '{name}' no está disponible; se pueden importar: {string.Join(", ", _modules.Keys)}");
        }

        internal void Write(string text)
        {
            _partialLine += text;
            int nl;
            while ((nl = _partialLine.IndexOf('\n')) >= 0)
            {
                var line = _partialLine.Substring(0, nl);
                _partialLine = _partialLine.Substring(nl + 1);
                _output.Add(line);
                LineWritten?.Invoke(line);
            }
        }

        private void FlushPartialLine()
        {
            if (_partialLine.Length == 0) return;
            Write("\n");
        }

        /// <summary>Lee una variable global del programa (para pruebas y depuración).</summary>
        public object GetGlobal(string name) =>
            _globals != null && _globals.TryGetValue(name, out var v) ? v : throw new KeyError(name);

        /// <summary>No existe esa variable global.</summary>
        public sealed class KeyError : Exception
        {
            public KeyError(string name) : base($"No hay ninguna variable global '{name}'.") { }
        }

        private sealed class ScriptTask
        {
            public ScriptTask(string name, bool isMain, IEnumerator<Signal> steps)
            {
                Name = name;
                IsMain = isMain;
                Steps = steps;
            }

            public string Name { get; }
            public bool IsMain { get; }
            public IEnumerator<Signal> Steps { get; }
            public double WakeAt { get; set; }
            public long SleptAtTick { get; set; } = -1;
            public bool Done { get; set; }
        }

        /// <summary>Referencia a una tarea de fondo para poder cancelarla.</summary>
        public sealed class BackgroundTask
        {
            private readonly ScriptTask _task;
            internal BackgroundTask(object task) => _task = (ScriptTask)task;

            public bool IsRunning => !_task.Done;

            /// <summary>Cancela la tarea; deja de ejecutarse desde el siguiente paso.</summary>
            public void Cancel() => _task.Done = true;
        }
    }
}
