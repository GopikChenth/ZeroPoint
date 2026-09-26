// Copyright 2021, Infima Games. All Rights Reserved.

using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Camera Look. Handles the rotation of the camera.
    /// </summary>
    public class CameraLook : MonoBehaviour
    {
        #region FIELDS SERIALIZED
        
        [Header("Settings")]
        
        [Tooltip("Sensitivity when looking around.")]
        [SerializeField]
        private Vector2 sensitivity = new Vector2(1, 1);

        [Tooltip("Minimum and maximum up/down rotation angle the camera can have.")]
        [SerializeField]
        private Vector2 yClamp = new Vector2(-60, 60);

        [Tooltip("Should the look rotation be interpolated?")]
        [SerializeField]
        private bool smooth;

        [Tooltip("The speed at which the look rotation is interpolated.")]
        [SerializeField]
        private float interpolationSpeed = 25.0f;

        [Header("Sliding Feel")]

        [Tooltip("Local Y height of the camera/arms holder while sliding.")]
        [SerializeField]
        private float slideHeight = 0.95f;

        [Tooltip("Speed of the height drop / raise transition.")]
        [SerializeField]
        private float heightTransitionSpeed = 10.0f;

        [Tooltip("Camera roll tilt angle in degrees while sliding. Set to 0 to disable rolling.")]
        [SerializeField]
        private float slideTiltAngle = 0.0f;

        [Tooltip("Speed of the roll tilt transition.")]
        [SerializeField]
        private float tiltTransitionSpeed = 10.0f;

        [Tooltip("Field of View kick amount while sliding.")]
        [SerializeField]
        private float slideFovKick = 4.0f;
        
        #endregion
        
        #region FIELDS
        
        /// <summary>
        /// Player Character.
        /// </summary>
        private CharacterBehaviour playerCharacter;
        /// <summary>
        /// The player character's rigidbody component.
        /// </summary>
        private Rigidbody playerCharacterRigidbody;

        /// <summary>
        /// Initial local position of this transform.
        /// </summary>
        private Vector3 initialLocalPosition;
        /// <summary>
        /// Current smoothed local height.
        /// </summary>
        private float currentHeight;
        /// <summary>
        /// Current smoothed roll tilt.
        /// </summary>
        private float currentRoll;
        /// <summary>
        /// Default Camera Field of View.
        /// </summary>
        private float defaultFov;
        /// <summary>
        /// Cached camera component.
        /// </summary>
        private Camera characterCamera;

        /// <summary>
        /// The player character's rotation.
        /// </summary>
        private Quaternion rotationCharacter;
        /// <summary>
        /// The camera's rotation.
        /// </summary>
        private Quaternion rotationCamera;

        /// <summary>
        /// Pure pitch rotation of the camera without roll.
        /// </summary>
        private Quaternion pitchRotation;

        #endregion
        
        #region UNITY

        private void Awake()
        {
            //Get Player Character.
            playerCharacter = ServiceLocator.Current.Get<IGameModeService>().GetPlayerCharacter();
            //Cache the rigidbody.
            playerCharacterRigidbody = playerCharacter.GetComponent<Rigidbody>();
        }
        private void Start()
        {
            //Cache the character's initial rotation.
            rotationCharacter = playerCharacter.transform.localRotation;
            //Cache the camera's initial rotation.
            rotationCamera = transform.localRotation;
            pitchRotation = transform.localRotation;

            //Cache initial local position and height.
            initialLocalPosition = transform.localPosition;
            currentHeight = initialLocalPosition.y;

            //Cache camera and default FOV.
            if (playerCharacter != null)
            {
                characterCamera = playerCharacter.GetCameraWorld();
                if (characterCamera != null)
                    defaultFov = characterCamera.fieldOfView;
            }
        }
        private void LateUpdate()
        {
            //Slide Camera Height.
            bool isSliding = playerCharacter != null && playerCharacter.IsSliding();
            float targetY = isSliding ? slideHeight : initialLocalPosition.y;
            currentHeight = Mathf.Lerp(currentHeight, targetY, Time.deltaTime * heightTransitionSpeed);
            transform.localPosition = new Vector3(initialLocalPosition.x, currentHeight, initialLocalPosition.z);

            //Slide Camera Roll (Dutch Tilt).
            float targetRoll = isSliding ? slideTiltAngle : 0.0f;
            currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.deltaTime * tiltTransitionSpeed);

            //Slide Dynamic FOV Kick.
            if (characterCamera != null)
            {
                float targetFov = isSliding ? defaultFov + slideFovKick : defaultFov;
                characterCamera.fieldOfView = Mathf.Lerp(characterCamera.fieldOfView, targetFov, Time.deltaTime * heightTransitionSpeed);
            }

            //Frame Input. The Input to add this frame!
            Vector2 frameInput = playerCharacter.IsCursorLocked() ? playerCharacter.GetInputLook() : default;
            //Sensitivity.
            frameInput *= sensitivity;

            //Yaw.
            Quaternion rotationYaw = Quaternion.Euler(0.0f, frameInput.x, 0.0f);
            //Pitch.
            Quaternion rotationPitch = Quaternion.Euler(-frameInput.y, 0.0f, 0.0f);
            
            //Save rotation. We use this for smooth rotation.
            rotationCamera *= rotationPitch;
            rotationCharacter *= rotationYaw;
            
            //Smooth.
            if (smooth)
            {
                //Interpolate pitch rotation.
                pitchRotation = Quaternion.Slerp(pitchRotation, rotationCamera, Time.deltaTime * interpolationSpeed);
                pitchRotation = Clamp(pitchRotation);
                //Interpolate character rotation.
                playerCharacterRigidbody.MoveRotation(Quaternion.Slerp(playerCharacterRigidbody.rotation, rotationCharacter, Time.deltaTime * interpolationSpeed));
            }
            else
            {
                //Rotate pitch.
                pitchRotation *= rotationPitch;
                //Clamp.
                pitchRotation = Clamp(pitchRotation);

                //Rotate character.
                playerCharacterRigidbody.MoveRotation(playerCharacterRigidbody.rotation * rotationYaw);
            }
            
            //Set with roll tilt applied without compounding into pitchRotation.
            Quaternion rollRotation = Quaternion.Euler(0.0f, 0.0f, currentRoll);
            transform.localRotation = pitchRotation * rollRotation;
        }

        #endregion

        #region FUNCTIONS

        /// <summary>
        /// Clamps the pitch of a quaternion according to our clamps.
        /// </summary>
        private Quaternion Clamp(Quaternion rotation)
        {
            rotation.x /= rotation.w;
            rotation.y /= rotation.w;
            rotation.z /= rotation.w;
            rotation.w = 1.0f;

            //Pitch.
            float pitch = 2.0f * Mathf.Rad2Deg * Mathf.Atan(rotation.x);

            //Clamp.
            pitch = Mathf.Clamp(pitch, yClamp.x, yClamp.y);
            rotation.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * pitch);

            //Return.
            return rotation;
        }

        #endregion
    }
}