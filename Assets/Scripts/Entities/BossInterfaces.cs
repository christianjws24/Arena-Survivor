using System;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// Jefe con Escudo de Energía que absorbe el daño de todas sus partes mientras esté activo.
    /// Permite que BossEnergyShieldPart, proyectiles y armas funcionen con cualquier jefe (Custodius, Mothership...).
    /// </summary>
    public interface IShieldedBoss
    {
        bool IsShieldActive { get; }
        void DamageEnergyShield(float amount);
    }

    /// <summary>
    /// Datos que la barra de jefe (BossHealthUI) necesita para pintarse, independientes del jefe concreto.
    /// </summary>
    public interface IBossStatus
    {
        string BossDisplayName { get; }
        string CoreLabel { get; }
        float CoreHealth { get; }
        float CoreMaxHealth { get; }
        float EnergyShieldHealth { get; }
        float EnergyShieldMaxHealth { get; }
        bool IsShieldActive { get; }
        bool IsRecharging { get; }
        float ShieldRechargeRemaining { get; }

        /// <summary>Texto de estado de las partes (izquierda / derecha) con rich text.</summary>
        string GetPartsStatus(bool leftSide);

        event Action OnBossStatusChanged;
    }

    /// <summary>
    /// Bus de eventos global de jefes: la UI y el WaveManager reaccionan sin conocer el tipo de jefe.
    /// </summary>
    public static class BossEvents
    {
        public static event Action<IBossStatus> OnBossSpawned;
        public static event Action<IBossStatus> OnBossDefeated;

        public static void RaiseSpawned(IBossStatus boss) => OnBossSpawned?.Invoke(boss);
        public static void RaiseDefeated(IBossStatus boss) => OnBossDefeated?.Invoke(boss);
    }
}
