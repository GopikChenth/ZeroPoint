// Copyright 2021, Infima Games. All Rights Reserved.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace InfimaGames.LowPolyShooterPack.Interface
{
    /// <summary>
    /// In-game Weapon Selection and Loadout Menu.
    /// Allows the player to toggle an overlay with the 'P' key to select Primary and Secondary weapons.
    /// </summary>
    public class WeaponSelectionMenu : MonoBehaviour
    {
        #region FIELDS

        /// <summary>
        /// Player Character reference.
        /// </summary>
        private CharacterBehaviour playerCharacter;

        /// <summary>
        /// Player Inventory reference.
        /// </summary>
        private InventoryBehaviour playerInventory;

        /// <summary>
        /// Root GameObject of the loadout menu overlay.
        /// </summary>
        private GameObject menuRoot;

        /// <summary>
        /// Whether the menu is currently visible/open.
        /// </summary>
        private bool isMenuOpen;

        /// <summary>
        /// Last time the menu was toggled, used for debounce.
        /// </summary>
        private float lastToggleTime = -1f;

        /// <summary>
        /// Minimum cooldown between toggles to prevent double-firing.
        /// </summary>
        private const float ToggleCooldown = 0.25f;

        /// <summary>
        /// Pending equip slot for instant UI feedback before animation completes.
        /// </summary>
        private int pendingEquipIndex = -1;

        /// <summary>
        /// Cached UI card elements for refreshing.
        /// </summary>
        private readonly List<WeaponCardUI> weaponCards = new List<WeaponCardUI>();

        /// <summary>
        /// Cached TMP font asset for consistent font styling.
        /// </summary>
        private static TMP_FontAsset cachedFont;

        /// <summary>
        /// Cached procedural 1x1 white sprite for UI fills.
        /// </summary>
        private static Sprite cachedWhiteSprite;

        #endregion

        #region STRUCTS

        private class WeaponCardUI
        {
            public int SlotIndex;
            public Image BorderImage;
            public Image CardBgImage;
            public Button CardButton;
            public TextMeshProUGUI SlotText;
            public TextMeshProUGUI HotkeyBadgeText;
            public TextMeshProUGUI NameText;
            public Image WeaponSilhouette;
            public TextMeshProUGUI StatsText;
            public Button ActionButton;
            public Image ActionButtonBg;
            public TextMeshProUGUI ActionButtonText;
        }

        #endregion

        #region INITIALIZATION

        /// <summary>
        /// Explicit initialization with character reference.
        /// </summary>
        public void Init(CharacterBehaviour character)
        {
            playerCharacter = character;
            if (playerCharacter != null)
                playerInventory = playerCharacter.GetInventory();
        }

        private void Awake()
        {
            EnsureEventSystem();
        }

        private void Start()
        {
            // Resolve character if not yet set
            if (playerCharacter == null)
            {
                var gameModeService = ServiceLocator.Current?.Get<IGameModeService>();
                if (gameModeService != null)
                    playerCharacter = gameModeService.GetPlayerCharacter();

                if (playerCharacter == null)
                    playerCharacter = GetComponentInParent<CharacterBehaviour>();

                if (playerCharacter == null)
                    playerCharacter = FindAnyObjectByType<CharacterBehaviour>();
            }

            if (playerCharacter != null && playerInventory == null)
                playerInventory = playerCharacter.GetInventory();

            // Build menu UI
            BuildMenuUI();

            // Update in-game tutorial prompt if present
            UpdateTutorialPrompt();

            // Initially closed
            SetMenuVisible(false);
        }

        #endregion

        #region UNITY UPDATE

        private void Update()
        {
            // Fallback inventory check
            if (playerInventory == null && playerCharacter != null)
                playerInventory = playerCharacter.GetInventory();

            // Check for 'P' key to toggle menu with debounce
            if (WasKeyPDown() && Time.unscaledTime - lastToggleTime >= ToggleCooldown)
            {
                ToggleMenu();
                return;
            }

            // When menu is open, handle ESC and hotkeys
            if (isMenuOpen)
            {
                if (WasKeyEscapeDown() && Time.unscaledTime - lastToggleTime >= ToggleCooldown)
                {
                    CloseMenu();
                    return;
                }

                // Hotkey 1 for Primary, 2 for Secondary inside menu
                if (WasKeyDigitDown(1))
                {
                    SelectWeapon(0);
                }
                else if (WasKeyDigitDown(2))
                {
                    SelectWeapon(1);
                }

                // Keep card states updated
                UpdateCardEquippedStates();
            }
        }

        #endregion

        #region MENU CONTROL

        /// <summary>
        /// Toggles menu open/closed.
        /// </summary>
        public void ToggleMenu()
        {
            if (isMenuOpen)
                CloseMenu();
            else
                OpenMenu();
        }

        /// <summary>
        /// Opens the loadout menu and unlocks the cursor.
        /// </summary>
        public void OpenMenu()
        {
            if (isMenuOpen)
                return;

            lastToggleTime = Time.unscaledTime;
            isMenuOpen = true;
            pendingEquipIndex = -1;
            SetMenuVisible(true);

            // Ensure menu is rendered in front of all other HUD elements
            if (menuRoot != null)
            {
                menuRoot.transform.SetAsLastSibling();
                var cg = menuRoot.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }

            // Unlock and reveal cursor for UI interaction
            if (playerCharacter != null)
                playerCharacter.SetCursorLocked(false);

            // Refresh cards
            RefreshCards();
        }

        /// <summary>
        /// Closes the loadout menu and locks cursor back to gameplay.
        /// </summary>
        public void CloseMenu()
        {
            if (!isMenuOpen)
                return;

            lastToggleTime = Time.unscaledTime;
            isMenuOpen = false;
            pendingEquipIndex = -1;
            SetMenuVisible(false);

            // Re-lock cursor for gameplay
            if (playerCharacter != null)
                playerCharacter.SetCursorLocked(true);
        }

        /// <summary>
        /// Equips a weapon at the specified slot index.
        /// </summary>
        public void SelectWeapon(int slotIndex)
        {
            if (playerCharacter == null)
                return;

            pendingEquipIndex = slotIndex;
            playerCharacter.EquipWeapon(slotIndex);
            UpdateCardEquippedStates();
        }

        private void SetMenuVisible(bool visible)
        {
            if (menuRoot != null)
                menuRoot.SetActive(visible);
        }

        #endregion

        #region INPUT HELPERS

        private bool WasKeyPDown()
        {
            #if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
                return Keyboard.current.pKey.wasPressedThisFrame;
            #endif

            try
            {
                return Input.GetKeyDown(KeyCode.P);
            }
            catch {}

            return false;
        }

        private bool WasKeyEscapeDown()
        {
            #if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
                return Keyboard.current.escapeKey.wasPressedThisFrame;
            #endif

            try
            {
                return Input.GetKeyDown(KeyCode.Escape);
            }
            catch {}

            return false;
        }

        private bool WasKeyDigitDown(int digit)
        {
            #if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (digit == 1) return Keyboard.current.digit1Key.wasPressedThisFrame;
                if (digit == 2) return Keyboard.current.digit2Key.wasPressedThisFrame;
                if (digit == 3) return Keyboard.current.digit3Key.wasPressedThisFrame;
                return false;
            }
            #endif

            try
            {
                if (digit == 1) return Input.GetKeyDown(KeyCode.Alpha1);
                if (digit == 2) return Input.GetKeyDown(KeyCode.Alpha2);
                if (digit == 3) return Input.GetKeyDown(KeyCode.Alpha3);
            }
            catch {}

            return false;
        }

        #endregion

        #region EVENT SYSTEM

        private void EnsureEventSystem()
        {
            if (EventSystem.current == null && FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem", typeof(EventSystem));
                esObj.layer = 5;
                #if ENABLE_INPUT_SYSTEM
                var uiModule = esObj.AddComponent<InputSystemUIInputModule>();
                if (uiModule != null)
                    uiModule.AssignDefaultActions();
                #endif
                if (esObj.GetComponent<BaseInputModule>() == null)
                {
                    var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                    if (inputModuleType != null)
                    {
                        var mod = esObj.AddComponent(inputModuleType);
                        var assignMethod = inputModuleType.GetMethod("AssignDefaultActions");
                        if (assignMethod != null)
                            assignMethod.Invoke(mod, null);
                    }
                }
            }
        }

        #endregion

        #region UI CONSTRUCTION

        private void BuildMenuUI()
        {
            if (menuRoot != null)
                return;

            Sprite white = GetWhiteSprite();

            // 1. Full-screen dark scrim overlay
            menuRoot = new GameObject("WeaponSelectionMenu_Root", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            menuRoot.layer = 5;
            menuRoot.transform.SetParent(transform, false);

            RectTransform rootRt = menuRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Image rootImage = menuRoot.GetComponent<Image>();
            rootImage.sprite = white;
            rootImage.color = new Color(0.02f, 0.04f, 0.07f, 0.94f);
            rootImage.raycastTarget = true;

            // 2. Central Window Panel (1080 x 760)
            GameObject dialog = new GameObject("DialogPanel", typeof(RectTransform), typeof(Image));
            dialog.layer = 5;
            dialog.transform.SetParent(menuRoot.transform, false);

            RectTransform dialogRt = dialog.GetComponent<RectTransform>();
            dialogRt.anchorMin = new Vector2(0.5f, 0.5f);
            dialogRt.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRt.pivot = new Vector2(0.5f, 0.5f);
            dialogRt.sizeDelta = new Vector2(1080, 760);
            dialogRt.anchoredPosition = Vector2.zero;

            Image dialogImg = dialog.GetComponent<Image>();
            dialogImg.sprite = white;
            dialogImg.color = new Color(0.07f, 0.09f, 0.14f, 0.98f);

            // 3. Header Section (Top of Dialog)
            GameObject header = new GameObject("Header", typeof(RectTransform));
            header.layer = 5;
            header.transform.SetParent(dialog.transform, false);

            RectTransform headerRt = header.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0.5f, 1f);
            headerRt.anchorMax = new Vector2(0.5f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.sizeDelta = new Vector2(1040, 90);
            headerRt.anchoredPosition = new Vector2(0, -10);

            var titleText = CreateText(header.transform, "Title", "TACTICAL LOADOUT", 36, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            titleText.textWrappingMode = TextWrappingModes.NoWrap;
            titleText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleText.rectTransform.sizeDelta = new Vector2(1000, 44);
            titleText.rectTransform.anchoredPosition = new Vector2(0, 0);

            var subText = CreateText(header.transform, "Subtitle", "CLICK A WEAPON CARD OR BUTTON TO EQUIP  •  HOTKEYS [1] / [2]  •  PRESS [P] OR [ESC] TO CLOSE", 15, FontStyles.Normal, TextAlignmentOptions.Center, new Color(0.40f, 0.82f, 0.92f, 1f));
            subText.textWrappingMode = TextWrappingModes.NoWrap;
            subText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            subText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            subText.rectTransform.pivot = new Vector2(0.5f, 1f);
            subText.rectTransform.sizeDelta = new Vector2(1000, 26);
            subText.rectTransform.anchoredPosition = new Vector2(0, -44);

            // Accent Divider Line
            var divider = CreateImage(header.transform, "Divider", new Color(0.18f, 0.75f, 0.90f, 0.85f), white);
            divider.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            divider.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            divider.rectTransform.pivot = new Vector2(0.5f, 0f);
            divider.rectTransform.sizeDelta = new Vector2(1000, 3);
            divider.rectTransform.anchoredPosition = new Vector2(0, 2);

            // 4. Two Fixed-Position Cards Side-by-Side (480 x 540 each)
            weaponCards.Clear();
            weaponCards.Add(CreateWeaponCard(dialog.transform, 0, -255, -12, "PRIMARY WEAPON", "[ KEY 1 ]"));
            weaponCards.Add(CreateWeaponCard(dialog.transform, 1, 255, -12, "SECONDARY WEAPON", "[ KEY 2 ]"));

            // 5. Resume Game Button at Bottom
            GameObject resumeBtnObj = new GameObject("ResumeButton", typeof(RectTransform), typeof(Image), typeof(Button));
            resumeBtnObj.layer = 5;
            resumeBtnObj.transform.SetParent(dialog.transform, false);

            RectTransform resumeRt = resumeBtnObj.GetComponent<RectTransform>();
            resumeRt.anchorMin = new Vector2(0.5f, 0f);
            resumeRt.anchorMax = new Vector2(0.5f, 0f);
            resumeRt.pivot = new Vector2(0.5f, 0f);
            resumeRt.sizeDelta = new Vector2(300, 48);
            resumeRt.anchoredPosition = new Vector2(0, 16);

            Image resumeImg = resumeBtnObj.GetComponent<Image>();
            resumeImg.sprite = white;
            resumeImg.color = new Color(0.20f, 0.26f, 0.35f, 1f);

            Button resumeBtn = resumeBtnObj.GetComponent<Button>();
            ColorBlock cb = resumeBtn.colors;
            cb.normalColor = new Color(0.20f, 0.26f, 0.35f, 1f);
            cb.highlightedColor = new Color(0.28f, 0.38f, 0.50f, 1f);
            cb.pressedColor = new Color(0.14f, 0.18f, 0.25f, 1f);
            cb.selectedColor = cb.highlightedColor;
            resumeBtn.colors = cb;
            resumeBtn.onClick.AddListener(CloseMenu);

            var resumeText = CreateText(resumeBtnObj.transform, "ResumeText", "RESUME GAME  [ESC]", 17, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            resumeText.textWrappingMode = TextWrappingModes.NoWrap;
            resumeText.rectTransform.anchorMin = Vector2.zero;
            resumeText.rectTransform.anchorMax = Vector2.one;
            resumeText.rectTransform.offsetMin = Vector2.zero;
            resumeText.rectTransform.offsetMax = Vector2.zero;
        }

        private WeaponCardUI CreateWeaponCard(Transform parent, int slotIndex, float posX, float posY, string slotTitle, string hotkeyLabel)
        {
            WeaponCardUI card = new WeaponCardUI();
            card.SlotIndex = slotIndex;
            Sprite white = GetWhiteSprite();

            // Outer border panel (480 x 540)
            GameObject borderObj = new GameObject($"Card_{slotIndex}_Border", typeof(RectTransform), typeof(Image));
            borderObj.layer = 5;
            borderObj.transform.SetParent(parent, false);

            RectTransform borderRt = borderObj.GetComponent<RectTransform>();
            borderRt.anchorMin = new Vector2(0.5f, 0.5f);
            borderRt.anchorMax = new Vector2(0.5f, 0.5f);
            borderRt.pivot = new Vector2(0.5f, 0.5f);
            borderRt.sizeDelta = new Vector2(480, 540);
            borderRt.anchoredPosition = new Vector2(posX, posY);

            card.BorderImage = borderObj.GetComponent<Image>();
            card.BorderImage.sprite = white;
            card.BorderImage.color = new Color(0.20f, 0.26f, 0.36f, 0.8f);

            // Inner background & Card Button (474 x 534, 3px inset)
            GameObject bgObj = new GameObject("InnerBg", typeof(RectTransform), typeof(Image), typeof(Button));
            bgObj.layer = 5;
            bgObj.transform.SetParent(borderObj.transform, false);

            RectTransform bgRt = bgObj.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(3, 3);
            bgRt.offsetMax = new Vector2(-3, -3);

            card.CardBgImage = bgObj.GetComponent<Image>();
            card.CardBgImage.sprite = white;
            card.CardBgImage.color = new Color(0.08f, 0.11f, 0.17f, 1f);

            // Clicking anywhere on the card also equips the weapon!
            card.CardButton = bgObj.GetComponent<Button>();
            int capturedSlot = slotIndex;
            card.CardButton.onClick.AddListener(() => SelectWeapon(capturedSlot));

            // Card Header Banner Ribbon (450 x 36)
            GameObject ribbonObj = new GameObject("Ribbon", typeof(RectTransform), typeof(Image));
            ribbonObj.layer = 5;
            ribbonObj.transform.SetParent(bgObj.transform, false);

            RectTransform ribbonRt = ribbonObj.GetComponent<RectTransform>();
            ribbonRt.anchorMin = new Vector2(0.5f, 1f);
            ribbonRt.anchorMax = new Vector2(0.5f, 1f);
            ribbonRt.pivot = new Vector2(0.5f, 1f);
            ribbonRt.sizeDelta = new Vector2(450, 36);
            ribbonRt.anchoredPosition = new Vector2(0, -12);

            Image ribbonImg = ribbonObj.GetComponent<Image>();
            ribbonImg.sprite = white;
            ribbonImg.color = new Color(0.12f, 0.17f, 0.25f, 0.95f);

            card.SlotText = CreateText(ribbonObj.transform, "SlotText", slotTitle, 16, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, Color.white);
            card.SlotText.textWrappingMode = TextWrappingModes.NoWrap;
            card.SlotText.rectTransform.anchorMin = new Vector2(0f, 0f);
            card.SlotText.rectTransform.anchorMax = new Vector2(0.65f, 1f);
            card.SlotText.rectTransform.offsetMin = new Vector2(14, 0);
            card.SlotText.rectTransform.offsetMax = Vector2.zero;

            card.HotkeyBadgeText = CreateText(ribbonObj.transform, "HotkeyBadge", hotkeyLabel, 15, FontStyles.Bold, TextAlignmentOptions.MidlineRight, new Color(0.40f, 0.82f, 0.92f, 1f));
            card.HotkeyBadgeText.textWrappingMode = TextWrappingModes.NoWrap;
            card.HotkeyBadgeText.rectTransform.anchorMin = new Vector2(0.65f, 0f);
            card.HotkeyBadgeText.rectTransform.anchorMax = new Vector2(1f, 1f);
            card.HotkeyBadgeText.rectTransform.offsetMin = Vector2.zero;
            card.HotkeyBadgeText.rectTransform.offsetMax = new Vector2(-14, 0);

            // Weapon Title (450 x 42)
            GameObject titleObj = new GameObject("WeaponTitle", typeof(RectTransform));
            titleObj.layer = 5;
            titleObj.transform.SetParent(bgObj.transform, false);

            RectTransform titleRt = titleObj.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(450, 42);
            titleRt.anchoredPosition = new Vector2(0, -54);

            card.NameText = CreateText(titleObj.transform, "NameText", "WEAPON NAME", 24, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            card.NameText.textWrappingMode = TextWrappingModes.NoWrap;
            card.NameText.rectTransform.anchorMin = Vector2.zero;
            card.NameText.rectTransform.anchorMax = Vector2.one;
            card.NameText.rectTransform.offsetMin = Vector2.zero;
            card.NameText.rectTransform.offsetMax = Vector2.zero;

            // Weapon Silhouette Box (450 x 135)
            GameObject iconBox = new GameObject("IconBox", typeof(RectTransform), typeof(Image));
            iconBox.layer = 5;
            iconBox.transform.SetParent(bgObj.transform, false);

            RectTransform iconBoxRt = iconBox.GetComponent<RectTransform>();
            iconBoxRt.anchorMin = new Vector2(0.5f, 1f);
            iconBoxRt.anchorMax = new Vector2(0.5f, 1f);
            iconBoxRt.pivot = new Vector2(0.5f, 1f);
            iconBoxRt.sizeDelta = new Vector2(450, 135);
            iconBoxRt.anchoredPosition = new Vector2(0, -102);

            Image iconBoxImg = iconBox.GetComponent<Image>();
            iconBoxImg.sprite = white;
            iconBoxImg.color = new Color(0.04f, 0.06f, 0.09f, 0.95f);

            GameObject silObj = new GameObject("Silhouette", typeof(RectTransform), typeof(Image));
            silObj.layer = 5;
            silObj.transform.SetParent(iconBox.transform, false);

            RectTransform silRt = silObj.GetComponent<RectTransform>();
            silRt.anchorMin = new Vector2(0.5f, 0.5f);
            silRt.anchorMax = new Vector2(0.5f, 0.5f);
            silRt.pivot = new Vector2(0.5f, 0.5f);
            silRt.sizeDelta = new Vector2(380, 115);
            silRt.anchoredPosition = Vector2.zero;

            card.WeaponSilhouette = silObj.GetComponent<Image>();
            card.WeaponSilhouette.preserveAspect = true;
            card.WeaponSilhouette.color = Color.white;

            // Stats Block (450 x 105)
            GameObject statsObj = new GameObject("StatsBlock", typeof(RectTransform), typeof(Image));
            statsObj.layer = 5;
            statsObj.transform.SetParent(bgObj.transform, false);

            RectTransform statsRt = statsObj.GetComponent<RectTransform>();
            statsRt.anchorMin = new Vector2(0.5f, 1f);
            statsRt.anchorMax = new Vector2(0.5f, 1f);
            statsRt.pivot = new Vector2(0.5f, 1f);
            statsRt.sizeDelta = new Vector2(450, 105);
            statsRt.anchoredPosition = new Vector2(0, -244);

            Image statsBg = statsObj.GetComponent<Image>();
            statsBg.sprite = white;
            statsBg.color = new Color(0.06f, 0.08f, 0.12f, 0.85f);

            card.StatsText = CreateText(statsObj.transform, "StatsText", "STATS", 16, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, new Color(0.80f, 0.86f, 0.92f, 1f));
            card.StatsText.lineSpacing = 15f;
            card.StatsText.rectTransform.anchorMin = Vector2.zero;
            card.StatsText.rectTransform.anchorMax = Vector2.one;
            card.StatsText.rectTransform.offsetMin = new Vector2(20, 4);
            card.StatsText.rectTransform.offsetMax = new Vector2(-20, -4);

            // Action / Equip Button (450 x 56)
            GameObject actionBtnObj = new GameObject("ActionButton", typeof(RectTransform), typeof(Image), typeof(Button));
            actionBtnObj.layer = 5;
            actionBtnObj.transform.SetParent(bgObj.transform, false);

            RectTransform actionRt = actionBtnObj.GetComponent<RectTransform>();
            actionRt.anchorMin = new Vector2(0.5f, 0f);
            actionRt.anchorMax = new Vector2(0.5f, 0f);
            actionRt.pivot = new Vector2(0.5f, 0f);
            actionRt.sizeDelta = new Vector2(450, 56);
            actionRt.anchoredPosition = new Vector2(0, 16);

            card.ActionButtonBg = actionBtnObj.GetComponent<Image>();
            card.ActionButtonBg.sprite = white;
            card.ActionButton = actionBtnObj.GetComponent<Button>();

            card.ActionButtonText = CreateText(actionBtnObj.transform, "ActionText", "EQUIP WEAPON", 20, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            card.ActionButtonText.textWrappingMode = TextWrappingModes.NoWrap;
            card.ActionButtonText.rectTransform.anchorMin = Vector2.zero;
            card.ActionButtonText.rectTransform.anchorMax = Vector2.one;
            card.ActionButtonText.rectTransform.offsetMin = Vector2.zero;
            card.ActionButtonText.rectTransform.offsetMax = Vector2.zero;

            card.ActionButton.onClick.AddListener(() => SelectWeapon(capturedSlot));

            return card;
        }

        #endregion

        #region REFRESH UI

        /// <summary>
        /// Fully refreshes weapon information and equipped statuses.
        /// </summary>
        public void RefreshCards()
        {
            if (playerInventory == null && playerCharacter != null)
                playerInventory = playerCharacter.GetInventory();

            var weapons = playerInventory != null ? playerInventory.GetAllWeapons() : null;

            for (int i = 0; i < weaponCards.Count; i++)
            {
                var card = weaponCards[i];
                WeaponBehaviour weapon = (weapons != null && i < weapons.Length) ? weapons[i] : null;

                if (weapon != null)
                {
                    // Weapon Name
                    card.NameText.text = GetFormattedWeaponName(weapon, i);

                    // Silhouette
                    Sprite sprite = null;
                    try { sprite = weapon.GetSpriteBody(); } catch {}
                    card.WeaponSilhouette.sprite = sprite;
                    card.WeaponSilhouette.enabled = (sprite != null);

                    // Stats
                    string fireMode = (i == 0) ? "FULL-AUTO" : "SEMI-AUTO";
                    try { fireMode = weapon.IsAutomatic() ? "FULL-AUTO" : "SEMI-AUTO"; } catch {}

                    int capacity = (i == 0) ? 30 : 10;
                    try
                    {
                        int cap = weapon.GetAmmunitionTotal();
                        if (cap > 0) capacity = cap;
                    }
                    catch {}

                    float rpm = (i == 0) ? 600 : 450;
                    try
                    {
                        float r = weapon.GetRateOfFire();
                        if (r > 0) rpm = r;
                    }
                    catch {}

                    card.StatsText.text = $"<b>FIRE MODE:</b>   {fireMode}\n" +
                                          $"<b>MAGAZINE:</b>    {capacity} ROUNDS\n" +
                                          $"<b>FIRE RATE:</b>   {rpm:F0} RPM";
                }
                else
                {
                    card.NameText.text = $"SLOT {i + 1} EMPTY";
                    card.WeaponSilhouette.enabled = false;
                    card.StatsText.text = "No weapon assigned";
                }
            }

            UpdateCardEquippedStates();
        }

        /// <summary>
        /// Updates the visual badges and border colors reflecting the equipped state.
        /// </summary>
        private void UpdateCardEquippedStates()
        {
            int equippedIndex = playerInventory != null ? playerInventory.GetEquippedIndex() : 0;

            // Clear pending index once the weapon has completed equipping
            if (pendingEquipIndex >= 0 && equippedIndex == pendingEquipIndex)
                pendingEquipIndex = -1;

            int activeIndex = (pendingEquipIndex >= 0) ? pendingEquipIndex : equippedIndex;

            for (int i = 0; i < weaponCards.Count; i++)
            {
                var card = weaponCards[i];
                bool isEquipped = (i == activeIndex);

                if (isEquipped)
                {
                    // Glowing Cyan/Teal border
                    card.BorderImage.color = new Color(0.20f, 0.88f, 0.80f, 1f);
                    card.CardBgImage.color = new Color(0.09f, 0.14f, 0.20f, 1f);

                    // Equipped status badge
                    card.ActionButtonBg.color = new Color(0.12f, 0.62f, 0.42f, 1f);
                    card.ActionButtonText.text = "[ CURRENTLY EQUIPPED ]";

                    ColorBlock cb = card.ActionButton.colors;
                    cb.normalColor = new Color(0.12f, 0.62f, 0.42f, 1f);
                    cb.highlightedColor = new Color(0.16f, 0.72f, 0.50f, 1f);
                    cb.pressedColor = new Color(0.10f, 0.50f, 0.35f, 1f);
                    cb.selectedColor = cb.highlightedColor;
                    card.ActionButton.colors = cb;
                }
                else
                {
                    // Subtle Slate border
                    card.BorderImage.color = new Color(0.20f, 0.26f, 0.36f, 0.8f);
                    card.CardBgImage.color = new Color(0.08f, 0.11f, 0.17f, 1f);

                    // Equip action button
                    card.ActionButtonBg.color = new Color(0.16f, 0.50f, 0.88f, 1f);
                    string keyHint = (i == 0) ? "[1]" : (i == 1) ? "[2]" : $"[{i + 1}]";
                    card.ActionButtonText.text = $"EQUIP WEAPON  {keyHint}";

                    ColorBlock cb = card.ActionButton.colors;
                    cb.normalColor = new Color(0.16f, 0.50f, 0.88f, 1f);
                    cb.highlightedColor = new Color(0.26f, 0.64f, 0.98f, 1f);
                    cb.pressedColor = new Color(0.12f, 0.38f, 0.70f, 1f);
                    cb.selectedColor = cb.highlightedColor;
                    card.ActionButton.colors = cb;
                }
            }
        }

        private string GetFormattedWeaponName(WeaponBehaviour weapon, int slotIndex)
        {
            if (weapon == null) return $"Weapon {slotIndex + 1}";
            string raw = weapon.gameObject.name.Replace("P_LPSP_WEP_", "").Replace("(Clone)", "").Trim();
            if (raw.StartsWith("AR_01", StringComparison.OrdinalIgnoreCase))
                return "AR-01 Assault Rifle";
            if (raw.StartsWith("Handgun_03", StringComparison.OrdinalIgnoreCase))
                return "Handgun-03 Pistol";

            return raw.Replace("_", " ");
        }

        private void UpdateTutorialPrompt()
        {
            var texts = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t != null && t.text.Contains("Hold TAB For Controls") && !t.text.Contains("[P]"))
                {
                    t.text = "Hold TAB For Controls | [P] Weapon Menu";
                }
            }
        }

        #endregion

        #region HELPERS

        private static Sprite GetWhiteSprite()
        {
            if (cachedWhiteSprite != null) return cachedWhiteSprite;
            Texture2D tex = Texture2D.whiteTexture;
            cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return cachedWhiteSprite;
        }

        private static TMP_FontAsset GetFont()
        {
            if (cachedFont != null) return cachedFont;
            try
            {
                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                foreach (var f in fonts)
                {
                    if (f != null && (f.name.Contains("Roboto-Bold") || f.name.Contains("Roboto-Condensed")))
                    {
                        cachedFont = f;
                        return cachedFont;
                    }
                }
            }
            catch {}
            return null;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = 5;
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            var font = GetFont();
            if (font != null)
                tmp.font = font;

            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Image CreateImage(Transform parent, string name, Color color, Sprite sprite = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : GetWhiteSprite();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        #endregion
    }
}
