using ArenaSurvivor.Entities;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Decide cuándo un enemigo derrotado suelta un botiquín. Solo suelta si al jugador le falta vida y la probabilidad
    /// crece cuanta más vida le falte, con enemigos pesados (Carrier, Sentry, jefes) más generosos y un tope de drops por tiempo.
    /// Se añade automáticamente al jugador.
    /// </summary>
    [DisallowMultipleComponent]
    public class HealthDropDirector : MonoBehaviour
    {
        [Header("Probabilidad")]
        [Tooltip("Probabilidad base de soltar un botiquín al matar un enemigo (si falta vida)")]
        [SerializeField, Range(0f, 1f)] private float baseDropChance = 0.04f;

        [Tooltip("Probabilidad extra cuando al jugador le falta TODA la vida (se escala con la vida que falta)")]
        [SerializeField, Range(0f, 1f)] private float missingHealthBonusChance = 0.10f;

        [Tooltip("Multiplicador para naves pesadas, torretas y jefes")]
        [SerializeField] private float heavyEnemyMultiplier = 4f;

        [Tooltip("Si pasan tantas muertes con el jugador herido sin que caiga un botiquín, el siguiente cae seguro")]
        [SerializeField] private int guaranteedDropAfterKills = 45;

        [Header("Límites")]
        [SerializeField] private float minSecondsBetweenDrops = 8f;
        [SerializeField] private int maxActivePickups = 2;

        private int _killsSinceDrop;
        private float _lastDropTime = -999f;

        private void OnEnable()
        {
            Enemy.OnEnemyDefeated += HandleEnemyDefeated;
        }

        private void OnDisable()
        {
            Enemy.OnEnemyDefeated -= HandleEnemyDefeated;
        }

        private void HandleEnemyDefeated(Enemy enemy)
        {
            PlayerController player = PlayerController.Instance;
            if (enemy == null || enemy.IsPhantomProjectile || player == null || player.IsDead) return;

            int missing = player.MaxLives - player.CurrentLives;
            if (missing <= 0)
            {
                _killsSinceDrop = 0;
                return;
            }

            _killsSinceDrop++;
            if (Time.time - _lastDropTime < minSecondsBetweenDrops) return;
            if (HealthPickup.ActiveCount >= maxActivePickups) return;

            float missingRatio = player.MaxLives > 0 ? (float)missing / player.MaxLives : 1f;
            float chance = baseDropChance + (missingHealthBonusChance * missingRatio);
            if (enemy.IsHeavy) chance *= heavyEnemyMultiplier;

            if (_killsSinceDrop >= guaranteedDropAfterKills || Random.value < chance)
            {
                HealthPickup.Spawn(enemy.transform.position);
                _lastDropTime = Time.time;
                _killsSinceDrop = 0;
            }
        }
    }
}
