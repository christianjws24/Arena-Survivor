using System.Collections;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Efecto de partículas reutilizable para la destrucción de entidades (enemigos o jugador).
    /// Optimizado mediante Object Pooling: no usa Instantiate ni Destroy.
    /// Modifica dinámicamente el color y tamaño de las partículas según quién muera.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class DeathVFX : MonoBehaviour, IPoolable
    {
        [SerializeField] private ParticleSystem particleSys;
        private Coroutine _returnCoroutine;

        private void Awake()
        {
            if (particleSys == null) particleSys = GetComponent<ParticleSystem>();
        }

        public void OnSpawnFromPool()
        {
            if (particleSys == null) particleSys = GetComponent<ParticleSystem>();
            particleSys.Clear();
            particleSys.Stop();
        }

        public void OnReturnToPool()
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }

            if (particleSys != null)
            {
                particleSys.Stop();
                particleSys.Clear();
            }
        }

        /// <summary>
        /// Dispara la explosión de partículas con el color del enemigo o jugador destruido.
        /// </summary>
        public void PlayEffect(Color color, int particleCount = 25, float scale = 1f)
        {
            if (particleSys == null) particleSys = GetComponent<ParticleSystem>();

            var main = particleSys.main;
            main.startColor = color;
            main.startSize = 0.25f * scale;

            transform.localScale = Vector3.one * scale;

            particleSys.Clear();
            particleSys.Emit(particleCount);

            if (_returnCoroutine != null) StopCoroutine(_returnCoroutine);
            _returnCoroutine = StartCoroutine(ReturnAfterDuration(main.startLifetime.constantMax > 0 ? main.startLifetime.constantMax + 0.1f : 0.8f));
        }

        private IEnumerator ReturnAfterDuration(float duration)
        {
            yield return new WaitForSeconds(duration);

            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.ReturnToPool(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
