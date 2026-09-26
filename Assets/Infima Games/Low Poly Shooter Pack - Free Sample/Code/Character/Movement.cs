// Copyright 2021, Infima Games. All Rights Reserved.

using System.Linq;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class Movement : MovementBehaviour
    {
        #region FIELDS SERIALIZED

        [Header("Audio Clips")]
        
        [Tooltip("The audio clip that is played while walking.")]
        [SerializeField]
        private AudioClip audioClipWalking;

        [Tooltip("The audio clip that is played while running.")]
        [SerializeField]
        private AudioClip audioClipRunning;

        [Tooltip("The audio clip that is played while sliding.")]
        [SerializeField]
        private AudioClip audioClipSlide;

        [Header("Speeds")]

        [SerializeField]
        private float speedWalking = 5.0f;

        [Tooltip("How fast the player moves while running."), SerializeField]
        private float speedRunning = 9.0f;

        [Header("Sliding")]

        [Tooltip("Initial burst speed when starting a slide.")]
        [SerializeField]
        private float slideSpeedInitial = 9.5f;

        [Tooltip("Friction / deceleration rate per second while sliding.")]
        [SerializeField]
        private float slideFriction = 10.0f;

        [Tooltip("Minimum speed below which the slide will conclude.")]
        [SerializeField]
        private float slideMinSpeed = 4.0f;

        [Tooltip("Maximum slide duration on flat ground in seconds.")]
        [SerializeField]
        private float slideMaxDuration = 0.65f;

        [Tooltip("Capsule collider height while sliding.")]
        [SerializeField]
        private float slideColliderHeight = 1.0f;

        [Header("Jumping")]

        [Tooltip("Vertical impulse force applied when jumping.")]
        [SerializeField]
        private float jumpForce = 5.5f;

        [Tooltip("Audio clip played when jumping.")]
        [SerializeField]
        private AudioClip audioClipJump;

        #endregion

        #region PROPERTIES

        //Velocity.
        private Vector3 Velocity
        {
            //Getter.
            get => rigidBody.linearVelocity;
            //Setter.
            set => rigidBody.linearVelocity = value;
        }

        #endregion

        #region FIELDS

        /// <summary>
        /// Attached Rigidbody.
        /// </summary>
        private Rigidbody rigidBody;
        /// <summary>
        /// Attached CapsuleCollider.
        /// </summary>
        private CapsuleCollider capsule;
        /// <summary>
        /// Attached AudioSource.
        /// </summary>
        private AudioSource audioSource;
        
        /// <summary>
        /// True if the character is currently grounded.
        /// </summary>
        private bool grounded;

        /// <summary>
        /// Player Character.
        /// </summary>
        private CharacterBehaviour playerCharacter;
        /// <summary>
        /// The player character's equipped weapon.
        /// </summary>
        private WeaponBehaviour equippedWeapon;
        
        /// <summary>
        /// Array of RaycastHits used for ground checking.
        /// </summary>
        private readonly RaycastHit[] groundHits = new RaycastHit[8];

        /// <summary>
        /// Default capsule height before sliding.
        /// </summary>
        private float defaultCapsuleHeight;
        /// <summary>
        /// Default capsule center before sliding.
        /// </summary>
        private Vector3 defaultCapsuleCenter;
        /// <summary>
        /// Direction of the current slide in world coordinates.
        /// </summary>
        private Vector3 slideDirection;
        /// <summary>
        /// Current forward speed during a slide.
        /// </summary>
        private float currentSlideSpeed;
        /// <summary>
        /// Elapsed time for the current slide.
        /// </summary>
        private float slideTimer;
        /// <summary>
        /// True if the character was sliding in the previous frame.
        /// </summary>
        private bool wasSliding;

        #endregion

        #region UNITY FUNCTIONS

        /// <summary>
        /// Awake.
        /// </summary>
        protected override void Awake()
        {
            //Get Player Character directly or fallback to ServiceLocator.
            playerCharacter = GetComponent<CharacterBehaviour>();
            if (playerCharacter == null && ServiceLocator.Current != null && ServiceLocator.Current.Get<IGameModeService>() != null)
                playerCharacter = ServiceLocator.Current.Get<IGameModeService>().GetPlayerCharacter();
        }

        /// Initializes the FpsController on start.
        protected override  void Start()
        {
            //Rigidbody Setup.
            rigidBody = GetComponent<Rigidbody>();
            rigidBody.constraints = RigidbodyConstraints.FreezeRotation;
            //Cache the CapsuleCollider.
            capsule = GetComponent<CapsuleCollider>();
            defaultCapsuleHeight = capsule.height;
            defaultCapsuleCenter = capsule.center;

            //Audio Source Setup.
            audioSource = GetComponent<AudioSource>();
            audioSource.clip = audioClipWalking;
            audioSource.loop = true;
        }

        /// Checks if the character is on the ground.
        private void OnCollisionStay()
        {
            //Bounds.
            Bounds bounds = capsule.bounds;
            //Extents.
            Vector3 extents = bounds.extents;
            //Radius.
            float radius = extents.x - 0.01f;
            
            //Cast. This checks whether there is indeed ground, or not.
            Physics.SphereCastNonAlloc(bounds.center, radius, Vector3.down,
                groundHits, extents.y - radius * 0.5f, ~0, QueryTriggerInteraction.Ignore);
            
            //We can ignore the rest if we don't have any proper hits.
            if (!groundHits.Any(hit => hit.collider != null && hit.collider != capsule)) 
                return;
            
            //Store RaycastHits.
            for (var i = 0; i < groundHits.Length; i++)
                groundHits[i] = new RaycastHit();

            //Set grounded. Now we know for sure that we're grounded.
            grounded = true;
        }
			
        protected override void FixedUpdate()
        {
            //Move.
            MoveCharacter();
            
            //Unground.
            grounded = false;
        }

        /// Moves the camera to the character, processes jumping and plays sounds every frame.
        protected override  void Update()
        {
            //Get the equipped weapon!
            equippedWeapon = playerCharacter.GetInventory().GetEquipped();
            
            //Play Sounds!
            PlayFootstepSounds();
        }

        #endregion

        #region METHODS

        private void MoveCharacter()
        {
            Vector2 frameInput = playerCharacter.GetInputMovement();

            #region Sliding

            if (playerCharacter.IsSliding())
            {
                // Initialize slide state on first frame
                if (!wasSliding)
                {
                    wasSliding = true;
                    slideTimer = 0.0f;
                    currentSlideSpeed = slideSpeedInitial;

                    // Calculate initial slide direction from input or forward
                    var inputDir = new Vector3(frameInput.x, 0.0f, frameInput.y);
                    if (inputDir.sqrMagnitude > 0.01f)
                        slideDirection = transform.TransformDirection(inputDir.normalized);
                    else
                        slideDirection = transform.forward;

                    slideDirection.y = 0.0f;
                    slideDirection.Normalize();

                    // Adjust Capsule Collider height & center
                    capsule.height = slideColliderHeight;
                    capsule.center = new Vector3(defaultCapsuleCenter.x, defaultCapsuleCenter.y - (defaultCapsuleHeight - slideColliderHeight) * 0.5f, defaultCapsuleCenter.z);
                }

                // Advance slide timer
                slideTimer += Time.fixedDeltaTime;

                // Friction deceleration
                currentSlideSpeed -= slideFriction * Time.fixedDeltaTime;

                // Downhill slope boost: raycast down to detect slope normal
                if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit slopeHit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Vector3 slopeDir = Vector3.ProjectOnPlane(slideDirection, slopeHit.normal).normalized;
                    float downhillFactor = -slopeDir.y; // Positive when heading downhill
                    if (downhillFactor > 0.05f)
                    {
                        // Accelerate or maintain speed on slopes
                        currentSlideSpeed += downhillFactor * 9.81f * Time.fixedDeltaTime;
                    }
                }

                // Check termination conditions
                bool timeExpired = slideTimer >= slideMaxDuration;
                bool speedExhausted = currentSlideSpeed <= slideMinSpeed;

                if (timeExpired || speedExhausted)
                {
                    if (CanStandUp())
                    {
                        StopSlide();
                        return;
                    }
                    else
                    {
                        // Under low ceiling: crawl until clear
                        currentSlideSpeed = Mathf.Max(currentSlideSpeed, speedWalking * 0.6f);
                    }
                }

                // Set slide velocity (preserve vertical physics velocity)
                Vector3 slideVelocity = slideDirection * currentSlideSpeed;
                Velocity = new Vector3(slideVelocity.x, rigidBody.linearVelocity.y, slideVelocity.z);
                return;
            }
            else if (wasSliding)
            {
                // Slide was cancelled externally
                if (CanStandUp())
                {
                    StopSlide();
                }
            }

            #endregion

            #region Calculate Normal Movement Velocity

            //Calculate local-space direction by using the player's input.
            var movement = new Vector3(frameInput.x, 0.0f, frameInput.y);
            
            //Running speed calculation.
            if(playerCharacter.IsRunning())
                movement *= speedRunning;
            else
            {
                //Multiply by the normal walking speed.
                movement *= speedWalking;
            }

            //World space velocity calculation. This allows us to add it to the rigidbody's velocity properly.
            movement = transform.TransformDirection(movement);

            #endregion
            
            //Update Velocity.
            if (grounded)
            {
                Velocity = new Vector3(movement.x, rigidBody.linearVelocity.y, movement.z);
            }
            else
            {
                // Air control: smoothly interpolate horizontal movement without killing jump momentum instantly.
                float airLerp = Time.fixedDeltaTime * 6.0f;
                Vector3 currentVel = rigidBody.linearVelocity;
                Vector3 targetVel = movement;
                Vector3 newHorizontal = Vector3.Lerp(new Vector3(currentVel.x, 0.0f, currentVel.z), targetVel, airLerp);
                Velocity = new Vector3(newHorizontal.x, currentVel.y, newHorizontal.z);
            }
        }

        /// <summary>
        /// Performs a jump or slide-jump if grounded.
        /// </summary>
        public void Jump()
        {
            if (!grounded)
                return;

            // Slide-jump: cancel slide but carry horizontal momentum into the jump!
            if (playerCharacter != null && playerCharacter.IsSliding())
            {
                StopSlide();
            }

            // Apply vertical jump impulse
            rigidBody.linearVelocity = new Vector3(rigidBody.linearVelocity.x, jumpForce, rigidBody.linearVelocity.z);
            grounded = false;

            if (audioClipJump != null)
                audioSource.PlayOneShot(audioClipJump);
        }

        /// <summary>
        /// Returns true if the character is grounded.
        /// </summary>
        public bool IsGrounded() => grounded;

        /// <summary>
        /// Restores capsule collider dimensions and stops the sliding state on the character.
        /// </summary>
        private void StopSlide()
        {
            capsule.height = defaultCapsuleHeight;
            capsule.center = defaultCapsuleCenter;
            wasSliding = false;

            if (playerCharacter is Character character)
                character.SetSliding(false);
        }

        /// <summary>
        /// Returns true if there is enough vertical clearance above the player to stand up.
        /// </summary>
        private bool CanStandUp()
        {
            float radius = capsule.radius * 0.9f;
            Vector3 castOrigin = transform.position + Vector3.up * (slideColliderHeight - radius);
            float castDistance = defaultCapsuleHeight - slideColliderHeight + radius;
            return !Physics.SphereCast(castOrigin, radius, Vector3.up, out _, castDistance, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Plays Footstep Sounds. This code is slightly old, so may not be great, but it functions alright-y!
        /// </summary>
        private void PlayFootstepSounds()
        {
            //Check if we're moving on the ground. We don't need footsteps in the air.
            if (grounded && rigidBody.linearVelocity.sqrMagnitude > 0.1f)
            {
                if (playerCharacter.IsSliding())
                {
                    AudioClip targetClip = audioClipSlide != null ? audioClipSlide : audioClipRunning;
                    if (audioSource.clip != targetClip)
                    {
                        audioSource.clip = targetClip;
                        audioSource.Play();
                    }
                }
                else
                {
                    //Select the correct audio clip to play.
                    AudioClip targetClip = playerCharacter.IsRunning() ? audioClipRunning : audioClipWalking;
                    if (audioSource.clip != targetClip)
                    {
                        audioSource.clip = targetClip;
                        if (audioSource.isPlaying)
                            audioSource.Play();
                    }
                }

                //Play it!
                if (!audioSource.isPlaying)
                    audioSource.Play();
            }
            //Pause it if we're doing something like flying, or not moving!
            else if (audioSource.isPlaying)
                audioSource.Pause();
        }

        #endregion
    }
}