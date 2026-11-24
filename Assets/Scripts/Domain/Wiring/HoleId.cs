using System;

namespace ImatPiSim.Domain.Wiring
{
    /// <summary>
    /// Filas de la protoboard, de arriba abajo: raíles superiores, filas a–e, (canal central),
    /// filas f–j y raíles inferiores. El valor numérico es la fila en la rejilla.
    /// </summary>
    public enum BreadboardRow
    {
        TopPlus, TopMinus,
        A, B, C, D, E,
        F, G, H, I, J,
        BottomPlus, BottomMinus
    }

    /// <summary>Dirección en la rejilla: hacia dónde van las patillas desde la primera.</summary>
    public enum Direction { Right, Down, Left, Up }

    /// <summary>
    /// Un agujero de la protoboard: fila y columna (1–30). Se escribe como en las placas reales:
    /// "a1"…"j30" para el área central y "T+1", "T-1", "B+1", "B-1" para los raíles de arriba
    /// (top) y de abajo (bottom).
    /// </summary>
    public readonly struct HoleId : IEquatable<HoleId>
    {
        public HoleId(BreadboardRow row, int column)
        {
            Row = row;
            Column = column;
        }

        public BreadboardRow Row { get; }
        public int Column { get; }

        /// <summary>Fila en la rejilla (0 = raíl superior +, 13 = raíl inferior −).</summary>
        public int GridRow => (int)Row;

        public bool IsRail => Row == BreadboardRow.TopPlus || Row == BreadboardRow.TopMinus ||
                              Row == BreadboardRow.BottomPlus || Row == BreadboardRow.BottomMinus;

        public string Name => Row switch
        {
            BreadboardRow.TopPlus => "T+" + Column,
            BreadboardRow.TopMinus => "T-" + Column,
            BreadboardRow.BottomPlus => "B+" + Column,
            BreadboardRow.BottomMinus => "B-" + Column,
            _ => (char)('a' + (Row - BreadboardRow.A)) + Column.ToString()
        };

        /// <summary>El agujero a <paramref name="steps"/> pasos en la dirección dada (puede caer fuera de la placa).</summary>
        public HoleId Move(Direction direction, int steps) => direction switch
        {
            Direction.Right => new HoleId(Row, Column + steps),
            Direction.Left => new HoleId(Row, Column - steps),
            Direction.Down => new HoleId((BreadboardRow)((int)Row + steps), Column),
            _ => new HoleId((BreadboardRow)((int)Row - steps), Column)
        };

        /// <summary>Lee "a1", "j30", "T+5", "B-12"... (mayúsculas o minúsculas en a–j).</summary>
        public static HoleId Parse(string name)
        {
            if (TryParse(name, out var hole)) return hole;
            throw new FormatException($"'{name}' no es un agujero de la protoboard (ejemplos: a1, j30, T+5, B-12).");
        }

        public static bool TryParse(string name, out HoleId hole)
        {
            hole = default;
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) return false;
            name = name.Trim();
            BreadboardRow row;
            string number;
            if (name.Length >= 3 && (name[0] == 'T' || name[0] == 'B' || name[0] == 't' || name[0] == 'b')
                && (name[1] == '+' || name[1] == '-'))
            {
                bool top = char.ToUpperInvariant(name[0]) == 'T';
                bool plus = name[1] == '+';
                row = top ? (plus ? BreadboardRow.TopPlus : BreadboardRow.TopMinus)
                          : (plus ? BreadboardRow.BottomPlus : BreadboardRow.BottomMinus);
                number = name.Substring(2);
            }
            else
            {
                char c = char.ToLowerInvariant(name[0]);
                if (c < 'a' || c > 'j') return false;
                row = BreadboardRow.A + (c - 'a');
                number = name.Substring(1);
            }
            if (!int.TryParse(number, out int column)) return false;
            hole = new HoleId(row, column);
            return true;
        }

        public bool Equals(HoleId other) => Row == other.Row && Column == other.Column;
        public override bool Equals(object obj) => obj is HoleId other && Equals(other);
        public override int GetHashCode() => ((int)Row * 397) ^ Column;
        public static bool operator ==(HoleId a, HoleId b) => a.Equals(b);
        public static bool operator !=(HoleId a, HoleId b) => !a.Equals(b);
        public override string ToString() => Name;
    }
}
