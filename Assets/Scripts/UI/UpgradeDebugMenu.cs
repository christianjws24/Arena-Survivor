using System.Collections.Generic;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Managers;
using ArenaSurvivor.Upgrades;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// Menú de testeo de power-ups (solo teclado). [F1] lo abre/cierra y pausa el juego.
    /// Lista todo el catálogo de mejoras (armas incluidas) y permite fijar cualquiera en el nivel deseado:
    ///   [↑/↓] o [W/S] elegir · [←/→] o [A/D] bajar/subir nivel · [0-9] nivel directo · [F1] cerrar.
    /// Se construye proceduralmente sobre el canvas del HUD.
    /// </summary>
    public class UpgradeDebugMenu : MonoBehaviour
    {
        public static UpgradeDebugMenu Instance { get; private set; }

        private const int VisibleRows = 22;

        private readonly List<UpgradeData> _entries = new List<UpgradeData>();
        private int _selected;
        private bool _isOpen;
        private int _closedFrame = -1;

        private GameObject _panel;
        private Text _listText;
        private Text _detailText;

        /// <summary>
        /// True mientras el menú está abierto (o se cerró este mismo frame): GameManager no debe abrir la pausa.
        /// </summary>
        public static bool BlocksPauseInput => Instance != null && (Instance._isOpen || Instance._closedFrame == Time.frameCount);

        public static bool IsOpen => Instance != null && Instance._isOpen;

        public static UpgradeDebugMenu EnsureExists()
        {
            if (Instance != null) return Instance;
            GameObject go = new GameObject("UpgradeDebugMenu");
            return go.AddComponent<UpgradeDebugMenu>();
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

        private void Update()
        {
            if (WasPressed(Key.Toggle))
            {
                if (_isOpen) Close();
                else TryOpen();
                return;
            }

            if (!_isOpen) return;

            if (WasPressed(Key.Up)) MoveSelection(-1);
            else if (WasPressed(Key.Down)) MoveSelection(1);
            else if (WasPressed(Key.Left)) ChangeLevel(-1);
            else if (WasPressed(Key.Right)) ChangeLevel(1);
            else
            {
                int digit = GetPressedDigit();
                if (digit >= 0) SetSelectedLevel(digit);
            }
        }

        #region Abrir / cerrar
        private void TryOpen()
        {
            if (UpgradeManager.Instance == null) return;
            if (PlayerController.Instance != null && PlayerController.Instance.IsDead) return;
            if (GameManager.Instance != null && (GameManager.Instance.IsPaused || GameManager.Instance.IsGameOver)) return;
            if (LevelUpUIManager.Instance != null && LevelUpUIManager.Instance.IsModalOpen) return;

            if (_panel == null && !BuildPanel()) return;

            RefreshEntries();
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _entries.Count - 1));

            Combat.HitStopManager.Instance?.CancelHitStop();
            Time.timeScale = 0f;

            _isOpen = true;
            _panel.transform.SetAsLastSibling();
            _panel.SetActive(true);
            Render();
        }

        private void Close()
        {
            _isOpen = false;
            _closedFrame = Time.frameCount;
            if (_panel != null) _panel.SetActive(false);
            Time.timeScale = 1f;
        }
        #endregion

        #region Lógica
        private void RefreshEntries()
        {
            _entries.Clear();
            foreach (UpgradeData data in UpgradeManager.Instance.AllUpgrades)
            {
                if (data != null) _entries.Add(data);
            }

            // Agrupadas por tipo (stats, comportamientos, zona, armas) y luego por nombre
            _entries.Sort((a, b) =>
            {
                int byType = a.upgradeType.CompareTo(b.upgradeType);
                return byType != 0 ? byType : string.Compare(a.upgradeName, b.upgradeName, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        private void MoveSelection(int delta)
        {
            if (_entries.Count == 0) return;
            _selected = (_selected + delta + _entries.Count) % _entries.Count;
            Render();
        }

        private void ChangeLevel(int delta)
        {
            UpgradeData data = SelectedEntry;
            if (data == null) return;
            SetSelectedLevel(UpgradeManager.Instance.GetLevel(data.id) + delta);
        }

        private void SetSelectedLevel(int level)
        {
            UpgradeData data = SelectedEntry;
            if (data == null || UpgradeManager.Instance == null) return;

            UpgradeManager.Instance.SetUpgradeLevel(data.id, level);
            AudioManager.Instance?.PlayUIClick();
            Render();
        }

        private UpgradeData SelectedEntry => _selected >= 0 && _selected < _entries.Count ? _entries[_selected] : null;
        #endregion

        #region Vista
        private bool BuildPanel()
        {
            Canvas canvas = UIBuild.FindHudCanvas();
            if (canvas == null) return false;

            RectTransform root = UIBuild.CreateRect("UpgradeDebugMenu", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 780f));
            _panel = root.gameObject;
            UIBuild.AddImage(_panel, new Color(0.03f, 0.04f, 0.08f, 0.96f), true);

            RectTransform title = UIBuild.CreateRect("Title", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(-40f, 60f));
            UIBuild.AddText(title.gameObject,
                "MENÚ DE TESTEO · POWER-UPS\n<size=15><color=#A0D8EF>[↑/↓] elegir   [←/→] bajar/subir nivel   [0-9] nivel directo   [F1] cerrar</color></size>",
                26, new Color(1f, 0.85f, 0.3f), TextAnchor.UpperCenter);

            RectTransform list = UIBuild.CreateRect("List", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(30f, -95f), new Vector2(-60f, 540f));
            _listText = UIBuild.AddText(list.gameObject, "", 18, Color.white, TextAnchor.UpperLeft, FontStyle.Normal);
            _listText.lineSpacing = 1.05f;

            RectTransform detail = UIBuild.CreateRect("Detail", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(30f, 20f), new Vector2(-60f, 110f));
            UIBuild.AddImage(detail.gameObject, new Color(0.08f, 0.1f, 0.16f, 0.9f));
            RectTransform detailTextRect = UIBuild.CreateRect("Text", detail, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24f, -16f));
            _detailText = UIBuild.AddText(detailTextRect.gameObject, "", 16, new Color(0.85f, 0.9f, 1f), TextAnchor.UpperLeft, FontStyle.Normal);
            _detailText.horizontalOverflow = HorizontalWrapMode.Wrap;

            _panel.SetActive(false);
            return true;
        }

        private void Render()
        {
            if (_listText == null || UpgradeManager.Instance == null) return;

            // Ventana de filas visibles que sigue a la selección
            int first = Mathf.Clamp(_selected - (VisibleRows / 2), 0, Mathf.Max(0, _entries.Count - VisibleRows));
            int last = Mathf.Min(_entries.Count, first + VisibleRows);

            var sb = new System.Text.StringBuilder(2048);
            if (first > 0) sb.Append("<color=#666666>   ▲ ...</color>\n");

            for (int i = first; i < last; i++)
            {
                UpgradeData data = _entries[i];
                int level = UpgradeManager.Instance.GetLevel(data.id);
                bool selected = i == _selected;

                string nameColor = selected ? "#FFE066" : (level > 0 ? "#FFFFFF" : "#8A93A6");
                string cursor = selected ? "▶ " : "   ";
                string pips = BuildPips(level, data.maxLevel);
                string tag = GetTag(data);

                sb.Append($"<color={nameColor}>{cursor}{data.upgradeName}</color>  <color=#5FD0FF>{pips}</color> <color=#AAAAAA>{level}/{data.maxLevel}</color>  <color=#6C7A90>{tag}</color>\n");
            }

            if (last < _entries.Count) sb.Append("<color=#666666>   ▼ ...</color>\n");
            _listText.text = sb.ToString();

            UpgradeData current = SelectedEntry;
            if (_detailText != null && current != null)
            {
                _detailText.text = $"<b><color=#FFE066>{current.upgradeName}</color></b> <color=#6C7A90>(id: {current.id})</color>\n{current.description}";
            }
        }

        private static string BuildPips(int level, int maxLevel)
        {
            var sb = new System.Text.StringBuilder(maxLevel);
            for (int i = 0; i < maxLevel; i++) sb.Append(i < level ? '■' : '□');
            return sb.ToString();
        }

        private static string GetTag(UpgradeData data)
        {
            string tag = data.upgradeType == UpgradeType.Weapon
                ? (data.isPassiveWeapon ? "[ARMA PASIVA]" : "[ARMA HJKL]")
                : $"[{data.upgradeType}]";
            if (data.rarity == UpgradeRarity.Rare) tag += " [RARO]";
            if (data.HasExclusiveTag) tag += $" [excluye: {data.exclusiveTag}]";
            if (data.upgradeType == UpgradeType.ZoneModifier) tag += " (fuera del pool)";
            return tag;
        }
        #endregion

        #region Entrada (teclado)
        private enum Key { Toggle, Up, Down, Left, Right }

        private static bool WasPressed(Key key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                switch (key)
                {
                    case Key.Toggle: if (kb.f1Key.wasPressedThisFrame) return true; break;
                    case Key.Up: if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) return true; break;
                    case Key.Down: if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) return true; break;
                    case Key.Left: if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) return true; break;
                    case Key.Right: if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) return true; break;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            switch (key)
            {
                case Key.Toggle: return Input.GetKeyDown(KeyCode.F1);
                case Key.Up: return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
                case Key.Down: return Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
                case Key.Left: return Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
                case Key.Right: return Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
            }
#endif
            return false;
        }

        private static int GetPressedDigit()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                UnityEngine.InputSystem.Controls.KeyControl[] digits =
                {
                    kb.digit0Key, kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key,
                    kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key
                };
                UnityEngine.InputSystem.Controls.KeyControl[] numpad =
                {
                    kb.numpad0Key, kb.numpad1Key, kb.numpad2Key, kb.numpad3Key, kb.numpad4Key,
                    kb.numpad5Key, kb.numpad6Key, kb.numpad7Key, kb.numpad8Key, kb.numpad9Key
                };
                for (int i = 0; i < 10; i++)
                {
                    if (digits[i].wasPressedThisFrame || numpad[i].wasPressedThisFrame) return i;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < 10; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i)) return i;
            }
#endif
            return -1;
        }
        #endregion
    }
}
