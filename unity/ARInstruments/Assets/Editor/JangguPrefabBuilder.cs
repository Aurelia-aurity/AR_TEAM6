using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class JangguPrefabBuilder
{
    private const string ModelPath = "Assets/Models/Janggu/janggu.glb";
    private const string PrefabPath = "Assets/Presentation/Prefabs/Janggu.prefab";

    public static void Validate()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset("Assets/Models/Buk/buk.glb", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var importer = new SerializedObject(AssetImporter.GetAtPath(ModelPath));
        var reports = importer.FindProperty("reportItems");
        for (int i = 0; i < reports.arraySize; i++)
        {
            var type = reports.GetArrayElementAtIndex(i).FindPropertyRelative("type").intValue;
            if (type == (int)LogType.Error || type == (int)LogType.Exception)
                throw new InvalidOperationException("GLB importer reported an error.");
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Prefab missing.");
        var names = prefab.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
        foreach (var zone in new[] { "Head_A_Inner", "Head_A_Outer", "Head_B_Inner", "Head_B_Outer" })
            if (!names.Contains(zone)) throw new InvalidOperationException("Missing prefab strike region: " + zone);
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length != 28 || prefab.GetComponentsInChildren<Collider>(true).Length != 0)
            throw new InvalidOperationException("Unexpected prefab components.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
            if (renderer.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                throw new InvalidOperationException("Invalid prefab materials.");
        }
        if ((bounds.size - new Vector3(0.53f, 0.45f, 0.45f)).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException("Unexpected prefab dimensions: " + bounds.size);
        var textures = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Texture2D>().ToArray();
        if (textures.Length == 0) throw new InvalidOperationException("Embedded textures missing.");
        EditorUtility.SetDirty(importer.targetObject);
        AssetDatabase.WriteImportSettingsIfDirty(ModelPath);
        AssetDatabase.SaveAssets();
        Debug.Log("JANGGU_VALIDATED: prefab linked; embedded textures=" + textures.Length + "; import errors=0.");
    }

    [MenuItem("ARInstruments/Create Janggu presentation prefab")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            throw new InvalidOperationException("Janggu prefab already exists; preserve its manual edits.");

        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("Janggu GLB import failed.");

        var root = new GameObject("Janggu");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            var names = visual.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
            foreach (var zone in new[] { "Head_A_Inner", "Head_A_Outer", "Head_B_Inner", "Head_B_Outer" })
                if (!names.Contains(zone)) throw new InvalidOperationException("Missing strike region: " + zone);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Presentation prefab must not contain colliders.");
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No model renderers.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                        throw new InvalidOperationException("Missing model material or shader.");
            }
            if ((bounds.size - new Vector3(0.53f, 0.45f, 0.45f)).sqrMagnitude > 0.000001f)
                throw new InvalidOperationException("Unexpected model dimensions: " + bounds.size);
            ConfigureHitFeedback(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
            if (!success) throw new InvalidOperationException("Prefab save failed.");
            AssetDatabase.SaveAssets();
            Debug.Log("JANGGU_PREFAB_OK: " + PrefabPath + "; size=" + bounds.size.ToString("F4")
                + "; renderers=" + renderers.Length + "; four strike regions preserved; colliders=0.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Defaults for newly authored prefabs. Existing mappings are edited on the prefab itself.
    public static void ConfigureHitFeedback(GameObject root)
    {
        if (root.GetComponent<HitRegionVibration>() != null) return;
        var definitions = new[]
        {
            (id: "janggu_head_a_inner", node: "Head_A_Inner", normal: Vector3.right),
            (id: "janggu_head_a_outer", node: "Head_A_Outer", normal: Vector3.right),
            (id: "janggu_head_b_inner", node: "Head_B_Inner", normal: Vector3.left),
            (id: "janggu_head_b_outer", node: "Head_B_Outer", normal: Vector3.left)
        };
        var nodes = root.GetComponentsInChildren<Transform>(true);
        var settings = new SerializedObject(root.AddComponent<HitRegionVibration>());
        var regions = settings.FindProperty("regions");
        regions.arraySize = definitions.Length;
        for (int i = 0; i < definitions.Length; i++)
        {
            var region = regions.GetArrayElementAtIndex(i);
            region.FindPropertyRelative("RegionId").stringValue = definitions[i].id;
            region.FindPropertyRelative("Target").objectReferenceValue = nodes.Single(t => t.name == definitions[i].node);
            region.FindPropertyRelative("LocalNormal").vector3Value = definitions[i].normal;
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
}
