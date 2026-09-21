using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CloudHarvest.EditorTools;

[InitializeOnLoad]
static class CloudHarvestPaletteCheck
{
    const string Report = @"C:\Users\User\Documents\Codex\2026-09-16\cl\work\palette\validation.txt";
    static CloudHarvestPaletteCheck() { EditorApplication.delayCall += Run; }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Run()
    {
        if (SessionState.GetBool("CHPaletteValidated-v1", false)) return;
        SessionState.SetBool("CHPaletteValidated-v1", true);
        var report = new List<string>();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var cubes = CloudHarvestCubePalette.LoadCubes();
            report.Add("Unit cube prefabs: " + cubes.Count);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] {CloudHarvestCubePalette.SourceFolder}))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                CubePlacement.TryBounds(prefab, out var bounds);
                report.Add(prefab.name + " size=" + bounds.size.ToString("F3") + " center=" + bounds.center.ToString("F3"));
            }
            Check(cubes.Count > 10, "Too few unit cube prefabs detected");
            var rootObject = new GameObject("Palette validation root");
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            var root = rootObject.transform;
            root.position = new Vector3(10, 3, -7);
            var prefabA = cubes[0]; var prefabB = cubes[1];
            CubePlacement.TryBounds(prefabA, out var bA);
            CubePlacement.TryBounds(prefabB, out var bB);
            var index = CubePlacement.Index(root);
            var cell = new Vector3Int(-2, 2, 3);
            Check(CubePlacement.CellAt(new Vector3(-0.1f, 0, -0.1f), -1) == new Vector3Int(-1, -1, -1), "Negative coordinates");
            Check(CubePlacement.Apply(root,index,cell,prefabA,bA,1,false,false,true,false), "Place failed");
            var placed = index[cell];
            Check(PrefabUtility.IsPartOfPrefabInstance(placed), "Prefab connection lost");
            Check(placed.GetComponent<Collider>() != null, "Collider missing");
            Vector3 actual = root.InverseTransformPoint(placed.transform.TransformPoint(bA.center));
            Check((actual - ((Vector3)cell + Vector3.one * 0.5f)).sqrMagnitude < 0.00001f, "Pivot/rotation alignment incorrect");
            Check(!CubePlacement.Apply(root,index,cell,prefabB,bB,0,false,false,true,false), "Duplicate prevention failed");
            Check(root.childCount == 1, "Duplicate cube created");
            Check(CubePlacement.Apply(root,index,cell,prefabB,bB,2,false,true,true,false), "Replace failed");
            Check(root.childCount == 1 && PrefabUtility.GetCorrespondingObjectFromSource(index[cell]) == prefabB, "Replace prefab mismatch");
            var upper = cell + Vector3Int.up;
            CubePlacement.Apply(root,index,upper,prefabA,bA,0,false,false,false,false);
            Check(CubePlacement.Index(root).Count == 2, "Layer indexing failed");
            var decoration = new GameObject("Existing user decoration");
            SceneManager.MoveGameObjectToScene(decoration, scene); decoration.transform.SetParent(root);
            Check(CubePlacement.Apply(root,index,cell,null,default,0,true,false,false,false), "Erase failed");
            Check(index.ContainsKey(upper) && decoration && root.childCount == 2, "Erase damaged unrelated object or layer");
            Check(!CubePlacement.Apply(root,index,cell,null,default,0,true,false,false,false), "Erase should ignore empty cell");
            report.Add("PASS: unit scale, negative coordinates, rotated pivot placement, prefab link, collider, duplicate prevention, replacement, layers, scoped erase.");
        }
        catch (Exception e) { report.Add("FAIL: " + e); Debug.LogException(e); }
        finally { EditorSceneManager.ClosePreviewScene(scene); File.WriteAllLines(Report, report); }
        CloudHarvestCubePalette.Open();
    }
}

