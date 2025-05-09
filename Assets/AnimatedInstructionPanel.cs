using UnityEngine;
using UnityEngine.UI;

namespace Voice2Action
{
    /// <summary>
    /// Extension to the instruction panel that supports animated sprites
    /// </summary>
    public class AnimatedInstructionPanel : MonoBehaviour
    {
        [Header("Animation Support")]
        [SerializeField] private GameObject animatedImagePrefab; // prefab with Animator
        [SerializeField] private Transform imageContainer;
        
        private GameObject currentAnimatedImage;
        
        /// Sets up an animated image for the instruction step
        /// <param name="animatorController">The Animator Controller with your animation</param>
        public void SetupAnimatedImage(RuntimeAnimatorController animatorController)
        {
            // Remove any existing image
            if (currentAnimatedImage != null)
            {
                Destroy(currentAnimatedImage);
            }
            
            // create new animated image
            if (animatedImagePrefab != null && imageContainer != null)
            {
                currentAnimatedImage = Instantiate(animatedImagePrefab, imageContainer);
                
                // set up the animator
                Animator animator = currentAnimatedImage.GetComponent<Animator>();
                if (animator != null && animatorController != null)
                {
                    animator.runtimeAnimatorController = animatorController;
                }
            }
        }
        
        public void HideAnimatedImage()
        {
            if (currentAnimatedImage != null)
            {
                currentAnimatedImage.SetActive(false);
            }
        }
        
        public void ShowAnimatedImage()
        {
            if (currentAnimatedImage != null)
            {
                currentAnimatedImage.SetActive(true);
            }
        }
    }
}