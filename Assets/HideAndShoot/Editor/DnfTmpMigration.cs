using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// Opt-in one-time conversion, persisted in source assets (never per-frame).
    /// The generated font includes pre-rendered Hangul, Latin, punctuation and
    /// numeric glyphs. Source TTF stays embedded for dynamic uncommon glyphs.
    /// </summary>
    [InitializeOnLoad]
    public static class DnfTmpMigration
    {
        private const string Request="Assets/HideAndShoot/QA/TMP_Migration.request";
        private const string Complete="Assets/HideAndShoot/QA/TMP_Migration.complete.txt";
        private const string RepairRequest="Assets/HideAndShoot/QA/TMP_Font_Repair.request";
        private const string RepairDone="Assets/HideAndShoot/QA/TMP_Font_Repair.complete.txt";
        private const string Ttf="Assets/HideAndShoot/Fonts/DNFBitBitv2.ttf";
        private const string FontPath="Assets/HideAndShoot/Resources/Fonts/DNFBitBitv2_SDF.asset";
        private const string ReportPath="Assets/HideAndShoot/QA/TMP_Font_Bake_Report.txt";
        private static bool running;
        private static int attempts;

        static DnfTmpMigration() => EditorApplication.update+=RunOnce;

        private static void RunOnce()
        {
            if(running || EditorApplication.isPlayingOrWillChangePlaymode ||
               EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            bool migration=File.Exists(Request) && !File.Exists(Complete);
            bool repair=File.Exists(RepairRequest) && !File.Exists(RepairDone);
            if(!migration && !repair)return;
            running=true;
            EditorApplication.update-=RunOnce;
            try { if(migration)Migrate(); else RepairFont(); }
            catch(Exception ex)
            {
                Debug.LogException(ex);
                running=false;
                if(++attempts<4)EditorApplication.update+=RunOnce;
            }
        }

        [MenuItem("Tools/HideAndShoot/TMP/Repair Font Atlas")]
        public static void RepairFont()
        {
            var font=BakeFont();
            int hangul=font.characterTable.Count(x=>x.unicode>=0xAC00 && x.unicode<=0xD7A3);
            if(hangul!=11172)
                throw new IOException("TMP font has "+hangul+"/11172 Hangul syllables after repair.");
            int labels=0,inputs=0;
            foreach(string path in new[]{
                "Assets/HideAndShoot/Scenes/MenuLobbyScene.unity",
                "Assets/HideAndShoot/Scenes/GameScene.unity"})
            {
                var scene=SceneManager.GetSceneByPath(path);
                bool opened=!scene.IsValid() || !scene.isLoaded;
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    foreach(var root in scene.GetRootGameObjects())
                    {
                        int oldTexts=root.GetComponentsInChildren<Text>(true).Length;
                        int oldInputs=root.GetComponentsInChildren<InputField>(true).Length;
                        if(oldTexts+oldInputs>0)
                            throw new IOException("Legacy UGUI components remain in "+path+
                                ": Text="+oldTexts+", InputField="+oldInputs);
                        var texts=root.GetComponentsInChildren<TextMeshProUGUI>(true);
                        foreach(var label in texts)
                            if(label.font!=font)
                                throw new IOException("Different UI font on "+label.name+" in "+path);
                        foreach(var input in root.GetComponentsInChildren<TMP_InputField>(true))
                        {
                            if(input.textComponent==null || input.textViewport==null)
                                throw new IOException("Incomplete TMP input on "+input.name);
                            inputs++;
                        }
                        labels+=texts.Length;
                    }
                }
                finally
                {
                    if(opened && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene,true);
                }
            }
            if(labels<24 || inputs!=2)
                throw new IOException("Expected TMP labels and lobby inputs: "+labels+"/2 "+inputs);
            File.WriteAllText(RepairDone,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+
                "\nHangulSyllables="+hangul+"/11172\nGlyphs="+font.characterTable.Count+
                "\nAtlasPages="+font.atlasTextures.Length+
                "\nSceneTMPLabels="+labels+"\nSceneTMPInputs="+inputs+"\nLegacyUGUIText=0\n");
            AssetDatabase.ImportAsset(RepairDone);
            Debug.Log("[HideAndShoot/TMP] Font coverage verified: "+hangul+" Hangul glyphs.");
        }

        [MenuItem("Tools/HideAndShoot/TMP/Bake Font and Migrate UI")]
        public static void Migrate()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play Mode before TMP migration.");
            if(TMP_Settings.instance==null)
            {
                string resourcePackage=Directory.GetFiles("Library/PackageCache",
                        "TMP Essential Resources.unitypackage",SearchOption.AllDirectories)
                    .FirstOrDefault();
                if(string.IsNullOrEmpty(resourcePackage))
                    throw new IOException("Missing TMP Essential Resources Unity package.");
                AssetDatabase.ImportPackage(resourcePackage,false);
                if(TMP_Settings.instance==null)
                    throw new IOException("TMP Essential Resources imported; wait for import then rerun TMP migration.");
            }
            var font=BakeFont();
            TMP_Settings.defaultFontAsset=font;
            if(TMP_Settings.instance!=null)EditorUtility.SetDirty(TMP_Settings.instance);

            int textCount=0,inputCount=0;
            foreach(string path in new[]{
                "Assets/HideAndShoot/Scenes/MenuLobbyScene.unity",
                "Assets/HideAndShoot/Scenes/GameScene.unity"})
            {
                var scene=SceneManager.GetSceneByPath(path);
                bool opened=!scene.IsValid() || !scene.isLoaded;
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    int texts=0,inputs=0;
                    foreach(var root in scene.GetRootGameObjects())
                        ConvertHierarchy(root,font,ref texts,ref inputs);
                    if(texts+inputs>0)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if(!EditorSceneManager.SaveScene(scene,path))
                            throw new IOException("Cannot save converted scene "+path);
                    }
                    textCount+=texts;inputCount+=inputs;
                    Debug.Log("[HideAndShoot/TMP] "+path+": "+texts+" labels and "+inputs+" inputs.");
                }
                finally
                {
                    if(opened && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene,true);
                }
            }
            foreach(string path in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/HideAndShoot"})
                         .Select(AssetDatabase.GUIDToAssetPath))
            {
                var prefab=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int texts=0,inputs=0;
                    ConvertHierarchy(prefab,font,ref texts,ref inputs);
                    if(texts+inputs>0)PrefabUtility.SaveAsPrefabAsset(prefab,path);
                    textCount+=texts;inputCount+=inputs;
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Complete,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+
                "\nConvertedText="+textCount+"\nConvertedInput="+inputCount+
                "\nFont="+FontPath+"\n");
            AssetDatabase.ImportAsset(Complete);
            Debug.Log("[HideAndShoot/TMP] Complete: "+textCount+" Text and "+inputCount+
                      " InputField converted, using "+FontPath);
        }

        private static TMP_FontAsset BakeFont()
        {
            if(!File.Exists(Ttf))throw new FileNotFoundException("Missing source font",Ttf);
            var source=AssetDatabase.LoadAssetAtPath<Font>(Ttf);
            if(source==null)throw new IOException("Font not imported: "+Ttf);
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Directory.CreateDirectory(Path.GetDirectoryName(FontPath));
            if(font==null)
            {
                // 40px distance fields retain the pixel outline while avoiding
                // a prohibitively large single texture for 11,172 Hangul syllables.
                font=TMP_FontAsset.CreateFontAsset(source,40,4,GlyphRenderMode.SDFAA,
                    4096,4096,AtlasPopulationMode.Dynamic,true);
                if(font==null)throw new IOException("TMP failed to create DNF font asset.");
                font.name="DNFBitBitv2_SDF";
                AssetDatabase.CreateAsset(font,FontPath);
            }
            // Explicitly keep the TTF connected. Unity can clear editor-only
            // source-font references when reloading a saved dynamic font asset.
            font.atlasPopulationMode=AtlasPopulationMode.Dynamic;
            var fontSource=new SerializedObject(font);
            var sourceReference=fontSource.FindProperty("m_SourceFontFile");
            if(sourceReference!=null)
            {
                sourceReference.objectReferenceValue=source;
                fontSource.ApplyModifiedPropertiesWithoutUndo();
            }
            var chars=new StringBuilder(12200);
            for(int c=0x20;c<=0x7E;c++)chars.Append((char)c);
            for(int c=0x00A0;c<=0x00FF;c++)chars.Append((char)c);
            for(int c=0x1100;c<=0x11FF;c++)chars.Append((char)c);
            for(int c=0x3130;c<=0x318F;c++)chars.Append((char)c);
            for(int c=0x2000;c<=0x206F;c++)chars.Append((char)c);
            for(int c=0x2190;c<=0x21FF;c++)chars.Append((char)c);
            for(int c=0xAC00;c<=0xD7A3;c++)chars.Append((char)c);
            var existingGlyphs=new HashSet<uint>(font.characterTable.Select(x=>x.unicode));
            var wanted=new StringBuilder();
            foreach(char ch in chars.ToString())
                if(!existingGlyphs.Contains(ch))wanted.Append(ch);
            string missing=wanted.ToString();
            // TMP can stop at a partial atlas during a single large request.
            // Retry the remaining glyphs to fill the next atlas page(s).
            for(int pass=0;pass<8 && !string.IsNullOrEmpty(missing);pass++)
            {
                int before=missing.Length;
                font.TryAddCharacters(missing,out missing);
                if(missing==null || missing.Length>=before)break;
            }

            var serialized=new SerializedObject(font);
            var clear=serialized.FindProperty("m_ClearDynamicDataOnBuild");
            if(clear!=null){clear.boolValue=false;serialized.ApplyModifiedPropertiesWithoutUndo();}
            if(font.material!=null && !AssetDatabase.Contains(font.material))
            {
                font.material.name=font.name+"_Material";
                AssetDatabase.AddObjectToAsset(font.material,font);
            }
            for(int i=0;i<font.atlasTextures.Length;i++)
            {
                var atlas=font.atlasTextures[i];
                if(atlas==null || AssetDatabase.Contains(atlas))continue;
                atlas.name=font.name+"_Atlas_"+i;
                AssetDatabase.AddObjectToAsset(atlas,font);
            }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            string report="Font="+FontPath+"\nGlyphs="+font.characterTable.Count+
                "\nAtlases="+font.atlasTextures.Length+
                "\nHangulSyllables="+font.characterTable.Count(x=>x.unicode>=0xAC00 && x.unicode<=0xD7A3)+"/11172"+
                "\nMissingCount="+(missing==null?0:missing.Length)+
                "\nMissingPreview="+(missing==null?"":missing.Substring(0,Mathf.Min(120,missing.Length)))+"\n";
            File.WriteAllText(ReportPath,report);
            AssetDatabase.ImportAsset(ReportPath);
            Debug.Log("[HideAndShoot/TMP] Font baked: "+report);
            if(font.characterTable.Count<11000)
                throw new IOException("Font glyph bake incomplete: "+font.characterTable.Count);
            return font;
        }

        private static void ConvertHierarchy(GameObject root,TMP_FontAsset font,
            ref int labels,ref int inputs)
        {
            // Snapshot before destroying components; includes inactive controls.
            var oldInputs=root.GetComponentsInChildren<InputField>(true);
            var oldTexts=root.GetComponentsInChildren<Text>(true);
            foreach(var old in oldTexts)
            {
                var go=old.gameObject;
                if(go.GetComponent<TextMeshProUGUI>()!=null)
                {
                    UnityEngine.Object.DestroyImmediate(old,true);
                    labels++;
                    continue;
                }
                // Text and TextMeshProUGUI are both Graphic components: they
                // cannot coexist on one GameObject. Snapshot before removal.
                var value=old.text;
                var size=old.fontSize;
                var style=old.fontStyle;
                var align=old.alignment;
                var color=old.color;
                var raycast=old.raycastTarget;
                var rich=old.supportRichText;
                var wrap=old.horizontalOverflow;
                var vertical=old.verticalOverflow;
                var autosize=old.resizeTextForBestFit;
                var minSize=old.resizeTextMinSize;
                var maxSize=old.resizeTextMaxSize;
                UnityEngine.Object.DestroyImmediate(old,true);
                var text=go.AddComponent<TextMeshProUGUI>();
                if(text==null)throw new IOException("Cannot add TMP label to "+go.name);
                text.font=font;
                text.text=value;
                text.fontSize=size;
                text.fontStyle=style==FontStyle.Bold?FontStyles.Bold:
                    style==FontStyle.Italic?FontStyles.Italic:FontStyles.Normal;
                text.alignment=HideAndShoot.UI.TacticalUI.Align(align);
                text.color=color;
                text.raycastTarget=raycast;
                text.richText=rich;
                text.enableWordWrapping=wrap==HorizontalWrapMode.Wrap;
                text.overflowMode=vertical==VerticalWrapMode.Truncate?
                    TextOverflowModes.Truncate:TextOverflowModes.Overflow;
                text.enableAutoSizing=autosize;
                text.fontSizeMin=minSize;
                text.fontSizeMax=maxSize;
                labels++;
            }
            foreach(var old in oldInputs)
            {
                var go=old.gameObject;
                var valueText=go.transform.Find("ValueText")?.GetComponent<TextMeshProUGUI>();
                var placeholder=go.transform.Find("Placeholder")?.GetComponent<TextMeshProUGUI>();
                if(valueText==null)throw new IOException("Input missing ValueText: "+go.name);
                string value=old.text;
                bool interactive=old.interactable;
                int limit=old.characterLimit;
                var graphic=old.targetGraphic;
                var oldLineType=old.lineType;
                UnityEngine.Object.DestroyImmediate(old,true);
                var input=go.AddComponent<TMP_InputField>();
                input.targetGraphic=graphic;
                var area=new GameObject("TMPViewport",typeof(RectTransform),typeof(RectMask2D));
                area.transform.SetParent(go.transform,false);
                var viewport=(RectTransform)area.transform;
                viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;
                viewport.offsetMin=new Vector2(8f,2f);viewport.offsetMax=new Vector2(-8f,-2f);
                valueText.transform.SetParent(viewport,false);
                valueText.rectTransform.anchorMin=Vector2.zero;
                valueText.rectTransform.anchorMax=Vector2.one;
                valueText.rectTransform.offsetMin=Vector2.zero;
                valueText.rectTransform.offsetMax=Vector2.zero;
                if(placeholder!=null)
                {
                    placeholder.transform.SetParent(viewport,false);
                    placeholder.rectTransform.anchorMin=Vector2.zero;
                    placeholder.rectTransform.anchorMax=Vector2.one;
                    placeholder.rectTransform.offsetMin=Vector2.zero;
                    placeholder.rectTransform.offsetMax=Vector2.zero;
                }
                input.textViewport=viewport;
                input.textComponent=valueText;
                input.placeholder=placeholder;
                input.lineType=oldLineType==InputField.LineType.SingleLine?
                    TMP_InputField.LineType.SingleLine:TMP_InputField.LineType.MultiLineNewline;
                input.interactable=interactive;
                input.characterLimit=limit;
                input.text=value;
                inputs++;
            }
        }
    }
}
