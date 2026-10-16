using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 위에서 내려다보는 미니맵 (내비게이션 / 디지털트윈 관제 화면 느낌)
// - 드론 위 높은 곳에 직교 카메라를 두고 Cesium 지도(구글 3D)를 수직으로 내려다봄
// - 북쪽이 위, 드론은 항상 가운데. 비행 방향 화살표 + 지나온 경로 + 축척 + 방위 표시
public class DroneMinimap : MonoBehaviour
{
    public Transform drone;                    // DroneRig (기수 = +X)
    public string areaName = "MANHATTAN, NEW YORK";
    public float viewRadius = 450f;            // 지도 반경 (m) — 클수록 넓게 보임
    public float cameraHeight = 2000f;         // 드론 위 카메라 높이 (m)
    public float mapSize = 400f;               // 화면에 보이는 지도 크기 (px, 1920x1080 기준)
    public Vector2 margin = new Vector2(24, 24);   // 오른쪽, 위
    public float trailInterval = 0.5f;         // 경로 점 찍는 간격 (초)
    public int trailLength = 80;
    public float mapDetail = 48f;              // 미니맵 전용 지도의 거칠기 (클수록 가볍고 흐림)

    static readonly Color Accent = new Color(0.35f, 0.85f, 1f, 0.95f);
    static readonly Color Panel  = new Color(0.03f, 0.05f, 0.08f, 0.88f);

    Camera cam;
    RenderTexture rt;
    RectTransform mapRect, marker, pulse;
    Text coordText;
    readonly Queue<Vector3> trail = new Queue<Vector3>();
    readonly List<Image> trailDots = new List<Image>();
    float trailTimer;
    CesiumForUnity.CesiumGeoreference geo;

    void Start()
    {
        if (drone == null) { var g = GameObject.Find("DroneRig"); if (g) drone = g.transform; }
        geo = FindFirstObjectByType<CesiumForUnity.CesiumGeoreference>();

        rt = new RenderTexture(640, 640, 24, RenderTextureFormat.ARGB32) { name = "Minimap_RT", antiAliasing = 2 };
        var camGO = new GameObject("Minimap Camera");
        cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = viewRadius;
        cam.nearClipPlane = 10f;
        cam.farClipPlane = cameraHeight + 3000f;
        cam.targetTexture = rt;
        cam.depth = -11;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.08f, 0.11f);
        var data = camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        // 미니맵 전용 저해상도 지도: 메인 지도를 위에서 넓게 보면 너무 많은 타일을 요구해서 따로 둠
        // (Minimap 레이어로 분리 — 미니맵 카메라는 이 지도만, 메인·FPV 카메라는 이 지도를 안 봄)
        int layer = LayerMask.NameToLayer("Minimap");
        if (geo != null && layer >= 0)
        {
            var main = FindFirstObjectByType<CesiumForUnity.Cesium3DTileset>();
            var tilesGO = new GameObject("Minimap Tiles") { layer = layer };
            tilesGO.transform.SetParent(geo.transform, false);
            var manager = tilesGO.AddComponent<CesiumForUnity.CesiumCameraManager>();   // 이 지도는 미니맵 카메라만 기준으로 불러옴
            manager.useMainCamera = false;
            manager.useSceneViewCameraInEditor = false;
            manager.additionalCameras.Add(cam);
            var tiles = tilesGO.AddComponent<CesiumForUnity.Cesium3DTileset>();
            tiles.tilesetSource = CesiumForUnity.CesiumDataSource.FromCesiumIon;
            tiles.ionAssetID = main != null ? main.ionAssetID : 2275207;
            tiles.maximumScreenSpaceError = mapDetail;
            tiles.createPhysicsMeshes = false;
            tiles.showCreditsOnScreen = false;    // 출처는 메인 지도가 화면에 표시
            cam.cullingMask = 1 << layer;
            foreach (var c in Camera.allCameras) if (c != cam) c.cullingMask &= ~(1 << layer);
            var fpvCam = GameObject.Find("FPV Camera");
            if (fpvCam) fpvCam.GetComponent<Camera>().cullingMask &= ~(1 << layer);
        }

        BuildUI();
    }

    void LateUpdate()
    {
        if (cam == null || drone == null) return;
        Vector3 p = drone.position;
        cam.transform.SetPositionAndRotation(new Vector3(p.x, p.y + cameraHeight, p.z), Quaternion.Euler(90f, 0f, 0f));   // 북쪽(+Z)이 위

        // 비행 방향 화살표
        Vector3 nose = drone.right;
        float hdg = Mathf.Atan2(nose.x, nose.z) * Mathf.Rad2Deg;
        marker.localRotation = Quaternion.Euler(0, 0, -hdg);

        // 펄스
        float t = Mathf.Repeat(Time.unscaledTime, 1.6f) / 1.6f;
        pulse.localScale = Vector3.one * Mathf.Lerp(0.6f, 2.4f, t);
        var pc = pulse.GetComponent<Image>().color; pc.a = (1f - t) * 0.6f; pulse.GetComponent<Image>().color = pc;

        // 지나온 경로
        trailTimer += Time.deltaTime;
        if (trailTimer >= trailInterval)
        {
            trailTimer = 0f;
            trail.Enqueue(p);
            while (trail.Count > trailLength) trail.Dequeue();
        }
        int i = 0;
        foreach (var tp in trail)
        {
            var dot = trailDots[i];
            Vector2 local = new Vector2(tp.x - p.x, tp.z - p.z) / viewRadius * (mapSize * 0.5f);
            bool inside = Mathf.Abs(local.x) < mapSize * 0.5f - 4 && Mathf.Abs(local.y) < mapSize * 0.5f - 4;
            dot.enabled = inside;
            dot.rectTransform.anchoredPosition = local;
            var c = dot.color; c.a = 0.25f + 0.65f * ((float)i / Mathf.Max(1, trail.Count - 1)); dot.color = c;
            i++;
        }
        for (; i < trailDots.Count; i++) trailDots[i].enabled = false;

        // 좌표
        if (geo != null && coordText != null)
        {
            var ecef = geo.TransformUnityPositionToEarthCenteredEarthFixed(new Unity.Mathematics.double3(p.x, p.y, p.z));
            var llh = CesiumForUnity.CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
            coordText.text = $"{System.Math.Abs(llh.y):0.0000}° {(llh.y >= 0 ? "N" : "S")}   {System.Math.Abs(llh.x):0.0000}° {(llh.x >= 0 ? "E" : "W")}";
        }
    }

    void OnDestroy()
    {
        if (rt != null) rt.Release();
    }

    // ── UI ─────────────────────────────────────────
    void BuildUI()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGO = new GameObject("Minimap Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 52;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        const float header = 34f, footer = 30f;
        var frame = NewImage("Minimap Frame", canvasGO.transform, Accent).rectTransform;
        frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(1, 1);
        frame.anchoredPosition = new Vector2(-margin.x, -margin.y);
        frame.sizeDelta = new Vector2(mapSize + 4, mapSize + header + footer + 4);

        var bg = NewImage("Panel", frame, Panel).rectTransform;
        Stretch(bg, 2, 2, -2, -2);

        // 위쪽 제목 띠
        var head = NewRect("Header", bg);
        head.anchorMin = new Vector2(0, 1); head.anchorMax = new Vector2(1, 1); head.pivot = new Vector2(0.5f, 1);
        head.sizeDelta = new Vector2(0, header);
        var title = NewText(head, "MAP  ·  " + areaName, 17, Color.white, font, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold; title.rectTransform.offsetMin = new Vector2(14, 0);
        var mode = NewText(head, "TOP VIEW ", 14, Accent, font, TextAnchor.MiddleRight);
        mode.fontStyle = FontStyle.Bold; mode.rectTransform.offsetMax = new Vector2(-10, 0);

        // 지도 영상
        var map = new GameObject("Map", typeof(RectTransform)).AddComponent<RawImage>();
        map.transform.SetParent(bg, false);
        map.texture = rt; map.raycastTarget = false;
        mapRect = map.rectTransform;
        mapRect.anchorMin = mapRect.anchorMax = new Vector2(0.5f, 1); mapRect.pivot = new Vector2(0.5f, 1);
        mapRect.anchoredPosition = new Vector2(0, -header);
        mapRect.sizeDelta = new Vector2(mapSize, mapSize);
        map.gameObject.AddComponent<RectMask2D>();

        // 격자 (디지털트윈 느낌)
        for (int k = 1; k < 4; k++)
        {
            var gx = NewImage("GridX", mapRect, new Color(0.35f, 0.85f, 1f, 0.12f)).rectTransform;
            gx.anchorMin = new Vector2(k / 4f, 0); gx.anchorMax = new Vector2(k / 4f, 1); gx.sizeDelta = new Vector2(1, 0);
            var gy = NewImage("GridY", mapRect, new Color(0.35f, 0.85f, 1f, 0.12f)).rectTransform;
            gy.anchorMin = new Vector2(0, k / 4f); gy.anchorMax = new Vector2(1, k / 4f); gy.sizeDelta = new Vector2(0, 1);
        }

        // 경로 점
        for (int k = 0; k < trailLength; k++)
        {
            var d = NewImage("Trail", mapRect, new Color(1f, 0.78f, 0.25f, 0.8f));
            d.rectTransform.sizeDelta = new Vector2(7, 7);
            d.enabled = false;
            trailDots.Add(d);
        }

        // 드론 표시 (펄스 + 화살표)
        var circle = MakeCircleSprite();
        var pulseImg = NewImage("Pulse", mapRect, new Color(0.35f, 0.85f, 1f, 0.5f));
        pulseImg.sprite = circle; pulse = pulseImg.rectTransform; pulse.sizeDelta = new Vector2(28, 28);
        var arrow = NewImage("DroneMarker", mapRect, new Color(1f, 0.85f, 0.2f));
        arrow.sprite = MakeArrowSprite(); marker = arrow.rectTransform; marker.sizeDelta = new Vector2(26, 26);
        var arrowShadow = arrow.gameObject.AddComponent<Shadow>(); arrowShadow.effectColor = new Color(0, 0, 0, 0.8f); arrowShadow.effectDistance = new Vector2(1.5f, -1.5f);

        // 방위 N
        var north = NewImage("North", mapRect, new Color(0.03f, 0.05f, 0.08f, 0.8f)).rectTransform;
        north.anchorMin = north.anchorMax = new Vector2(1, 1); north.pivot = new Vector2(1, 1);
        north.anchoredPosition = new Vector2(-10, -10); north.sizeDelta = new Vector2(34, 34);
        NewText(north, "N ▲", 14, Color.white, font, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;

        // 축척 막대 (100 m)
        float px100 = 100f / viewRadius * (mapSize * 0.5f);
        var bar = NewImage("ScaleBar", mapRect, Color.white).rectTransform;
        bar.anchorMin = bar.anchorMax = new Vector2(0, 0); bar.pivot = new Vector2(0, 0);
        bar.anchoredPosition = new Vector2(14, 14); bar.sizeDelta = new Vector2(px100, 3);
        var scaleText = NewText(mapRect, "100 m", 14, Color.white, font, TextAnchor.LowerLeft);
        scaleText.fontStyle = FontStyle.Bold;
        var sr = scaleText.rectTransform; sr.anchorMin = sr.anchorMax = new Vector2(0, 0); sr.pivot = new Vector2(0, 0);
        sr.anchoredPosition = new Vector2(14, 20); sr.sizeDelta = new Vector2(120, 20);

        // 아래 좌표 띠
        var foot = NewRect("Footer", bg);
        foot.anchorMin = Vector2.zero; foot.anchorMax = new Vector2(1, 0); foot.pivot = new Vector2(0.5f, 0);
        foot.sizeDelta = new Vector2(0, footer);
        coordText = NewText(foot, "", 15, new Color(0.75f, 0.82f, 0.92f), font, TextAnchor.MiddleCenter);
        coordText.fontStyle = FontStyle.Bold;
    }

    static Sprite MakeArrowSprite()
    {
        const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // 위쪽을 가리키는 화살표 (끝이 파인 삼각형)
                float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n;
                bool inTri = v > 0.05f && v < 0.95f && Mathf.Abs(u) < (0.95f - v) * 0.5f;
                bool notch = v < 0.35f && Mathf.Abs(u) < (0.35f - v) * 0.6f;
                px[y * n + x] = inTri && !notch ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    static Sprite MakeCircleSprite()
    {
        const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                byte a = (byte)(Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) * 12f) * 255);   // 링
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    static void Stretch(RectTransform r, float l, float b, float rgt, float t)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(rgt, t);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image NewImage(string name, Transform parent, Color c)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    static Text NewText(Transform parent, string s, int size, Color c, Font font, TextAnchor align)
    {
        var t = NewRect("Text", parent).gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.color = c; t.text = s; t.alignment = align; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        Stretch(t.rectTransform, 0, 0, 0, 0);
        var shadow = t.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.8f); shadow.effectDistance = new Vector2(1.2f, -1.2f);
        return t;
    }
}