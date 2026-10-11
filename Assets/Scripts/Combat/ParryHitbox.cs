using ArenaSurvivor.Entities;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Hitbox de detección para la mecánica de Parry.
    /// Se ubica preferentemente como hijo del Player y se activa exclusivamente durante los 0.2s de la ventana activa.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    public class ParryHitbox : MonoBehaviour
    {
        [SerializeField] private PlayerController playerController;
        [SerializeField] private SpriteRenderer visualIndicator;
        [SerializeField] private Color parryFlashColor = new Color(0.3f, 1f, 1f, 0.6f);

        private CircleCollider2D _collider;
        private Vector3 _baseScale = Vector3.one;

        private void Awake()
        {
            _collider = GetComponent<CircleCollider2D>();
            _collider.isTrigger = true;
            _baseScale = transform.localScale;

            if (playerController == null)
            {
                playerController = GetComponentInParent<PlayerController>();
            }

            if (visualIndicator == null)
            {
                visualIndicator = GetComponent<SpriteRenderer>();
            }

            // Inicia desactivado
            SetHitboxActive(false);
        }

        public void SetHitboxActive(bool active)
        {
            if (active)
            {
                // Mejora 'expansive_parry' (Parry Expansivo): +20% de radio por nivel (escala collider y visual a la vez)
                int expansiveLevel = Upgrades.UpgradeManager.Instance != null ? Upgrades.UpgradeManager.Instance.GetLevel("expansive_parry") : 0;
                transform.localScale = _baseScale * (1f + (expansiveLevel * 0.20f));
            }

            if (_collider != null) _collider.enabled = active;

            if (visualIndicator != null)
            {
                visualIndicator.enabled = active;
                if (active) visualIndicator.color = parryFlashColor;
            }
        }

        /// <summary>
        /// Aumenta porcentualmente el radio de detección del Parry (ej. Mejora Parry Expansivo).
        /// </summary>
        public void IncreaseRadiusPercent(float percent)
        {
            if (_collider == null) _collider = GetComponent<CircleCollider2D>();
            if (_collider != null)
            {
                _collider.radius *= (1f + percent);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Solo procesa colisiones si el hitbox está explícitamente activo
            if (_collider == null || !_collider.enabled) return;

            if (other.TryGetComponent(out Entities.BossEnergyShieldPart shieldPart))
            {
                if (playerController != null && shieldPart.MainBoss != null)
                {
                    playerController.OnParryTriggered(shieldPart.MainBoss);
                }
                return;
            }

            Enemy enemy = other.GetComponent<Enemy>();
            if (enemy == null) enemy = other.GetComponentInParent<Enemy>();

            if (enemy != null)
            {
                if (playerController != null)
                {
                    playerController.OnParryTriggered(enemy);
                }
            }
            else if (other.TryGetComponent(out EnemyProjectile projectile))
            {
                if (playerController != null)
                {
                    playerController.OnParryProjectile(projectile);
                }
            }
        }
    }
}
