using System.Collections;
using ArenaSurvivor.Managers;
using ArenaSurvivor.UI;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Gestiona el efecto "Hit Stop" (ralentización dramática del tiempo) para potenciar el Game Feel.
    /// Utiliza tiempo real (WaitForSecondsRealtime / Time.unscaledDeltaTime) para que el congelamiento
    /// sea independiente de la escala de tiempo física de Unity.
    /// </summary>
    [DisallowMultipleComponent]
    public class HitStopManager : MonoBehaviour
    {
        public static HitStopManager Instance { get; private set; }

        private Coroutine _hitStopCoroutine;
        private float _originalTimeScale = 1f;

        public bool IsHitStopActive => _hitStopCoroutine != null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDisable()
        {
            // Seguridad: asegurar que el juego nunca quede congelado si el objeto se desactiva,
            // pero sin romper la pausa si el modal de subida de nivel, pausa o game over está activo.
            bool isModalActive = LevelUpUIManager.Instance != null && LevelUpUIManager.Instance.IsModalOpen;
            bool isGameOver = GameManager.Instance != null && GameManager.Instance.IsGameOver;
            bool isPaused = GameManager.Instance != null && GameManager.Instance.IsPaused;

            if (!isModalActive && !isGameOver && !isPaused)
            {
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// Cancela de inmediato cualquier HitStop activo sin restaurar la escala a 1f si el juego fue pausado.
        /// </summary>
        public void CancelHitStop()
        {
            if (_hitStopCoroutine != null)
            {
                StopCoroutine(_hitStopCoroutine);
                _hitStopCoroutine = null;
            }
        }

        /// <summary>
        /// Reduce la escala de tiempo a un valor dramático (ej: 0.1f) durante una duración en segundos de tiempo real (ej: 0.15s).
        /// Con 'recoverRealtime' > 0 el tiempo no vuelve de golpe: acelera hasta la velocidad normal con ease-out,
        /// así el congelado se siente como un impacto y no como un tirón.
        /// </summary>
        /// <param name="durationRealtime">Duración del impacto en tiempo real.</param>
        /// <param name="timeScale">Escala de tiempo durante el impacto (0.01f a 0.2f recomendado).</param>
        /// <param name="recoverRealtime">Duración (tiempo real) de la vuelta suave a velocidad normal. 0 = de golpe.</param>
        public void TriggerHitStop(float durationRealtime = 0.15f, float timeScale = 0.1f, float recoverRealtime = 0f)
        {
            // Salvaguarda crítica: Si el modal de mejoras está abierto o el juego está pausado, ignorar HitStop
            if (LevelUpUIManager.Instance != null && LevelUpUIManager.Instance.IsModalOpen) return;
            if (GameManager.Instance != null && (GameManager.Instance.IsGameOver || GameManager.Instance.IsPaused)) return;
            if (Time.timeScale == 0f) return;

            if (_hitStopCoroutine != null)
            {
                StopCoroutine(_hitStopCoroutine);
            }

            _hitStopCoroutine = StartCoroutine(HitStopRoutine(durationRealtime, timeScale, recoverRealtime));
        }

        /// <summary>True si se abrió el modal de mejoras, la pausa o el Game Over (entonces no se toca la escala de tiempo).</summary>
        private static bool IsTimeOwnedElsewhere()
        {
            bool isModalActive = LevelUpUIManager.Instance != null && LevelUpUIManager.Instance.IsModalOpen;
            bool isGameOver = GameManager.Instance != null && GameManager.Instance.IsGameOver;
            bool isPaused = GameManager.Instance != null && GameManager.Instance.IsPaused;
            return isModalActive || isGameOver || isPaused || Time.timeScale == 0f;
        }

        private IEnumerator HitStopRoutine(float durationRealtime, float targetScale, float recoverRealtime)
        {
            Time.timeScale = targetScale;

            // Esperar en tiempo real desacoplado del Time.timeScale reducido
            yield return new WaitForSecondsRealtime(durationRealtime);

            // Vuelta suave: arranca rápido y se asienta en la velocidad normal (ease-out)
            float elapsed = 0f;
            while (elapsed < recoverRealtime)
            {
                if (IsTimeOwnedElsewhere())
                {
                    _hitStopCoroutine = null;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / recoverRealtime);
                Time.timeScale = Mathf.Lerp(targetScale, _originalTimeScale, 1f - ((1f - t) * (1f - t)));
                yield return null;
            }

            // Verificación crítica: Solo restaurar a 1f si no se abrió el modal de pausa o Game Over durante la espera
            if (!IsTimeOwnedElsewhere())
            {
                Time.timeScale = _originalTimeScale;
            }

            _hitStopCoroutine = null;
        }
    }
}
