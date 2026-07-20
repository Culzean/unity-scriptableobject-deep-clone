using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Atticar.DeepCloneLite.Editor
{
	public static class DeepCloneLite
	{
	    private static Dictionary<ScriptableObject, ScriptableObject> _cloneLookup = new();
	    private static string _folderPath;
	    private static readonly Dictionary<Type, Func<object, object>> TypeHandlers = new()
	    {
		    [typeof(AnimationCurve)] = obj => CloneAnimationCurve((AnimationCurve)obj),
		    // Further types to be added here:
	    };

	    /// <summary>
	    /// Deep clone a ScriptableObject and all of its ScriptableObject references,
	    /// saving the clones to the specified target folder.
	    /// </summary>
	    public static ScriptableObject Clone(ScriptableObject original, string targetFolder)
	    {
		    if (original == null)
		    {
			    throw new ArgumentNullException(nameof(original));
		    }

		    _cloneLookup.Clear();

		    // Sanitize folder path
		    if (targetFolder[^1] != '/')
		    {
			    targetFolder += "/";
		    }

		    _folderPath = targetFolder;

		    // Check if the directory exists, and if not, create it
		    if (!System.IO.Directory.Exists(_folderPath))
		    {
			    System.IO.Directory.CreateDirectory(_folderPath);
		    }

		    // Instantiate a copy of the original object
		    ScriptableObject clone = CloneScriptableObject(original, true);

		    // Save the changes to the AssetDatabase
		    EditorUtility.SetDirty(clone);
		    AssetDatabase.SaveAssets();

		    return clone;
	    }

	    private static T CloneScriptableObject<T>(T original) where T : ScriptableObject
	    {
	        if (_cloneLookup.ContainsKey(original))
	        {
	            return (T)_cloneLookup[original];
	        }

	        return CloneScriptableObject(original, true);
	    }

	    private static T CloneScriptableObject<T>(T original, bool cloneFields) where T : ScriptableObject
	    {
		    T clone = ScriptableObject.Instantiate(original);
		    _cloneLookup.Add(original, clone);

		    if (cloneFields)
		    {
			    CloneFields(clone);
		    }

		    // Sanitize folder path
		    if (_folderPath[_folderPath.Length - 1] != '/')
		    {
			    _folderPath += "/";
		    }

			// Create a new name for the object
			string updatedName = original.name + "(Clone)";
		    var path = _folderPath + updatedName + ".asset";

		    // Save the clone to disk
		    AssetDatabase.CreateAsset(clone, AssetDatabase.GenerateUniqueAssetPath(path));

		    return clone;
	    }

	    private static void CloneFields(object obj)
		{
		    if (obj == null)
		    {
			    return;
		    }

		    Type type = obj.GetType();

		    var seenTokens = new HashSet<int>(); // Uniquely identifies fields in the inheritance chain
		    while (type != null)
		    {
		        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		        foreach (FieldInfo field in fields)
		        {
			        // Skip field if it's already been handled via a base type
			        if (!seenTokens.Add(field.MetadataToken))
				        continue;

		            object fieldValue = field.GetValue(obj);

			        // First check for any custom types to clone
		            if (TypeHandlers.TryGetValue(field.FieldType, out var handler))
		            {
			            object clonedValue = handler(fieldValue);
			            continue;
		            }

		            if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
		            {
		                // Struct - clone it by creating a new instance and copying fields
		                object structClone = CloneStruct(fieldValue);
		                field.SetValue(obj, structClone);
		            }
		            else if (field.FieldType.IsSubclassOf(typeof(ScriptableObject)))
		            {
		                ScriptableObject originalChild = fieldValue as ScriptableObject;
		                if (originalChild != null)
		                {
		                    ScriptableObject clonedChild = CloneScriptableObject(originalChild);
		                    field.SetValue(obj, clonedChild);
		                }
		            }
		            else if (field.FieldType.IsClass && !field.FieldType.IsSubclassOf(typeof(UnityEngine.Object)) && !(fieldValue is string) && !(fieldValue is IList) && !(fieldValue is IDictionary))
		            {
		                // Class - clone it by creating a new instance and copying fields
		                if (field.FieldType.GetConstructor(Type.EmptyTypes) != null) // check for parameterless constructor
		                {
		                    object classClone = Activator.CreateInstance(field.FieldType);
		                    CloneFields(classClone);
		                    field.SetValue(obj, classClone);
		                }
		            }
		            else if (field.FieldType.GetInterfaces().Any(x => x == typeof(IEnumerable)))
		            {
			            IEnumerable collection = fieldValue as IEnumerable;

			            if (collection != null)
			            {
				            Type collectionType = fieldValue.GetType();

				            // Handling for arrays
				            if (fieldValue is Array array)
				            {
					            Array clonedArray = (Array)Activator.CreateInstance(collectionType, array.Length);
					            int index = 0;
					            foreach (var element in collection)
					            {
						            if (element != null)
						            {
							            object clonedElement = CloneObject(element);
							            clonedArray.SetValue(clonedElement, index++);
						            }
					            }
					            field.SetValue(obj, clonedArray);
				            }
				            // Handling for lists
				            else if (fieldValue is IList)
				            {
					            Type[] genericArgs = collectionType.GetGenericArguments();
					            Type listType = typeof(List<>);
					            Type genericListType = listType.MakeGenericType(genericArgs);
					            IList clonedList = (IList)Activator.CreateInstance(genericListType);

					            foreach (var element in collection)
					            {
						            if (element != null)
						            {
							            object clonedElement = CloneObject(element);
							            clonedList.Add(clonedElement);
						            }
					            }
					            field.SetValue(obj, clonedList);
				            }
				            // Add more handling for other collection types if needed
			            }
		            }
		        }
		        type = type.BaseType;
		    }
		}

	    private static object CloneObject(object original)
	    {
		    if (original == null)
		    {
			    return null;
		    }

		    Type type = original.GetType();

		    // Check for ScriptableObject
		    if (type.IsSubclassOf(typeof(ScriptableObject)))
		    {
			    return CloneScriptableObject(original as ScriptableObject);
		    }

		    // Check for struct
		    if (type.IsValueType && !type.IsPrimitive && !type.IsEnum)
		    {
			    return CloneStruct(original);
		    }

		    // Check for array
		    if (type.IsArray)
		    {
			    if (original is Array originalArray)
			    {
				    Array clonedArray = (Array)Activator.CreateInstance(type, originalArray.Length);
				    for (int i = 0; i < originalArray.Length; i++)
				    {
					    clonedArray.SetValue(CloneObject(originalArray.GetValue(i)), i);
				    }
				    return clonedArray;
			    }
			    else
			    {
				    throw new MissingFieldException("Array Type expected but not found.");
			    }
		    }

		    // Check for list
		    if (original is IList originalList)
		    {
			    Type listType = typeof(List<>);
			    Type genericListType = listType.MakeGenericType(type.GetGenericArguments());
			    IList clonedList = (IList)Activator.CreateInstance(genericListType);
			    foreach (var item in originalList)
			    {
				    clonedList.Add(CloneObject(item));
			    }
			    return clonedList;
		    }

		    // Unity Object types (GameObject, Material, etc.) — keep as reference
		    if (type.IsSubclassOf(typeof(UnityEngine.Object)))
		    {
			    return original;
		    }

		    // Check for class
		    if (!type.IsValueType && !type.IsPrimitive && type != typeof(string))
		    {
			    ConstructorInfo defaultConstructor = type.GetConstructor(Type.EmptyTypes);
			    if (defaultConstructor == null)
			    {
				    // No default constructor - issue warning and perform shallow copy
				    Debug.LogWarning($"Unable to perform deep copy for type {type} as it lacks a default constructor. A shallow copy will be performed instead.");
				    return original;
			    }

			    // Default constructor exists - perform deep copy and recursively clone fields
			    object classClone = Activator.CreateInstance(type);
			    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
			    {
				    object fieldValue = field.GetValue(original);
				    if (fieldValue != null)
				    {
					    field.SetValue(classClone, CloneObject(fieldValue));
				    }
			    }
			    return classClone;
		    }

		    // For simple types or other reference types, return the original value
		    return original;
	    }

	    private static object CloneStruct(object originalStruct)
		{
			if (originalStruct == null)
				return null;

		    // Create a new struct instance
		    object structClone = Activator.CreateInstance(originalStruct.GetType());

		    // Copy fields from the original struct
		    FieldInfo[] fields = originalStruct.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		    foreach (FieldInfo field in fields)
		    {
		        object fieldValue = field.GetValue(originalStruct);

		        if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
		        {
		            // Struct - clone it by creating a new instance and copying fields
		            object nestedStructClone = CloneStruct(fieldValue);
		            field.SetValue(structClone, nestedStructClone);
		        }
		        else if (field.FieldType.IsSubclassOf(typeof(ScriptableObject)))
		        {
		            // ScriptableObject - clone it using the CloneScriptableObject method
		            ScriptableObject originalChild = fieldValue as ScriptableObject;
		            if (originalChild != null)
		            {
		                ScriptableObject clonedChild = CloneScriptableObject(originalChild);
		                field.SetValue(structClone, clonedChild);
		            }
		        }
		        else if (field.FieldType.IsClass && !field.FieldType.IsSubclassOf(typeof(UnityEngine.Object)) && !(fieldValue is string) && !(fieldValue is IList) && !(fieldValue is IDictionary))
		        {
		            // Class - clone it by creating a new instance and copying fields
		            if (fieldValue != null && fieldValue.GetType().GetConstructor(Type.EmptyTypes) != null) // check for parameterless constructor
		            {
		                object classClone = Activator.CreateInstance(fieldValue.GetType());
		                CloneFields(classClone);
		                field.SetValue(structClone, classClone);
		            }
		        }
		        else
		        {
		            // Primitive type, string, or enumerable - just copy the value
		            field.SetValue(structClone, fieldValue);
		        }
		    }

		    return structClone;
		}

		private static object CloneAnimationCurve(AnimationCurve source)
		{
			if (source == null) return null;
			var clone = new AnimationCurve(source.keys)
			{
				preWrapMode = source.preWrapMode,
				postWrapMode = source.postWrapMode
			};
			return clone;
		}
	}

	public class DeepCloneLiteWindow : EditorWindow
	{
	    private ScriptableObject _selectedScriptableObject;
	    private string _folderPathRoot = "Assets";

	    [MenuItem("Tools/Atticar/Deep Clone (Lite)")]
	    public static void ShowWindow()
	    {
	        GetWindow<DeepCloneLiteWindow>("Deep Clone (Lite)");
	    }

	    private void OnGUI()
	    {
	        GUILayout.Label("Select a scriptable object to clone", EditorStyles.boldLabel);
	        _selectedScriptableObject = (ScriptableObject)EditorGUILayout.ObjectField(
	            _selectedScriptableObject, typeof(ScriptableObject), false);

	        EditorGUILayout.BeginHorizontal();

	        GUILayout.Label("Output Folder: " + _folderPathRoot, GUILayout.MaxWidth(500));

	        if (GUILayout.Button("Select Folder"))
	        {
		        string absoluteFolderPath = EditorUtility.OpenFolderPanel("Select a folder", _folderPathRoot, "");

		        // Remove absolute path leading up to Assets to get a relative path
		        if (absoluteFolderPath.StartsWith(Application.dataPath))
		        {
			        _folderPathRoot = "Assets" + absoluteFolderPath.Substring(Application.dataPath.Length);
		        }
		        else if (!string.IsNullOrEmpty(absoluteFolderPath))
		        {
			        Debug.LogWarning("Folder must be located in the project's Assets folder.");
		        }
	        }

	        EditorGUILayout.EndHorizontal();

	        EditorGUILayout.Space(8f);

	        if (GUILayout.Button("Clone Scriptable Object", GUILayout.Height(28)))
	        {
	            if (_selectedScriptableObject != null)
	            {
		            DeepCloneLite.Clone(_selectedScriptableObject, _folderPathRoot);
	            }
	            else
	            {
	                Debug.LogWarning("No file has been provided, please select one to clone.");
	            }
	        }
	    }
	}
}
