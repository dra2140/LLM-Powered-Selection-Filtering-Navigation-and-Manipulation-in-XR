using UnityEngine;
using Voice2Action;
using System.Collections.Generic;

public class SelectionHighlightManager : MonoBehaviour
{
    [SerializeField]
    private Material outlineMaterial;  // Assign the OutlineMaterial in inspector
    [SerializeField]
    private Color outlineColor = Color.cyan;
    [SerializeField]
    private float outlineWidth = 0.005f;

    private VoiceIntentController voiceIntentController;
    private SceneManager sceneManager;
    private Dictionary<ShapeController, GameObject> outlineObjects = new Dictionary<ShapeController, GameObject>();

    private void Start()
    {
        // Get references to required components
        voiceIntentController = FindObjectOfType<VoiceIntentController>();
        sceneManager = FindObjectOfType<SceneManager>();

        // Subscribe to selection events
        if (sceneManager != null)
        {
            sceneManager.activateInteractable += OnSelectionChanged;
            sceneManager.destroyObjectNotGrabbed += OnSelectionChanged;
        }

        // Create outline objects for all shape controllers
        var allShapeControllers = FindObjectsOfType<ShapeController>();
        foreach (var controller in allShapeControllers)
        {
            if (!controller.interactableTarget.isProxy)
            {
                CreateOutlineObject(controller);
            }
        }
    }

    private void CreateOutlineObject(ShapeController controller)
    {
        if (outlineObjects.ContainsKey(controller))
            return;

        // Create a new GameObject to hold the outline mesh
        GameObject outlineObj = new GameObject($"{controller.gameObject.name}_Outline");
        outlineObj.transform.SetParent(controller.transform);
        outlineObj.transform.localPosition = Vector3.zero;
        outlineObj.transform.localRotation = Quaternion.identity;
        outlineObj.transform.localScale = Vector3.one * 1.05f; // Slightly larger scale for better outline visibility

        // Copy all mesh filters and mesh renderers from the original object
        var originalMeshFilters = controller.GetComponentsInChildren<MeshFilter>();
        foreach (var originalMeshFilter in originalMeshFilters)
        {
            var meshFilter = outlineObj.AddComponent<MeshFilter>();
            meshFilter.mesh = originalMeshFilter.mesh;

            var meshRenderer = outlineObj.AddComponent<MeshRenderer>();
            meshRenderer.material = new Material(outlineMaterial); // Create a unique instance
            meshRenderer.material.SetColor("_OutlineColor", outlineColor);
            meshRenderer.material.SetFloat("_OutlineWidth", outlineWidth);
            meshRenderer.enabled = false; // Start with outline disabled
        }

        outlineObjects[controller] = outlineObj;
    }

    private void OnSelectionChanged(Transform selectedObject)
    {
        // Disable all outlines first
        foreach (var kvp in outlineObjects)
        {
            var outlineObj = kvp.Value;
            if (outlineObj != null)
            {
                foreach (var renderer in outlineObj.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.enabled = false;
                }
            }
        }

        // If we have a selected object, enable its outline
        if (selectedObject != null)
        {
            var selectedController = selectedObject.GetComponent<ShapeController>();
            if (selectedController != null && outlineObjects.TryGetValue(selectedController, out GameObject outlineObj))
            {
                foreach (var renderer in outlineObj.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.enabled = true;
                }
            }
        }
    }

    private void Update()
    {
        // First, clean up any destroyed objects from our dictionary
        var keysToRemove = new List<ShapeController>();
        foreach (var kvp in outlineObjects)
        {
            if (kvp.Key == null || kvp.Value == null)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in keysToRemove)
        {
            if (outlineObjects[key] != null)
            {
                Destroy(outlineObjects[key]);
            }
            outlineObjects.Remove(key);
        }

        // Check for any new ShapeControllers that might have been added
        var allShapeControllers = FindObjectsOfType<ShapeController>();
        foreach (var controller in allShapeControllers)
        {
            if (!controller.interactableTarget.isProxy && !outlineObjects.ContainsKey(controller))
            {
                CreateOutlineObject(controller);
            }
        }
    }

    private void OnDestroy()
    {
        if (sceneManager != null)
        {
            sceneManager.activateInteractable -= OnSelectionChanged;
            sceneManager.destroyObjectNotGrabbed -= OnSelectionChanged;
        }

        // Clean up outline objects
        foreach (var outlineObj in outlineObjects.Values)
        {
            if (outlineObj != null)
            {
                Destroy(outlineObj);
            }
        }
        outlineObjects.Clear();
    }
} 