using UnityEngine;

namespace ArenaSurvivor.Data
{
    public enum EnemyColorType
    {
        Red,     // Básico y agresivo
        Green,   // Rápido y frágil (enjambre)
        Blue,    // Tanque resistente y lento
        Yellow   // Especial / Acechador
    }

    /// <summary>
    /// Configuración de atributos y comportamiento de los enemigos según su color.
    /// Permite balancear estadísticas fácilmente sin modificar prefabs ni código.
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyData_", menuName = "Arena Survivor/Enemy Data", order = 1)]
    public class EnemyData : ScriptableObject
    {
        [Header("Identificación")]
        public string enemyName = "Basic Enemy";
        public EnemyColorType colorType = EnemyColorType.Red;
        public Color spriteColor = Color.red;

        [Header("Estadísticas de Combate")]
        [Tooltip("Puntos de vida máximos")]
        [Min(1f)]
        public float maxHealth = 10f;

        [Tooltip("Velocidad de persecución hacia el jugador")]
        [Min(0.1f)]
        public float moveSpeed = 3f;

        [Tooltip("Vidas/daño que resta al jugador al hacer contacto")]
        [Min(1)]
        public int damage = 1;

        [Header("Apariencia y Físicas")]
        [Tooltip("Escala relativa del sprite (ej: Tanque azul más grande, verde más pequeño)")]
        public Vector2 visualScale = Vector2.one;

        [Header("Game Feel (Juice) al Morir")]
        [Tooltip("Intensidad de sacudida de cámara al ser derrotado este enemigo")]
        [Range(0.01f, 1f)]
        public float deathShakeIntensity = 0.12f;

        [Tooltip("Duración de la sacudida de cámara en segundos")]
        [Range(0.02f, 0.5f)]
        public float deathShakeDuration = 0.08f;

        [Tooltip("Puntuación o experiencia otorgada al ser destruido")]
        public int scoreValue = 10;
    }
}
