#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="ReactionEventHub"/>: mode-aware threshold fields and Play-mode layout debug buttons.
/// </summary>
[CustomEditor(typeof(ReactionEventHub))]
public class ReactionEventHubEditor : Editor
{
    static readonly GUIContent ThresholdLabel = new GUIContent(
        "Expand Threshold",
        "Hand-distance mode only: expanded when distance exceeds this (meters).");

    static readonly GUIContent MarginLabel = new GUIContent(
        "Expand Guide Extra Margin M",
        "Guide mode only: extra meters beyond the guide half-width before counting as expanded.");

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Expansion Detection", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("leftHand"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("rightHand"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("expansionMode"));

        var modeProp = serializedObject.FindProperty("expansionMode");
        bool guideMode = modeProp.enumValueIndex == (int)ReactionEventHub.ExpansionDetectionMode.RightHandOutsideGuideLine;

        EditorGUI.BeginDisabledGroup(guideMode);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("expandThreshold"), ThresholdLabel);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!guideMode);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("expandGuideExtraMarginM"), MarginLabel);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("monitorExpansion"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Layout lock (optional)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("modelSpawner"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lineGuide"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Expansion Transition Events", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("onExpandStart"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("onExpandEnd"));

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        var hub = (ReactionEventHub)target;

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("Debug: Decide layout (spawner + LineGuide)"))
            hub.DecideLayoutFromHub();

        if (GUILayout.Button("Debug: Unlock layout"))
            hub.UnlockLayoutFromHub();
        EditorGUI.EndDisabledGroup();

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Decide / Unlock are available in Play mode only.", MessageType.Info);
    }
}
#endif
