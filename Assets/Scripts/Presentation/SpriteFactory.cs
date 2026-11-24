using UnityEngine;

namespace ImatPiSim.Presentation
{
    /// <summary>
    /// Sprites provisionales generados en código (cuadrado y círculo blancos que se tiñen con
    /// <c>SpriteRenderer.color</c>). Se crean una vez y se comparten entre todas las vistas.
    /// </summary>
    /// <remarks>Arte definitivo: etapa E19. Hasta entonces no hace falta ningún archivo de imagen.</remarks>
    public static class SpriteFactory
    {
        private const int Size = 64;
        private static Sprite _square;
        private static Sprite _circle;
        private static Font _font;

        public static Sprite Square => _square != null ? _square : (_square = Create(circle: false));
        public static Sprite Circle => _circle != null ? _circle : (_circle = Create(circle: true));

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private static Sprite Create(bool circle)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = circle ? "circulo" : "cuadrado"
            };
            float r = Size / 2f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float a = 1f;
                if (circle)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    a = Mathf.Clamp01(r - d);   // borde suavizado de 1 píxel
                }
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            // pixelsPerUnit = Size → el sprite mide 1 × 1 unidades; se escala con el Transform.
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }

        /// <summary>Crea un objeto hijo con un sprite teñido.</summary>
        public static SpriteRenderer Shape(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size,
                                           Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Crea un texto en el mundo (TextMesh con la fuente integrada de Unity).</summary>
        public static TextMesh Label(Transform parent, string text, Vector2 position, float height, Color color,
                                     TextAnchor anchor = TextAnchor.MiddleCenter, int order = 20)
        {
            var go = new GameObject("Texto: " + text.Replace("\n", " "));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = height / 6.4f;   // altura de línea ≈ height unidades
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Font.material;
            mr.sortingOrder = order;
            return tm;
        }
    }
}
