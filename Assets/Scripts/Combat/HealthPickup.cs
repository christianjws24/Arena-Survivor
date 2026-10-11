using ArenaSurvivor.Entities;
using ArenaSurvivor.Managers;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Botiquín que suelta un enemigo: recupera vida al jugador al tocarlo (o al acercarse, lo atrae un imán suave).
    /// Solo se recoge si al jugador le falta vida; si está al máximo, espera en el suelo hasta que caduque.
    /// Se construye proceduralmente (sin prefab) y desaparece parpadeando pasado su tiempo de vida.
    /// </summary>
    public class HealthPickup : MonoBehaviour
    {
        private const float Lifetime = 12f;
        private const float BlinkTime = 3f;
        private const float PickupRadius = 0.9f;
        private const float MagnetRadius = 3f;
        private const float MagnetSpeed = 8f;

        private static readonly Color PickupColor = new Color(0.25f, 1f, 0.5f);

        /// <summary>Botiquines activos en la arena (para limitar cuántos hay a la vez).</summary>
        public static int ActiveCount { get; private set; }

        private int _healAmount = 1;
        private float _timeLeft = Lifetime;
        private SpriteRenderer[] _renderers;
        private Color[] _baseColors;
        private float _bobSeed;

        /// <summary>
        /// Crea un botiquín en 'position' (se limita al interior de la arena).
        /// </summary>
        public static HealthPickup Spawn(Vector2 position, int healAmount = 1)
        {
            if (ArenaBoundary.Instance != null)
            {
                position = ArenaBoundary.Instance.ClampPosition(position, 0.8f);
            }

            GameObject root = new GameObject("HealthPickup");
            root.transform.position = position;

            Sprite medkit = FxSprites.Medkit;
            if (medkit != null)
            {
                // Botiquín en pixel art: cápsula con cruz verde y halo tramado
                AddSprite(root.transform, "Medkit", medkit, Color.white, 0.95f, Vector2.one, 6);
            }
            else
            {
                // Disco base + anillo exterior + cruz (+)
                AddSprite(root.transform, "Disc", ProceduralSprites.Circle, new Color(0.05f, 0.25f, 0.12f, 0.9f), 0.8f, Vector2.one, 5);
                AddSprite(root.transform, "Ring", ProceduralSprites.Ring, PickupColor, 0.95f, Vector2.one, 6);
                AddSprite(root.transform, "CrossH", ProceduralSprites.Square, Color.white, 1f, new Vector2(0.4f, 0.12f), 7);
                AddSprite(root.transform, "CrossV", ProceduralSprites.Square, Color.white, 1f, new Vector2(0.12f, 0.4f), 7);
            }

            HealthPickup pickup = root.AddComponent<HealthPickup>();
            pickup._healAmount = healAmount;
            return pickup;
        }

        private static void AddSprite(Transform parent, string name, Sprite sprite, Color color, float size, Vector2 scale, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(size * scale.x, size * scale.y, 1f);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
        }

        private void Awake()
        {
            _renderers = GetComponentsInChildren<SpriteRenderer>();
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) _baseColors[i] = _renderers[i].color;
            _bobSeed = Random.value * 10f;
        }

        private void OnEnable()
        {
            ActiveCount++;
        }

        private void OnDisable()
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        private void Update()
        {
            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            AnimateVisual();

            PlayerController player = PlayerController.Instance;
            if (player == null || player.IsDead) return;

            // "De ser necesario": solo se recoge si falta vida
            if (player.CurrentLives >= player.MaxLives) return;

            Vector2 toPlayer = (Vector2)player.transform.position - (Vector2)transform.position;
            float distance = toPlayer.magnitude;

            if (distance <= PickupRadius)
            {
                Collect(player);
                return;
            }

            if (distance <= MagnetRadius)
            {
                transform.Translate(toPlayer / distance * (MagnetSpeed * Time.deltaTime), Space.World);
            }
        }

        private void AnimateVisual()
        {
            // Pulso suave + parpadeo en los últimos segundos
            float pulse = 1f + (Mathf.Sin((Time.time + _bobSeed) * 5f) * 0.08f);
            transform.localScale = Vector3.one * pulse;

            float alpha = 1f;
            if (_timeLeft < BlinkTime)
            {
                alpha = Mathf.PingPong(Time.time * 8f, 1f) > 0.5f ? 1f : 0.25f;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                Color c = _baseColors[i];
                c.a *= alpha;
                _renderers[i].color = c;
            }
        }

        private void Collect(PlayerController player)
        {
            player.Heal(_healAmount);
            AudioManager.Instance?.PlayShieldAbsorb();

            if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(PickupColor, 14, 0.9f);
                }
            }

            Destroy(gameObject);
        }
    }
}
