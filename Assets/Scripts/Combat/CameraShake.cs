using System.Collections;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Gestiona sacudidas de cámara procedimentales reutilizables ("Juice" / Game Feel).
    /// Se aplica en LateUpdate para no entrar en conflicto con el seguimiento del jugador.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class CameraShake : MonoBehaviour
    {
        public static CameraShake Instance { get; private set; }

        [Header("Seguimiento Opcional del Jugador")]
        [Tooltip("Transform del objetivo (normalmente el jugador). Si se asigna, la cámara lo seguirá suavemente.")]
        [SerializeField] private Transform targetToFollow;
        [SerializeField] private float followSpeed = 10f;
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -10f);

        private Vector3 _basePosition;
        private Vector3 _shakeOffset;
        private Coroutine _shakeCoroutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _basePosition = transform.position;
        }

        private void LateUpdate()
        {
            // Si hay un objetivo a seguir (Jugador), actualizamos la posición base
            if (targetToFollow != null)
            {
                Vector3 desiredPosition = targetToFollow.position + followOffset;
                _basePosition = Vector3.Lerp(_basePosition, desiredPosition, followSpeed * Time.deltaTime);
            }

            // Aplicamos la posición base sumando el desplazamiento de la sacudida
            transform.position = _basePosition + _shakeOffset;
        }

        /// <summary>
        /// Permite asignar o cambiar el objetivo de seguimiento en tiempo de ejecución.
        /// </summary>
        public void SetFollowTarget(Transform target)
        {
            targetToFollow = target;
            if (target != null)
            {
                _basePosition = target.position + followOffset;
                transform.position = _basePosition;
            }
        }

        private float _currentShakeIntensity;

        /// <summary>
        /// Activa una sacudida de cámara con la duración e intensidad especificadas.
        /// Utiliza Time.unscaledDeltaTime para que el HitStop NO extienda el temblor en cámara lenta.
        /// </summary>
        public void Shake(float duration, float intensity)
        {
            if (duration <= 0f || intensity <= 0f) return;

            // Si ya hay una sacudida más intensa en curso, evitamos que impactos menores la reinicien
            if (_shakeCoroutine != null && _currentShakeIntensity > intensity)
            {
                return;
            }

            if (_shakeCoroutine != null)
            {
                StopCoroutine(_shakeCoroutine);
            }

            _currentShakeIntensity = intensity;
            _shakeCoroutine = StartCoroutine(DoShake(Mathf.Min(duration, 0.4f), intensity));
        }

        /// <summary>
        /// Detiene inmediatamente cualquier sacudida activa y devuelve la cámara a su posición base.
        /// </summary>
        public void StopShake()
        {
            if (_shakeCoroutine != null)
            {
                StopCoroutine(_shakeCoroutine);
                _shakeCoroutine = null;
            }

            _shakeOffset = Vector3.zero;
            _currentShakeIntensity = 0f;
            transform.position = _basePosition;
        }

        private IEnumerator DoShake(float duration, float intensity)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                // CRÍTICO: Usar unscaledDeltaTime para no verse ralentizado por el HitStop
                elapsed += Time.unscaledDeltaTime;
                float progress = elapsed / duration;

                // Amortiguación cúbica para un decaimiento suave y natural
                float damping = 1f - (progress * progress);
                float currentMagnitude = intensity * damping;

                // Desplazamiento aleatorio dentro del círculo unitario
                Vector2 randomPoint = Random.insideUnitCircle * currentMagnitude;
                _shakeOffset = new Vector3(randomPoint.x, randomPoint.y, 0f);

                yield return null;
            }

            _shakeOffset = Vector3.zero;
            _currentShakeIntensity = 0f;
            _shakeCoroutine = null;
        }
    }
}
