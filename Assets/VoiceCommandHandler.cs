using UnityEngine;
using Voice2Action;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Specialized;
using System;
using OpenAI;

public class VoiceCommandHandler : MonoBehaviour
{
    [SerializeField] private VoiceIntentController voiceIntentController;
    [SerializeField] private PropertyClassifier propertyClassifier;
    [SerializeField] private PropertyExtractor propertyExtractor;
    [SerializeField] private PropertyExecutor propertyExecutor;
    [SerializeField] private ShapeController[] shapeControllers;
    [SerializeField] private Embeddings embeddings;

    private Dictionary<string, Tool> toolDict;
    private Type shapeControllerType;
    private Type embeddingsType;
    private List<string> historyMessages;

    private void Start()
    {
        // Ensure we have all required components
        if (voiceIntentController == null)
            voiceIntentController = GetComponent<VoiceIntentController>();
        if (propertyClassifier == null)
            propertyClassifier = GetComponent<PropertyClassifier>();
        if (propertyExtractor == null)
            propertyExtractor = GetComponent<PropertyExtractor>();
        if (propertyExecutor == null)
            propertyExecutor = GetComponent<PropertyExecutor>();
        if (embeddings == null)
            embeddings = GetComponent<Embeddings>();

        shapeControllerType = typeof(ShapeController);
        embeddingsType = typeof(Embeddings);
        historyMessages = new List<string>();
        
        toolDict = propertyExecutor.InitFunctionCalls(shapeControllerType, new List<string>
        {
            "ModifyPositionX",
            "ModifyPositionY",
            "ModifyPositionZ",
            "ModifyScale",
            "ModifyRotationX",  
            "ModifyRotationY",
            "ModifyRotationZ"
        });
    }

    public async Task ProcessVoiceCommand(string command)
    {
        // First, classify the command
        var classifiedActions = await propertyClassifier.ClassifyProperty(command);
        
        // Process each action in order
        foreach (var action in classifiedActions)
        {
            string actionType = action.Key;
            string actionPhrase = action.Value;

            // Extract properties for this action
            var extractedProperties = await propertyExtractor.ExtractProperty(actionType, actionPhrase);
            
            // Create array of selected controllers (all true for now)
            bool[] selectedControllers = new bool[shapeControllers.Length];
            for (int i = 0; i < selectedControllers.Length; i++)
            {
                selectedControllers[i] = true;
            }
            
            // Execute the action with extracted properties
            await propertyExecutor.ExecuteProperty(
                extractedProperties,
                toolDict,
                shapeControllerType,
                shapeControllers,
                selectedControllers,
                embeddingsType,
                embeddings,
                historyMessages
            );
        }
    }

    private float ParseMovementValue(string command)
    {
        string[] words = command.ToLower().Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i] == "by" && i + 1 < words.Length)
            {
                if (float.TryParse(words[i + 1], out float value))
                {
                    return value;
                }
            }
        }
        return 1.0f; 
    }

    private Vector3 GetMovementDirection(string command)
    {
        command = command.ToLower();
        if (command.Contains("left")) return Vector3.left;
        if (command.Contains("right")) return Vector3.right;
        if (command.Contains("up")) return Vector3.up;
        if (command.Contains("down")) return Vector3.down;
        if (command.Contains("forward")) return Vector3.forward;
        if (command.Contains("backward")) return Vector3.back;
        return Vector3.zero;
    }

    private Vector3 GetRotationDirection(string command)
    {
        command = command.ToLower();
        
        if (command.Contains("x axis"))
            return Vector3.right; 
        if (command.Contains("y axis"))
            return Vector3.up;    
        if (command.Contains("z axis"))
            return Vector3.forward; 

        if (command.Contains("left") || command.Contains("counterclockwise"))
            return Vector3.up;     
        if (command.Contains("right") || command.Contains("clockwise"))
            return Vector3.down;   
        if (command.Contains("forward") || command.Contains("down"))
            return Vector3.right;
        if (command.Contains("backward") || command.Contains("up"))
            return Vector3.left; 
        
        return Vector3.zero; 
    }
} 
