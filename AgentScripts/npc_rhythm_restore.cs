string previous=UnityEditor.SessionState.GetString("NpcRhythm.PreviousScene","");
if(!UnityEditor.EditorApplication.isPlaying && !UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty && !string.IsNullOrEmpty(previous))
 UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previous,UnityEditor.SceneManagement.OpenSceneMode.Single);
UnityEditor.SessionState.EraseString("NpcRhythm.PreviousScene");
return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;