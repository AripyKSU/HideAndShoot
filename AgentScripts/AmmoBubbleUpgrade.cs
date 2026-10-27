using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class AmmoBubbleUpgrade
{
    public static object Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
        const string shaderPath="Assets/HideAndShoot/Resources/Shaders/PickupBubble.shader";
        const string materialPath="Assets/HideAndShoot/Materials/AmmoPickup_SubtleEmission.mat";
        const string shellMaterialPath="Assets/HideAndShoot/Materials/AmmoPickup_Bubble.mat";
        const string actorPath="Assets/HideAndShoot/Scripts/World/AmmoPickupActor.cs";
        const string installerPath="Assets/HideAndShoot/Editor/FirstPersonPistolInstaller.cs";
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            File.WriteAllText(shaderPath,File.ReadAllText("AgentScripts/PickupBubble.shader.txt"));
            AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if(shader==null || ShaderUtil.ShaderHasError(shader))throw new Exception("Bubble shader import failed: "+string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
            // Restore the original palette material, retaining its existing GUID and prefab references.
            File.WriteAllText(materialPath,File.ReadAllText("AgentReports/AmmoGlow_2026-10-16/AmmoPickup_Before.mat.txt"));
            AssetDatabase.ImportAsset(materialPath,ImportAssetOptions.ForceSynchronousImport);
            var original=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            original.shader=Shader.Find("Universal Render Pipeline/Lit");
            original.SetColor("_BaseColor",Color.white);
            original.SetColor("_Color",Color.white);
            original.SetColor("_EmissionColor",Color.black);
            original.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(original);AssetDatabase.SaveAssetIfDirty(original);
            var shell=AssetDatabase.LoadAssetAtPath<Material>(shellMaterialPath);
            if(shell==null){shell=new Material(shader);AssetDatabase.CreateAsset(shell,shellMaterialPath);}
            shell.shader=shader;
            shell.SetColor("_Tint",new Color(.04f,.3f,.8f,1));
            shell.SetColor("_EdgeColor",new Color(.12f,1.5f,4f,1));
            shell.SetFloat("_EdgeOpacity",.55f);
            shell.SetFloat("_CenterOpacity",.008f);
            shell.SetFloat("_RimPower",3f);
            EditorUtility.SetDirty(shell);AssetDatabase.SaveAssetIfDirty(shell);
            const string prefabPath="Assets/HideAndShoot/Prefabs/AmmoPickup.prefab";
            var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                prefab.GetComponent<HideAndShoot.World.AmmoPickupActor>().PrepareVisual();
                var existing=prefab.transform.Find("PickupGlowShell");
                var sphere=existing!=null?existing.gameObject:GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name="PickupGlowShell";sphere.layer=prefab.layer;
                sphere.transform.SetParent(prefab.transform,false);
                sphere.transform.localPosition=Vector3.zero;sphere.transform.localRotation=Quaternion.identity;
                sphere.transform.localScale=Vector3.one*.70f;
                foreach(var collider in sphere.GetComponents<Collider>())UnityEngine.Object.DestroyImmediate(collider);
                var renderer=sphere.GetComponent<MeshRenderer>();
                renderer.sharedMaterial=shell;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            var actor=File.ReadAllText(actorPath).Replace("\r\n","\n");
            if(!actor.Contains("private Transform glowShell;"))
            {
                actor=actor.Replace("        private Transform visual;","        private Transform visual;\n        private Transform glowShell;");
                actor=actor.Replace("            // The pickup material emits its own blue glow; no floor-lighting point light.","            // Keep the ammo palette intact; its separate transparent shell owns the glow.");
                actor=actor.Replace("            PrepareVisual();\n            visual.gameObject.SetActive(true);","            PrepareVisual();\n            if(glowShell!=null){glowShell.gameObject.SetActive(true);glowShell.localPosition=Vector3.zero;}\n            visual.gameObject.SetActive(true);");
                actor=actor.Replace("            visualPrepared=true;","            glowShell=transform.Find(\"PickupGlowShell\");\n            visualPrepared=true;");
                actor=actor.Replace("        public override void OnNetworkDespawn()\n        {","        public override void OnNetworkDespawn()\n        {\n            if(glowShell!=null){glowShell.gameObject.SetActive(false);glowShell.localPosition=Vector3.zero;}");
                actor=actor.Replace("            visual.Rotate(Vector3.up,Time.deltaTime*30f,Space.World);","            visual.Rotate(Vector3.up,Time.deltaTime*30f,Space.World);\n            if(glowShell!=null)glowShell.localPosition=Vector3.up*(Mathf.Sin(Time.time*3f)*.1f);");
                File.WriteAllText(actorPath,actor);
            }
            var installer=File.ReadAllText(installerPath).Replace("\r\n","\n");
            installer=installer.Replace("                    material.EnableKeyword(\"_EMISSION\");\n                    material.SetColor(\"_EmissionColor\",new Color(.07f,.35f,.40f));\n                    EditorUtility.SetDirty(material);","                    // Preserve the authored ammo palette; the separate shell owns the glow.");
            installer=installer.Replace("foreach(var renderer in ammo.GetComponentsInChildren<Renderer>(true))","foreach(var renderer in ammo.transform.Find(\"AmmoBoxVisual\").GetComponentsInChildren<Renderer>(true))");
            File.WriteAllText(installerPath,installer);
            AssetDatabase.DeleteAsset("Assets/HideAndShoot/Resources/Shaders/PickupGlow.shader");
            AssetDatabase.SaveAssets();
            return new {ammoRestored=original.shader.name,ammoTexture=AssetDatabase.GetAssetPath(original.GetTexture("_BaseMap")),sphereDiameter=.70f,centerOpacity=.008f,bubbleShaderErrors=ShaderUtil.GetShaderMessages(shader).Select(m=>m.message).ToArray(),scriptsUpdatedTogether=true};
        }
        finally{AssetDatabase.AllowAutoRefresh();AssetDatabase.Refresh();}
    }
}
