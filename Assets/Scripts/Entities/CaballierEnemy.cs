using ArenaSurvivor.Combat;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// 7_Caballier: 300hp.
    /// Spawn: aparece desde los bordes de la pantalla.
    /// Comportamiento: avanza en zigzag rápido (tramos cortos alternando ±ángulo) siempre hacia el jugador o cerca de él.
    /// Si el jugador lo esquiva, sigue de largo hasta una parte de la pantalla, frena y rebota de nuevo hacia el jugador.
    /// Ataque: al estar cerca se detiene, carga un instante (aviso) y lanza un tajo de energía en arco frente a él.
    /// El tajo se puede bloquear con el Parry durante la carga.
    /// </summary>
    public class CaballierEnemy : Enemy
    {
        private enum State { Dashing, Turning, WindUp, Slashing, Recover }

        [Header("Zigzag")]
        [SerializeField] private float dashSpeed = 10.5f;
        [SerializeField] private float dashDuration = 0.38f;
        [SerializeField] private float zigzagAngle = 38f;
        [SerializeField] private float turnPause = 0.12f;
        [Tooltip("Tras pasar de largo al jugador, distancia extra que recorre antes de rebotar")]
        [SerializeField] private float overshootDistance = 4f;

        [Header("Tajo de Energía")]
        [SerializeField] private float slashTriggerRange = 2.4f;
        [SerializeField] private float slashRange = 2.9f;
        [Tooltip("Semiángulo del arco del tajo")]
        [SerializeField] private float slashHalfAngle = 70f;
        [SerializeField] private float windUpDuration = 0.35f;
        [SerializeField] private float slashVisualDuration = 0.18f;
        [SerializeField] private float recoverDuration = 0.55f;

        private State _state;
        private float _stateTimer;
        private Vector2 _dashDir;
        private int _zigzagSign = 1;
        private bool _overshooting;
        private float _overshootRemaining;
        private Vector2 _slashDir;
        private SpriteRenderer _slashVisual;

        protected override void Awake()
        {
            base.Awake();
            Transform existing = transform.Find("SlashArc");
            GameObject arc = existing != null ? existing.gameObject : new GameObject("SlashArc");
            arc.transform.SetParent(transform, false);
            _slashVisual = arc.GetComponent<SpriteRenderer>();
            if (_slashVisual == null)
            {
                _slashVisual = arc.AddComponent<SpriteRenderer>();
                _slashVisual.sprite = ProceduralSprites.Ring;
                _slashVisual.sortingOrder = 9;
            }
            _slashVisual.enabled = false;
        }

        public override void OnSpawnFromPool()
        {
            base.OnSpawnFromPool();
            _zigzagSign = Random.value > 0.5f ? 1 : -1;
            _overshooting = false;
            if (_slashVisual != null) _slashVisual.enabled = false;
            BeginTurn(0.05f);
        }

        public override void OnReturnToPool()
        {
            base.OnReturnToPool();
            if (_slashVisual != null) _slashVisual.enabled = false;
        }

        public override void RecoverFromParry()
        {
            base.RecoverFromParry();
            if (_currentState == EnemyState.Chasing)
            {
                if (_slashVisual != null) _slashVisual.enabled = false;
                BeginTurn(turnPause);
            }
        }

        private void BeginTurn(float pause)
        {
            _state = State.Turning;
            _stateTimer = pause;
        }

        protected override void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            Vector2 pos = transform.position;
            Vector2 toPlayer = playerPos - pos;
            float dist = toPlayer.magnitude;
            Vector2 dirToPlayer = dist > 0.001f ? toPlayer / dist : Vector2.up;
            float timeScale = _stasisSpeedMultiplier;

            _stateTimer -= deltaTime * timeScale * AttackRateMultiplier;

            switch (_state)
            {
                case State.Turning:
                    if (_stateTimer <= 0f) StartDash(dirToPlayer, dist);
                    break;

                case State.Dashing:
                {
                    Vector2 step = _dashDir * (dashSpeed * timeScale * deltaTime);
                    transform.Translate(step + (separation * deltaTime), Space.World);

                    if (_overshooting)
                    {
                        _overshootRemaining -= step.magnitude;
                        // Rebotar al llegar a una parte de la pantalla o al borde de la cámara
                        if (_overshootRemaining <= 0f || IsHeadingOffScreen(transform.position, _dashDir))
                        {
                            _overshooting = false;
                            BeginTurn(turnPause * 2f);
                        }
                        break;
                    }

                    if (dist <= slashTriggerRange)
                    {
                        StartWindUp(dirToPlayer);
                        break;
                    }

                    // Esquivado: el jugador quedó atrás respecto a la dirección del dash -> pasa de largo
                    if (Vector2.Dot(_dashDir, dirToPlayer) < -0.2f)
                    {
                        _overshooting = true;
                        _overshootRemaining = overshootDistance;
                        break;
                    }

                    if (_stateTimer <= 0f || IsHeadingOffScreen(transform.position, _dashDir))
                    {
                        BeginTurn(turnPause);
                    }
                    break;
                }

                case State.WindUp:
                    // Aviso: parpadeo blanco
                    if (spriteRenderer != null && _hitFlashCoroutine == null)
                    {
                        spriteRenderer.color = Mathf.PingPong(Time.time * 20f, 1f) > 0.5f ? Color.white : _defaultColor;
                    }
                    if (_stateTimer <= 0f) PerformSlash(playerPos);
                    break;

                case State.Slashing:
                    if (_slashVisual != null)
                    {
                        float a = Mathf.Clamp01(_stateTimer / slashVisualDuration);
                        _slashVisual.color = new Color(0.6f, 1f, 1f, a);
                    }
                    if (_stateTimer <= 0f)
                    {
                        if (_slashVisual != null) _slashVisual.enabled = false;
                        _state = State.Recover;
                        _stateTimer = recoverDuration;
                    }
                    break;

                case State.Recover:
                    // Retrocede un poco tras atacar
                    transform.Translate(-dirToPlayer * (2f * timeScale * deltaTime), Space.World);
                    if (_stateTimer <= 0f) BeginTurn(0f);
                    break;
            }
        }

        private void StartDash(Vector2 dirToPlayer, float dist)
        {
            _state = State.Dashing;
            _stateTimer = dashDuration;
            _zigzagSign = -_zigzagSign;

            // Cerca del jugador el zigzag se cierra para ir directo
            float angle = dist < 4f ? zigzagAngle * 0.35f : zigzagAngle;
            _dashDir = Quaternion.Euler(0f, 0f, angle * _zigzagSign) * dirToPlayer;
            float rot = Mathf.Atan2(_dashDir.y, _dashDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, rot);
        }

        private void StartWindUp(Vector2 dirToPlayer)
        {
            _state = State.WindUp;
            _stateTimer = windUpDuration;
            _slashDir = dirToPlayer;
        }

        private void PerformSlash(Vector2 playerPos)
        {
            if (spriteRenderer != null) spriteRenderer.color = _defaultColor;

            // Corrige la dirección hacia la posición actual del jugador (el tajo es rápido pero no teledirigido del todo)
            Vector2 toPlayer = playerPos - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                _slashDir = Vector2.Lerp(_slashDir, toPlayer.normalized, 0.5f).normalized;
            }

            float rot = Mathf.Atan2(_slashDir.y, _slashDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, rot);

            if (_slashVisual != null)
            {
                float parentScale = Mathf.Max(0.01f, transform.lossyScale.x);
                _slashVisual.enabled = true;
                _slashVisual.transform.localPosition = new Vector3(0.9f / parentScale, 0f, 0f);
                _slashVisual.transform.localScale = Vector3.one * ((slashRange * 1.4f) / parentScale);
                _slashVisual.color = new Color(0.6f, 1f, 1f, 1f);
            }

            _state = State.Slashing;
            _stateTimer = slashVisualDuration;

            // Impacto: el jugador dentro del arco recibe daño (si su Parry no está activo)
            PlayerController player = PlayerController.Instance;
            if (player == null || player.IsDead || IsBeingPulledByVortex) return;

            Vector2 toTarget = (Vector2)player.transform.position - (Vector2)transform.position;
            if (toTarget.magnitude <= slashRange && Vector2.Angle(_slashDir, toTarget) <= slashHalfAngle)
            {
                if (player.CurrentParryState == ParryState.Active)
                {
                    // Tajo bloqueado: el Parry lo convierte en contraataque
                    player.OnParryTriggered(this);
                }
                else
                {
                    player.TakeDamage(OutgoingDamage);
                }
            }
        }

        /// <summary>
        /// True si está en el borde de la cámara y el dash lo sacaría aún más de la pantalla (punto de rebote).
        /// Mientras entra desde fuera de la pantalla hacia el jugador devuelve false.
        /// </summary>
        private static bool IsHeadingOffScreen(Vector2 position, Vector2 dir)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 vp = cam.WorldToViewportPoint(position);
            return (vp.x < 0.04f && dir.x < 0f) || (vp.x > 0.96f && dir.x > 0f) ||
                   (vp.y < 0.05f && dir.y < 0f) || (vp.y > 0.95f && dir.y > 0f);
        }
    }
}
