using System;
using System.Linq;
using NinjaRealm.Core;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Draws a type dropdown for [SerializeReference, SubclassSelector] fields, so designers
    /// can pick an effect type (Damage, Heal, Stat Modifier...) in the inspector.
    /// </summary>
    [CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
    public sealed class SubclassSelectorDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUI.GetPropertyHeight(property, label, true);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var labelWidth = EditorGUIUtility.labelWidth;
            var buttonRect = new Rect(position.x + labelWidth + 2f, position.y,
                Mathf.Max(60f, position.width - labelWidth - 2f), EditorGUIUtility.singleLineHeight);

            var current = property.managedReferenceValue?.GetType();
            var buttonLabel = current == null ? "(Pick a type)" : ObjectNames.NicifyVariableName(current.Name);
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(buttonLabel), FocusType.Keyboard))
                ShowTypeMenu(property);

            EditorGUI.PropertyField(position, property, label, true);
            EditorGUI.EndProperty();
        }

        static void ShowTypeMenu(SerializedProperty property)
        {
            var baseType = GetFieldType(property);
            if (baseType == null) return;

            var serializedObject = property.serializedObject;
            var path = property.propertyPath;
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("(None)"), false, () => Assign(serializedObject, path, null));

            var types = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(type => !type.IsAbstract && !type.IsGenericType && type.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(type => type.Name);

            foreach (var type in types)
            {
                var chosen = type;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.Name)), false,
                    () => Assign(serializedObject, path, Activator.CreateInstance(chosen)));
            }

            menu.ShowAsContext();
        }

        static void Assign(SerializedObject serializedObject, string path, object value)
        {
            serializedObject.Update();
            var property = serializedObject.FindProperty(path);
            property.managedReferenceValue = value;
            property.isExpanded = value != null;
            serializedObject.ApplyModifiedProperties();
        }

        static Type GetFieldType(SerializedProperty property)
        {
            // Format: "AssemblyName Namespace.TypeName"
            var parts = property.managedReferenceFieldTypename.Split(' ');
            return parts.Length == 2 ? Type.GetType($"{parts[1]}, {parts[0]}") : null;
        }
    }
}
