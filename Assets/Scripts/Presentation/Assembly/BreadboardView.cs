using System;
using System.Collections.Generic;
using ImatPiSim.Domain.Wiring;
using UnityEngine;

namespace ImatPiSim.Presentation.Assembly
{
    /// <summary>
    /// Dibuja la protoboard (agujeros, raíles, números de columna y letras de fila) y traduce
    /// entre agujeros del modelo (<see cref="HoleId"/>) y posiciones del mundo.
    /// </summary>
    /// <remarks>
    /// <para><b>Del ratón a la rejilla.</b> La cámara pasa el ratón de píxeles a unidades del
    /// mundo (<c>ScreenToWorldPoint</c>). Esta clase lo pasa a coordenadas locales de la placa y
    /// busca la columna y la fila más cercanas: columna = redondear(x / paso); fila = la de
    /// <see cref="RowY"/> más próxima. Si el punto está a más de medio paso del agujero, no es
    /// ninguno. Es la operación inversa de <see cref="HoleToWorld"/>.</para>
    /// <para>Las filas no están todas a la misma distancia: entre los raíles y el área central,
    /// y en el canal central (entre e y f), hay un hueco extra, como en una placa real. La
    /// rejilla del A* no tiene huecos (cuenta pasos), así que solo cambia el dibujo.</para>
    /// </remarks>
    public sealed class BreadboardView : MonoBehaviour, IWorkspaceArea
    {
        /// <summary>Centro de la protoboard en el mundo.</summary>
        public static readonly Vector2 Centre = new Vector2(0, -4.9f);

        /// <summary>Distancia entre agujeros (unidades del mundo).</summary>
        public const float Pitch = 0.36f;

        private const float RailGap = 0.30f;
        private const float ChannelGap = 0.36f;
        private const float Margin = 0.45f;

        private static readonly Color BoardColor = new Color(0.93f, 0.92f, 0.88f);
        private static readonly Color HoleColor = new Color(0.32f, 0.32f, 0.33f);
        private static readonly Color PlusColor = new Color(0.85f, 0.20f, 0.18f);
        private static readonly Color MinusColor = new Color(0.20f, 0.35f, 0.80f);
        private static readonly Color TextColor = new Color(0.35f, 0.35f, 0.38f);

        private static readonly float[] RowY = BuildRowY();
        private static readonly float Height = RowY[0] - RowY[Breadboard.RowCount - 1];
        private static readonly float Width = (Breadboard.Columns - 1) * Pitch;

        private Transform _root;
        private readonly Dictionary<HoleId, SpriteRenderer> _holes = new Dictionary<HoleId, SpriteRenderer>();

        /// <summary>Rectángulo de la placa en el mundo.</summary>
        public Rect Bounds
        {
            get
            {
                var size = new Vector2(Width + 2 * Margin, Height + 2 * Margin + 0.35f);   // + números de columna
                var centre = (Vector2)transform.position + Centre + new Vector2(0, 0.175f);
                return new Rect(centre - size / 2, size);
            }
        }

        /// <summary>Fila (en la rejilla) → y local, con los huecos de los raíles y del canal central.</summary>
        private static float[] BuildRowY()
        {
            var y = new float[Breadboard.RowCount];
            float total = (Breadboard.RowCount - 1) * Pitch + 2 * RailGap + ChannelGap;
            float top = total / 2;
            for (int r = 0; r < y.Length; r++)
            {
                float extra = 0;
                if (r >= (int)BreadboardRow.A) extra += RailGap;
                if (r >= (int)BreadboardRow.F) extra += ChannelGap;
                if (r >= (int)BreadboardRow.BottomPlus) extra += RailGap;
                y[r] = top - r * Pitch - extra;
            }
            return y;
        }

        private void Awake() => Build();

        private void Build()
        {
            _root = new GameObject("Protoboard").transform;
            _root.SetParent(transform, false);
            _root.localPosition = Centre;

            SpriteFactory.Shape(_root, "Placa", SpriteFactory.Square, Vector2.zero,
                                new Vector2(Width + 2 * Margin, Height + 2 * Margin), BoardColor, 0);
            SpriteFactory.Shape(_root, "Canal central", SpriteFactory.Square,
                                new Vector2(0, (RowY[(int)BreadboardRow.E] + RowY[(int)BreadboardRow.F]) / 2),
                                new Vector2(Width + Margin, 0.12f), new Color(0.80f, 0.79f, 0.75f), 1);

            // Líneas de los raíles: rojo junto a +, azul junto a −
            foreach (var (row, color, side) in new[]
                     {
                         (BreadboardRow.TopPlus, PlusColor, 1f), (BreadboardRow.TopMinus, MinusColor, -1f),
                         (BreadboardRow.BottomPlus, PlusColor, 1f), (BreadboardRow.BottomMinus, MinusColor, -1f)
                     })
            {
                float y = RowY[(int)row] + side * Pitch * 0.45f;
                SpriteFactory.Shape(_root, "Raíl " + row, SpriteFactory.Square, new Vector2(0, y), new Vector2(Width, 0.035f), color, 1);
                string sign = row == BreadboardRow.TopPlus || row == BreadboardRow.BottomPlus ? "+" : "−";
                SpriteFactory.Label(_root, sign, new Vector2(-Width / 2 - 0.28f, RowY[(int)row]), 0.26f, color);
            }

            // Agujeros
            for (int r = 0; r < Breadboard.RowCount; r++)
                for (int c = 1; c <= Breadboard.Columns; c++)
                {
                    var hole = new HoleId((BreadboardRow)r, c);
                    _holes[hole] = SpriteFactory.Shape(_root, hole.Name, SpriteFactory.Square, Local(hole), Vector2.one * 0.15f, HoleColor, 2);
                }

            // Letras de fila y números de columna
            for (var row = BreadboardRow.A; row <= BreadboardRow.J; row++)
            {
                string letter = ((char)('a' + (row - BreadboardRow.A))).ToString();
                SpriteFactory.Label(_root, letter, new Vector2(-Width / 2 - 0.28f, RowY[(int)row]), 0.2f, TextColor);
                SpriteFactory.Label(_root, letter, new Vector2(Width / 2 + 0.28f, RowY[(int)row]), 0.2f, TextColor);
            }
            for (int c = 1; c <= Breadboard.Columns; c++)
                if (c == 1 || c % 5 == 0)
                    SpriteFactory.Label(_root, c.ToString(), new Vector2(-Width / 2 + (c - 1) * Pitch, RowY[0] + Margin + 0.12f), 0.2f, TextColor);
        }

        private static Vector2 Local(HoleId hole) => new Vector2(-Width / 2 + (hole.Column - 1) * Pitch, RowY[hole.GridRow]);

        /// <summary>Posición en el mundo del centro de un agujero.</summary>
        public Vector2 HoleToWorld(HoleId hole) => (Vector2)transform.position + Centre + Local(hole);

        /// <summary>
        /// El agujero que hay bajo un punto del mundo, o <c>null</c> si el punto no está a menos
        /// de medio paso de ninguno.
        /// </summary>
        public HoleId? WorldToHole(Vector2 world)
        {
            var local = world - (Vector2)transform.position - Centre;
            int column = Mathf.RoundToInt((local.x + Width / 2) / Pitch) + 1;
            if (column < 1 || column > Breadboard.Columns) return null;

            int row = 0;
            for (int r = 1; r < RowY.Length; r++)
                if (Math.Abs(RowY[r] - local.y) < Math.Abs(RowY[row] - local.y)) row = r;

            var hole = new HoleId((BreadboardRow)row, column);
            return Vector2.Distance(Local(hole), local) <= Pitch * 0.5f ? hole : (HoleId?)null;
        }

        /// <summary>Resalta (o quita el resalte de) unos agujeros: vista previa al colocar.</summary>
        public void Highlight(IEnumerable<HoleId> holes, Color? color)
        {
            foreach (var kv in _holes) kv.Value.color = HoleColor;
            if (holes == null || color == null) return;
            foreach (var h in holes)
                if (_holes.TryGetValue(h, out var sr)) sr.color = color.Value;
        }
    }
}
