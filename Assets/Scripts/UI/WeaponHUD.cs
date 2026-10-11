using ArenaSurvivor.Combat;
using ArenaSurvivor.Upgrades;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// HUD de armas (esquina inferior derecha): 3 ranuras normales (H J K / R2 L2 L1) y 1 especial (L / Triángulo)
    /// con nombre, nivel, munición y cooldown de recarga, más el estado del Energy Shield. Se construye proceduralmente.
    /// La etiqueta del botón cambia a la del mando si hay uno conectado.
    /// </summary>
    public class WeaponHUD : MonoBehaviour
    {
        public static WeaponHUD Instance { get; private set; }

        private class SlotView
        {
            public Image Frame;
            public RectTransform CooldownOverlay;
            public Text KeyText;
            public Text NameText;
            public Text TimerText;
        }

        private readonly SlotView[] _slots = new SlotView[PlayerWeapons.SlotCount];
        private GameObject _shieldRow;
        private Text _shieldText;
        private Image _shieldBar;
        private RectTransform _shieldFill;

        public static WeaponHUD EnsureExists()
        {
            if (Instance != null) return Instance;

            Canvas canvas = UIBuild.FindHudCanvas();
            if (canvas == null) return null;

            RectTransform root = UIBuild.CreateRect("WeaponHUD", canvas.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 20f), new Vector2(4 * 78f, 112f));
            Instance = root.gameObject.AddComponent<WeaponHUD>();
            Instance.Build(root);
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build(RectTransform root)
        {
            var cg = root.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            for (int i = 0; i < PlayerWeapons.SlotCount; i++)
            {
                RectTransform slot = UIBuild.CreateRect($"Slot_{PlayerWeapons.SlotKeyLabels[i]}", root,
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(i * 78f, 0f), new Vector2(72f, 72f));

                var view = new SlotView();
                view.Frame = UIBuild.AddImage(slot.gameObject, new Color(0.05f, 0.06f, 0.1f, 0.85f));

                // Overlay de cooldown: crece desde abajo y se vacía al recargar
                view.CooldownOverlay = UIBuild.CreateRect("Cooldown", slot, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
                UIBuild.AddImage(view.CooldownOverlay.gameObject, new Color(0f, 0f, 0f, 0.65f));

                RectTransform keyRect = UIBuild.CreateRect("Key", slot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(5f, -3f), new Vector2(24f, 24f));
                view.KeyText = UIBuild.AddText(keyRect.gameObject, PlayerWeapons.GetSlotLabel(i), 20, Color.white, TextAnchor.UpperLeft);
                view.KeyText.horizontalOverflow = HorizontalWrapMode.Overflow;

                RectTransform nameRect = UIBuild.CreateRect("Name", slot, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(-4f, 30f));
                view.NameText = UIBuild.AddText(nameRect.gameObject, "", 11, Color.gray, TextAnchor.LowerCenter);
                view.NameText.horizontalOverflow = HorizontalWrapMode.Wrap;

                RectTransform timerRect = UIBuild.CreateRect("Timer", slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(70f, 26f));
                view.TimerText = UIBuild.AddText(timerRect.gameObject, "", 18, Color.white, TextAnchor.MiddleCenter);

                _slots[i] = view;
            }

            // Fila del Energy Shield sobre las ranuras
            RectTransform shieldRow = UIBuild.CreateRect("ShieldRow", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 82f), new Vector2(-6f, 22f));
            _shieldRow = shieldRow.gameObject;
            _shieldBar = UIBuild.AddImage(_shieldRow, new Color(0.05f, 0.06f, 0.1f, 0.85f));
            _shieldFill = UIBuild.CreateRect("Fill", shieldRow, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            UIBuild.AddImage(_shieldFill.gameObject, new Color(0.2f, 0.75f, 1f, 0.6f));
            RectTransform shieldTextRect = UIBuild.Stretch("Text", shieldRow);
            _shieldText = UIBuild.AddText(shieldTextRect.gameObject, "", 12, Color.white, TextAnchor.MiddleCenter);
            _shieldRow.SetActive(false);
        }

        private void Update()
        {
            PlayerWeapons weapons = PlayerWeapons.Instance;
            if (weapons == null) return;

            for (int i = 0; i < _slots.Length; i++)
            {
                SlotView view = _slots[i];
                if (view == null) continue;

                string label = PlayerWeapons.GetSlotLabel(i);
                if (view.KeyText.text != label) view.KeyText.text = label;

                string id = weapons.GetSlotWeaponId(i);
                if (id == null)
                {
                    view.Frame.color = new Color(0.05f, 0.06f, 0.1f, 0.45f);
                    view.KeyText.color = new Color(1f, 1f, 1f, 0.3f);
                    view.NameText.text = PlayerWeapons.IsSpecialSlot(i) ? "ESPECIAL" : "VACÍO";
                    view.NameText.color = new Color(1f, 1f, 1f, 0.25f);
                    view.TimerText.text = "";
                    SetOverlay(view, 0f);
                    continue;
                }

                Color theme = Color.white;
                string displayName = id;
                int level = 0;
                if (UpgradeManager.Instance != null && UpgradeManager.Instance.TryGetUpgradeData(id, out UpgradeData data))
                {
                    theme = data.themeColor;
                    displayName = data.upgradeName;
                    level = UpgradeManager.Instance.GetLevel(id);
                }

                float remaining = weapons.GetCooldownRemaining(id);
                float duration = Mathf.Max(0.01f, weapons.GetCooldownDuration(id));
                bool ready = remaining <= 0f;

                view.Frame.color = ready
                    ? new Color(theme.r * 0.35f, theme.g * 0.35f, theme.b * 0.35f, 0.92f)
                    : new Color(0.05f, 0.06f, 0.1f, 0.85f);
                view.KeyText.color = ready ? theme : new Color(1f, 1f, 1f, 0.5f);
                view.NameText.text = $"{displayName.ToUpper()} {level}";
                view.NameText.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                // Listo: munición restante (ej. 2/3). Recargando: segundos que faltan
                view.TimerText.text = ready
                    ? $"{weapons.GetAmmo(id)}/{weapons.GetMaxAmmo(id)}"
                    : Mathf.CeilToInt(remaining).ToString();
                SetOverlay(view, remaining / duration);
            }

            bool hasShield = weapons.HasEnergyShield;
            if (_shieldRow.activeSelf != hasShield) _shieldRow.SetActive(hasShield);
            if (hasShield)
            {
                float duration = Mathf.Max(0.01f, weapons.ShieldRechargeDuration);
                float fill = weapons.IsShieldReady ? 1f : 1f - (weapons.ShieldRechargeRemaining / duration);
                _shieldFill.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
                _shieldText.text = weapons.IsShieldReady
                    ? "ENERGY SHIELD: LISTO"
                    : $"ENERGY SHIELD: {Mathf.CeilToInt(weapons.ShieldRechargeRemaining)}s";
            }
        }

        private static void SetOverlay(SlotView view, float normalized)
        {
            view.CooldownOverlay.anchorMax = new Vector2(1f, Mathf.Clamp01(normalized));
        }
    }
}
