from pathlib import Path
p=Path('C:/UnityProject/HideAndShoot/AgentScripts/outline_checks.cs')
s=p.read_text(encoding='utf-8')
a=s.index(' var root=new UnityEngine.GameObject("OutlineTestPlayer")');b=s.index(' target=root.AddComponent',a)
s=s[:a]+''' var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab");
 var root=UnityEngine.Object.Instantiate(prefab);root.name="ActualSkinnedPlayer";root.transform.position=new UnityEngine.Vector3(0,-.9f,4);
 var body=root.transform.Find("WorldVisual");
 bodyMaterial=new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"));bodyMaterial.SetColor("_BaseColor",new UnityEngine.Color(.5f,.02f,.02f,1));
 foreach(var renderer in body.GetComponentsInChildren<UnityEngine.Renderer>(true))
 {renderer.gameObject.layer=31;renderer.sharedMaterial=bodyMaterial;}
 var animator=body.GetComponentInChildren<UnityEngine.Animator>(true);if(animator!=null){animator.Rebind();animator.Update(0);}
'''+s[b:]
s=s.replace('target.PreviewTarget=true;','target.PreviewTarget=true;target.PreviewNickname="관전_Player";')
s=s.replace('  camera.Render();','  UnityEngine.Physics.SyncTransforms();camera.Render();')
s=s.replace('"Outline_Visible"','"Skinned_Name_Visible"').replace('"Outline_Hidden"','"Skinned_Name_Hidden"').replace('"Outline_Partial"','"Skinned_Name_Partial"')
s=s.replace('wall.SetActive(false);int visible=Capture("Skinned_Name_Visible");','wall.SetActive(false);int visible=Capture("Skinned_Name_Visible");\n if(root.transform.Find("SpectatorNickname")==null || !root.transform.Find("SpectatorNickname").gameObject.activeSelf)throw new System.Exception("Visible name missing");')
s=s.replace('wall.SetActive(true);int hidden=Capture("Skinned_Name_Hidden");','wall.SetActive(true);int hidden=Capture("Skinned_Name_Hidden");\n if(root.transform.Find("SpectatorNickname").gameObject.activeSelf)throw new System.Exception("Name leaked behind wall");')
(p.parent/'skinned_checks.cs').write_text(s,encoding='utf-8')