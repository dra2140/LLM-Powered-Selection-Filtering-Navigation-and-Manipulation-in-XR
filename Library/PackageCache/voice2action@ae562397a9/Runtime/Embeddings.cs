using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Networking;


namespace Voice2Action
{
    /// <summary>
    /// Storage class for all property mappings and initialization of all required components of Voice2Action (except function calling). <br/>
    /// Fields in this class should be updated as soon as the scene starts, and they are used when function calling with similarity match is attached. <br/>
    /// <br/>Details: <br/>
    /// When the system recognizes that the user's instruction (1) contains one of the mappings declared here,
    /// it matches the closest key in that dictionary, which maps to the actual type/instance in the game engine. <br/>
    /// Users can add their own property mappings by inheriting this class, see MyEmbeddings class for details as provided in the CityDemo scripts. <br/>
    /// For (1) to work, functions need to be attached with a custom PropertyMethodAttribute with the name of the property mappings, so that the system can
    /// recognize the user's instruction by fetching this attribute during reflection, see readme for example usage. <br/>
    /// </summary>
    /// <remarks>This class is expected to be actual embedding vector matches in future package version, right now it is an under-optimized version by explicit rankings.</remarks>
    public class Embeddings: MonoBehaviour
    {
        /// <value>User specified directory name to store embedding data of the current Unity Scene.</value>
        public string m_EmbeddingDataDir = "";
        
        private Dictionary<string, Dictionary<string, IReadOnlyList<double>>> m_EmbeddingMap = new();
        
        private Dictionary<string, object> m_ShapeMap = new()
        {
            {k_DefaultShape, k_DefaultShape}, // default shape - it represents any object
        };
        
        /// <summary>
        /// Stores object types, used for reflection in PropertyExecutor atomic functions. <br/>
        /// When the scene starts, all object types under the "Parent Interactable" hierarchy of the VoiceIntentController class should be loaded. <br/>
        /// e.g. User: "make the car bigger" -> "car" object type is matched. <br/>
        /// </summary>
        /// <value>Object type property mapping.</value>
        /// <remarks>
        /// This functionality is expected to be implemented with vision model, so the system can recognize object types by their visual appearance. <br/>
        /// We will add that in future package version.
        /// </remarks>
        public Dictionary<string, object> shapeMap
        {
            get => m_ShapeMap;
            set => m_ShapeMap = value;
        }
        
        /// <value>The default object type.</value>
        public const string k_DefaultShape = "object";

        /// <summary>
        /// [Deprecated] Initialize all embedding data of the given property functions.
        /// </summary>
        /// <param name="myShapeControllerType">Type of user-defined ShapeController, used to check existence of atomic property functions.</param>
        /// <param name="propertyFunctionNames">Names of property functions.</param>
        /// <returns>None.</returns>
        public async Task InitEmbeddings(Type myShapeControllerType, List<string> propertyFunctionNames)
        {
            foreach (var functionName in propertyFunctionNames)
            {
                // the atomic function must exist
                var methodInfo = myShapeControllerType.GetMethod(functionName);
                if (methodInfo == null)
                {
                    Debug.LogWarning($"propertyFunction with name {functionName} does not exist, skipped");
                    continue;
                }
                // skip methods without property method attribute to denote "embedding match"
                var methodAttribute = methodInfo.GetCustomAttribute<ShapeController.PropertyMethodAttribute>();
                if (methodAttribute == null) continue;
                // the attribute must point to the storage field of the embedding
                var propertyName = methodAttribute.property;
                var fieldInfo = GetType().GetProperty(propertyName);
                if (fieldInfo == null)
                {
                    Debug.LogWarning($"propertyFunction with name {functionName} does not have corresponding fieldInfo to access embeddings, get {propertyName}");
                    continue;
                }
                var fieldValue = fieldInfo.GetValue(this);
                // this should never happen because Embedding class is accessed right before this line
                if (fieldValue == null)
                {
                    Debug.LogWarning($"fieldValue with name {propertyName} is not field member of the given embedding");
                    continue;
                }
                // the embedding storage must be the following type for auto casting
                Dictionary<string, object> fieldDict = fieldValue as Dictionary<string, object>;
                if (fieldDict == null)
                {
                    Debug.LogWarning($"fieldValue with name {fieldInfo.Name} must be of type {typeof(Dictionary<string, object>)} for dynamic casting");
                    continue;
                }
                await ProcessEmbeddingData(fieldDict, propertyName);
            }
        }

        /// <summary>
        /// Loads and saves embedding data for the given propertyMap and its name.
        /// </summary>
        /// <param name="propertyMap">A propertyMap instance, i.e. shapeMap.</param>
        /// <param name="propertyMapName">The string name of the propertyMap, i.e. "shapeMap".</param>
        /// <returns>Whether the process succeeds.</returns>
        public async Task<bool> ProcessEmbeddingData(Dictionary<string, object> propertyMap, string propertyMapName)
        {
            if (m_EmbeddingMap.ContainsKey(propertyMapName)) return true;
            // create embedding dir path if it does not exist
            DirectoryInfo gameRootDir = Directory.GetParent(Application.persistentDataPath);
            if (gameRootDir == null)
            {
                Debug.LogWarning($"gameRootDir does not exist given Assets folder is {Application.dataPath}, exiting");
                return false;
            }
            string fileDir = Path.Combine(gameRootDir.FullName, "Embeddings", m_EmbeddingDataDir);
            if (!Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }
            // create embedding file path if it does not exist
            string fileName = $"model={Utils.k_EmbeddingModel}-dim={Utils.k_EmbeddingDim}-property={propertyMapName}.txt";
            string filePath = Path.Combine(fileDir, fileName);
            if (!File.Exists(filePath))
            {
                await using (File.Create(filePath)) {}
            }
            Dictionary<string, IReadOnlyList<double>> propertyMapData = new Dictionary<string, IReadOnlyList<double>>();
            // load existing file content to propertyMapData
            foreach (var line in File.ReadLines(filePath))
            {
                // each line of the file is expected to be formatted as
                // {property_name},{embed_dim_1},{embed_dim_2},...,{embed_dim_{Utils.k_EmbeddingDim - 1}}
                var parts = line.Split(',');
                if (parts.Length != Utils.k_EmbeddingDim + 1)
                {
                    continue;
                }
                Assert.AreEqual(parts.Length, Utils.k_EmbeddingDim + 1);
                var propertyName = parts[0];
                List<double> propertyEmbedding = new List<double>();
                for (int i = 1; i < parts.Length; i++)
                {
                    propertyEmbedding.Add(double.Parse(parts[i]));
                }
                propertyMapData[propertyName] = propertyEmbedding;
            }
            // save remaining content of propertyMap to both propertyMapData and local file for future reuse
            int debugCount = 0;
            const int maxRetries = 5;
            const int delayBetweenRetriesMs = 100;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    await using (var writer = new StreamWriter(filePath, true))
                    {
                        // Your existing logic for writing to the file
                        foreach (var propertyName in propertyMap.Keys.ToList())
                        {
                            if (propertyMapData.ContainsKey(propertyName)) continue;
                            IReadOnlyList<double> propertyEmbedding = await VoiceIntentController.CallEmbedding(propertyName);
                            propertyMapData[propertyName] = propertyEmbedding;
                            string propertyData = propertyName;
                            for (int i = 0; i < Utils.k_EmbeddingDim; i++)
                            {
                                propertyData += "," + propertyEmbedding[i].ToString("G");
                            }
                            await writer.WriteLineAsync(propertyData);
                            debugCount += 1;
                        }
                    }
                    break; // Exit the retry loop if successful
                }
                catch (IOException ex) when (retry < maxRetries - 1)
                {
                    Debug.LogWarning($"IOException encountered: {ex.Message}. Retrying in {delayBetweenRetriesMs}ms...");
                    await Task.Delay(delayBetweenRetriesMs);
                }
            }
            m_EmbeddingMap[propertyMapName] = propertyMapData;
            Debug.Log($"{propertyMapName} propertyMap is processed with {debugCount} embedding calls");
            return true;
        }
        
        /// <summary>
        /// Utility function to get cos similarity of two vectors.
        /// </summary>
        /// <param name="vectorA">Vector A</param>
        /// <param name="vectorB">Vector B</param>
        /// <returns>Cos similarity score, in the range of [0, 1].</returns>
        private double GetCosSimilarity(IReadOnlyList<double> vectorA, IReadOnlyList<double> vectorB)
        {
            double dotProduct = 0;
            double normA = 0;
            double normB = 0;
            for (int i = 0; i < vectorA.Count; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
                normA += Math.Pow(vectorA[i], 2);
                normB += Math.Pow(vectorB[i], 2);
            }
            return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }
        
        public class GetShapeObjects
        {
            public List<GetShapeObject> objects { get; set; }
        }

        public class GetShapeObject
        {
            public int GameObjectID { get; set; }
            public string Name { get; set; }
            public Vector3 Scale { get; set; }
            public Vector3 RelativePosition { get; set; }
            public Quaternion RelativeRotation { get; set; }        }
        
        public static void UpdateGetShapeObjectsTransforms(GetShapeObjects targetObjects)
        {
            var xrOrigin = FindObjectOfType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogWarning("XROrigin not found in the scene.");
                return;
            }

            foreach (var obj in targetObjects.objects)
            {
                GameObject targetObject = GameObject.Find(obj.Name);
                if (targetObject != null)
                {
                    obj.RelativePosition = xrOrigin.transform.InverseTransformPoint(targetObject.transform.position);
                    obj.RelativeRotation = Quaternion.Inverse(xrOrigin.transform.rotation) * targetObject.transform.rotation;
                    obj.Scale = targetObject.transform.localScale;
                }
                else
                {
                    Debug.LogWarning($"GameObject with  ID {obj.GameObjectID} not found.");
                }
            }
        }
        
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
        
        public class LabeledObject
        {
            public int GameObjectID { get; set; }
        }

        public class LabeledObjectRoot
        {
            public List<LabeledObject> root { get; set; }
        }

        public static string GetObjectsStringFromJSON()
        {
            string filePath = Path.Combine(Application.persistentDataPath, "objectsSeen.json");
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"JSON file at {filePath} does not exist");
                return "";
            }

            string jsonContent = File.ReadAllText(filePath);
            GetShapeObjects targetObjects = JsonConvert.DeserializeObject<GetShapeObjects>(jsonContent);
            UpdateGetShapeObjectsTransforms(targetObjects);
            // Convert the objects to a string format
            string[] gameObjectsText = targetObjects.objects.Select(obj =>
                $"{{ \"GameObjectID\": {obj.GameObjectID}, \"Name\": \"{obj.Name}\", \"RelativePosition to user\": [{obj.RelativePosition.x}, {obj.RelativePosition.y}, {obj.RelativePosition.z}], \"RelativeRotation to user\": [{obj.RelativeRotation.x}, {obj.RelativeRotation.y}, {obj.RelativeRotation.z}, {obj.RelativeRotation.w}] }}").ToArray();
            string gameObjectsTextString = string.Join(", ", gameObjectsText);
            return gameObjectsTextString;
        }


        public async Task<string[]> GetClosestShapes(string userInput, List<string> historyMessages)
        {
            string filePath = Path.Combine(Application.persistentDataPath, "objectsSeen.json");
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"JSON file at {filePath} does not exist");
                return Array.Empty<string>();
            }

            string jsonContent = File.ReadAllText(filePath);
            GetShapeObjects targetObjects = JsonConvert.DeserializeObject<GetShapeObjects>(jsonContent);
            UpdateGetShapeObjectsTransforms(targetObjects);
            // Convert the objects to a string format
            string[] gameObjectsText = targetObjects.objects.Select(obj =>
                $"{{ \"GameObjectID\": {obj.GameObjectID}, \"Name\": \"{obj.Name}\", \"RelativePosition to user\": [{obj.RelativePosition.x}, {obj.RelativePosition.y}, {obj.RelativePosition.z}], \"RelativeRotation to user\": [{obj.RelativeRotation.x}, {obj.RelativeRotation.y}, {obj.RelativeRotation.z}, {obj.RelativeRotation.w}] }}").ToArray();
            string gameObjectsTextString = string.Join(", ", gameObjectsText);
            Debug.Log("GameObjects: " + gameObjectsTextString);
            
            string url = "https://api.openai.com/v1/chat/completions";

            var requestData = new
            {
                model = "gpt-4.1-mini",
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
                                text = $"You are analyzing a userInput describing what they want to do in a Unity game world, along with a list of objects in a 3D scene and their 3D transforms. Your task is to review the userInput and determine the GameObjectIDs that are most closely related to what the user wants to do. For example, if the userInput is about selecting all buses that are nearby, return the GameObjectIDs of objects that are relatively close to the user and have names indicating they are buses, based on their relative positions and transforms. \n\nThe userInput is: {userInput}. \n\nFor context, here are the previous messages between the user and the agent: {historyMessages}.\n\n Here are the objects in the game world: {gameObjectsTextString}"
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
                                            GameObjectID = new { type = "integer" }
                                        },
                                        required = new[] { "GameObjectID" },
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
            request.SetRequestHeader("Authorization", "Bearer " + Resources.Load<OpenAIConfiguration>("OpenAIConfiguration").ApiKey);


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
                    string[] gameObjectIDs = labeledObjects.Select(obj => obj.GameObjectID.ToString()).ToArray();
                    string[] matchingNames = targetObjects.objects
                        .Where(obj => gameObjectIDs.Contains(obj.GameObjectID.ToString()))
                        .Select(obj => obj.Name)
                        .ToArray();
                    Debug.Log("Matching GameObject Names: " + string.Join(", ", matchingNames));
                    return matchingNames;
                }
            }
            Debug.LogError("Error: " + request.error);
            return Array.Empty<string>();
        }

        /// <summary>
        /// Get closest embedding match by retrieval.
        /// </summary>
        /// <param name="userInput">Input user message.</param>
        /// <param name="propertyMapName">The string name of the propertyMap, i.e. "shapeMap".</param>
        /// <returns>A tuple of (one key from the given property map, its corresponding cos-similarity score to userInput).</returns>
        public async Task<(string, double)> GetEmbedding(string userInput, string propertyMapName)
        {
            string retName = Utils.k_FailureResponse;
            double maxSimilarity = double.MinValue;
            // fetch target embeddings
            if (!m_EmbeddingMap.TryGetValue(propertyMapName,
                    out Dictionary<string, IReadOnlyList<double>> targetVectors))
            {
                Debug.LogWarning($"propertyMap with name {propertyMapName} does not exist");
                return (retName, maxSimilarity);
            }
            // fetch query embedding
            Debug.Log("Get Embedding Query: " + userInput);
            IReadOnlyList<double> queryVector = await VoiceIntentController.CallEmbedding(userInput);
            foreach (var (targetName, targetVector) in targetVectors)
            {
                if (targetName == "object")
                {
                    // skip the default object type
                    continue;
                }
                Debug.Log("Get Embedding Target: " + targetName);
                double similarity = GetCosSimilarity(queryVector, targetVector);
                Debug.Log($"Get Embedding Cosine Similarity: {similarity}");
                if (similarity > maxSimilarity)
                {
                    maxSimilarity = similarity;
                    retName = targetName;
                }
            }
            return (retName, maxSimilarity);
        }

        /// <summary>
        /// Get closest k embedding matches by retrieval in descending order.
        /// </summary>
        /// <param name="userInput">Input user message.</param>
        /// <param name="propertyMapName">The string name of the propertyMap, i.e. "shapeMap".</param>
        /// <param name="topK">The closest k embeddings.</param>
        /// <returns>A tuple of (one key from the given property map, its corresponding cos-similarity score to userInput).</returns>
        public async Task<List<(string, double)>> GetTopKEmbedding(string userInput, string propertyMapName, int topK)
        {
            // fetch target embeddings
            if (!m_EmbeddingMap.TryGetValue(propertyMapName,
                    out Dictionary<string, IReadOnlyList<double>> targetVectors))
            {
                Debug.LogWarning($"propertyMap with name {propertyMapName} does not exist");
                return null;
            }
            // fetch query embedding
            IReadOnlyList<double> queryVector = await VoiceIntentController.CallEmbedding(userInput);
            List<(string, double)> ret = new List<(string, double)>();
            foreach (var (targetName, targetVector) in targetVectors)
            {
                double similarity = GetCosSimilarity(queryVector, targetVector);
                ret.Add((targetName, similarity));
            }
            ret = ret.OrderByDescending(tuple => tuple.Item2).Take(topK).ToList();
            return ret;
        }
        
        /// <summary>
        /// Initializes the components of the given instance for ExpandPanel to expand them, and their other default components.
        /// </summary>
        /// <param name="instance">Game object that might be expanded in the future.</param>
        /// <param name="shapeType">Object type of the given instance.</param>
        /// <param name="myShapeControllerType">Type of user-defined ShapeController.</param>
        private void InitInstance(GameObject instance, string shapeType, Type myShapeControllerType)
        {
            var xrGrabInteractable = instance.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            var interactableTarget = instance.AddComponent<InteractableTarget>();
            interactableTarget.isProxy = false;
            xrGrabInteractable.useDynamicAttach = true;
            xrGrabInteractable.throwOnDetach = false;
            instance.GetComponent<Rigidbody>().useGravity = false;
            instance.GetComponent<Rigidbody>().isKinematic = true;
            var shapeController = (ShapeController) instance.AddComponent(myShapeControllerType);
            shapeController.shape = shapeType;
            shapeController.InitShape();
            shapeController.InitMyShape();
        }
        
        /// <summary>
        /// The entry point for initializing all property LLMs (large language models). <br/>
        /// This class is intentionally left blank so user can override it, see CityDemo.Scripts.MyEmbeddings for its example usage. <br/>
        /// </summary>
        /// <param name="classifier">The "LLM for Classification".</param>
        /// <param name="extractor">The "LLM for Extraction".</param>
        /// <param name="executor">The "LLM for Execution".</param>
        public virtual void InitProperty(PropertyClassifier classifier, PropertyExtractor extractor, PropertyExecutor executor) {}
        
        /// <summary>
        /// Initializes the default components of the given parent interactable in the scene. <br/>
        /// By default, this class initialize all object types under the given parent interactable by checking each children's object name. <br/>
        /// Hence, the default behavior is that each children object is expected to contain all objects with the same type (shape). <br/>
        /// User can choose to not use this, however, by doing it differently in InitMyInteractable(...). <br/>
        /// </summary>
        /// <param name="parentInteractable">Default game object that holds all interactable targets.</param>
        /// <param name="myShapeControllerType">Type of user-defined ShapeController.</param>
        public void InitInteractable(GameObject parentInteractable, Type myShapeControllerType)
        {
            foreach (Transform obj in parentInteractable.transform)
            {
                //var shape = obj.gameObject.name;
                //shapeMap[shape] = shape;
                //foreach (Transform instance in category)
                InitInstance(obj.gameObject, "placeholder", myShapeControllerType);
            }
        }
        
        /// <summary>
        /// Initializes user-defined components of the given parent interactable in the scene.<br/>
        /// The function is intentionally left blank so the user can override it, see CityDemo.Scripts.MyEmbeddings for its example usage. <br/>
        /// </summary>
        /// <param name="defaultParentInteractable">Default game object that holds all interactable targets.</param>
        /// <param name="myParentInteractable">Optional (can be null), user-defined game object that holds all interactable targets.</param>
        /// <param name="myShapeControllerType">Type of user-defined ShapeController.</param>
        public virtual void InitMyInteractable(GameObject defaultParentInteractable, GameObject myParentInteractable, Type myShapeControllerType) {}

        public void AddToShapeMap(IEnumerable<string> shapeNames)
        {
            foreach (var shape in shapeNames)
            {
                if (!shapeMap.ContainsKey(shape))
                {
                    shapeMap[shape] = shape;
                }
            }
        }
        public void PrintShapeMapKeys()
        {
            Debug.Log("🔑 shapeMap keys: " + string.Join(", ", shapeMap.Keys));
        }


    }


}