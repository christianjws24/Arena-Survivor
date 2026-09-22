using System.Collections.Generic;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using ArenaSurvivor.Upgrades;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Controla los orbes orbitales del power-up "Módulo Satélite" (ID: 'satellite').
    /// Spawnea y posiciona en órbita continua N orbes según el nivel de la mejora.
    /// Cada orbe daña y destruye enemigos que intenten colisionar con el jugador.
    /// </summary>
    public class SatelliteOrbitController : MonoBehaviour
    {
        [Header("Configuración de Órbita")]
        [SerializeField] private float orbitRadius = 1.85f;
        [SerializeField] private float rotationSpeed = 200f; // Grados por segundo
        [SerializeField] private float damagePerHit = 25f;
        [SerializeField] private float hitCooldownPerEnemy = 0.3f; // Tiempo mínimo antes de volver a dañar al mismo enemigo

        [Header("Estética del Orbe")]
        [SerializeField] private Color orbColor = new Color(0.25f, 1f, 0.65f, 1f); // Verde menta / cian fluorescente
        [SerializeField] private float orbScale = 0.42f;

        private readonly List<GameObject> _activeOrbs = new List<GameObject>();
        private float _currentAngle;
        private int _cachedLevel = -1;
        private static Sprite _cachedOrbSprite;

        private void Start()
        {
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradeAdded += OnUpgradeChanged;
            }

            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnPlayerDeath += ClearOrbs;
            }

            RefreshOrbs();
        }

        private void OnDestroy()
        {
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradeAdded -= OnUpgradeChanged;
            }

            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnPlayerDeath -= ClearOrbs;
            }
        }

        private void OnUpgradeChanged(UpgradeData data, int newLevel)
        {
            if (data != null && data.id == "satellite")
            {
                RefreshOrbs();
            }
        }

        private void Update()
        {
            // Si el jugador está muerto, eliminar todos los orbes de inmediato
            if (PlayerController.Instance != null && PlayerController.Instance.IsDead)
            {
                if (_activeOrbs.Count > 0) ClearOrbs();
                return;
            }

            // Verificación continua de nivel de mejora
            int currentLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("satellite") : 0;
            if (currentLevel != _cachedLevel)
            {
                RefreshOrbs();
            }

            if (_activeOrbs.Count == 0) return;

            // Actualizar rotación continua
            _currentAngle += rotationSpeed * Time.deltaTime;
            if (_currentAngle >= 360f) _currentAngle -= 360f;

            int count = _activeOrbs.Count;
            float stepAngle = 360f / count;

            for (int i = 0; i < count; i++)
            {
                if (_activeOrbs[i] == null) continue;

                float angleDeg = _currentAngle + (i * stepAngle);
                float rad = angleDeg * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * orbitRadius;

                _activeOrbs[i].transform.position = transform.position + offset;
            }
        }

        public void ClearOrbs()
        {
            for (int i = 0; i < _activeOrbs.Count; i++)
            {
                if (_activeOrbs[i] != null)
                {
                    Destroy(_activeOrbs[i]);
                }
            }
            _activeOrbs.Clear();
            _cachedLevel = 0;
        }

        public void RefreshOrbs()
        {
            if (PlayerController.Instance != null && PlayerController.Instance.IsDead)
            {
                ClearOrbs();
                return;
            }

            int level = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("satellite") : 0;
            _cachedLevel = level;

            // Limpiar orbes sobrantes o añadir los necesarios
            while (_activeOrbs.Count > level)
            {
                int lastIdx = _activeOrbs.Count - 1;
                if (_activeOrbs[lastIdx] != null)
                {
                    Destroy(_activeOrbs[lastIdx]);
                }
                _activeOrbs.RemoveAt(lastIdx);
            }

            while (_activeOrbs.Count < level)
            {
                int orbIndex = _activeOrbs.Count;
                GameObject orb = CreateOrbInstance(orbIndex);
                _activeOrbs.Add(orb);
            }
        }

        private GameObject CreateOrbInstance(int index)
        {
            GameObject orb = new GameObject($"SatelliteOrb_{index}");
            orb.transform.position = transform.position;
            orb.transform.localScale = Vector3.one * orbScale;

            // SpriteRenderer con sprite circular suave
            SpriteRenderer sr = orb.AddComponent<SpriteRenderer>();
            sr.sprite = GetOrbSprite();
            sr.color = orbColor;
            sr.sortingOrder = 5;

            // Collider para detección de enemigos
            CircleCollider2D col = orb.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            // Rigidbody cinemático para disparar eventos OnTriggerEnter2D con enemigos
            Rigidbody2D rb = orb.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;

            // Componente de daño
            SatelliteOrbDamager damager = orb.AddComponent<SatelliteOrbDamager>();
            damager.Initialize(damagePerHit, hitCooldownPerEnemy, orbColor);

            return orb;
        }

        private static Sprite GetOrbSprite()
        {
            if (_cachedOrbSprite != null) return _cachedOrbSprite;

            // Generación procedural de textura circular con degradado suave
            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float radius = (size / 2f) - 2f;
            Vector2 center = new Vector2(size / 2f, size / 2f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= radius)
                    {
                        // Brillo central suave
                        float alpha = Mathf.Clamp01(1f - (dist / radius));
                        alpha = Mathf.Pow(alpha, 0.7f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();

            _cachedOrbSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
            return _cachedOrbSprite;
        }
    }

    /// <summary>
    /// Gestiona el impacto de cada orbe individual sobre los enemigos.
    /// </summary>
    public class SatelliteOrbDamager : MonoBehaviour
    {
        private float _damage;
        private float _hitCooldown;
        private Color _vfxColor;
        private readonly Dictionary<Enemy, float> _lastHitTimes = new Dictionary<Enemy, float>();

        public void Initialize(float damage, float hitCooldown, Color vfxColor)
        {
            _damage = damage;
            _hitCooldown = hitCooldown;
            _vfxColor = vfxColor;
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryDamageEnemy(other);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryDamageEnemy(other);
        }

        private void TryDamageEnemy(Collider2D other)
        {
            if (other.TryGetComponent(out Enemy enemy) && enemy.CurrentState == EnemyState.Chasing)
            {
                float now = Time.time;
                if (_lastHitTimes.TryGetValue(enemy, out float lastHit) && (now - lastHit) < _hitCooldown)
                {
                    return;
                }

                _lastHitTimes[enemy] = now;
                enemy.TakeDamage(_damage);

                // Feedback visual de impacto
                if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
                {
                    GameObject vfx = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                    if (vfx != null && vfx.TryGetComponent(out DeathVFX deathVfx))
                    {
                        deathVfx.PlayEffect(_vfxColor, 8, 0.4f);
                    }
                }
            }
        }
    }
}
