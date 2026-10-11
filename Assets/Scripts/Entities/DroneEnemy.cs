using ArenaSurvivor.Combat;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// 2_Drone: 110hp.
    /// Spawn: Aparece desde los bordes de la pantalla.
    /// Comportamiento: Se desplaza en círculos (órbita) alrededor del jugador y dispara un proyectil lento periódicamente.
    /// </summary>
    public class DroneEnemy : Enemy
    {
        [Header("Comportamiento de Órbita")]
        [Tooltip("Radio objetivo de órbita alrededor del jugador")]
        [SerializeField] private float targetOrbitRadius = 6.0f;

        [Tooltip("Sentido de giro de órbita (1 horario, -1 antihorario)")]
        [SerializeField] private float orbitDirection = 1f;

        [Header("Ataque a Distancia")]
        [Tooltip("Intervalo entre disparos de proyectiles lentos")]
        [SerializeField] private float fireInterval = 2.8f;

        [Tooltip("Tag del pool para el proyectil")]
        [SerializeField] private string projectilePoolTag = "EnemyBullet";

        [Tooltip("Velocidad del proyectil lento")]
        [SerializeField] private float projectileSpeed = 4.5f;

        private float _fireTimer;

        public override void OnSpawnFromPool()
        {
            base.OnSpawnFromPool();
            // Variación aleatoria de sentido de giro para que no todos giren hacia el mismo lado
            orbitDirection = Random.value > 0.5f ? 1f : -1f;
            _fireTimer = Random.Range(1.0f, fireInterval);
        }

        protected override void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            Vector2 currentPos = transform.position;
            Vector2 toPlayer = playerPos - currentPos;
            float currentDist = toPlayer.magnitude;

            if (currentDist < 0.001f) currentDist = 0.001f;

            Vector2 radialDir = toPlayer / currentDist;

            // Vector perpendicular (tangencial) para orbitar
            Vector2 tangentDir = new Vector2(-radialDir.y, radialDir.x) * orbitDirection;

            // Ajuste radial para aproximarse o alejarse según el radio deseado
            float radialError = currentDist - targetOrbitRadius;
            float radialCorrectionWeight = Mathf.Clamp(radialError, -1.5f, 1.5f);

            Vector2 desiredMoveDir = (tangentDir * 0.85f + radialDir * (radialCorrectionWeight * 0.7f)).normalized;

            float speed = _currentMoveSpeed * _stasisSpeedMultiplier;
            Vector2 velocity = (desiredMoveDir * speed) + separation;

            transform.Translate(velocity * deltaTime, Space.World);

            // Orientación: mirar suavemente hacia el jugador o en la dirección de vuelo
            float angle = Mathf.Atan2(radialDir.y, radialDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            // Temporizador de disparo
            _fireTimer -= deltaTime * AttackRateMultiplier;
            if (_fireTimer <= 0f)
            {
                _fireTimer = fireInterval;
                ShootProjectile(playerPos);
            }
        }

        private void ShootProjectile(Vector2 targetPos)
        {
            FireProjectile(projectilePoolTag, transform.position, targetPos - (Vector2)transform.position, projectileSpeed);
        }
    }
}
