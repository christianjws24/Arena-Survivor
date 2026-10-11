using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// 5_Worm: 200hp.
    /// Spawn: aparecen 4 a la vez, uno desde cada esquina de la pantalla (lo coordina el WaveManager).
    /// Comportamiento: avanzan hacia el jugador a velocidad regular, ondulando ligeramente.
    /// Visual: cabeza (collider) + segmentos de cola que siguen su rastro (solo decorativos).
    /// </summary>
    public class WormEnemy : Enemy
    {
        [Header("Movimiento Serpenteante")]
        [Tooltip("Amplitud del ondulado lateral (0 = línea recta)")]
        [SerializeField] private float wiggleAmplitude = 0.55f;
        [SerializeField] private float wiggleFrequency = 3.2f;

        [Header("Cola (Visual)")]
        [SerializeField] private int segmentCount = 4;
        [Tooltip("Distancia entre segmentos en unidades de mundo")]
        [SerializeField] private float segmentSpacing = 0.45f;

        private Transform[] _segments;
        private SpriteRenderer[] _segmentRenderers;
        private Vector3[] _segmentPositions;
        private float _phase;

        protected override void Awake()
        {
            base.Awake();
            BuildSegments();
        }

        private void BuildSegments()
        {
            _segments = new Transform[segmentCount];
            _segmentRenderers = new SpriteRenderer[segmentCount];
            _segmentPositions = new Vector3[segmentCount];

            for (int i = 0; i < segmentCount; i++)
            {
                Transform existing = transform.Find($"Segment_{i}");
                GameObject seg = existing != null ? existing.gameObject : new GameObject($"Segment_{i}");
                seg.transform.SetParent(transform, false);

                SpriteRenderer sr = seg.GetComponent<SpriteRenderer>();
                if (sr == null)
                {
                    sr = seg.AddComponent<SpriteRenderer>();
                    sr.sprite = Combat.ProceduralSprites.Circle;
                    sr.sortingOrder = (spriteRenderer != null ? spriteRenderer.sortingOrder : 3) - 1;
                }

                _segments[i] = seg.transform;
                _segmentRenderers[i] = sr;
            }
        }

        public override void OnSpawnFromPool()
        {
            base.OnSpawnFromPool();
            _phase = Random.Range(0f, Mathf.PI * 2f);

            // Reiniciar la cola apilada en la cabeza
            for (int i = 0; i < _segments.Length; i++)
            {
                _segmentPositions[i] = transform.position;
                float t = (i + 1f) / (_segments.Length + 1f);
                _segments[i].localScale = Vector3.one * Mathf.Lerp(0.85f, 0.45f, t);
            }
            UpdateSegments(true);
        }

        public override void Tick(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            base.Tick(deltaTime, playerPos, separation);
            UpdateSegments(false);
        }

        protected override void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            Vector2 toPlayer = playerPos - (Vector2)transform.position;
            Vector2 dir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.up;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            _phase += deltaTime * wiggleFrequency;
            Vector2 moveDir = (dir + (perp * (Mathf.Sin(_phase) * wiggleAmplitude))).normalized;

            float speed = _currentMoveSpeed * _stasisSpeedMultiplier;
            transform.Translate(((moveDir * speed) + separation) * deltaTime, Space.World);
        }

        /// <summary>
        /// Cada segmento sigue al anterior manteniendo la distancia (cadena cinemática simple).
        /// </summary>
        private void UpdateSegments(bool snap)
        {
            if (_segments == null) return;

            Vector3 leader = transform.position;
            Color segColor = spriteRenderer != null ? spriteRenderer.color : _defaultColor;

            for (int i = 0; i < _segments.Length; i++)
            {
                Vector3 pos = _segmentPositions[i];
                Vector3 delta = pos - leader;
                if (snap || delta.sqrMagnitude < 0.0001f)
                {
                    pos = leader;
                }
                else if (delta.magnitude > segmentSpacing)
                {
                    pos = leader + (delta.normalized * segmentSpacing);
                }

                _segmentPositions[i] = pos;
                _segments[i].position = pos;
                _segments[i].rotation = Quaternion.identity;
                _segmentRenderers[i].color = new Color(segColor.r * 0.85f, segColor.g * 0.85f, segColor.b * 0.85f, segColor.a);
                leader = pos;
            }
        }
    }
}
