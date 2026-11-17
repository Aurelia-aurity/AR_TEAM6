using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HitRegionVibration))]
public sealed class HitRegionVibrationEditor : Editor
{
    private float previewForce = 0.75f;

    public override void OnInspectorGUI()
    {
        // Bindings are captured on enable; keep configuration changes outside Play mode.
        using (new EditorGUI.DisabledScope(Application.isPlaying)) DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Local vibration preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("In Play mode, preview a region without publishing an audio or scoring event.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || !((HitRegionVibration)target).isActiveAndEnabled))
        {
            previewForce = EditorGUILayout.Slider("Hit force", previewForce, 0, 1);
            SerializedProperty regions = serializedObject.FindProperty("regions");
            for (int i = 0; i < regions.arraySize; i++)
            {
                string id = regions.GetArrayElementAtIndex(i).FindPropertyRelative("RegionId").stringValue;
                if (GUILayout.Button("Hit " + id)) ((HitRegionVibration)target).PlayHit(id, previewForce);
            }
        }
    }
}
