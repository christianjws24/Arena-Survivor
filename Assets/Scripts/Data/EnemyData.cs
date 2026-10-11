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

    public enum EnemyType
    {
        Grunt,              // 1_Grunt: 100hp, avanza lento hacia el jugador
        Drone,              // 2_Drone: 110hp, se mueve en círculos y dispara proyectil lento
        Parasite,           // 3_Parasite: 70hp, avanza rápidamente hacia el jugador
        Carrier,            // 4_Carrier: 500hp, aparece en pantalla y genera Grunts y Drones
        BossCentinelMajoris, // BOSS 1_CUSTODIUS: nave con generadores, escudo de energía y láseres
        Worm,               // 5_Worm: 200hp, aparecen 4 desde las 4 esquinas y avanzan a velocidad regular
        Sentry,             // 6_Sentry: 400hp, fasea en pantalla, inmóvil, dispara 3 tiros (|/) cada 3s
        Caballier,          // 7_Caballier: 300hp, zigzag rápido hacia el jugador, rebota y lanza tajo de energía
        Sidewinder,         // 8_Sidewinder: 500hp, orbita, se detiene a disparar 2 proyectiles rápidos cada 4s
        BossMothership      // BOSS 2_MOTHERSHIP: barre la parte superior con 5 armas y escudo de energía
    }

    /// <summary>
    /// Configuración de atributos y comportamiento de los enemigos.
    /// Permite balancear estadísticas fácilmente sin modificar prefabs ni código.
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyData_", menuName = "Arena Survivor/Enemy Data", order = 1)]
    public class EnemyData : ScriptableObject
    {
        [Header("Identificación")]
        public string enemyName = "Basic Enemy";
        public EnemyType enemyType = EnemyType.Grunt;
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

        [Tooltip("Puntuación y experiencia otorgada al ser destruido. La energía/EXP que gana el jugador es proporcional: " +
                 "10 = enemigo de referencia (Grunt), 20 = el doble, etc. (ver PlayerEnergy)")]
        public int scoreValue = 10;
    }
}
