using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// 8_Sidewinder: 500hp.
    /// Spawn: aparece desde los bordes de la pantalla.
    /// Comportamiento: orbita alrededor del jugador; cada 4s se detiene, apunta (aviso) y dispara 2 proyectiles rápidos,
    /// luego retoma la órbita hasta la siguiente ráfaga.
    /// </summary>
    public class SidewinderEnemy : Enemy
    {
        [Header("Órbita")]
        [SerializeField] private float orbitRadius = 7f;

        [Header("Ráfaga")]
        [SerializeField] private float burstInterval = 4f;
        [Tooltip("Tiempo detenido apuntando antes del primer disparo")]
        [SerializeField] private float aimDuration = 0.45f;
        [SerializeField] private float shotSpacing = 0.14f;
        [SerializeField] private float projectileSpeed = 12f;
        [SerializeField] private string projectilePoolTag = "EnemyLaser";

        private enum State { Orbiting, Aiming, Firing }

        private State _state;
        private float _timer;
        private int _shotsFired;
        private float _orbitDirection = 1f;

        public override void OnSpawnFromPool()
        {
            base.OnSpawnFromPool();
            _state = State.Orbiting;
            _timer = Random.Range(2f, burstInterval);
            _orbitDirection = Random.value > 0.5f ? 1f : -1f;
        }

        protected override void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            Vector2 pos = transform.position;
            Vector2 toPlayer = playerPos - pos;
            float dist = Mathf.Max(0.001f, toPlayer.magnitude);
            Vector2 radialDir = toPlayer / dist;

            float angle = Mathf.Atan2(radialDir.y, radialDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            _timer -= deltaTime * AttackRateMultiplier;

            switch (_state)
            {
                case State.Orbiting:
                {
                    Vector2 tangent = new Vector2(-radialDir.y, radialDir.x) * _orbitDirection;
                    float radialError = Mathf.Clamp(dist - orbitRadius, -1.5f, 1.5f);
                    Vector2 moveDir = ((tangent * 0.9f) + (radialDir * (radialError * 0.6f))).normalized;
                    float speed = _currentMoveSpeed * _stasisSpeedMultiplier;
                    transform.Translate(((moveDir * speed) + separation) * deltaTime, Space.World);

                    if (_timer <= 0f)
                    {
                        _state = State.Aiming;
                        _timer = aimDuration;
                    }
                    break;
                }

                case State.Aiming:
                    if (spriteRenderer != null && _hitFlashCoroutine == null)
                    {
                        float t = 1f - Mathf.Clamp01(_timer / aimDuration);
                        spriteRenderer.color = Color.Lerp(_defaultColor, Color.white, t * 0.85f);
                    }
                    if (_timer <= 0f)
                    {
                        _state = State.Firing;
                        _shotsFired = 0;
                        _timer = 0f;
                    }
                    break;

                case State.Firing:
                    if (_timer <= 0f)
                    {
                        FireProjectile(projectilePoolTag, pos + (radialDir * 0.7f), radialDir, projectileSpeed);
                        _shotsFired++;
                        _timer = shotSpacing;

                        if (_shotsFired >= 2)
                        {
                            if (spriteRenderer != null) spriteRenderer.color = _defaultColor;
                            _state = State.Orbiting;
                            _timer = burstInterval;
                            _orbitDirection = Random.value > 0.3f ? _orbitDirection : -_orbitDirection;
                        }
                    }
                    break;
            }
        }
    }
}
