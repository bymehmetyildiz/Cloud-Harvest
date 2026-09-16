using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class CloudHarvestMaterialRepair
{
    const string Root = "Assets/KUBIKOS - World";
    const string Work = @"C:\Users\User\Documents\Codex\2026-09-16\cl\work\material-repair";
    static double nextCheck;
    static CloudHarvestMaterialRepair() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        string request = Path.Combine(Work, "request.txt");
        if (!File.Exists(request)) return;
        string action = File.ReadAllText(request).Trim();
        File.Delete(request);
        try { if (action == "repair") Repair(); else if (action == "particles") RepairParticles(); else Audit(); }
        catch (Exception e) { File.WriteAllText(Path.Combine(Work, "failure.txt"), e.ToString()); Debug.LogException(e); }
    }
    static void Backup(string path)
    {
        string target = Path.Combine(Work, "backup", path);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        if (!File.Exists(target)) File.Copy(path, target);
        if (File.Exists(path + ".meta") && !File.Exists(target + ".meta")) File.Copy(path + ".meta", target + ".meta");
    }
    static void Repair()
    {
        var lines = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root + "/Materials" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string source = Root + "/URP Support/Materials URP" + path.Substring((Root + "/Materials").Length);
            var src = AssetDatabase.LoadAssetAtPath<Material>(source);
            var dst = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (src == null || dst == null) throw new Exception("Missing material counterpart: " + path);
            Backup(path);
            string name = dst.name;
            EditorUtility.CopySerialized(src, dst);
            dst.name = name;
            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssetIfDirty(dst);
            lines.Add("URP counterpart: " + path);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null || !path.EndsWith(".mat")) continue;
            string oldShader = mat.shader.name;
            MaterialUpgrader upgrader = null;
            if (oldShader == "Standard" || oldShader == "Standard (Specular setup)") upgrader = new StandardUpgrader(oldShader);
            if (oldShader == "Particles/Standard Surface" || oldShader == "Particles/Standard Unlit" || oldShader == "Particles/VertexLit Blended") upgrader = new ParticleUpgrader(oldShader);
            if (upgrader == null) continue;
            Backup(path);
            MaterialUpgrader.Upgrade(mat, upgrader, MaterialUpgrader.UpgradeFlags.None);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            lines.Add("Built-in upgrade: " + path + " | " + oldShader + " -> " + mat.shader.name);
        }
        File.WriteAllLines(Path.Combine(Work, "changes.txt"), lines);
        SceneView.RepaintAll();
        Audit();
    }
    static void RepairParticles()
    {
        var lines = new List<string>();
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) throw new Exception("URP particle shader missing");
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null || !path.EndsWith(".mat")) continue;
            string old = mat.shader.name;
            if (old != "Legacy Shaders/Particles/Additive" && old != "Mobile/Particles/Additive" && old != "Mobile/Particles/Alpha Blended") continue;
            Backup(path);
            var texture = mat.GetTexture("_MainTex");
            var scale = mat.GetTextureScale("_MainTex");
            var offset = mat.GetTextureOffset("_MainTex");
            var tint = mat.HasProperty("_TintColor") ? mat.GetColor("_TintColor") * 2f : Color.white;
            bool additive = old.EndsWith("Additive");
            int queue = mat.renderQueue;
            mat.shader = shader;
            mat.shaderKeywords = Array.Empty<string>();
            mat.SetTexture("_BaseMap", texture);
            mat.SetTextureScale("_BaseMap", scale);
            mat.SetTextureOffset("_BaseMap", offset);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", additive ? 2 : 0);
            mat.SetFloat("_Cull", 0);
            mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = queue >= 2500 ? queue : 3000;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            lines.Add("Particle upgrade: " + path + " | " + old);
        }
        File.AppendAllLines(Path.Combine(Work, "changes.txt"), lines);
        SceneView.RepaintAll();
        Audit();
    }
    static void Audit()
    {
        var lines = new List<string>();
        lines.Add("Unity: " + Application.unityVersion);
        lines.Add("Pipeline: " + GraphicsSettings.currentRenderPipeline);
        var shaders = new HashSet<Shader>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            if (mat.shader == null) { lines.Add("MISSING SHADER | " + path); continue; }
            shaders.Add(mat.shader);
            string tag = mat.GetTag("RenderPipeline", false, "");
            lines.Add("MAT | " + path + " | " + mat.shader.name + " | supported=" + mat.shader.isSupported + " | pipeline=" + tag);
            if (path.EndsWith(".mat") && (tag == "UniversalPipeline" || mat.shader.name.StartsWith("Universal Render Pipeline/")))
                for (int i = 0; i < mat.passCount; i++) ShaderUtil.CompilePass(mat, i, true);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { Root + "/URP Support/Shaders URP" }))
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
            if (shader == null || shaders.Contains(shader)) continue;
            var temp = new Material(shader);
            if (temp.GetTag("RenderPipeline", false, "") == "UniversalPipeline")
            {
                shaders.Add(shader);
                for (int i = 0; i < temp.passCount; i++) ShaderUtil.CompilePass(temp, i, true);
            }
            UnityEngine.Object.DestroyImmediate(temp);
        }
        foreach (var shader in shaders)
            foreach (var msg in ShaderUtil.GetShaderMessages(shader))
                lines.Add("SHADER " + msg.severity + " | " + shader.name + " | " + msg.message + " | " + msg.file + ":" + msg.line);
        File.WriteAllLines(Path.Combine(Work, "audit.txt"), lines);
        Debug.Log("Cloud Harvest material repair/audit complete. Report: " + Work);
    }
}
