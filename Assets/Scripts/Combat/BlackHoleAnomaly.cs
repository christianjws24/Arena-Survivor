using System.Collections;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Prefab / Entidad del Power-Up "Anomalía Gravitacional".
    /// Creado desde el ObjectPooler al impactar un Parry.
    /// Atrae lentamente a los enemigos hacia su epicentro durante 3 segundos antes de auto-reciclarse.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    public class BlackHoleAnomaly : MonoBehaviour, IPoolable
    {
        [Header("Parámetros Gravitacionales")]
        [Tooltip("Duración activa en segundos antes de colapsar")]
        [SerializeField] private float duration = 3f;

        [Tooltip("Velocidad o fuerza con la que atrae a los enemigos")]
        [SerializeField] private float pullSpeed = 5.5f;

        [Tooltip("Radio de atracción efectivo")]
        [SerializeField] private float pullRadius = 4.5f;

        [Header("Referencias Visuales")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        private CircleCollider2D _col;
        private float _timer;
        private bool _isRecycling;
        private Vector3 _baseScale = Vector3.one * 1.5f;

        private void Awake()
        {
            _col = GetComponent<CircleCollider2D>();
            if (_col != null)
            {
                _col.isTrigger = true;
                _col.radius = pullRadius;
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
        }

        public void OnSpawnFromPool()
        {
            _timer = duration;
            _isRecycling = false;
            transform.localScale = Vector3.zero;

            if (_col != null)
            {
                _col.enabled = true;
            }

            StartCoroutine(SpawnPopInRoutine());
        }

        public void OnReturnToPool()
        {
            _isRecycling = true;
            if (_col != null)
            {
                _col.enabled = false;
            }
        }

        private IEnumerator SpawnPopInRoutine()
        {
            float elapsed = 0f;
            float popDuration = 0.25f;
            while (elapsed < popDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / popDuration);
                transform.localScale = Vector3.Lerp(Vector3.zero, _baseScale, t);
                yield return null;
            }
            transform.localScale = _baseScale;
        }

        private void Update()
        {
            if (_isRecycling) return;

            // Rotación hipnótica constante del vórtice
            transform.Rotate(0f, 0f, -220f * Time.deltaTime);

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                CollapseAndReturn();
            }
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_isRecycling) return;

            // Detección de enemigos en persecución para atracción gravitacional
            if (other.TryGetComponent(out Enemy enemy) && enemy.CurrentState == EnemyState.Chasing)
            {
                // Desarmar al enemigo mientras es arrastrado hacia el vórtice para no dañar injustamente al jugador
                enemy.MarkPulledByVortex(0.35f);

                Vector2 toCenter = (Vector2)transform.position - (Vector2)enemy.transform.position;
                float distance = toCenter.magnitude;

                if (distance > 0.15f)
                {
                    // La atracción es más intensa a medida que se acercan al vórtice
                    float intensity = Mathf.Clamp01(1f - (distance / pullRadius));
                    float currentPull = pullSpeed * (0.65f + intensity * 0.85f);

                    Vector2 step = toCenter.normalized * (currentPull * Time.deltaTime);
                    enemy.transform.Translate(step, Space.World);

                    // Si tiene Rigidbody2D, anular impulsos de escape
                    if (enemy.TryGetComponent(out Rigidbody2D rb))
                    {
#if UNITY_6000_0_OR_NEWER
                        rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, toCenter.normalized * currentPull, 0.2f);
#else
                        rb.velocity = Vector2.Lerp(rb.velocity, toCenter.normalized * currentPull, 0.2f);
#endif
                    }
                }
            }
        }

        private void CollapseAndReturn()
        {
            if (_isRecycling) return;
            _isRecycling = true;

            StartCoroutine(CollapseRoutine());
        }

        private IEnumerator CollapseRoutine()
        {
            float elapsed = 0f;
            float collapseDuration = 0.2f;
            Vector3 currentScale = transform.localScale;

            while (elapsed < collapseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / collapseDuration);
                transform.localScale = Vector3.Lerp(currentScale, Vector3.zero, t);
                yield return null;
            }

            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.ReturnToPool(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
