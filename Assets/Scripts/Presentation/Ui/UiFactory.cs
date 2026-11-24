using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ImatPiSim.Presentation.Ui
{
    /// <summary>
    /// Crea elementos de uGUI (Canvas, paneles, textos TextMeshPro, botones) desde código.
    /// </summary>
    /// <remarks>
    /// La interfaz se construye en tiempo de ejecución, igual que la placa (<see cref="BoardView"/>):
    /// la escena solo contiene el objeto "Simulador" y no hay prefabs que mantener a mano. Las
    /// medidas están en píxeles de una pantalla de referencia de 1920 × 1080 (el
    /// <see cref="CanvasScaler"/> las adapta a la resolución real).
    /// </remarks>
    public static class UiFactory
    {
        public static readonly Color PanelColor = new Color(0.13f, 0.14f, 0.17f, 1f);
        public static readonly Color EditorColor = new Color(0.08f, 0.09f, 0.11f, 1f);
        public static readonly Color TextColor = new Color(0.90f, 0.91f, 0.93f);
        public static readonly Color DimTextColor = new Color(0.50f, 0.53f, 0.58f);

        private static TMP_FontAsset _codeFont;

        /// <summary>Fuente por defecto de TextMeshPro (LiberationSans SDF, de los TMP Essential Resources).</summary>
        public static TMP_FontAsset UiFont => TMP_Settings.defaultFontAsset;

        /// <summary>
        /// Fuente monoespaciada para el código, creada a partir de una fuente instalada en el sistema
        /// (Consolas en Windows, Menlo en macOS, DejaVu Sans Mono en Linux). Si no hay ninguna, se
        /// usa la fuente por defecto: el editor funciona igual, solo cambia el aspecto.
        /// </summary>
        public static TMP_FontAsset CodeFont
        {
            get
            {
                if (_codeFont != null) return _codeFont;
                foreach (var family in new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New" })
                {
                    _codeFont = TMP_FontAsset.CreateFontAsset(family, "Regular");
                    if (_codeFont != null) break;
                }
                if (_codeFont == null) _codeFont = UiFont;
                else if (UiFont != null) _codeFont.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { UiFont };
                return _codeFont;
            }
        }

        /// <summary>Canvas a pantalla completa (Screen Space Overlay) y su EventSystem, si no existe ya.</summary>
        public static Canvas CreateCanvas(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(parent, false);
            }
            return canvas;
        }

        /// <summary>Objeto vacío con RectTransform colocado por anclas (0–1) y márgenes en píxeles.</summary>
        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                         Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>Rectángulo que ocupa todo su padre, con un margen opcional.</summary>
        public static RectTransform Fill(Transform parent, string name, float margin = 0) =>
            Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(margin, margin), new Vector2(-margin, -margin));

        /// <summary>Añade un fondo de color liso (Image sin sprite).</summary>
        public static Image Background(RectTransform rt, Color color, bool raycastTarget = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        /// <summary>Texto TextMeshPro que ocupa todo <paramref name="rt"/>.</summary>
        public static TextMeshProUGUI Text(RectTransform rt, string text, float size, Color color,
                                           TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft,
                                           TMP_FontAsset font = null)
        {
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = font != null ? font : UiFont;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.text = text;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>Botón con fondo de color y una etiqueta centrada.</summary>
        public static Button Button(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax,
                                    Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var rt = Rect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            var img = Background(rt, color, raycastTarget: true);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            button.colors = colors;
            Text(Fill(rt, "Etiqueta"), label, 24, Color.white, TextAlignmentOptions.Center);
            return button;
        }
    }
}
