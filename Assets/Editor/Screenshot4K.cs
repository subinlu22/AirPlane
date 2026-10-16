using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Tools > Screenshot 4K  (단축키 Ctrl + Shift + K)
// Main Camera 화면을 3840x2160 PNG 로 저장. Play 중에도, 정지 상태에서도 됨.
// 저장 위치: 프로젝트 폴더/Screenshots
public static class Screenshot4K
{
    const int W = 3840, H = 2160;

    [MenuItem("Tools/Screenshot 4K %#k")]
    static void Capture()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[Screenshot4K] Main Camera 를 못 찾았어요. 카메라 Tag 가 MainCamera 인지 확인하세요.");
            return;
        }

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            antiAliasing = 8
        };
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());

        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);

        Debug.Log($"[Screenshot4K] 저장: {path}");
        EditorUtility.RevealInFinder(path);
    }
}
