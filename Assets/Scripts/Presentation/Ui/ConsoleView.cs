using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ImatPiSim.Presentation.Ui
{
    /// <summary>
    /// Consola de salida: lo que escribe el programa con <c>print</c>, seguido de los errores del
    /// programa (en rojo) y los fallos eléctricos (en naranja).
    /// </summary>
    /// <remarks>
    /// El texto se alinea abajo dentro de un área con máscara: si no cabe, se ven las últimas
    /// líneas, como en un terminal. Solo se reconstruye cuando cambia algo.
    /// </remarks>
    public sealed class ConsoleView : MonoBehaviour
    {
        /// <summary>Líneas de salida que se muestran como máximo (las anteriores se descartan).</summary>
        public const int MaxLines = 200;

        private TextMeshProUGUI _text;
        private int _signature = -1;

        /// <summary>Texto que se está mostrando, sin etiquetas de formato (para las pruebas).</summary>
        public string PlainText { get; private set; } = "";

        public static ConsoleView Create(RectTransform area)
        {
            UiFactory.Background(area, UiFactory.EditorColor);
            var viewport = UiFactory.Fill(area, "Área", 8);
            viewport.gameObject.AddComponent<RectMask2D>();
            var view = area.gameObject.AddComponent<ConsoleView>();
            view._text = UiFactory.Text(UiFactory.Fill(viewport, "Texto"), "", 20, UiFactory.TextColor,
                                        TextAlignmentOptions.BottomLeft, UiFactory.CodeFont);
            view._text.textWrappingMode = TextWrappingModes.Normal;
            view._text.overflowMode = TextOverflowModes.Overflow;
            return view;
        }

        /// <summary>Muestra la salida y los mensajes; no hace nada si no ha cambiado.</summary>
        /// <param name="output">Líneas de <c>print</c>.</param>
        /// <param name="errors">Mensajes de error del programa.</param>
        /// <param name="warnings">Fallos eléctricos.</param>
        public void Show(IReadOnlyList<string> output, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
        {
            int signature = Hash(output, Hash(errors, Hash(warnings, 17)));
            if (signature == _signature) return;
            _signature = signature;

            var rich = new StringBuilder();
            var plain = new StringBuilder();
            int first = Mathf.Max(0, output.Count - MaxLines);
            for (int i = first; i < output.Count; i++) Line(rich, plain, output[i], null);
            foreach (var e in errors) Line(rich, plain, e, "#ff6b6b");
            foreach (var w in warnings) Line(rich, plain, "Fallo eléctrico: " + w, "#ffb347");
            _text.text = rich.ToString();
            PlainText = plain.ToString();
        }

        private static void Line(StringBuilder rich, StringBuilder plain, string text, string color)
        {
            // <noparse> evita que un print("<b>") se interprete como formato.
            if (color != null) rich.Append("<color=").Append(color).Append('>');
            rich.Append("<noparse>").Append(text.Replace("</noparse>", "</no parse>")).Append("</noparse>");
            if (color != null) rich.Append("</color>");
            rich.Append('\n');
            plain.Append(text).Append('\n');
        }

        private static int Hash(IReadOnlyList<string> lines, int seed)
        {
            unchecked
            {
                int h = seed * 31 + lines.Count;
                if (lines.Count > 0) h = h * 31 + lines[lines.Count - 1].GetHashCode();
                return h;
            }
        }
    }
}
