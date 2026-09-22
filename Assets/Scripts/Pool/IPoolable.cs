namespace ArenaSurvivor.Pool
{
    /// <summary>
    /// Interfaz obligatoria para cualquier objeto que sea gestionado por el ObjectPooler.
    /// Reemplaza el uso de Start() o Awake() para inicializar variables cuando el objeto es reciclado.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>
        /// Se llama automáticamente cada vez que el objeto sale del pool y es activado.
        /// </summary>
        void OnSpawnFromPool();

        /// <summary>
        /// Se llama opcionalmente antes de devolver el objeto al pool.
        /// </summary>
        void OnReturnToPool();
    }
}
