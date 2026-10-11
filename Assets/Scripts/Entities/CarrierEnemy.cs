using System.Collections;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// 4_Carrier: 500hp.
    /// Spawn: Se desmaterializa/fasea en un punto visible dentro de la pantalla (Phases into screen).
    /// Comportamiento: Comienza a generar más Grunts y Drones en el campo de batalla.
    /// </summary>
    public class CarrierEnemy : Enemy
    {
        [Header("Generación de Enemigos (Spawning)")]
        [Tooltip("Intervalo en segundos entre cada spawn de Grunt o Drone")]
        [SerializeField] private float spawnInterval = 4.0f;

        [Tooltip("Tag de pool para Grunts")]
        [SerializeField] private string gruntPoolTag = "Enemy_Grunt";

        [Tooltip("Tag de pool para Drones")]
        [SerializeField] private string dronePoolTag = "Enemy_Drone";

        [Header("Efecto de Faseo (Phase-in)")]
        [Tooltip("Duración de la animación de materialización")]
        [SerializeField] private float phaseInDuration = 1.2f;

        private float _spawnTimer;
        private int _spawnCounter;

        public override bool IsHeavy => true;

        public override void OnSpawnFromPool()
        {
            base.OnSpawnFromPool();

            _spawnTimer = 2.0f; // Primer spawn rápido
            _spawnCounter = 0;

            BeginPhaseIn(phaseInDuration);
        }

        protected override void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            // Mientras esté en fase de materialización no se mueve ni spawnea
            if (_isPhasingIn) return;

            Vector2 currentPos = transform.position;
            Vector2 toPlayer = playerPos - currentPos;
            float dist = toPlayer.magnitude;

            // Mantener distancia media (alejarse si está muy cerca, acercarse si está muy lejos)
            Vector2 desiredDir;
            if (dist < 5.5f)
            {
                desiredDir = -toPlayer.normalized; // Retroceder
            }
            else if (dist > 8.5f)
            {
                desiredDir = toPlayer.normalized; // Aproximarse lentamente
            }
            else
            {
                // Deriva lateral lenta
                desiredDir = new Vector2(-toPlayer.y, toPlayer.x).normalized * 0.5f;
            }

            float speed = _currentMoveSpeed * _stasisSpeedMultiplier;
            Vector2 velocity = (desiredDir * speed) + separation;

            transform.Translate(velocity * deltaTime, Space.World);

            // Temporizador de generación
            _spawnTimer -= deltaTime * AttackRateMultiplier;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer = spawnInterval;
                SpawnMinion();
            }
        }

        private void SpawnMinion()
        {
            if (ObjectPooler.Instance == null) return;

            // Alternar entre Grunt y Drone
            bool spawnGrunt = (_spawnCounter % 2 == 0);
            _spawnCounter++;

            string chosenTag = spawnGrunt ? gruntPoolTag : dronePoolTag;

            // Si por alguna razón el pool tag específico no existe, fallback a Enemy_Red o Enemy_Yellow
            if (!ObjectPooler.Instance.HasPool(chosenTag))
            {
                chosenTag = spawnGrunt ? "Enemy_Red" : "Enemy_Yellow";
            }

            // Offset de aparición alrededor del Carrier
            Vector2 offset = Random.insideUnitCircle.normalized * 1.5f;
            Vector2 spawnPos = (Vector2)transform.position + offset;

            GameObject minion = ObjectPooler.Instance.SpawnFromPool(chosenTag, spawnPos, Quaternion.identity);

            // Pequeño flash visual de disparo/generación
            if (spriteRenderer != null)
            {
                StartCoroutine(SpawnPulseVisual());
            }

            // VFX de aparición
            if (ObjectPooler.Instance.HasPool("DeathVFX"))
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", spawnPos, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(new Color(0.7f, 0.4f, 1f), 12, 0.8f);
                }
            }
        }

        private IEnumerator SpawnPulseVisual()
        {
            if (spriteRenderer == null) yield break;
            Color original = spriteRenderer.color;
            spriteRenderer.color = Color.white;
            yield return new WaitForSeconds(0.08f);
            if (spriteRenderer != null && _currentState == EnemyState.Chasing)
            {
                spriteRenderer.color = original;
            }
        }

        public override void ConvertIntoParriedProjectile(Vector2 impulseDirection, float impulseForce, float damage = 50f, float maxDuration = 2f)
        {
            // El Carrier es una nave nodriza capital; recibe daño directo de contraataque
            TakeDamage(damage);
        }
    }
}
