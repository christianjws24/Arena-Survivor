using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// Colisionador y receptor de daño para el Escudo de Energía (500 HP) del Boss Custodius.
    /// Envuelve completamente la nave mientras el escudo está activo.
    /// Redirige todos los impactos al sistema central del escudo del jefe.
    /// Al caer el escudo, el GameObject visual se desactiva junto con este colisionador,
    /// permitiendo impactar directamente a los generadores laterales (1000 HP) o al núcleo central (1500 HP).
    /// 
    /// NOTA DE FÍSICAS: Hereda de MonoBehaviour (NO de Enemy) para evitar la adición obligatoria
    /// de Rigidbody2D dinámico y BoxCollider2D que causaban que la gravedad de Unity tirara el escudo
    /// hacia abajo al spawnear. Funciona como un Compound Collider hijo sujeto a la nave principal.
    /// </summary>
    public class BossEnergyShieldPart : MonoBehaviour
    {
        [Header("Referencia al Boss Principal")]
        [Tooltip("Jefe dueño del escudo (debe implementar IShieldedBoss)")]
        [SerializeField] private Enemy mainBoss;
        [Tooltip("Si es false, respeta la forma del collider existente (ej: elipse del Mothership)")]
        [SerializeField] private bool forceCircleCollider = true;

        private Collider2D _col;

        public Enemy MainBoss => mainBoss;
        private IShieldedBoss ShieldOwner => mainBoss as IShieldedBoss;

        private void Awake()
        {
            EnsureCollider();

            if (mainBoss == null)
            {
                mainBoss = GetComponentInParent<IShieldedBoss>() as Enemy;
            }

            // Eliminar cualquier Rigidbody residual no deseado en este hijo
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Destroy(rb);
            }
        }

        public void Initialize(Enemy boss, bool useCircleCollider = true)
        {
            mainBoss = boss;
            forceCircleCollider = useCircleCollider;
            EnsureCollider();

            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Destroy(rb);
            }
        }

        private void EnsureCollider()
        {
            if (_col == null)
            {
                _col = GetComponent<Collider2D>();
            }

            if (_col == null)
            {
                _col = gameObject.AddComponent<CircleCollider2D>();
            }

            _col.isTrigger = true;
            if (forceCircleCollider && _col is CircleCollider2D circle)
            {
                circle.radius = 0.5f;
                circle.offset = Vector2.zero;
            }
        }

        public void TakeDamage(float amount)
        {
            IShieldedBoss owner = ShieldOwner;
            if (owner != null && owner.IsShieldActive)
            {
                owner.DamageEnergyShield(amount);
            }
        }
    }
}
