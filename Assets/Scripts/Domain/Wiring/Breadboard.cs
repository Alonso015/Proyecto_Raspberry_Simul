using System;
using System.Collections.Generic;
using System.Linq;
using ImatPiSim.Domain.Components;
using ImatPiSim.Domain.Electrical;

namespace ImatPiSim.Domain.Wiring
{
    /// <summary>
    /// Protoboard de 400 puntos: 30 columnas con las filas a–e y f–j, y dos raíles de
    /// alimentación arriba (T+, T−) y dos abajo (B+, B−).
    /// </summary>
    /// <remarks>
    /// <para><b>Conexiones internas</b> (como las láminas metálicas de una placa real):</para>
    /// <list type="bullet">
    /// <item>En cada columna, a–e es una tira y f–j es otra; el canal central las separa.</item>
    /// <item>Cada raíl une sus 30 agujeros de punta a punta (decidido el 24/11/2026: continuos y
    /// sin tensión hasta que el jugador los cablea).</item>
    /// </list>
    /// <para><b>En el modelo eléctrico</b>, cada tira es un único <see cref="Terminal"/> y todos sus
    /// agujeros son <see cref="Pin"/>es que comparten ese terminal (igual que los pines de un
    /// conector de la HAT). Así una tira es un nodo ideal (0 Ω) y no hacen falta resistencias
    /// entre agujeros. Solo se añaden al circuito las tiras que tienen algo pinchado.</para>
    /// <para><b>Aproximación:</b> en una placa real los raíles tienen 25 agujeros en grupos de 5;
    /// aquí tienen uno por columna para que la rejilla sea regular (la usará el A*, etapa E13).</para>
    /// </remarks>
    public sealed class Breadboard : Component
    {
        public const int Columns = 30;
        public const int RowCount = 14;

        private readonly Dictionary<HoleId, Pin> _holes = new Dictionary<HoleId, Pin>();
        private readonly Dictionary<Pin, HoleId> _holeOfPin = new Dictionary<Pin, HoleId>();
        private readonly Dictionary<Terminal, ConductorStrip> _strips = new Dictionary<Terminal, ConductorStrip>();
        private readonly Dictionary<HoleId, object> _occupants = new Dictionary<HoleId, object>();

        public Breadboard(string id = "PB") : base(id, ComponentTypes.Breadboard)
        {
            foreach (BreadboardRow row in Enum.GetValues(typeof(BreadboardRow)))
            {
                if (IsRailRow(row))
                {
                    var strip = NewStrip(RailName(row));
                    for (int c = 1; c <= Columns; c++) AddHole(new HoleId(row, c), strip);
                }
            }
            for (int c = 1; c <= Columns; c++)
            {
                var top = NewStrip($"{c}a-e");
                var bottom = NewStrip($"{c}f-j");
                for (var row = BreadboardRow.A; row <= BreadboardRow.E; row++) AddHole(new HoleId(row, c), top);
                for (var row = BreadboardRow.F; row <= BreadboardRow.J; row++) AddHole(new HoleId(row, c), bottom);
            }
        }

        private ConductorStrip NewStrip(string name)
        {
            var strip = new ConductorStrip(new Terminal($"{Id}.{name}"));
            _strips[strip.Terminal] = strip;
            return strip;
        }

        private void AddHole(HoleId hole, ConductorStrip strip)
        {
            var pin = AddPin(hole.Name, strip.Terminal);
            _holes[hole] = pin;
            _holeOfPin[pin] = hole;
        }

        private static bool IsRailRow(BreadboardRow row) => new HoleId(row, 1).IsRail;

        private static string RailName(BreadboardRow row) => row switch
        {
            BreadboardRow.TopPlus => "T+",
            BreadboardRow.TopMinus => "T-",
            BreadboardRow.BottomPlus => "B+",
            _ => "B-"
        };

        // ------------------------------------------------------------------ geometría

        /// <summary>Todos los agujeros (14 filas × 30 columnas = 420: 300 del área central y 4 raíles de 30; la placa real de 400 puntos tiene 25 por raíl).</summary>
        public IEnumerable<HoleId> Holes => _holes.Keys;

        public bool Contains(HoleId hole) => _holes.ContainsKey(hole);

        /// <summary>El pin (punto de conexión) de un agujero.</summary>
        public Pin Hole(HoleId hole)
        {
            if (!_holes.TryGetValue(hole, out var pin))
                throw new AssemblyException($"El agujero {hole} no existe en la protoboard (columnas 1–{Columns}).");
            return pin;
        }

        public Pin Hole(string name) => Hole(HoleId.Parse(name));

        /// <summary>El agujero de un pin de esta protoboard, o <c>null</c> si el pin no es suyo.</summary>
        public HoleId? HoleOf(Pin pin) => pin != null && _holeOfPin.TryGetValue(pin, out var h) ? h : (HoleId?)null;

        /// <summary>Si dos agujeros están unidos por dentro (misma tira o mismo raíl).</summary>
        public bool AreConnected(HoleId a, HoleId b) => Hole(a).Terminal == Hole(b).Terminal;

        /// <summary>Los agujeros de la tira o raíl de <paramref name="hole"/> (incluido él).</summary>
        public IEnumerable<HoleId> StripOf(HoleId hole)
        {
            var t = Hole(hole).Terminal;
            return _holes.Where(kv => kv.Value.Terminal == t).Select(kv => kv.Key);
        }

        // ------------------------------------------------------------------ ocupación

        /// <summary>Lo que hay pinchado en el agujero: la patilla (<see cref="Pin"/>) de un componente, un <see cref="Wire"/> o <c>null</c>.</summary>
        public object Occupant(HoleId hole) => _occupants.TryGetValue(hole, out var o) ? o : null;

        /// <summary>Texto para los mensajes: "R1.A", "el cable W3"...</summary>
        public static string Describe(object occupant) => occupant switch
        {
            Pin p => p.FullName,
            Wire w => "el cable " + w.Id,
            _ => "algo"
        };

        public bool IsFree(HoleId hole) => Contains(hole) && !_occupants.ContainsKey(hole);

        public IReadOnlyDictionary<HoleId, object> Occupied => _occupants;

        internal void Occupy(HoleId hole, object occupant)
        {
            if (!Contains(hole)) throw new AssemblyException($"El agujero {hole} no existe en la protoboard.");
            if (_occupants.TryGetValue(hole, out var current))
                throw new AssemblyException($"El agujero {hole} ya está ocupado por {Describe(current)}.");
            _occupants[hole] = occupant;
        }

        internal void Release(HoleId hole) => _occupants.Remove(hole);

        // ------------------------------------------------------------------ modelo eléctrico

        /// <summary>Una tira por cada grupo de agujeros con algo pinchado; las vacías no aportan nodos.</summary>
        public override IEnumerable<IElectricalElement> ElectricalElements() =>
            _occupants.Keys.Select(h => _strips[_holes[h].Terminal]).Distinct();

        /// <summary>Lámina metálica de la protoboard: un conductor ideal con un solo terminal.</summary>
        /// <remarks>No estampa nada: solo hace que su terminal exista como nodo para poder unirle patillas y cables.</remarks>
        private sealed class ConductorStrip : IElectricalElement
        {
            public ConductorStrip(Terminal terminal)
            {
                Terminal = terminal;
                Terminals = new[] { terminal };
            }

            public Terminal Terminal { get; }
            public IReadOnlyList<Terminal> Terminals { get; }
            public void Stamp(IStampContext ctx) { }
            public void OnSolved(ISolution solution) { }
        }
    }

    /// <summary>Error de montaje: agujero ocupado, fuera de la placa, cable no permitido...</summary>
    public sealed class AssemblyException : Exception
    {
        public AssemblyException(string message) : base(message) { }
    }
}
