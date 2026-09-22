using UnityEngine;

namespace ArenaSurvivor.Upgrades
{
    /// <summary>
    /// Categoría de la mejora dentro del diseño de juego.
    /// </summary>
    public enum UpgradeType
    {
        StatModifier,       // Escala estadísticas numéricas acumulables (perforación, daño, rebotes)
        BehaviorUnlock,     // Desbloquea una nueva mecánica o comportamiento (dash, división, contagio)
        ZoneModifier        // Modifica la zona de extracción (quemadura por DPS, estasis)
    }

    /// <summary>
    /// Estructura de datos pura (ScriptableObject) para definir mejoras y power-ups.
    /// Arquitectura desacoplada: sirve como catálogo para la UI y definición de límites (Nivel Máximo).
    /// </summary>
    [CreateAssetMenu(fileName = "Upgrade_", menuName = "Arena Survivor/Upgrades/Upgrade Data", order = 1)]
    public class UpgradeData : ScriptableObject
    {
        [Header("Identificación Canónica")]
        [Tooltip("ID único utilizado por los sistemas para consultar el nivel (ej: 'pierce', 'parry_dash')")]
        public string id = "upgrade_id";

        [Tooltip("Nombre legible en la interfaz")]
        public string upgradeName = "Nueva Mejora";

        [TextArea(2, 4)]
        [Tooltip("Descripción general del efecto")]
        public string description = "Descripción del power-up.";

        [Header("Reglas de Progresión y Exclusión")]
        [Tooltip("Nivel Máximo: 1 para binarias/mecánicas únicas, 3 a 5 para acumulables")]
        [Min(1)]
        public int maxLevel = 1;

        [Tooltip("Tipo de mejora")]
        public UpgradeType upgradeType = UpgradeType.StatModifier;

        [Tooltip("Tag para exclusión mutua. Mejoras con el mismo tag no pueden coexistir (ej: 'Parry_Comportamiento'). Dejar vacío si no tiene exclusión.")]
        public string exclusiveTag = "";

        /// <summary>
        /// Indica si la mejora pertenece a una categoría de comportamiento excluyente.
        /// </summary>
        public bool HasExclusiveTag => !string.IsNullOrEmpty(exclusiveTag);

        [Header("Presentación Visual (UI)")]
        public Sprite icon;
        public Color themeColor = Color.cyan;

        /// <summary>
        /// Comprueba si la mejora ya no puede seguir subiendo de nivel.
        /// </summary>
        public bool IsMaxLevel(int currentLevel) => currentLevel >= maxLevel;

        /// <summary>
        /// Helper para formatear la descripción dinámica según el nivel a adquirir.
        /// </summary>
        public virtual string GetDescriptionForLevel(int nextLevel)
        {
            return description;
        }
    }
}
