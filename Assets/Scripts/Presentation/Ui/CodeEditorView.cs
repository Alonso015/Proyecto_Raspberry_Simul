using System.Text;
using ImatPiSim.Domain.Interpreter;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ImatPiSim.Presentation.Ui
{
    /// <summary>
    /// Editor de código: un <see cref="TMP_InputField"/> de varias líneas con números de línea,
    /// sangría automática y dos marcas de línea (la del error y la que se está ejecutando).
    /// </summary>
    /// <remarks>
    /// <para>Solo es interfaz: no sabe nada de la simulación. <see cref="EditorPanel"/> le dice qué
    /// líneas marcar y le pide el texto al pulsar Ejecutar.</para>
    /// <para>Las marcas y los números de línea son objetos aparte del texto. En cada frame se
    /// colocan a la altura de la línea que dice TextMeshPro (<c>textInfo.lineInfo</c>) y siguen el
    /// desplazamiento del texto cuando el campo hace scroll. Sin ajuste de línea, la línea
    /// <i>n</i> del programa es la línea <i>n</i> que dibuja TextMeshPro.</para>
    /// </remarks>
    public sealed class CodeEditorView : MonoBehaviour
    {
        public const float FontSize = 22;
        private const float GutterWidth = 56;
        private const float Padding = 8;

        private static readonly Color ErrorLineColor = new Color(0.85f, 0.20f, 0.20f, 0.35f);
        private static readonly Color ExecLineColor = new Color(0.95f, 0.80f, 0.25f, 0.22f);

        private TMP_InputField _input;
        private TextMeshProUGUI _text;
        private TextMeshProUGUI _gutter;
        private RectTransform _errorMark;
        private RectTransform _execMark;
        private string _lastText = "";
        private int _gutterLines = -1;

        /// <summary>El campo de texto (público para las pruebas).</summary>
        public TMP_InputField Input => _input;

        /// <summary>Texto de la columna de números de línea.</summary>
        public string GutterText => _gutter.text;

        /// <summary>Línea marcada en rojo (error) o <c>null</c>. Se borra al editar el texto.</summary>
        public int? ErrorLine { get; set; }

        /// <summary>Línea que se está ejecutando, o <c>null</c> si el programa no está en marcha.</summary>
        public int? ExecutingLine { get; set; }

        /// <summary>Si la marca de error está visible en este momento (para las pruebas).</summary>
        public bool ErrorMarkVisible => _errorMark.gameObject.activeSelf;

        public string Text
        {
            get => _input.text;
            set
            {
                _input.SetTextWithoutNotify(value ?? "");
                _lastText = _input.text;
                ErrorLine = null;
            }
        }

        /// <summary>Crea el editor dentro de <paramref name="area"/>.</summary>
        public static CodeEditorView Create(RectTransform area, string initialText)
        {
            var view = area.gameObject.AddComponent<CodeEditorView>();
            view.Build(area);
            view.Text = initialText;
            return view;
        }

        private void Build(RectTransform area)
        {
            var font = UiFactory.CodeFont;
            UiFactory.Background(area, UiFactory.EditorColor);

            // Columna de números de línea (con máscara, para que se recorte al hacer scroll)
            var gutterArea = UiFactory.Rect(area, "Números de línea", Vector2.zero, new Vector2(0, 1),
                                            new Vector2(0, Padding), new Vector2(GutterWidth, -Padding));
            gutterArea.gameObject.AddComponent<RectMask2D>();
            _gutter = UiFactory.Text(UiFactory.Fill(gutterArea, "Texto"), "1", FontSize, UiFactory.DimTextColor,
                                     TextAlignmentOptions.TopRight, font);
            _gutter.textWrappingMode = TextWrappingModes.NoWrap;
            _gutter.margin = new Vector4(0, 0, 10, 0);

            // Campo de texto: GameObject con TMP_InputField → "Área de texto" (viewport con máscara) → marcas + texto
            var field = UiFactory.Rect(area, "Código", Vector2.zero, Vector2.one,
                                       new Vector2(GutterWidth, Padding), new Vector2(-Padding, -Padding));
            var viewport = UiFactory.Fill(field, "Área de texto");
            viewport.gameObject.AddComponent<RectMask2D>();

            _execMark = Mark(viewport, "Línea en ejecución", ExecLineColor);
            _errorMark = Mark(viewport, "Línea con error", ErrorLineColor);

            _text = UiFactory.Text(UiFactory.Fill(viewport, "Texto"), "", FontSize, UiFactory.TextColor,
                                   TextAlignmentOptions.TopLeft, font);
            _text.richText = false;

            _input = field.gameObject.AddComponent<TMP_InputField>();
            _input.textViewport = viewport;
            _input.textComponent = _text;
            _input.fontAsset = font;
            _input.pointSize = FontSize;
            _input.lineType = TMP_InputField.LineType.MultiLineNewline;
            _input.richText = false;
            _input.scrollSensitivity = 20;
            _input.caretColor = Color.white;
            _input.customCaretColor = true;
            _input.selectionColor = new Color(0.25f, 0.45f, 0.85f, 0.45f);
            _input.onValueChanged.AddListener(OnValueChanged);

            // El campo activa el ajuste de línea al ponerlo multilínea: se quita después. Sin ajuste,
            // una línea larga hace scroll horizontal y cada línea del programa es una línea dibujada
            // (si no, los números y las marcas se desplazarían).
            _text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static RectTransform Mark(RectTransform parent, string name, Color color)
        {
            // Ancho completo del área; altura y posición vertical se fijan en LateUpdate.
            var rt = UiFactory.Rect(parent, name, new Vector2(0, 1), new Vector2(1, 1));
            rt.pivot = new Vector2(0.5f, 0.5f);
            UiFactory.Background(rt, color);
            rt.gameObject.SetActive(false);
            return rt;
        }

        // ------------------------------------------------------------------ sangría automática

        /// <summary>
        /// Si el cambio ha sido escribir un salto de línea, añade detrás la sangría que toca
        /// (<see cref="SourceText.IndentForNewLine"/>).
        /// </summary>
        /// <remarks>
        /// Se detecta comparando con el texto anterior: el nuevo es el viejo con un '\n' justo
        /// antes del cursor. Así un pegado de varias líneas no se toca (cambia más de un carácter).
        /// </remarks>
        private void OnValueChanged(string newText)
        {
            int caret = _input.stringPosition;
            bool newline = newText.Length == _lastText.Length + 1 && caret > 0 && caret <= newText.Length
                           && newText[caret - 1] == '\n' && newText.Remove(caret - 1, 1) == _lastText;
            if (newline)
            {
                string indent = SourceText.IndentForNewLine(SourceText.LineBefore(newText, caret - 1));
                if (indent.Length > 0)
                {
                    newText = newText.Insert(caret, indent);
                    _input.SetTextWithoutNotify(newText);
                    _input.stringPosition = caret + indent.Length;
                }
            }
            _lastText = newText;
            ErrorLine = null;   // al editar, la línea del error ya puede no ser la misma
        }

        // ------------------------------------------------------------------ marcas y números de línea

        private void LateUpdate()
        {
            int lines = SourceText.CountLines(_input.text);
            if (lines != _gutterLines)
            {
                var sb = new StringBuilder();
                for (int i = 1; i <= lines; i++) sb.Append(i).Append('\n');
                _gutter.text = sb.ToString();
                _gutterLines = lines;
            }
            // El campo desplaza el texto moviendo su RectTransform: los números lo siguen.
            var offset = _text.rectTransform.anchoredPosition.y;
            _gutter.rectTransform.anchoredPosition = new Vector2(0, offset);

            Place(_errorMark, ErrorLine);
            Place(_execMark, ErrorLine.HasValue ? null : ExecutingLine);
        }

        private void Place(RectTransform mark, int? line)
        {
            var info = _text.textInfo;
            bool show = line.HasValue && info != null && line.Value >= 1 && line.Value <= info.lineCount;
            if (mark.gameObject.activeSelf != show) mark.gameObject.SetActive(show);
            if (!show) return;

            var li = info.lineInfo[line.Value - 1];
            float top = li.ascender, bottom = li.descender;
            if (top - bottom < 1) { top = li.baseline + FontSize * 0.8f; bottom = li.baseline - FontSize * 0.25f; }

            // De coordenadas del texto a coordenadas del área de texto (los dos cuelgan del mismo padre).
            var textRt = _text.rectTransform;
            var centre = textRt.parent.InverseTransformPoint(textRt.TransformPoint(new Vector3(0, (top + bottom) / 2, 0)));
            var parentRect = ((RectTransform)mark.parent).rect;
            mark.sizeDelta = new Vector2(0, top - bottom + 2);
            mark.anchoredPosition = new Vector2(0, centre.y - parentRect.yMax);
        }
    }
}
