using System.Collections;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// Controlador exclusivo de "Game Feel" y Efectos Visuales (Juice) del Jugador.
    /// Desacoplado de la lógica de físicas y combate mediante eventos.
    /// Gestiona:
    /// 1. Anillo de Tensión que se encoge durante la ventana activa del Parry (0.2s).
    /// 2. Onda expansiva (Shockwave) instanciada desde el ObjectPooler al conectar un Parry exitoso.
    /// 3. Feedback visual de vulnerabilidad (opacidad al 50% y tinte grisáceo) durante el Cooldown de castigo.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerVFXController : MonoBehaviour
    {
        [Header("Referencias del Jugador")]
        [SerializeField] private PlayerController player;
        [SerializeField] private SpriteRenderer playerSpriteRenderer;

        [Header("1. Feedback de Ventana Activa (Anillo de Tensión)")]
        [Tooltip("Objeto hijo del jugador con SpriteRenderer de anillo hueco")]
        [SerializeField] private GameObject tensionRingObject;
        [SerializeField] private float tensionStartScale = 2.0f;
        [SerializeField] private float tensionEndScale = 1.0f;
        [SerializeField] private float parryActiveDuration = 0.2f;

        [Header("2. Feedback de Impacto (Onda Expansiva)")]
        [Tooltip("Tag registrado en el ObjectPooler para el prefab de la onda expansiva")]
        [SerializeField] private string shockwavePoolTag = "ShockwaveVFX";

        [Header("3. Feedback de Cooldown (Vulnerabilidad)")]
        [Tooltip("Opacidad alfa aplicada durante el estado de Cooldown/Recuperación")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float cooldownAlpha = 0.5f;
        [Tooltip("Factor de atenuación de color durante el Cooldown (tinte grisáceo)")]
        [Range(0.2f, 1f)]
        [SerializeField] private float cooldownBrightness = 0.65f;

        private Color _originalPlayerColor = Color.white;
        private Coroutine _tensionRingCoroutine;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (playerSpriteRenderer == null) playerSpriteRenderer = GetComponent<SpriteRenderer>();

            if (playerSpriteRenderer != null)
            {
                _originalPlayerColor = playerSpriteRenderer.color;
            }

            if (tensionRingObject != null)
            {
                tensionRingObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void Start()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            ResetVisualState();
        }

        private void SubscribeEvents()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (player == null) return;

            player.OnParryStateChanged -= HandleParryStateChanged;
            player.OnParryStateChanged += HandleParryStateChanged;

            player.OnParrySuccess -= HandleParrySuccess;
            player.OnParrySuccess += HandleParrySuccess;
        }

        private void UnsubscribeEvents()
        {
            if (player != null)
            {
                player.OnParryStateChanged -= HandleParryStateChanged;
                player.OnParrySuccess -= HandleParrySuccess;
            }
        }

        /// <summary>
        /// Reacciona a los cambios de estado del Parry (Ready, Active, Cooldown).
        /// </summary>
        private void HandleParryStateChanged(ParryState state)
        {
            switch (state)
            {
                case ParryState.Active:
                    // 1. Activar animación de contracción del anillo de tensión
                    StartTensionRing();
                    RestorePlayerOpacity();
                    break;

                case ParryState.Cooldown:
                    // Si el parry falló, ocultar anillo y aplicar feedback de vulnerabilidad
                    StopTensionRing();
                    ApplyCooldownVulnerability();
                    break;

                case ParryState.Ready:
                    // Estado neutral: ocultar anillo y restaurar opacidad completa
                    StopTensionRing();
                    RestorePlayerOpacity();
                    break;
            }
        }

        /// <summary>
        /// Invocado cuando un Parry conecta exitosamente contra un enemigo.
        /// Instancia la onda expansiva desde el Object Pool en la posición del impacto.
        /// </summary>
        private void HandleParrySuccess()
        {
            // Ocultar el anillo de tensión inmediatamente
            StopTensionRing();

            // Spawnear onda expansiva desde el pool
            if (ObjectPooler.Instance != null && !string.IsNullOrEmpty(shockwavePoolTag))
            {
                ObjectPooler.Instance.SpawnFromPool(shockwavePoolTag, transform.position, Quaternion.identity);
            }
        }

        #region Feedback 1: Anillo de Tensión
        private void StartTensionRing()
        {
            if (tensionRingObject == null) return;

            if (_tensionRingCoroutine != null) StopCoroutine(_tensionRingCoroutine);
            _tensionRingCoroutine = StartCoroutine(TensionRingRoutine());
        }

        private void StopTensionRing()
        {
            if (_tensionRingCoroutine != null)
            {
                StopCoroutine(_tensionRingCoroutine);
                _tensionRingCoroutine = null;
            }

            if (tensionRingObject != null)
            {
                tensionRingObject.SetActive(false);
            }
        }

        private IEnumerator TensionRingRoutine()
        {
            tensionRingObject.SetActive(true);
            tensionRingObject.transform.localScale = Vector3.one * tensionStartScale;

            float elapsed = 0f;
            while (elapsed < parryActiveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / parryActiveDuration);

                // Contracción lineal o ligeramente suavizada hacia la escala final (1.0)
                float currentScale = Mathf.Lerp(tensionStartScale, tensionEndScale, t);
                tensionRingObject.transform.localScale = Vector3.one * currentScale;

                yield return null;
            }

            tensionRingObject.SetActive(false);
            _tensionRingCoroutine = null;
        }
        #endregion

        #region Feedback 3: Cooldown y Vulnerabilidad
        private void ApplyCooldownVulnerability()
        {
            if (playerSpriteRenderer == null) return;

            // Opacidad al 50% con tinte grisáceo para comunicar debilidad
            Color greyedColor = new Color(
                _originalPlayerColor.r * cooldownBrightness,
                _originalPlayerColor.g * cooldownBrightness,
                _originalPlayerColor.b * cooldownBrightness,
                cooldownAlpha
            );

            playerSpriteRenderer.color = greyedColor;
        }

        private void RestorePlayerOpacity()
        {
            if (playerSpriteRenderer == null) return;
            playerSpriteRenderer.color = _originalPlayerColor;
        }

        private void ResetVisualState()
        {
            StopTensionRing();
            RestorePlayerOpacity();
        }
        #endregion
    }
}
