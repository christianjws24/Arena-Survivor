using ArenaSurvivor.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// Controlador de UI para la barra de energía del jugador y estado de la extracción.
    /// Actualiza el relleno, colores dinámicos según el estado y textos informativos.
    /// </summary>
    public class EnergyBarUI : MonoBehaviour
    {
        [Header("Referencias del Jugador")]
        [SerializeField] private PlayerEnergy playerEnergy;

        [Header("Elementos de UI")]
        [Tooltip("Slider o barra de relleno")]
        [SerializeField] private Slider energySlider;
        [SerializeField] private Image fillImage;
        [SerializeField] private Text statusText;
        [SerializeField] private Text energyValueText;

        [Header("Paleta de Colores de Estado")]
        [SerializeField] private Color chargingColor = new Color(0.15f, 0.75f, 1f, 1f);       // Cian brillante
        [SerializeField] private Color readyColor = new Color(0.25f, 1f, 0.45f, 1f);        // Verde esmeralda / listo
        [SerializeField] private Color zoneActiveColor = new Color(1f, 0.75f, 0.2f, 1f);     // Ámbar / Zona desplegada

        private void Awake()
        {
            if (playerEnergy == null)
            {
#if UNITY_2023_1_OR_NEWER
                playerEnergy = Object.FindFirstObjectByType<PlayerEnergy>();
#else
                playerEnergy = Object.FindObjectOfType<PlayerEnergy>();
#endif
            }
            ConfigureSlider();
        }

        private void ConfigureSlider()
        {
            if (energySlider != null)
            {
                energySlider.interactable = false;
                energySlider.transition = Selectable.Transition.None;
                var nav = energySlider.navigation;
                nav.mode = Navigation.Mode.None;
                energySlider.navigation = nav;
            }
        }

        private void OnEnable()
        {
            if (playerEnergy == null)
            {
#if UNITY_2023_1_OR_NEWER
                playerEnergy = Object.FindFirstObjectByType<PlayerEnergy>();
#else
                playerEnergy = Object.FindObjectOfType<PlayerEnergy>();
#endif
            }

            if (playerEnergy != null)
            {
                playerEnergy.OnEnergyChanged += UpdateBar;
                playerEnergy.OnExtractionReady += HandleExtractionReady;
                playerEnergy.OnExtractionStarted += HandleExtractionStarted;
                playerEnergy.OnExtractionEnded += HandleExtractionEnded;

                UpdateBar(playerEnergy.CurrentEnergy, playerEnergy.MaxEnergy);
            }
        }

        private void OnDisable()
        {
            if (playerEnergy != null)
            {
                playerEnergy.OnEnergyChanged -= UpdateBar;
                playerEnergy.OnExtractionReady -= HandleExtractionReady;
                playerEnergy.OnExtractionStarted -= HandleExtractionStarted;
                playerEnergy.OnExtractionEnded -= HandleExtractionEnded;
            }
        }

        private void Update()
        {
            // Efecto de pulso visual cuando la extracción está lista para presionar 'E'
            if (playerEnergy != null && playerEnergy.IsEnergyFull && !playerEnergy.IsZoneActive)
            {
                if (fillImage != null)
                {
                    float pingPong = Mathf.PingPong(Time.unscaledTime * 3f, 0.25f);
                    fillImage.color = new Color(readyColor.r, readyColor.g, readyColor.b, 0.75f + pingPong);
                }
            }
        }

        private void UpdateBar(float current, float max)
        {
            float fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;

            if (energySlider != null)
            {
                energySlider.value = fillAmount;
            }

            if (energyValueText != null)
            {
                energyValueText.text = $"{Mathf.FloorToInt(current)} / {Mathf.FloorToInt(max)}";
            }

            // Estados de color y texto
            if (playerEnergy != null && playerEnergy.IsZoneActive)
            {
                SetVisualState(zoneActiveColor, "⚠ ZONA ACTIVA: PERMANECE DENTRO (5s)");
            }
            else if (current >= max && max > 0f)
            {
                SetVisualState(readyColor, "⭐ ¡EXTRACCIÓN DESPLEGADA!");
            }
            else
            {
                int percent = Mathf.FloorToInt(fillAmount * 100f);
                SetVisualState(chargingColor, $"ENERGÍA: {percent}% (Elimina cuadrados o haz Parry)");
            }
        }

        private void SetVisualState(Color color, string message)
        {
            if (fillImage != null)
            {
                fillImage.color = color;
            }

            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = color;
            }
        }

        private void HandleExtractionReady()
        {
            UpdateBar(playerEnergy.CurrentEnergy, playerEnergy.MaxEnergy);
        }

        private void HandleExtractionStarted()
        {
            SetVisualState(zoneActiveColor, "⏳ EXTRACCIÓN EN CURSO (Aguanta dentro)");
        }

        private void HandleExtractionEnded()
        {
            if (playerEnergy != null)
            {
                UpdateBar(playerEnergy.CurrentEnergy, playerEnergy.MaxEnergy);
            }
        }
    }
}
