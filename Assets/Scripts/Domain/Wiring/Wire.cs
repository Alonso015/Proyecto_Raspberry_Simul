using System.Collections.Generic;
using ImatPiSim.Domain.Components;

namespace ImatPiSim.Domain.Wiring
{
    /// <summary>
    /// Cable entre dos puntos de conexión: agujeros de la protoboard, pines de los conectores de
    /// la HAT o patillas de componentes con cable propio (el servo). Es ideal (0 Ω).
    /// </summary>
    public sealed class Wire
    {
        internal Wire(string id, Pin a, Pin b)
        {
            Id = id;
            A = a;
            B = b;
        }

        public string Id { get; }
        public Pin A { get; }
        public Pin B { get; }

        public override string ToString() => $"{Id}: {A.FullName} — {B.FullName}";
    }

    /// <summary>Un componente pinchado en la protoboard: su primer agujero, la dirección y el agujero de cada patilla.</summary>
    public sealed class Placement
    {
        internal Placement(Component component, HoleId start, Direction direction, IReadOnlyList<HoleId> holes)
        {
            Component = component;
            Start = start;
            Direction = direction;
            Holes = holes;
        }

        public Component Component { get; }
        public HoleId Start { get; }
        public Direction Direction { get; }

        /// <summary>Agujero de cada patilla, en el orden de <c>Component.Pins</c>.</summary>
        public IReadOnlyList<HoleId> Holes { get; }

        /// <summary>Agujeros de cada patilla, en el orden de <c>ComponentType.PinNames</c>, a partir de un agujero y una dirección.</summary>
        public static HoleId[] HolesFor(ComponentType type, HoleId start, Direction direction)
        {
            var holes = new HoleId[type.PinNames.Count];
            for (int i = 0; i < holes.Length; i++) holes[i] = start.Move(direction, i * type.LeadSpacing);
            return holes;
        }
    }
}
