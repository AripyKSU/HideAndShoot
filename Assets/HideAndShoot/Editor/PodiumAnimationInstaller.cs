using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using HideAndShoot.UI;
using UnityEditor;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    public static class PodiumAnimationInstaller
    {
        static PodiumAnimationInstaller()=>EditorApplication.update+=Ready;
        private static void Ready()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating)return;
            EditorApplication.update-=Ready;
            Install();
        }
        public static string MotionHash(AnimationClip clip)
        {
            var content=new StringBuilder(4096);
            content.Append(clip.length.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            foreach(var binding in AnimationUtility.GetCurveBindings(clip)
                .OrderBy(b=>b.path).ThenBy(b=>b.propertyName))
            {
                content.Append(binding.path).Append(binding.propertyName);
                var curve=AnimationUtility.GetEditorCurve(clip,binding);
                foreach(var key in curve.keys)
                    content.Append(key.time.ToString("R",System.Globalization.CultureInfo.InvariantCulture))
                        .Append(key.value.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            }
            using(var hash=SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(content.ToString())));
        }
        [MenuItem("Tools/HideAndShoot/Install Diverse Podium Animations")]
        public static void Install()
        {
            const string folder="Assets/HideAndShoot/Resources/PodiumAnimations";
            Directory.CreateDirectory(folder);
            var seen=new HashSet<string>();
            var clips=new List<AnimationClip>();
            foreach(var name in new[]{"Victory_Wave_Gesture","Victory_Wave","Victory_LookAround_Body","Victory_LookAround"})
            {
                var imported=AssetDatabase.LoadAllAssetsAtPath("Assets/HideAndShoot/ThirdParty/Mixamo/"+name+".fbx")
                    .OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview"));
                if(imported==null || !seen.Add(MotionHash(imported)))continue;
                string path=folder+"/"+name+".anim";
                var copy=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if(copy==null)
                {
                    copy=UnityEngine.Object.Instantiate(imported);
                    copy.name=name;copy.wrapMode=WrapMode.Loop;
                    var settings=AnimationUtility.GetAnimationClipSettings(copy);
                    settings.loopTime=true;settings.loopBlend=true;
                    settings.loopBlendOrientation=true;settings.loopBlendPositionY=true;
                    settings.loopBlendPositionXZ=true;
                    AnimationUtility.SetAnimationClipSettings(copy,settings);
                    AssetDatabase.CreateAsset(copy,path);
                }
                clips.Add(copy);
            }
            if(clips.Count<3)throw new InvalidOperationException("Fewer than three distinct podium motions.");
            const string setPath="Assets/HideAndShoot/Resources/PodiumAnimationSet.asset";
            var set=AssetDatabase.LoadAssetAtPath<PodiumAnimationSet>(setPath);
            if(set==null)
            {set=ScriptableObject.CreateInstance<PodiumAnimationSet>();AssetDatabase.CreateAsset(set,setPath);}
            if(set.clips==null || !set.clips.SequenceEqual(clips))
            {set.clips=clips.ToArray();EditorUtility.SetDirty(set);AssetDatabase.SaveAssets();}
            Debug.Log("[HideAndShoot/Podium] Distinct looping celebrations="+clips.Count);
        }
    }
}
