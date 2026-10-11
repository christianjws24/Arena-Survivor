using UnityEngine;

namespace ArenaSurvivor.Data
{
    public enum ShipType
    {
        Gunner,     // SLAYER: especializada en disparos
        Parry,      // SPARTAN: especializada en parry
        Speedster   // CELERUS: especializada en movimiento
    }

    /// <summary>
    /// Estadísticas de cada nave elegible en el menú. Los valores son modificadores sobre la base del PlayerController
    /// y de PlayerWeapons, así que el balance general sigue ajustándose en el Inspector.
    /// </summary>
    public sealed class ShipDefinition
    {
        public ShipType Type;
        public string DisplayName;
        public string Tagline;
        public string Description;
        public Color Color;

        /// <summary>Nombre del sprite en Resources/Ships (48x48 a 30 PPU = 1.6 unidades, mirando hacia arriba).</summary>
        public string SpriteName;

        /// <summary>Multiplicador de la cadencia del disparo automático (1 = la base del jugador).</summary>
        public float FireRateMultiplier = 1f;

        /// <summary>Multiplicador del daño de los disparos (automático y armas HJKL). No afecta al Parry.</summary>
        public float ShotDamageMultiplier = 1f;

        /// <summary>Multiplicador de la velocidad de movimiento base.</summary>
        public float MoveSpeedMultiplier = 1f;

        /// <summary>Multiplicador de la munición de las armas HJKL (se redondea hacia arriba).</summary>
        public float WeaponAmmoMultiplier = 1f;

        /// <summary>
        /// Cooldown fijo del Parry tanto si acierta como si falla (segundos). 0 = cooldowns normales (éxito corto, fallo largo).
        /// La mejora 'rapid_parry' lo sigue reduciendo.
        /// </summary>
        public float FixedParryCooldown;

        public static readonly ShipDefinition Gunner = new ShipDefinition
        {
            Type = ShipType.Gunner,
            DisplayName = "SLAYER",
            Tagline = "Especialista en disparos",
            Description = "• Dispara un 30% más rápido\n• +15% de daño en los disparos\n• +50% de munición en las armas H J K L\n• Parry lento: 5s de recarga, aciertes o falles",
            Color = new Color(1f, 0.6f, 0.2f),
            SpriteName = "ship_shooter",
            FireRateMultiplier = 1.3f,
            ShotDamageMultiplier = 1.15f,
            WeaponAmmoMultiplier = 1.5f,
            FixedParryCooldown = 5f,
        };

        public static readonly ShipDefinition ParrySpecialist = new ShipDefinition
        {
            Type = ShipType.Parry,
            DisplayName = "SPARTAN",
            Tagline = "Especialista en parry",
            Description = "• Parry rápido: si aciertas se recarga casi al instante\n• Cadencia y daño de disparo normales\n• Munición normal en las armas H J K L",
            Color = new Color(0.25f, 0.85f, 1f),
            SpriteName = "ship_parry",
        };

        public static readonly ShipDefinition Speedster = new ShipDefinition
        {
            Type = ShipType.Speedster,
            DisplayName = "CELERUS",
            Tagline = "Especialista en velocidad",
            Description = "• Se mueve un 30% más rápido\n• Parry: 2.5s de recarga, aciertes o falles\n• Cadencia, daño y munición normales",
            Color = new Color(1f, 0.4f, 0.8f),
            SpriteName = "ship_speed",
            MoveSpeedMultiplier = 1.3f,
            FixedParryCooldown = 2.5f, // la mitad que Slayer
        };

        /// <summary>Sprite de la nave, o null si aún no existe (el jugador vuelve al círculo tintado).</summary>
        public Sprite LoadSprite()
        {
            return string.IsNullOrEmpty(SpriteName) ? null : Resources.Load<Sprite>("Ships/" + SpriteName);
        }

        public static readonly ShipDefinition[] All = { Gunner, ParrySpecialist, Speedster };

        public static ShipDefinition Get(ShipType type)
        {
            switch (type)
            {
                case ShipType.Gunner: return Gunner;
                case ShipType.Speedster: return Speedster;
                default: return ParrySpecialist;
            }
        }
    }

    /// <summary>
    /// Nave elegida en el menú. Persiste entre escenas (estático) y entre sesiones (PlayerPrefs).
    /// Si se entra directo a la escena de juego desde el editor, se usa la última nave elegida.
    /// </summary>
    public static class ShipSelection
    {
        private const string PrefsKey = "ArenaSurvivor.SelectedShip";
        private static ShipType? _selected;

        public static ShipType SelectedType
        {
            get
            {
                if (!_selected.HasValue)
                {
                    int stored = PlayerPrefs.GetInt(PrefsKey, (int)ShipType.Parry);
                    _selected = System.Enum.IsDefined(typeof(ShipType), stored) ? (ShipType)stored : ShipType.Parry;
                }
                return _selected.Value;
            }
            set
            {
                _selected = value;
                PlayerPrefs.SetInt(PrefsKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static ShipDefinition Current => ShipDefinition.Get(SelectedType);
    }
}
