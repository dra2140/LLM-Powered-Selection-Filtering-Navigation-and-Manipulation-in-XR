using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace Voice2Action
{
    /// <summary>
    /// Manages instruction panels for Voice2Action that automatically advance after a set duration.
    /// Shows instruction panels when the app starts and auto-advances through them.
    /// </summary>
    public class Voice2ActionOnboardingManager : MonoBehaviour
    {
        [Header("Instruction Panel Settings")]
        [SerializeField] private GameObject instructionPanelPrefab;
        [SerializeField] private Transform panelParent;
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private float initialDelay = 1.5f;
        [SerializeField] private float panelDisplayDuration = 3.0f; // How long each panel shows before advancing
        
        [Header("Camera Reference")]
        [SerializeField] private Camera xrCamera;
        
        [Header("Panel Positioning")]
        [SerializeField] private float panelDistance = 1.5f;
        [SerializeField] private float panelHeight = 0.0f;
        [SerializeField] private bool followUser = true;
        [SerializeField] private float followSpeed = 2.0f;
        
        [Header("Input Controls")]
        [SerializeField] private InputAction showInstructionsAction;
        [SerializeField] private bool allowSkipWithButton = true; // Allow users to skip with a button press
        
        [Header("Voice2Action References")]
        [SerializeField] private GameObject expandPanel;
        [SerializeField] private GameObject infoPanel;

        [Header("Menu Button Settings")]
        [SerializeField] private InputActionReference menuButtonActionReference;

        [Header("Controller Navigation")]
        [SerializeField] private InputActionReference exitButtonInputReference; // X button
        [SerializeField] private InputActionReference leftSelectButtonReference;  //  previous button
        [SerializeField] private InputActionReference rightSelectButtonReference; //  next button
        [SerializeField] private float buttonNavigationCooldown = 0.2f; 

        // Add a reference to the XR controller
        [SerializeField] private XRBaseController leftXRController;
        [SerializeField] private XRBaseController rightXRController;


        private float lastButtonNavigationTime = 0f;



        /// <summary>
        /// State representation for the Voice2Action onboarding steps.
        /// </summary>
        public enum OnboardingSteps
        {
            Welcome,
            VoiceActivation,
            SelectionCommands,
            ExpandPanel,
            ModificationCommands,
            Ready
        }
        
        [System.Serializable]
        public class InstructionStep
        {
            public string title = "Voice2Action";
            [TextArea(3, 10)]
            public string content = "Learn how to use Voice2Action.";
            public Sprite staticImage;     
            public RuntimeAnimatorController animationController; 
            public float customDuration = 3f;
            public GameObject stepObject;
        }
        
        [SerializeField] private List<InstructionStep> instructionSteps = new List<InstructionStep>();
        
        private GameObject currentPanel;
        private int currentStepIndex = 0;
        private Coroutine autoAdvanceCoroutine;
        private bool onboardingActive = false;
        private Queue<OnboardingSteps> remainingSteps;
        private float currentStepTimer = 0f;
        
        private void Awake()
        {
            if (xrCamera == null)
            {
                xrCamera = Camera.main;
            }
            
            if (showInstructionsAction != null)
            {
                showInstructionsAction.Enable();
            }
            
            if (instructionSteps.Count == 0)
            {
                instructionSteps = new List<InstructionStep>(GetDefaultInstructions());
            }
            if (menuButtonActionReference != null)
            {
                menuButtonActionReference.action.Enable();
                menuButtonActionReference.action.performed += OnMenuButtonPressed;
            }
        }

        private void OnMenuButtonPressed(InputAction.CallbackContext context)
        {
            Debug.Log("Menu button pressed - showing instructions");
            RestartInstructions();
        }
        
        private void OnEnable()
        {
            if (showInstructionsAction != null)
            {
                showInstructionsAction.Enable();
                showInstructionsAction.performed += ctx => RestartInstructions();
            }

                // Enable the new button references
            if (leftSelectButtonReference != null)
            {
                leftSelectButtonReference.action.Enable();
            }
            
            if (rightSelectButtonReference != null)
            {
                rightSelectButtonReference.action.Enable();
            }

        }

        private void OnDestroy()
        {
            if (menuButtonActionReference != null)
            {
                menuButtonActionReference.action.performed -= OnMenuButtonPressed;
            }
        }
                
        private void OnDisable()
        {
            if (showInstructionsAction != null)
            {
                showInstructionsAction.performed -= ctx => RestartInstructions();
                showInstructionsAction.Disable();
            }

            // Disable the new button references
            if (leftSelectButtonReference != null)
            {
                leftSelectButtonReference.action.Disable();
            }
            
            if (rightSelectButtonReference != null)
            {
                rightSelectButtonReference.action.Disable();
            }
        }
        
        private void Start()
        {
            InitializeOnboarding();
            
            if (showOnStart)
            {
                // Wait a moment before showing instructions
                StartCoroutine(StartOnboardingDelayed(initialDelay));
            }
        }
        
        private void Update()
        {
            if (currentPanel != null && followUser && xrCamera != null)
            {
                UpdatePanelPosition();
            }
            
            // Handle auto-advancing panels
            if (onboardingActive && currentPanel != null)
            {
                currentStepTimer += Time.deltaTime;
                
                // Use custom duration if set, otherwise use default
                float stepDuration = (currentStepIndex < instructionSteps.Count && instructionSteps[currentStepIndex].customDuration > 0) 
                    ? instructionSteps[currentStepIndex].customDuration 
                    : panelDisplayDuration;
                
                if (currentStepTimer >= stepDuration)
                {
                    NextStep();
                }
            }
            
            // Allow manual skip if enabled
            if (allowSkipWithButton && onboardingActive && Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                NextStep();
            }

            // Check for button navigation
            if (Time.time - lastButtonNavigationTime > buttonNavigationCooldown)
            {
                // Right controller select button = next panel
                if (rightSelectButtonReference != null && rightSelectButtonReference.action.enabled && 
                    rightSelectButtonReference.action.WasPressedThisFrame())
                {
                    Debug.Log("Right select button pressed - advancing to next step");
                    NextStep();
                    lastButtonNavigationTime = Time.time;
                }
                
                // Left controller select button = previous panel
                if (leftSelectButtonReference != null && leftSelectButtonReference.action.enabled && 
                    leftSelectButtonReference.action.WasPressedThisFrame())
                {
                    Debug.Log("Left select button pressed - going to previous step");
                    PreviousStep();
                    lastButtonNavigationTime = Time.time;
                }
            }
            
            // Check for exit button (X button) press
            if (exitButtonInputReference != null && exitButtonInputReference.action.enabled)
            {
                if (exitButtonInputReference.action.WasPressedThisFrame())
                {
                    CloseInstructions();
                }
            }

        }
        

        private void InitializeOnboarding()
        {
            remainingSteps = new Queue<OnboardingSteps>();
            
            // Add all onboarding steps to the queue
            remainingSteps.Enqueue(OnboardingSteps.Welcome);
            remainingSteps.Enqueue(OnboardingSteps.VoiceActivation);
            remainingSteps.Enqueue(OnboardingSteps.SelectionCommands);
            remainingSteps.Enqueue(OnboardingSteps.ExpandPanel);
            remainingSteps.Enqueue(OnboardingSteps.ModificationCommands);
            remainingSteps.Enqueue(OnboardingSteps.Ready);
            
            currentStepIndex = 0;
            onboardingActive = false;
        }
        


        private IEnumerator StartOnboardingDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            StartOnboarding();
        }
        


        public void StartOnboarding()
        {
            if (remainingSteps.Count > 0)
            {
                currentStepIndex = 0;
                currentStepTimer = 0f;
                ShowCurrentStep();
                onboardingActive = true;
            }
        }
        


        public void RestartInstructions()
        {
            Debug.Log("Menu button pressed - restarting instructions");
            CloseInstructions();
            InitializeOnboarding();
            StartOnboarding();
        }
        


        private void ShowCurrentStep()
        {
            if (currentPanel != null)
            {
                Destroy(currentPanel);
            }
            
            if (instructionSteps.Count <= currentStepIndex)
            {
                Debug.LogWarning("No instruction step available at index: " + currentStepIndex);
                return;
            }
            
            // Create panel
            CreatePanel();
            
            // Reset the timer for the new step
            currentStepTimer = 0f;
            
            // Activate step-specific objects if any
            if (currentStepIndex < instructionSteps.Count && instructionSteps[currentStepIndex].stepObject != null)
            {
                instructionSteps[currentStepIndex].stepObject.SetActive(true);
            }
        }
        


        private void UpdatePanelPosition()
        {
            // Calculate target position
            Vector3 targetPosition = xrCamera.transform.position + 
                                    xrCamera.transform.forward * panelDistance;
            targetPosition.y += panelHeight;
            
            // Move gradually to target position
            currentPanel.transform.position = Vector3.Lerp(
                currentPanel.transform.position, 
                targetPosition, 
                followSpeed * Time.deltaTime
            );
            
            // Face panel toward user
            currentPanel.transform.rotation = Quaternion.Lerp(
                currentPanel.transform.rotation,
                Quaternion.LookRotation(currentPanel.transform.position - xrCamera.transform.position),
                followSpeed * Time.deltaTime
            );
        }
        

        private void CreatePanel()
        {
            // Create panel game object
            if (panelParent != null)
            {
                currentPanel = Instantiate(instructionPanelPrefab, panelParent);
            }
            else
            {
                currentPanel = Instantiate(instructionPanelPrefab);
            }
            
            // Position panel in front of user
            if (xrCamera != null)
            {
                Vector3 position = xrCamera.transform.position + xrCamera.transform.forward * panelDistance;
                position.y += panelHeight;
                currentPanel.transform.position = position;
                currentPanel.transform.rotation = Quaternion.LookRotation(
                    currentPanel.transform.position - xrCamera.transform.position
                );
            }
            
            // Set up content
            UpdatePanelContent();
            
            // Set up navigation buttons for interaction
            SetupNavigationButtons();
        }



        private void SetupNavigationButtons()
        {
            // Show Next button
            Transform nextButtonTransform = currentPanel.transform.Find("NextButton");
            if (nextButtonTransform != null) 
            {
                Button nextButton = nextButtonTransform.GetComponent<Button>();
                if (nextButton != null)
                {
                    // Remove any existing listeners
                    nextButton.onClick.RemoveAllListeners();
                    // Add the Next step functionality
                    nextButton.onClick.AddListener(() => {
                        Debug.Log("Next button clicked!");
                        NextStep();
                    });
                    // Only show if there are more steps
                    nextButtonTransform.gameObject.SetActive(currentStepIndex < instructionSteps.Count - 1);
                }
            }
            
            // Show Previous button
            Transform prevButtonTransform = currentPanel.transform.Find("PrevButton");
            if (prevButtonTransform != null) 
            {
                Button prevButton = prevButtonTransform.GetComponent<Button>();
                if (prevButton != null)
                {
                    // Remove any existing listeners
                    prevButton.onClick.RemoveAllListeners();
                    // Add the Previous step functionality
                    prevButton.onClick.AddListener(() => {
                        Debug.Log("Previous button clicked!");
                        PreviousStep();
                    });
                    // Only show if not on first step
                    prevButtonTransform.gameObject.SetActive(currentStepIndex > 0);
                }
            }
            
            // Show Skip button (optional)
            Transform skipButtonTransform = currentPanel.transform.Find("SkipButton");
            if (skipButtonTransform != null) 
            {
                Button skipButton = skipButtonTransform.GetComponent<Button>();
                if (skipButton != null)
                {
                    // Remove any existing listeners
                    skipButton.onClick.RemoveAllListeners();
                    // Add the Close instructions functionality
                    skipButton.onClick.AddListener(() => {
                        Debug.Log("Skip button clicked!");
                        CloseInstructions();
                    });
                    // Always show skip button
                    skipButtonTransform.gameObject.SetActive(true);
                }
            }
            
            // Setup Close button
            Transform closeButtonTransform = currentPanel.transform.Find("CloseButton");
            if (closeButtonTransform != null)
            {
                Button closeButton = closeButtonTransform.GetComponent<Button>();
                if (closeButton != null)
                {
                    // Remove any existing listeners
                    closeButton.onClick.RemoveAllListeners();
                    // Add the Close instructions functionality
                    closeButton.onClick.AddListener(() => {
                        Debug.Log("Close button clicked!");
                        CloseInstructions();
                    });
                    // Always show close button
                    closeButtonTransform.gameObject.SetActive(true);
                }
            }
        }
        


        private void UpdatePanelContent()
        {
            if (currentPanel == null || currentStepIndex >= instructionSteps.Count) return;
            
            InstructionStep step = instructionSteps[currentStepIndex];
            
            // Update title
            TextMeshProUGUI titleText = currentPanel.transform.Find("Title")?.GetComponent<TextMeshProUGUI>();
            if (titleText != null)
            {
                titleText.text = step.title;
            }
            
            // Update content
            TextMeshProUGUI contentText = currentPanel.transform.Find("Content")?.GetComponent<TextMeshProUGUI>();
            if (contentText != null)
            {
                contentText.text = step.content;
            }
            
            GameObject imageObject = currentPanel.transform.Find("Image")?.gameObject;
            if (imageObject != null)
            {
                Image image = imageObject.GetComponent<Image>();
                Animator animator = imageObject.GetComponent<Animator>();
                
                // ALWAYS set preserve aspect for both static and animated images
                if (image != null)
                {
                    image.preserveAspect = true;
                }
                
                // Check if we have an animation controller
                if (step.animationController != null && animator != null)
                {
                    // Use animation
                    animator.runtimeAnimatorController = step.animationController;
                    imageObject.SetActive(true);
                    
                    // Start the animation directly
                    animator.Play("Idle", 0, 0f); // Replace "Idle" with your animation state name
                    
                    // Optionally adjust size for animated content
                    AutoFitImageSize(image, step.animationController);
                }
                else if (step.staticImage != null && image != null)
                {
                    // Use static image
                    if (animator != null) 
                        animator.runtimeAnimatorController = null; // Stop any animation
                    
                    image.sprite = step.staticImage;
                    imageObject.SetActive(true);
                    
                    // Adjust size for static image
                    AutoFitStaticImageSize(image, step.staticImage);
                }
                else
                {
                    // Hide image component
                    imageObject.SetActive(false);
                }
            }
                                
            // Update page indicator if you want to show it
            TextMeshProUGUI pageIndicator = currentPanel.transform.Find("PageIndicator")?.GetComponent<TextMeshProUGUI>();
            if (pageIndicator != null)
            {
                pageIndicator.text = $"{currentStepIndex + 1} / {instructionSteps.Count}";
                // Optionally hide the page indicator for auto-advancing panels
                // pageIndicator.gameObject.SetActive(false);
            }
        }


        private void AutoFitStaticImageSize(Image image, Sprite sprite)
        {
            if (sprite == null || image == null) return;
            
            // Get the sprite's original aspect ratio
            float aspectRatio = (float)sprite.texture.width / sprite.texture.height;
            
            // Set RectTransform to maintain aspect ratio
            RectTransform rectTransform = image.GetComponent<RectTransform>();
            
            // Example: Fit within a max size while maintaining aspect
            float maxWidth = 40f;
            float maxHeight = 20f;
            
            if (aspectRatio > 1.0f) // Wider than tall
            {
                rectTransform.sizeDelta = new Vector2(maxWidth, maxWidth / aspectRatio);
            }
            else // Taller than wide or square
            {
                rectTransform.sizeDelta = new Vector2(maxHeight * aspectRatio, maxHeight);
            }
        }

        // Helper method for fitting animated content
        private void AutoFitImageSize(Image image, RuntimeAnimatorController animatorController)
        {
            if (animatorController == null || image == null) return;
            
            RectTransform rectTransform = image.GetComponent<RectTransform>();

        }


        private void HideNavigationButtons()
        {
            // Hide Next button
            Transform nextButton = currentPanel.transform.Find("NextButton");
            if (nextButton != null) nextButton.gameObject.SetActive(false);
            
            // Hide Previous button
            Transform prevButton = currentPanel.transform.Find("PrevButton");
            if (prevButton != null) prevButton.gameObject.SetActive(false);
            
            // Hide Skip button
            Transform skipButton = currentPanel.transform.Find("SkipButton");
            if (skipButton != null) skipButton.gameObject.SetActive(false);
            
            // Optionally keep the close button if you want users to be able to exit
            Transform closeButton = currentPanel.transform.Find("CloseButton");
            if (closeButton != null)
            {
                Button close = closeButton.GetComponent<Button>();
                if (close != null)
                {
                    close.onClick.RemoveAllListeners();
                    close.onClick.AddListener(CloseInstructions);
                }
            }
        }
        


        public void NextStep()
        {
            // Hide the current step's object if it exists
            if (currentStepIndex < instructionSteps.Count && instructionSteps[currentStepIndex].stepObject != null)
            {
                instructionSteps[currentStepIndex].stepObject.SetActive(false);
            }
            
            // Move to next step
            currentStepIndex++;
            
            // If we reached the end, close instructions
            if (currentStepIndex >= instructionSteps.Count)
            {
                CloseInstructions();
                return;
            }
            
            // Show the next step
            ShowCurrentStep();

    
            if (rightXRController != null)
            {
                rightXRController.SendHapticImpulse(0.5f, 0.1f);
            }
        }

 

        public void PreviousStep()
        {
            // Reset timer on manual navigation
            currentStepTimer = 0f;
            
            // Hide the current step's object if it exists
            if (currentStepIndex < instructionSteps.Count && instructionSteps[currentStepIndex].stepObject != null)
            {
                instructionSteps[currentStepIndex].stepObject.SetActive(false);
            }
            
            // Move to previous step
            if (currentStepIndex > 0)
            {
                currentStepIndex--;
                ShowCurrentStep();

                // Add haptic feedback for left controller when going to previous step
                if (leftXRController != null)
                {
                    leftXRController.SendHapticImpulse(0.5f, 0.1f);
                }
            }
        }
        
    
        public void CloseInstructions()
        {
            // Hide the current step's object if it exists
            if (currentStepIndex < instructionSteps.Count && instructionSteps[currentStepIndex].stepObject != null)
            {
                instructionSteps[currentStepIndex].stepObject.SetActive(false);
            }
            
            // Destroy panel
            if (currentPanel != null)
            {
                Destroy(currentPanel);
                currentPanel = null;
            }
            
            onboardingActive = false;
        }
        


        private InstructionStep[] GetDefaultInstructions()
        {
            return new InstructionStep[]
            {
                new InstructionStep
                {
                    title = "Welcome to Voice2Action",
                    content = "Voice2Action lets you control VR objects with natural language commands.\n\nThis tutorial will guide you through the basics.",
                    customDuration = 4f // Show this welcome screen a bit longer
                },
                
                new InstructionStep
                {
                    title = "Voice Command Activation",
                    content = "Hold the B button on your right controller while speaking.\n\nRelease the button when you're done speaking.\n\nThe system will process your command automatically.",
                    customDuration = 4f
                },
                
                new InstructionStep
                {
                    title = "Selection Commands",
                    content = "Try these selection commands:\n\n• \"Select buildings on my left\"\n• \"Get the red cars\"\n• \"Find objects within 5 meters in front of me\"\n• \"Select the largest building\"",
                    customDuration = 5f // Give more time to read examples
                },
                
                new InstructionStep
                {
                    title = "The Expand Panel",
                    content = "Selected objects appear in the Expand Panel.\n\nHover over an object to see information and a ray pointing to its location.",
                    customDuration = 5f
                },
                
                new InstructionStep
                {
                    title = "Modification Commands",
                    content = "After selecting objects, try:\n\n• \"Make them bigger by 20%\"\n• \"Move them closer by a factor of 0.5\"\n• \"Rotate by 45 degrees clockwise\"",
                    customDuration = 5f
                },
                
                new InstructionStep
                {
                    title = "Ready to Start",
                    content = "You're now ready to use Voice2Action!\n\nHold B while speaking, release when done.\n\nEnjoy using voice commands!",
                    customDuration = 4f
                }
            };
        }
    }
}