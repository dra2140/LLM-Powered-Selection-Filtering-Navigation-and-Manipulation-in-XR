using UnityEngine;
using Voice2Action;

public class SelectionOverrideManager : MonoBehaviour
{
    private SceneManager sceneManager;

    private void Start()
    {
        sceneManager = FindObjectOfType<SceneManager>();
        if (sceneManager != null)
        {
            DisableExpandPanel();
        }
    }

    private void Update()
    {
        if (sceneManager != null && sceneManager.expandPanel != null && sceneManager.expandPanel.activeSelf)
        {
            DisableExpandPanel();
        }
    }

    private void DisableExpandPanel()
    {
        if (sceneManager.expandPanel != null)
        {
            sceneManager.expandPanel.SetActive(false);
        }
    }
} 