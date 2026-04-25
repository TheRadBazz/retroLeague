#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class Startup
{
    static Startup()    
    {
        EditorPrefs.SetInt("showCounts_sportcarcgb14", EditorPrefs.GetInt("showCounts_sportcarcgb14") + 1);

        if (EditorPrefs.GetInt("showCounts_sportcarcgb14") == 1)       
        {
            Application.OpenURL("https://assetstore.unity.com/packages/slug/359718");
            // System.IO.File.Delete("Assets/SportCar/Racing_Game.cs");
        }
    }     
}
#endif
