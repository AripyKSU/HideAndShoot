exec(open('C:/UnityProject/HideAndShoot/AgentScripts/feature_core.py',encoding='utf-8').read().split("write('Assets/")[0])
p='Assets/HideAndShoot/Scripts/Player/PlayerHighlightTarget.cs';s=read(p)
s=replace(s,'public bool PreviewTarget;','public bool PreviewTarget;\n        public string PreviewNickname;')
s=replace(s,'            targets.Add(this);','            if(!targets.Contains(this))targets.Add(this);')
s=replace(s,'if(nameLabel!=null)Destroy(nameLabel.gameObject);\n            if(nameMaterial!=null)Destroy(nameMaterial);','if(nameLabel!=null)CoreUtils.Destroy(nameLabel.gameObject);\n            if(nameMaterial!=null)CoreUtils.Destroy(nameMaterial);')
s=replace(s,'            Vector3 headPosition=head!=null?', '''#if UNITY_EDITOR
            bool preview=camera==PreviewCamera && PreviewTarget && !string.IsNullOrEmpty(PreviewNickname);
            visible|=preview;
#endif
            Vector3 headPosition=head!=null?''')
s=replace(s,'nameLabel.fontSharedMaterial=nameMaterial;','''nameMaterial.SetColor("_OutlineColor",new Color(.015f,.025f,.035f,1));
                nameMaterial.SetFloat("_OutlineWidth",.15f);
                nameLabel.fontSharedMaterial=nameMaterial;nameLabel.UpdateMeshPadding();''')
s=replace(s,'            nameLabel.text=RelayLobbySession.Instance.GetRankedNickname(life.OwnerClientId);','''#if UNITY_EDITOR
            if(preview)nameLabel.text=PreviewNickname;
            else
#endif
                nameLabel.text=RelayLobbySession.Instance.GetRankedNickname(life.OwnerClientId);''')
write(p,s)
print('Nameplate preview checks and dark text edge added; edit-mode resource cleanup corrected.')