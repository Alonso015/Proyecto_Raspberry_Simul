using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Simulation;

namespace ImatPiSim.Domain.Challenges
{
    /// <summary>Pantallas del juego (lo que la interfaz tiene que mostrar en cada momento).</summary>
    public enum GameScreen { Menu, Selection, Building, Victory, Defeat }

    /// <summary>
    /// El bucle de juego (requisito R4): menú → selección de reto → montaje y programación →
    /// comprobar → victoria o derrota → reintentar, siguiente o menú.
    /// </summary>
    /// <remarks>
    /// <para><b>Patrón State</b>: cada pantalla es un objeto (<see cref="GameState"/>) que solo
    /// admite sus propias acciones; las demás lanzan <see cref="InvalidOperationException"/> con el
    /// motivo. Así no hace falta un <c>switch</c> por estado en cada método, y una acción imposible
    /// (comprobar desde el menú) no puede dejar el juego a medias.</para>
    /// <para><b>Reinicio limpio</b>: entrar en un reto crea un circuito y una fachada nuevos a
    /// partir de su montaje de partida; reintentar reconstruye el circuito desde la descripción
    /// del montaje (<see cref="AssemblyPlan"/>), así que no queda ningún estado eléctrico
    /// residual (LED quemados, pulsadores pulsados, pines ocupados).</para>
    /// <para>Progreso: el reto 1 está desbloqueado y superar uno desbloquea el siguiente.</para>
    /// </remarks>
    public sealed class Game
    {
        private readonly HashSet<int> _completed = new HashSet<int>();
        private GameState _state;

        public Game(IReadOnlyList<Challenge> challenges = null)
        {
            Challenges = challenges ?? ChallengeCatalog.All;
            _state = new MenuState(this);
        }

        public IReadOnlyList<Challenge> Challenges { get; }
        public GameScreen Screen => _state.Screen;

        /// <summary>Reto en curso (o el último jugado, en victoria y derrota).</summary>
        public Challenge Current { get; private set; }

        /// <summary>Simulación en la que el jugador monta y prueba (la del reto en curso).</summary>
        public SimulatorFacade Facade { get; private set; }

        /// <summary>Programa del jugador para el reto en curso.</summary>
        public string Program { get; set; } = "";

        public ChallengeResult LastResult { get; private set; }
        public IReadOnlyCollection<int> Completed => _completed;

        public bool IsCompleted(int number) => _completed.Contains(number);

        public bool IsUnlocked(int number) =>
            number == Challenges.Min(c => c.Number) || _completed.Contains(number - 1) || _completed.Contains(number);

        /// <summary>Se lanza al cambiar de pantalla (Observer para la interfaz).</summary>
        public event Action<GameScreen> ScreenChanged;

        // ------------------------------------------------------------------ acciones (las delega en el estado)

        /// <summary>Menú → selección de reto.</summary>
        public void Play() => _state.Play();

        /// <summary>Selección → montaje del reto elegido (si está desbloqueado).</summary>
        public void Choose(int number) => _state.Choose(number);

        /// <summary>Montaje → victoria o derrota, según el verificador.</summary>
        public ChallengeResult Check() => _state.Check();

        /// <summary>Victoria o derrota → otra vez el mismo reto, con el mismo montaje y programa, en un circuito limpio.</summary>
        public void Retry() => _state.Retry();

        /// <summary>Montaje → el reto desde cero (montaje de partida, programa vacío).</summary>
        public void Restart() => _state.Restart();

        /// <summary>Victoria → siguiente reto.</summary>
        public void Next() => _state.Next();

        /// <summary>Cualquier pantalla → menú.</summary>
        public void BackToMenu() => _state.BackToMenu();

        /// <summary>Montaje, victoria o derrota → selección de reto.</summary>
        public void BackToSelection() => _state.BackToSelection();

        // ------------------------------------------------------------------ ayudas para los estados

        private void Go(GameState next)
        {
            _state = next;
            ScreenChanged?.Invoke(next.Screen);
        }

        private void Enter(Challenge challenge, AssemblyPlan assembly, string program)
        {
            Facade?.Reset();   // para el programa anterior y suelta sus pines
            Current = challenge;
            Facade = new SimulatorFacade(assembly.Build());
            Program = program ?? "";
            LastResult = null;
            Go(new BuildingState(this));
        }

        // ================================================================== estados

        private abstract class GameState
        {
            protected GameState(Game game) => Game = game;
            protected Game Game { get; }
            public abstract GameScreen Screen { get; }

            public virtual void Play() => Invalid("empezar a jugar");
            public virtual void Choose(int number) => Invalid("elegir un reto");
            public virtual ChallengeResult Check() { Invalid("comprobar el reto"); return null; }
            public virtual void Retry() => Invalid("reintentar");
            public virtual void Restart() => Invalid("empezar el reto desde cero");
            public virtual void Next() => Invalid("pasar al siguiente reto");
            public virtual void BackToSelection() => Invalid("volver a la selección de retos");

            public virtual void BackToMenu()
            {
                Game.Facade?.Reset();
                Game.Facade = null;
                Game.Current = null;
                Game.Go(new MenuState(Game));
            }

            protected void Invalid(string action) =>
                throw new InvalidOperationException($"No se puede {action} en la pantalla {Screen}.");
        }

        private sealed class MenuState : GameState
        {
            public MenuState(Game game) : base(game) { }
            public override GameScreen Screen => GameScreen.Menu;
            public override void Play() => Game.Go(new SelectionState(Game));
            public override void BackToMenu() { }
        }

        private sealed class SelectionState : GameState
        {
            public SelectionState(Game game) : base(game) { }
            public override GameScreen Screen => GameScreen.Selection;

            public override void Choose(int number)
            {
                var challenge = Game.Challenges.FirstOrDefault(c => c.Number == number)
                                ?? throw new ArgumentOutOfRangeException(nameof(number), $"No hay reto {number}.");
                if (!Game.IsUnlocked(number))
                    throw new InvalidOperationException($"El reto {number} está bloqueado: supera antes el {number - 1}.");
                Game.Enter(challenge, challenge.InitialAssembly, "");
            }
        }

        private sealed class BuildingState : GameState
        {
            public BuildingState(Game game) : base(game) { }
            public override GameScreen Screen => GameScreen.Building;

            public override ChallengeResult Check()
            {
                Game.Facade.Stop();   // como pulsar Parar: el montaje queda libre
                var result = Game.Current.Verify(AssemblyPlan.Capture(Game.Facade.Circuit), Game.Program);
                Game.LastResult = result;
                if (result.Passed)
                {
                    Game._completed.Add(Game.Current.Number);
                    Game.Go(new VictoryState(Game));
                }
                else Game.Go(new DefeatState(Game));
                return result;
            }

            public override void Restart() => Game.Enter(Game.Current, Game.Current.InitialAssembly, "");

            public override void BackToSelection()
            {
                Game.Facade.Reset();
                Game.Go(new SelectionState(Game));
            }
        }

        /// <summary>Base de victoria y derrota: las dos permiten reintentar o volver.</summary>
        private abstract class ResultState : GameState
        {
            protected ResultState(Game game) : base(game) { }

            public override void Retry() =>
                Game.Enter(Game.Current, AssemblyPlan.Capture(Game.Facade.Circuit), Game.Program);

            public override void BackToSelection()
            {
                Game.Facade.Reset();
                Game.Go(new SelectionState(Game));
            }
        }

        private sealed class VictoryState : ResultState
        {
            public VictoryState(Game game) : base(game) { }
            public override GameScreen Screen => GameScreen.Victory;

            public override void Next()
            {
                var next = Game.Challenges.Where(c => c.Number > Game.Current.Number).OrderBy(c => c.Number).FirstOrDefault();
                if (next == null) Invalid("pasar al siguiente reto (era el último)");
                Game.Enter(next, next.InitialAssembly, "");
            }
        }

        private sealed class DefeatState : ResultState
        {
            public DefeatState(Game game) : base(game) { }
            public override GameScreen Screen => GameScreen.Defeat;
        }
    }
}
