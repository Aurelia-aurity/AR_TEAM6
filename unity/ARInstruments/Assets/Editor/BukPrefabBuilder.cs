using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BukPrefabBuilder
{
    private const string ModelPath = "Assets/Models/Buk/buk.glb";
    private const string PrefabPath = "Assets/Presentation/Prefabs/Buk.prefab";
    private static readonly string[] Zones = {
        "Head_A_Inner", "Head_A_Outer", "Head_A_Rim",
        "Head_B_Inner", "Head_B_Outer", "Head_B_Rim"
    };

    [MenuItem("ARInstruments/Create Buk presentation prefab")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            throw new InvalidOperationException("Buk prefab already exists; preserve its manual edits.");
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var importer = new SerializedObject(AssetImporter.GetAtPath(ModelPath));
        var reports = importer.FindProperty("reportItems");
        for (int i = 0; i < reports.arraySize; i++)
        {
            int type = reports.GetArrayElementAtIndex(i).FindPropertyRelative("type").intValue;
            if (type == (int)LogType.Error || type == (int)LogType.Exception)
                throw new InvalidOperationException("Buk GLB import reported an error.");
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("Buk model missing.");
        var root = new GameObject("Buk");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            ValidateObject(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
            if (!success) throw new InvalidOperationException("Buk prefab save failed.");
            EditorUtility.SetDirty(importer.targetObject);
            AssetDatabase.WriteImportSettingsIfDirty(ModelPath);
            AssetDatabase.SaveAssets();
            ValidateObject(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            int textures = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Texture2D>().Count();
            if (textures == 0) throw new InvalidOperationException("Buk textures missing.");
            Debug.Log("BUK_PREFAB_OK: " + PrefabPath + "; six strike regions; size=0.252 x 0.49732 x 0.48060 m; colliders=0; textures=" + textures);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ValidateObject(GameObject root)
    {
        if (root == null) throw new InvalidOperationException("Buk prefab missing.");
        var nodes = root.GetComponentsInChildren<Transform>(true);
        foreach (string zone in Zones)
        {
            var node = nodes.Single(t => t.name == zone);
            var renderer = node.GetComponent<Renderer>();
            if (renderer == null) throw new InvalidOperationException("Missing zone renderer: " + zone);
            float diameter = zone.EndsWith("Inner") ? 0.21f : zone.EndsWith("Outer") ? 0.42f : 0.47227f;
            if (Mathf.Abs(renderer.bounds.size.y - diameter) > 0.0001f || Mathf.Abs(renderer.bounds.size.z - diameter) > 0.0001f)
                throw new InvalidOperationException("Unexpected zone diameter: " + zone);
        }
        if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            throw new InvalidOperationException("Presentation prefab must not contain physics components.");
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length != 14) throw new InvalidOperationException("Unexpected Buk mesh count.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
            if (renderer.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                throw new InvalidOperationException("Invalid Buk material.");
        }
        if ((bounds.size - new Vector3(0.252f, 0.49732f, 0.48060f)).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException("Unexpected Buk dimensions: " + bounds.size);
    }
}
