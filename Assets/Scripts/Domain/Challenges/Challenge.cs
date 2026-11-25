using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;
using ImatPiSim.Domain.Simulation;

namespace ImatPiSim.Domain.Challenges
{
    /// <summary>Cómo ha terminado una comprobación.</summary>
    public enum ResultKind
    {
        /// <summary>Reto superado.</summary>
        Passed,
        /// <summary>El programa tiene errores de sintaxis o el montaje usa piezas no permitidas: no se llega a probar.</summary>
        CannotRun,
        /// <summary>Excepción de Python no capturada.</summary>
        ProgramError,
        /// <summary>Fallo eléctrico real: LED quemado, GPIO sobrecargado, cortocircuito, sobretensión...</summary>
        ElectricalFault,
        /// <summary>El circuito funciona, pero no hace lo que pide el reto.</summary>
        WrongBehaviour,
        /// <summary>Derrota propia del reto 3: un LED cambia varias veces en una sola pulsación.</summary>
        MultipleToggles,
        /// <summary>Derrota propia del reto 8: algo no ocurre a tiempo.</summary>
        TooSlow
    }

    /// <summary>Resultado de comprobar un reto: si se ha superado y, si no, por qué y cuándo.</summary>
    public sealed class ChallengeResult
    {
        public ChallengeResult(ResultKind kind, string message, double time, IReadOnlyList<string> log, Fault fault = null)
        {
            Kind = kind;
            Message = message;
            Time = time;
            Log = log ?? Array.Empty<string>();
            Fault = fault;
        }

        public ResultKind Kind { get; }
        public bool Passed => Kind == ResultKind.Passed;

        /// <summary>Victoria o derrota del bucle de juego: solo gana <see cref="ResultKind.Passed"/>.</summary>
        public bool IsVictory => Passed;

        /// <summary>Explicación para el jugador (la causa física si es un fallo eléctrico).</summary>
        public string Message { get; }

        /// <summary>Instante simulado (s) en el que se decidió el resultado.</summary>
        public double Time { get; }

        /// <summary>Lo que ha ido comprobando el verificador, paso a paso.</summary>
        public IReadOnlyList<string> Log { get; }

        public Fault Fault { get; }

        public override string ToString() => $"{Kind}: {Message}";
    }

    /// <summary>
    /// Un reto: enunciado, piezas disponibles, montaje de partida y cómo se comprueba.
    /// </summary>
    /// <remarks>
    /// <para><b>Template Method</b>: <see cref="Verify"/> hace siempre lo mismo (copiar el montaje,
    /// cargar el programa, ejecutarlo con el <see cref="Verifier"/> y traducir lo que pase a un
    /// <see cref="ChallengeResult"/>) y cada reto solo escribe su guion en <see cref="Script"/>:
    /// qué estímulos da y qué comprueba. Para el juego, cada reto es intercambiable (Strategy).</para>
    /// <para>Los criterios de cada reto se aprobaron el 25/11/2026 (docs/requisitos.md §2.8).</para>
    /// </remarks>
    public abstract class Challenge
    {
        protected Challenge(int number, string title, string statement, string concept, IEnumerable<string> availableParts,
                            bool spiEnabled = false)
        {
            Number = number;
            Title = title;
            Statement = statement;
            Concept = concept;
            AvailableParts = availableParts?.ToArray() ?? Array.Empty<string>();
            SpiEnabledAtStart = spiEnabled;
        }

        public int Number { get; }
        public string Title { get; }

        /// <summary>Enunciado que ve el jugador.</summary>
        public string Statement { get; }

        /// <summary>Qué enseña el reto.</summary>
        public string Concept { get; }

        /// <summary>Tipos de pieza que el jugador puede añadir (ids de <see cref="ComponentType"/>).</summary>
        public IReadOnlyList<string> AvailableParts { get; }

        public bool SpiEnabledAtStart { get; }

        /// <summary>Montaje de partida: lo que ya viene puesto (el servo del reto 8, el SPI activado...).</summary>
        public virtual AssemblyPlan InitialAssembly => AssemblyPlan.Empty(SpiEnabledAtStart);

        /// <summary>Componentes de partida que el jugador no puede quitar.</summary>
        public virtual IReadOnlyList<string> FixedParts => Array.Empty<string>();

        public override string ToString() => $"Reto {Number}: {Title}";

        /// <summary>Comprueba un montaje y un programa en una simulación aparte (tiempo simulado).</summary>
        public ChallengeResult Verify(AssemblyPlan assembly, string program)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            var log = new List<string>();

            var notAllowed = assembly.Parts.Where(p => !FixedParts.Contains(p.Id) && !AvailableParts.Contains(p.TypeId)).ToList();
            if (notAllowed.Count > 0)
                return new ChallengeResult(ResultKind.CannotRun,
                    $"En este reto no se puede usar: {string.Join(", ", notAllowed.Select(p => p.Id))}.", 0, log);

            Circuit circuit;
            try { circuit = assembly.Build(); }
            catch (Wiring.AssemblyException ex) { return new ChallengeResult(ResultKind.CannotRun, ex.Message, 0, log); }

            var facade = new SimulatorFacade(circuit);
            var load = facade.LoadProgram(program ?? "");
            if (!load.Success)
                return new ChallengeResult(ResultKind.CannotRun, "El programa no se puede ejecutar: " + load.Error.Message, 0, log);

            var verifier = new Verifier(facade, log);
            facade.Run();
            try
            {
                Script(verifier);
                verifier.Log("Reto superado.");
                return new ChallengeResult(ResultKind.Passed, "¡Reto superado!", facade.Time, log);
            }
            catch (VerificationEnded end)
            {
                return end.Result;
            }
        }

        /// <summary>Guion del reto: estímulos y comprobaciones con <paramref name="v"/>.</summary>
        protected abstract void Script(Verifier v);
    }

    /// <summary>Corta el guion en cuanto se decide el resultado.</summary>
    internal sealed class VerificationEnded : Exception
    {
        public VerificationEnded(ChallengeResult result) : base(result.Message) => Result = result;
        public ChallengeResult Result { get; }
    }

    /// <summary>
    /// Lo que un guion de reto puede hacer: dejar pasar tiempo simulado, pulsar, girar, cambiar la
    /// luz, y comprobar. En cada paso vigila los fallos eléctricos y los errores del programa:
    /// si aparece uno, el reto termina ahí con su causa.
    /// </summary>
    public sealed class Verifier
    {
        /// <summary>Paso de simulación del verificador (s).</summary>
        public const double Step = 0.01;

        /// <summary>Brillo de un LED de la HAT encendido en continua (≈ 4 mA de 20 mA): la referencia de "encendido".</summary>
        public static readonly double HatLedOnBrightness =
            (GpioPin.SupplyVoltage - Led.DefaultForwardVoltage)
            / (GpioPin.DefaultOutputResistance + ImatHat.LedResistance + Led.DefaultSeriesResistance)
            / Led.DefaultMaxCurrent;

        private readonly List<string> _log;
        private readonly List<Action> _watchers = new List<Action>();

        internal Verifier(SimulatorFacade facade, List<string> log)
        {
            Facade = facade;
            _log = log;
        }

        public SimulatorFacade Facade { get; }
        public Circuit Circuit => Facade.Circuit;
        public ImatHat Hat => Facade.Hat;
        public double Time => Facade.Time;

        public void Log(string line) => _log.Add($"[{Time,5:0.00} s] {line}");

        // ------------------------------------------------------------------ tiempo

        /// <summary>Deja correr la simulación <paramref name="seconds"/> segundos simulados.</summary>
        public void Run(double seconds)
        {
            int steps = (int)Math.Round(seconds / Step);
            for (int i = 0; i < steps; i++)
            {
                Facade.Tick(Step);
                foreach (var w in _watchers) w();
                if (Facade.State == SimulationState.Faulted)
                {
                    var f = Facade.Faults.First();
                    End(ResultKind.ElectricalFault, "Fallo eléctrico: " + f.Message, f);
                }
                if (Facade.State == SimulationState.ProgramError)
                    End(ResultKind.ProgramError, "El programa se ha detenido con un error: " + Facade.ProgramError.Message);
            }
        }

        /// <summary>
        /// Cuenta los cambios de una señal (encendido/apagado) mientras corre la simulación. Sirve
        /// para detectar un LED que conmuta varias veces en una sola pulsación.
        /// </summary>
        public Counter Watch(Func<bool> signal)
        {
            var c = new Counter(signal());
            _watchers.Add(() => c.Sample(signal()));
            return c;
        }

        public sealed class Counter
        {
            private bool _last;
            internal Counter(bool initial) => _last = initial;
            public int Changes { get; private set; }
            public void Reset() => Changes = 0;

            internal void Sample(bool value)
            {
                if (value != _last) Changes++;
                _last = value;
            }
        }

        // ------------------------------------------------------------------ estímulos

        /// <summary>Pulsa un pulsador, lo mantiene <paramref name="hold"/> s, lo suelta y deja pasar <paramref name="after"/> s.</summary>
        public void Click(PushButton button, double hold = 0.15, double after = 0.35)
        {
            Facade.PressButton(button);
            Run(hold);
            Facade.ReleaseButton(button);
            Run(after);
        }

        /// <summary>Lo mismo con SWn de la HAT.</summary>
        public void Click(int hatButton, double hold = 0.15, double after = 0.35) => Click(Hat.Button(hatButton), hold, after);

        // ------------------------------------------------------------------ lecturas

        /// <summary>Brillo de Dn relativo al de un LED de la HAT encendido (≈ 1 = encendido del todo).</summary>
        public double LedLevel(int n) => Hat.Led(n).Brightness / HatLedOnBrightness;

        public bool IsOn(int n) => LedLevel(n) >= 0.5;
        public bool IsOff(int n) => LedLevel(n) <= 0.05;

        /// <summary>"D1 D3" para los mensajes: los LED que están encendidos.</summary>
        public string LitLeds()
        {
            var lit = Enumerable.Range(1, 8).Where(IsOn).Select(n => "D" + n).ToList();
            return lit.Count == 0 ? "ninguno" : string.Join(" ", lit);
        }

        public IEnumerable<T> External<T>() where T : Component => Circuit.ExternalComponents.OfType<T>();

        // ------------------------------------------------------------------ comprobaciones

        /// <summary>Si no se cumple, el reto termina sin superar con este mensaje.</summary>
        public void Expect(bool condition, string whatShouldHappen, ResultKind kind = ResultKind.WrongBehaviour)
        {
            if (condition)
            {
                Log("✓ " + whatShouldHappen);
                return;
            }
            string message = "No se cumple: " + whatShouldHappen;
            if (Facade.State == SimulationState.Finished)
                message += " (el programa ya había terminado: ¿falta un bucle o pause()?)";
            End(kind, message);
        }

        /// <summary>Comprueba qué LED de la HAT deben estar encendidos (y el resto apagados).</summary>
        public void ExpectLeds(IEnumerable<int> on, string when)
        {
            var set = new HashSet<int>(on);
            bool ok = Enumerable.Range(1, 8).All(n => set.Contains(n) ? IsOn(n) : IsOff(n));
            string expected = set.Count == 0 ? "ninguno" : string.Join(" ", set.OrderBy(n => n).Select(n => "D" + n));
            Expect(ok, $"{when}: encendidos {expected} (hay: {LitLeds()})");
        }

        internal void End(ResultKind kind, string message, Fault fault = null)
        {
            Log("✗ " + message);
            throw new VerificationEnded(new ChallengeResult(kind, message, Time, _log.ToList(), fault));
        }
    }
}
