// Copyright 2021, Infima Games. All Rights Reserved.

using UnityEngine;

namespace InfimaGames.LowPolyShooterPack.Interface
{
    /// <summary>
    /// Player Interface.
    /// </summary>
    public class CanvasSpawner : MonoBehaviour
    {
        #region FIELDS SERIALIZED

        [Header("Settings")]
        
        [Tooltip("Canvas prefab spawned at start. Displays the player's user interface.")]
        [SerializeField]
        private GameObject canvasPrefab;

        #endregion

        #region UNITY FUNCTIONS

        /// <summary>
        /// Awake.
        /// </summary>
        private void Awake()
        {
            //Spawn Interface.
            GameObject canvasInstance = Instantiate(canvasPrefab);
            if (canvasInstance != null)
            {
                var menu = canvasInstance.GetComponentInChildren<WeaponSelectionMenu>();
                if (menu == null)
                    menu = canvasInstance.AddComponent<WeaponSelectionMenu>();

                menu.Init(GetComponent<CharacterBehaviour>());
            }
        }

        #endregion
    }
}