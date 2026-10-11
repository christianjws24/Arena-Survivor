using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Sprites procedurales cacheados (cuadrado, círculo, anillo, triángulo) generados en runtime.
    /// Permite construir enemigos, zonas y efectos sin depender de assets en disco.
    /// Todos miden 1 unidad de mundo (pixelsPerUnit = resolución).
    /// </summary>
    public static class ProceduralSprites
    {
        private static Sprite _square;
        private static Sprite _circle;
        private static Sprite _ring;
        private static Sprite _triangle;

        public static Sprite Square => _square != null ? _square : (_square = Create("Proc_Square", 16, (x, y, s) => 1f));

        public static Sprite Circle => _circle != null ? _circle : (_circle = Create("Proc_Circle", 64, (x, y, s) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f));
            return Mathf.Clamp01((s * 0.5f) - d);
        }));

        public static Sprite Ring => _ring != null ? _ring : (_ring = Create("Proc_Ring", 128, (x, y, s) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f));
            float outer = s * 0.5f;
            float inner = outer - (s * 0.06f);
            return Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
        }));

        /// <summary>Triángulo que apunta a +X (derecha), útil como flecha de orientación.</summary>
        public static Sprite Triangle => _triangle != null ? _triangle : (_triangle = Create("Proc_Triangle", 64, (x, y, s) =>
        {
            float u = (x + 0.5f) / s;           // 0..1 a lo largo del eje X
            float v = Mathf.Abs(((y + 0.5f) / s) - 0.5f) * 2f; // 0 en el centro, 1 en los bordes
            return v <= (1f - u) ? 1f : 0f;
        }));

        private delegate float AlphaFunc(int x, int y, int size);

        private static Sprite Create(string name, int size, AlphaFunc alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte a = (byte)(Mathf.Clamp01(alpha(x, y, size)) * 255f);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = name;
            return sprite;
        }
    }
}
