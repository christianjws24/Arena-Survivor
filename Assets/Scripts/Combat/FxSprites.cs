using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Sprites de pixel art de los efectos visuales (Resources/FX), generados con Tools/Art/fx.py.
    /// Cada uno tiene los mismos 'bounds' que la forma provisional que reemplaza (1 unidad como ProceduralSprites,
    /// 2 como Ring.png), así los efectos conservan sus escalas. Si falta alguno devuelve null y el efecto usa la forma de siempre.
    /// </summary>
    public static class FxSprites
    {
        private const string Folder = "FX/";

        private static Sprite _ring, _disc, _ringTension, _ringAnchor, _shieldBoss, _shieldPlayer, _vortex, _satellite, _medkit, _pixel;
        private static Sprite[] _flash, _shockwave, _railBeam, _railCap, _railMuzzle;
        private static Texture2D _sparksSheet;
        private static Material _spriteMaterial;

        // Tintables (blancos/grises): el código les da el color
        public static Sprite Ring => Load(ref _ring, "fx_ring");
        public static Sprite Disc => Load(ref _disc, "fx_disc");
        public static Sprite RingTension => Load(ref _ringTension, "fx_ring_tension");
        public static Sprite RingAnchor => Load(ref _ringAnchor, "fx_ring_anchor");
        public static Sprite ShieldBoss => Load(ref _shieldBoss, "fx_shield_boss");
        public static Sprite ShieldPlayer => Load(ref _shieldPlayer, "fx_shield_player");
        public static Sprite Pixel => Load(ref _pixel, "fx_pixel");
        public static Sprite[] Flash => LoadFrames(ref _flash, "fx_flash_");

        // A color (se dibujan con tinte blanco)
        public static Sprite Vortex => Load(ref _vortex, "fx_vortex");
        public static Sprite Satellite => Load(ref _satellite, "fx_satellite");
        public static Sprite Medkit => Load(ref _medkit, "fx_medkit");
        /// <summary>Fotogramas de la onda expansiva; cada uno mide exactamente el diámetro de su aro.</summary>
        public static Sprite[] Shockwave => LoadFrames(ref _shockwave, "fx_shockwave_");

        // Rail Gun: tramo del rayo (malla Full Rect, se dibuja en modo Tiled), punta redondeada y destello de boca
        public static Sprite[] RailBeam => LoadFrames(ref _railBeam, "fx_rail_beam_");
        public static Sprite[] RailCap => LoadFrames(ref _railCap, "fx_rail_cap_");
        public static Sprite[] RailMuzzle => LoadFrames(ref _railMuzzle, "fx_rail_muzzle_");

        /// <summary>Tira de 3 fotogramas de chispa (21x7) para el Texture Sheet Animation de las explosiones.</summary>
        public static Texture2D SparksSheet
        {
            get
            {
                if (_sparksSheet == null) _sparksSheet = Resources.Load<Texture2D>(Folder + "fx_sparks_sheet");
                return _sparksSheet;
            }
        }

        /// <summary>
        /// Material por defecto de los SpriteRenderer en este pipeline (URP), para que las partículas
        /// se dibujen igual que los sprites. Se obtiene de un SpriteRenderer temporal.
        /// </summary>
        public static Material SpriteMaterial
        {
            get
            {
                if (_spriteMaterial == null)
                {
                    var go = new GameObject("SpriteMaterialProbe") { hideFlags = HideFlags.HideAndDontSave };
                    _spriteMaterial = go.AddComponent<SpriteRenderer>().sharedMaterial;
                    Object.Destroy(go);
                }
                return _spriteMaterial;
            }
        }

        private static Sprite Load(ref Sprite cache, string name)
        {
            if (cache == null) cache = Resources.Load<Sprite>(Folder + name);
            return cache;
        }

        /// <summary>Carga prefix0, prefix1... hasta el primero que falte (así el número de fotogramas lo decide fx.py).</summary>
        private static Sprite[] LoadFrames(ref Sprite[] cache, string prefix)
        {
            if (cache != null) return cache;
            var frames = new System.Collections.Generic.List<Sprite>();
            for (int i = 0; i < 64; i++)
            {
                Sprite frame = Resources.Load<Sprite>(Folder + prefix + i);
                if (frame == null) break;
                frames.Add(frame);
            }
            if (frames.Count == 0) return null;
            cache = frames.ToArray();
            return cache;
        }
    }
}
