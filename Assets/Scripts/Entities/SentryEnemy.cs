using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// 6_Sentry: 400hp.
    /// Spawn: se materializa (fasea) en un punto visible de la pantalla.
    /// Comportamiento: torreta inmóvil que cada 3s dispara 3 tiros de velocidad media hacia el jugador en abanico ( \ | / ).
    /// Avisa medio segundo antes con un destello de carga. Es una torreta pesada: el Parry la daña pero no la lanza.
    /// </summary>
    public class SentryEnemy : Enemy
    {
        [Header("Ataque")]
        [SerializeField] private float fireInterval = 3f;
        [SerializeField] private float chargeTelegraph = 0.5f;
        [SerializeField] private float projectileSpeed = 7f;
        [SerializeField] private float spreadAngle = 18f;
        [SerializeField] private string projectilePoolTag = "EnemyBullet";

        [Header("Faseo")]
        [SerializeField] private float phaseInDuration = 1.2f;

        private float _fireTimer;
        private Transform _barrel;

        public override bool IsHeavy => true;

        protected override void Awake()
        {
            base.Awake();
            _barrel = transform.Find("Barrel");
            if (_barrel == null)
            {
                GameObject barrel = new GameObject("Barrel");
                barrel.transform.SetParent(transform, false);
                barrel.transform.localPosition = new Vector3(0.45f, 0f, 0f);
                barrel.transform.localScale = new Vector3(0.7f, 0.28f, 1f);
                SpriteRenderer sr = barrel.AddComponent<SpriteRenderer>();
                sr.sprite = Combat.ProceduralSprites.Square;
                sr.color = new Color(0.9f, 0.9f, 1f, 1f);
                sr.sortingOrder = (spriteRenderer != null ? spriteRenderer.sortingOrder : 3) + 1;
                _barrel = barrel.transform;
            }
        }

        public override void OnSpawnFromPool()
        {
            base.OnSpawnFromPool();
            _fireTimer = fireInterval;
            transform.rotation = Quaternion.identity;
            BeginPhaseIn(phaseInDuration);
        }

        protected override void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            if (_isPhasingIn) return;

            // Inmóvil: solo apunta el cuerpo hacia el jugador
            Vector2 toPlayer = playerPos - (Vector2)transform.position;
            float angle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            _fireTimer -= deltaTime * AttackRateMultiplier;

            // Telegrafiado: el cuerpo se ilumina mientras carga el disparo
            if (spriteRenderer != null && _hitFlashCoroutine == null)
            {
                float charge = 1f - Mathf.Clamp01(_fireTimer / chargeTelegraph);
                spriteRenderer.color = Color.Lerp(_defaultColor, Color.white, charge * 0.8f);
            }

            if (_fireTimer <= 0f)
            {
                _fireTimer = fireInterval;
                Vector2 baseDir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.down;
                Vector2 muzzle = (Vector2)transform.position + (baseDir * 0.8f);
                FireProjectile(projectilePoolTag, muzzle, Quaternion.Euler(0f, 0f, spreadAngle) * baseDir, projectileSpeed);
                FireProjectile(projectilePoolTag, muzzle, baseDir, projectileSpeed);
                FireProjectile(projectilePoolTag, muzzle, Quaternion.Euler(0f, 0f, -spreadAngle) * baseDir, projectileSpeed);
            }
        }

        public override void ConvertIntoParriedProjectile(Vector2 impulseDirection, float impulseForce, float damage = 50f, float maxDuration = 2f)
        {
            // Torreta anclada: recibe el daño del contraataque sin salir despedida
            TakeDamage(damage);
        }
    }
}
