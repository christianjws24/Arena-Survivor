using System.Collections;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Entities;
using UnityEngine;

namespace ArenaSurvivor.Managers
{
    /// <summary>
    /// Gestor de Audio centralizado y desacoplado para SFX, música y filtros de procesamiento.
    /// Diseñado para cero dependencias rígidas: escucha eventos Action de C# o llamadas públicas directas.
    /// Incluye síntesis procedural de sonido (Fallback) para funcionar inmediatamente sin requerir archivos de audio externos.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Fuentes de Audio")]
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioLowPassFilter musicLowPassFilter;

        [Header("Clips de Sonido (Opcional - Si están vacíos, se sintetizan proceduralmente)")]
        [SerializeField] private AudioClip uiHoverClip;
        [SerializeField] private AudioClip uiClickClip;
        [SerializeField] private AudioClip parryImpactClip;
        [SerializeField] private AudioClip playerDeathClip;

        [Header("Configuración del Filtro Low-Pass (Zona de Extracción)")]
        [Tooltip("Frecuencia de corte normal (sonido claro sin ahogo)")]
        [SerializeField] private float normalCutoff = 22000f;
        [Tooltip("Frecuencia de corte ahogada/subacuática (cuando estás dentro de la Zona)")]
        [SerializeField] private float muffledCutoff = 750f;
        [Tooltip("Velocidad de transición del filtro en segundos")]
        [SerializeField] private float filterTransitionDuration = 0.25f;

        private Coroutine _filterCoroutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            EnsureAudioSources();
            GenerateProceduralFallbacks();
        }

        private void Start()
        {
            // Suscripción desacoplada a eventos del juego
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnParrySuccess += OnParrySuccessHandler;
            }
        }

        private void OnEnable()
        {
            // Eventos estáticos o directos
            ExtractionZone.OnZoneClosed += HandleZoneClosed;
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnParrySuccess += OnParrySuccessHandler;
            }
        }

        private void OnDisable()
        {
            ExtractionZone.OnZoneClosed -= HandleZoneClosed;
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnParrySuccess -= OnParrySuccessHandler;
            }
        }

        private void OnParrySuccessHandler()
        {
            PlayParryImpact();
        }

        private void HandleZoneClosed()
        {
            SetExtractionMuffle(false);
        }

        private void EnsureAudioSources()
        {
            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
                sfxSource.playOnAwake = false;
                sfxSource.spatialBlend = 0f; // 2D UI/SFX
            }

            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.loop = true;
                musicSource.playOnAwake = false;
                musicSource.spatialBlend = 0f;
                musicSource.volume = 0.5f;
            }

            if (musicLowPassFilter == null)
            {
                musicLowPassFilter = musicSource.GetComponent<AudioLowPassFilter>();
                if (musicLowPassFilter == null)
                {
                    musicLowPassFilter = musicSource.gameObject.AddComponent<AudioLowPassFilter>();
                }
            }

            musicLowPassFilter.cutoffFrequency = normalCutoff;
            musicLowPassFilter.enabled = false;
        }

        #region Reproducción de SFX
        /// <summary>
        /// Reproduce el sonido de hover sobre botones o tarjetas de mejoras.
        /// </summary>
        public void PlayUIHover()
        {
            if (sfxSource != null && uiHoverClip != null)
            {
                sfxSource.PlayOneShot(uiHoverClip, 0.4f);
            }
        }

        /// <summary>
        /// Reproduce el sonido de clic de selección en la interfaz.
        /// </summary>
        public void PlayUIClick()
        {
            if (sfxSource != null && uiClickClip != null)
            {
                sfxSource.PlayOneShot(uiClickClip, 0.75f);
            }
        }

        /// <summary>
        /// Reproduce el impacto pesado y contundente al conectar un Parry exitoso.
        /// </summary>
        public void PlayParryImpact()
        {
            if (sfxSource != null && parryImpactClip != null)
            {
                sfxSource.PlayOneShot(parryImpactClip, 1.0f);
            }
        }

        /// <summary>
        /// Reproduce el sonido de explosión y derrota del jugador.
        /// </summary>
        public void PlayPlayerDeath()
        {
            if (sfxSource != null && playerDeathClip != null)
            {
                sfxSource.PlayOneShot(playerDeathClip, 1.0f);
            }
        }
        #endregion

        #region Filtro Low-Pass (Zona de Extracción)
        /// <summary>
        /// Conmuta suavemente el ahogo de sonido (filtro paso bajo) mientras el jugador está en la zona de extracción.
        /// </summary>
        public void SetExtractionMuffle(bool isMuffled)
        {
            if (musicLowPassFilter == null) return;

            musicLowPassFilter.enabled = true;
            float targetFreq = isMuffled ? muffledCutoff : normalCutoff;

            if (_filterCoroutine != null) StopCoroutine(_filterCoroutine);
            _filterCoroutine = StartCoroutine(TransitionFilterRoutine(targetFreq));
        }

        private IEnumerator TransitionFilterRoutine(float targetFrequency)
        {
            float startFreq = musicLowPassFilter.cutoffFrequency;
            float elapsed = 0f;

            while (elapsed < filterTransitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / filterTransitionDuration);
                // Interpolación exponencial más natural para frecuencias acústicas
                musicLowPassFilter.cutoffFrequency = Mathf.Lerp(startFreq, targetFrequency, t);
                yield return null;
            }

            musicLowPassFilter.cutoffFrequency = targetFrequency;

            // Desactivar el componente si volvió al estado normal para ahorrar rendimiento de DSP
            if (Mathf.Approximately(targetFrequency, normalCutoff))
            {
                musicLowPassFilter.enabled = false;
            }

            _filterCoroutine = null;
        }
        #endregion

        #region Síntesis Procedural de Audio (Zero-Dependencies)
        private void GenerateProceduralFallbacks()
        {
            if (uiHoverClip == null) uiHoverClip = CreateToneClip("SFX_Hover", 0.05f, 600f, 900f, 0.25f);
            if (uiClickClip == null) uiClickClip = CreateToneClip("SFX_Click", 0.09f, 900f, 400f, 0.4f);
            if (parryImpactClip == null) parryImpactClip = CreateHeavyImpactClip("SFX_ParryImpact", 0.35f);
            if (playerDeathClip == null) playerDeathClip = CreateNoiseExplosionClip("SFX_PlayerDeath", 0.6f);
        }

        private AudioClip CreateToneClip(string clipName, float duration, float startFreq, float endFreq, float gain)
        {
            int sampleRate = 44100;
            int samples = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float freq = Mathf.Lerp(startFreq, endFreq, t);
                float envelope = Mathf.Sin(t * Mathf.PI); // Ventana suave
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * ((float)i / sampleRate)) * envelope * gain;
            }

            AudioClip clip = AudioClip.Create(clipName, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip CreateHeavyImpactClip(string clipName, float duration)
        {
            int sampleRate = 44100;
            int samples = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                // Onda sinusoidal con pitch drop violento (de 220Hz a 45Hz) simulando impacto cinematográfico
                float freq = Mathf.Lerp(240f, 45f, Mathf.Pow(t, 0.35f));
                float noise = (Random.value * 2f - 1f) * Mathf.Exp(-t * 18f) * 0.45f;
                float subBass = Mathf.Sin(2f * Mathf.PI * freq * ((float)i / sampleRate)) * Mathf.Exp(-t * 6f);
                data[i] = Mathf.Clamp((subBass + noise) * 0.85f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(clipName, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip CreateNoiseExplosionClip(string clipName, float duration)
        {
            int sampleRate = 44100;
            int samples = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float noise = (Random.value * 2f - 1f);
                float envelope = Mathf.Exp(-t * 4.5f);
                data[i] = noise * envelope * 0.7f;
            }

            AudioClip clip = AudioClip.Create(clipName, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
        #endregion
    }
}
