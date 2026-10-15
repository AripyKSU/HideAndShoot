if(UnityEditor.EditorApplication.isPlaying)throw new System.Exception("Already playing");
for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new System.Exception("Preserve unsaved scenes before changing the verification scene");
UnityEditor.SessionState.SetString("NpcRhythm.PreviousScene",UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/HideAndShoot/Scenes/GameScene.unity",UnityEditor.SceneManagement.OpenSceneMode.Single);
return new {scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,dirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty};