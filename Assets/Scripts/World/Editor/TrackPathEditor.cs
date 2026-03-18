using UnityEditor;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Инспектор для <see cref="TrackPath"/> с кнопками генерации пути в редакторе.
    /// </summary>
    [CustomEditor(typeof(TrackPath))]
    public class TrackPathEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Editor-инструменты", EditorStyles.boldLabel);

            var trackPath = (TrackPath)target;

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Сгенерировать точки (Editor)"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(trackPath.gameObject, "Generate TrackPath Points");
                    trackPath.GeneratePointsInEditor();
                    EditorUtility.SetDirty(trackPath);
                }

                if (GUILayout.Button("Очистить точки (Editor)"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(trackPath.gameObject, "Clear TrackPath Points");
                    trackPath.ClearPointsInEditor();
                    EditorUtility.SetDirty(trackPath);
                }
            }
        }
    }
}

