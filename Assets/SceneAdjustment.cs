using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI.Chat;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Networking;
using Utilities.Async;
using Voice2Action;
using UnityEditor;

public class SceneAdjustment : MonoBehaviour
{
    public string APIKey;

    public Camera GameCamera;

    private bool _isCalled = false;

    private VoiceIntentController voiceIntentController;
    private const string PARENT_OBJECT_TAG = "ParentObject";

    private Embeddings embeddings;

    [SerializeField] private GameObject leftController;
    [SerializeField] private GameObject rightController;

    [SerializeField] public GetShapeObjects objectsSeen;
    
    public class GetShapeObjects
    {
        public List<GetShapeObject> objects { get; set; }
    }

    public class GetShapeObject
    {
        public int GameObjectID { get; set; }
        public string Name { get; set; }
        public Transform TransformRelativeToOrigin { get; set; }
    }

    //new_add
    public class LabeledObject
    {
        public int GameObjectID { get; set; }
        public string Name { get; set; }
    }

    public class LabeledObjectRoot
    {
        public List<LabeledObject> root { get; set; }
    }



    // Start is called before the first frame update
    async void Start()
    {
        await DeleteEmbeddingFileAsync();
        objectsSeen = new GetShapeObjects
        {
            objects = new List<GetShapeObject>()
        };
        voiceIntentController = FindObjectOfType<VoiceIntentController>();
        if (voiceIntentController == null)
        {
            Debug.LogError("VoiceIntentController not found in the scene!");
        }


        CreateTagIfNotExists(PARENT_OBJECT_TAG);

        embeddings = FindObjectOfType<Embeddings>();
        if (embeddings == null)
        {
            Debug.LogError("Embeddings component not found in scene.");
        }
        RunThis();
    }
    
    private async Task DeleteEmbeddingFileAsync()
    {
        string m_EmbeddingDataDir = "";
        string propertyMapName = "shapeMap";
        DirectoryInfo gameRootDir = Directory.GetParent(Application.dataPath);
        string fileDir = Path.Combine(gameRootDir.FullName, "Embeddings", m_EmbeddingDataDir);
        if (!Directory.Exists(fileDir))
        {
            Directory.CreateDirectory(fileDir);
        }
        // delete embedding file if it exists
        string fileName = $"StarterScene/model={Utils.k_EmbeddingModel}-dim={Utils.k_EmbeddingDim}-property={propertyMapName}.txt";
        Debug.Log($"Looking for embedding file: {fileDir}/{fileName}");
        string filePath = Path.Combine(fileDir, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Debug.Log($"Deleted embedding file: {filePath}");
        }
    }
    

    //looking for parenttag and dynamically creating "parenttag" at runtime if not found
    private void CreateTagIfNotExists(string tag)
    {
#if UNITY_EDITOR
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        bool found = false;
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
            if (t.stringValue.Equals(tag))
            {
                found = true;
                break;
            }
        }

        // ff not found, add it
        if (!found)
        {
            tagsProp.arraySize++;
            SerializedProperty sp = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
            sp.stringValue = tag;
            tagManager.ApplyModifiedProperties();
            Debug.Log($"Created new tag: {tag}");
        }
#endif
    }

    // Update is called once per frame
    void Update()
    {
        // if (Time.time >= 3f && !_isCalled)
        // {
        //     _isCalled = true;
        //     RunThis();
        // }
    }

    private void RunThis()
    {

        StartCoroutine(WaitForOpenAIClient());  // ✅ This actually starts the loop and continues to RunAfterOpenAIReady

    }


    class OpenAIResponse
    {
        public Junk root;
    }

    class Junk
    {
        public ParentDescriptor[] root;
    }

    class ParentDescriptor
    {
        public string ParentName;
        public string[] ChildrenGameObjectIDs;
    }

    private void SetVisible(GameObject obj, bool visible)
    {
        if (obj == null) return;
        foreach (var renderer in obj.GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = visible;
        }
    }

    private async Task<object> TakeScreenShotsOfAllInteractableObjects()
    {
        Vector3 cameraPosition = GameCamera.transform.position;
        var parentObjects = GameObject.FindGameObjectsWithTag(PARENT_OBJECT_TAG);
        Debug.Log("Number of parent objects: " + parentObjects.Length);

        // List to hold all tasks
        List<Task> completionTasks = new List<Task>();

        foreach (var parentObject in parentObjects)
        {
            foreach (var o in parentObjects) o.SetActive(false);
            parentObject.SetActive(true);

            // Set the camera to look at the object
            GameCamera.transform.position = parentObject.transform.position + new Vector3(1, 1, 0);
            GameCamera.transform.LookAt(parentObject.transform.position);

            // Move the camera back incrementally until the object is fully visible
            while (!IsObjectFullyVisible(GameCamera, parentObject))
            {
                GameCamera.transform.position -= GameCamera.transform.forward * 0.5f;
            }

            List<string> photos = new List<string>();
            string base64Image = CaptureScreenshot(GameCamera, "photo1"  + parentObject.name); 
            photos.Add(base64Image);

            GameCamera.transform.position = parentObject.transform.position + new Vector3(1, 1, 1);
            GameCamera.transform.LookAt(parentObject.transform.position);

            while (!IsObjectFullyVisible(GameCamera, parentObject))
            {
                GameCamera.transform.position -= GameCamera.transform.forward * 0.5f;
            }

            string base64ImageTwo = CaptureScreenshot(GameCamera, "photo2" + parentObject.name); 
            photos.Add(base64ImageTwo);

            List<string> prefabRoots = new List<string>();
            Vector3 relativePosition = GameCamera.transform.InverseTransformPoint(parentObject.transform.position);
            Quaternion relativeRotation = Quaternion.Inverse(GameCamera.transform.rotation) * parentObject.transform.rotation;
            prefabRoots.Add(JsonConvert.SerializeObject(new
            {
                GameObjectID = parentObject.gameObject.GetInstanceID(),
                Transform = new
                {
                    Position = new { x = relativePosition.x, y = relativePosition.y, z = relativePosition.z },
                    Rotation = new
                    {
                        x = relativeRotation.x,
                        y = relativeRotation.y,
                        z = relativeRotation.z,
                        w = relativeRotation.w
                    }
                }
            }));

            // Start the task but do not await it yet
            var task = CallCompletionImage(photos, JsonConvert.SerializeObject(prefabRoots), parentObject);
            completionTasks.Add(task);
        }

        // Restore camera position
        GameCamera.transform.position = cameraPosition;
        foreach (var o in parentObjects) o.SetActive(true);

        // Await all tasks to complete
        await Task.WhenAll(completionTasks);
        
        return "";
    }

    
    private bool IsObjectFullyVisible(Camera camera, GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer == null) return false;

        // Get the object's bounds
        Bounds bounds = renderer.bounds;

        // Check if all corners of the bounds are within the camera's view
        Vector3[] corners = new Vector3[8];
        corners[0] = bounds.min;
        corners[1] = bounds.max;
        corners[2] = new Vector3(bounds.min.x, bounds.min.y, bounds.max.z);
        corners[3] = new Vector3(bounds.min.x, bounds.max.y, bounds.min.z);
        corners[4] = new Vector3(bounds.max.x, bounds.min.y, bounds.min.z);
        corners[5] = new Vector3(bounds.min.x, bounds.max.y, bounds.max.z);
        corners[6] = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
        corners[7] = new Vector3(bounds.max.x, bounds.max.y, bounds.min.z);

        foreach (var corner in corners)
        {
            Vector3 viewportPoint = camera.WorldToViewportPoint(corner);
            if (viewportPoint.x < 0 || viewportPoint.x > 1 || viewportPoint.y < 0 || viewportPoint.y > 1 || viewportPoint.z < 0)
            {
                return false;
            }
        }

        return true;
    }


    string CaptureScreenshot(Camera cam, string path)
    {
        RenderTexture rt = new RenderTexture(Screen.width, Screen.height, 24);
        cam.targetTexture = rt;
        Texture2D screenShot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        cam.Render();
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        byte[] bytes = screenShot.EncodeToJPG();
        string filePath = "/Users/vinayakkannan/Desktop/" + path + "Screenshot.jpg";
        System.IO.File.WriteAllBytes(filePath, bytes);
        string base64Image = Convert.ToBase64String(bytes);
        return base64Image;
    }

    // Define the response classes with unique names 
    public class ChatResponse
    {
        [JsonProperty("choices")]
        public List<ChatChoice> Choices { get; set; }
    }

    public class ChatChoice
    {
        [JsonProperty("message")]
        public ChatMessage Message { get; set; }
    }

    public class ChatMessage
    {
        [JsonProperty("content")]
        public string Content { get; set; }
    }

    private System.Collections.IEnumerator WaitForOpenAIClient()
    {
        Debug.Log("🟡 Entered WaitForOpenAIClient()");

        while (!Utils.EnsureOpenAIClient())
        {
            Debug.Log("⏳ Waiting for OpenAIConfiguration to load...");
            yield return new WaitForSeconds(0.5f);
        }

        Debug.Log("🚀 OpenAIClient is ready — running scene setup.");
        RunAfterOpenAIReady();
    }


    private async void RunAfterOpenAIReady()
    {
        Debug.Log("🏁 Starting dynamic tagging...");

        await TakeScreenShotsOfAllInteractableObjects();
        var parentObjects = GameObject.FindGameObjectsWithTag(PARENT_OBJECT_TAG);
        foreach (var parentObject in parentObjects)
        {
            objectsSeen.objects.Add(new GetShapeObject
            {
                GameObjectID = parentObject.gameObject.GetInstanceID(),
                Name = parentObject.name,
                TransformRelativeToOrigin = parentObject.transform
            });
        }
        // Save objectsSeen as a JSON file
        string jsonOutput = JsonConvert.SerializeObject(objectsSeen, Formatting.Indented);
        string outputPath = Path.Combine(Application.dataPath, "objectsSeen.json");
        File.WriteAllText(outputPath, jsonOutput);
        Debug.Log($"Saved objectsSeen to: {outputPath}");
        try
        {
            Debug.Log("Scene labeling task completed successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError("Error processing response: " + ex.Message);
        }
    }




    public async Task<object> CallCompletionImage(List<string> photos, string gameObjectsText, GameObject gameObject)
    {

        SetVisible(leftController, false);
        SetVisible(rightController, false);
        
        
        SetVisible(leftController, true);
        SetVisible(rightController, true);

        string url = "https://api.openai.com/v1/chat/completions";

        var requestData = new
        {
            model = "gpt-4.1",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = @"You are analyzing a multiple images of a 3D scene of the same object, along with its 3D transforms.

                            Your task is to assign a short, category-style name to the object.

                            Only name the objects listed below. Do not name any objects you see in the image that are not in the list.

                            Use descriptive sentences to describe each object. Try to describe each object with at least 2 sentences

                            Return a list like this:
                            [
                              { ""GameObjectID"": 12345, ""Name"": ""green cube sitting on a yellow table"" },
                            ]

                            Here are the objects:"
                            + gameObjectsText
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = "data:image/jpeg;base64," + photos[0]
                            }
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = "data:image/jpeg;base64," + photos[1]
                            }
                        }
                    }
                }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "object_labeling_response",
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            root = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        GameObjectID = new { type = "integer" },
                                        Name = new { type = "string" }
                                    },
                                    required = new[] { "GameObjectID", "Name" },
                                    additionalProperties = false
                                }
                            }
                        },
                        required = new[] { "root" },
                        additionalProperties = false
                    },
                    strict = true
                }
            }

        };

        string jsonData = JsonConvert.SerializeObject(requestData); //gets rid of the 400 ba request errors
        Debug.Log("Request JSON: " + jsonData);

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + APIKey);

        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Response: " + request.downloadHandler.text);
            ChatResponse jsonResponse = JsonConvert.DeserializeObject<ChatResponse>(request.downloadHandler.text);
            if (jsonResponse != null && jsonResponse.Choices != null && jsonResponse.Choices.Count > 0)
            {
                string response = jsonResponse.Choices[0].Message.Content;
                
                    LabeledObjectRoot parsed = JsonConvert.DeserializeObject<LabeledObjectRoot>(response);
                    List<LabeledObject> labeledObjects = parsed.root;
                    HashSet<string> newShapes = new HashSet<string>();

                    foreach (var labeled in labeledObjects)
                    {
                        // Convert '.' in labeled.Name to ';' to avoid issues with embedding.cs
                        labeled.Name = labeled.Name.Replace('.', ';');

                            // Create ShapeController if it doesn't exist
                            ShapeController controller = gameObject.GetComponent<ShapeController>();
                            if (controller == null)
                            {
                                controller = gameObject.AddComponent<ShapeController>();
                            }
                            controller.shape = labeled.Name;
                            newShapes.Add(labeled.Name);
                            gameObject.gameObject.name = labeled.Name;
                            gameObject.GetComponent<ShapeController>().InitShape();
                            Debug.Log($"Assigned shape '{labeled.Name}' to object ID {labeled.GameObjectID}");
                    }
                    
                    // embeddings.AddToShapeMap(newShapes);
                    // await embeddings.ProcessEmbeddingData(embeddings.shapeMap, "shapeMap");
                    // embeddings.PrintShapeMapKeys();  // ✅ don't forget the ()

                    //foreach (var shape in newShapes)
                    //{
                    //    if (!embeddings.shapeMap.ContainsKey(shape))
                    //    {
                    //        embeddings.shapeMap[shape] = shape;
                    //    }
                    //}

                    //await embeddings.ProcessEmbeddingData(embeddings.shapeMap, "shapeMap");

                    return response;
                // catch (Exception e)
                // {
                //     Debug.LogError("Error parsing labeled object response: " + e.Message);
                //     return "Error parsing response";
                // }

            }
            else
            {
                Debug.LogError("Error parsing JSON response");
                return "Error parsing response";
            }
        }

        else
        {
            Debug.LogError("Error: " + request.error);
            Debug.LogError("Response: " + request.downloadHandler.text);
            return "Error: " + request.error;
        }
    }

    
}