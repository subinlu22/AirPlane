using UnityEngine;
using UnityEngine.UI;

// 드론에 달린 카메라 시점(FPV)을 화면 오른쪽 아래 미니 화면으로 보여줌
// 드론 기울기를 그대로 따라가서, 센서로 기울이면 미니 화면의 지평선도 같이 기움
public class DroneFPV : MonoBehaviour
{
    public Transform drone;                  // DroneRig (기수 = +X)
    public Transform mount;                  // 카메라 붙일 위치 (비우면 드론 모델의 camera_jnt 를 찾음)
    public float fieldOfView = 78f;
    public float tiltDown = 12f;             // 카메라를 살짝 아래로 (도)
    public float forwardOffset = 0.6f;       // 드론 몸체·짐벌이 화면을 가리지 않게 앞으로 뺀 거리 (m)
    public Vector2 size = new Vector2(480, 270);
    public Vector2 margin = new Vector2(24, 214);   // 오른쪽, 아래 (아래는 HUD 바 위로)

    Camera cam;
    RenderTexture rt;
    Image recDot;

    void Start()
    {
        if (drone == null) { var g = GameObject.Find("DroneRig"); if (g) drone = g.transform; }
        if (mount == null && drone != null)
            foreach (var t in drone.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("camera_jnt")) { mount = t; break; }
        if (mount == null) mount = drone;

        rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32) { name = "FPV_RT", antiAliasing = 2 };

        var camGO = new GameObject("FPV Camera");
        cam = camGO.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.fieldOfView = fieldOfView;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 30000f;
        cam.depth = -10;
        int minimapLayer = LayerMask.NameToLayer("Minimap");
        if (minimapLayer >= 0) cam.cullingMask &= ~(1 << minimapLayer);   // 미니맵 전용 저해상도 지도는 안 보이게
        var data = camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.volumeLayerMask = 0;            // 메인 화면용 아웃포커스·비네트는 적용 안 함 (거리 안개는 적용됨)

        // Cesium 은 기본적으로 메인 카메라가 보는 곳의 지도만 불러오므로 FPV 카메라도 등록
        var geo = FindFirstObjectByType<CesiumForUnity.CesiumGeoreference>();
        if (geo != null) CesiumForUnity.CesiumCameraManager.GetOrCreate(geo.gameObject).additionalCameras.Add(cam);

        BuildUI();
    }

    void LateUpdate()
    {
        if (cam == null || drone == null) return;
        Vector3 fwd = drone.right, up = drone.up;
        cam.transform.SetPositionAndRotation(
            mount.position + fwd * forwardOffset - up * 0.05f,
            Quaternion.LookRotation(fwd, up) * Quaternion.Euler(tiltDown, 0, 0));
        if (recDot) { var c = recDot.color; c.a = Mathf.PingPong(Time.unscaledTime, 1f) > 0.5f ? 1f : 0.25f; recDot.color = c; }
    }

    void OnDestroy()
    {
        if (rt != null) rt.Release();
    }

    void BuildUI()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGO = new GameObject("FPV Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 51;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 테두리 + 영상
        var frame = NewImage("FPV Frame", canvasGO.transform, new Color(0.35f, 0.85f, 1f, 0.9f));
        var frameRect = frame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = frameRect.pivot = new Vector2(1, 0);
        frameRect.anchoredPosition = new Vector2(-margin.x, margin.y);
        frameRect.sizeDelta = size + new Vector2(4, 4);

        var view = new GameObject("FPV View", typeof(RectTransform)).AddComponent<RawImage>();
        view.transform.SetParent(frameRect, false);
        view.texture = rt; view.raycastTarget = false;
        var viewRect = view.rectTransform;
        viewRect.anchorMin = Vector2.zero; viewRect.anchorMax = Vector2.one; viewRect.offsetMin = new Vector2(2, 2); viewRect.offsetMax = new Vector2(-2, -2);

        // 위쪽 라벨 띠
        var strip = NewImage("Label Strip", viewRect, new Color(0.03f, 0.05f, 0.08f, 0.82f)).rectTransform;
        strip.anchorMin = new Vector2(0, 1); strip.anchorMax = new Vector2(1, 1); strip.pivot = new Vector2(0.5f, 1);
        strip.sizeDelta = new Vector2(0, 34);
        recDot = NewImage("Rec", strip, new Color(1f, 0.3f, 0.3f));
        var dotRect = recDot.rectTransform;
        dotRect.anchorMin = dotRect.anchorMax = new Vector2(0, 0.5f); dotRect.sizeDelta = new Vector2(12, 12); dotRect.anchoredPosition = new Vector2(17, 0);
        var label = NewText(strip, "FPV  ·  DRONE CAM", 18, Color.white, font);
        label.rectTransform.offsetMin = new Vector2(34, 0);
        label.fontStyle = FontStyle.Bold;
        var live = NewText(strip, "LIVE ", 16, new Color(0.35f, 0.85f, 1f), font);
        live.fontStyle = FontStyle.Bold;
        live.alignment = TextAnchor.MiddleRight;
        live.rectTransform.offsetMax = new Vector2(-10, 0);

        // 가운데 조준선
        foreach (var s in new[] { new Vector2(18, 1), new Vector2(1, 18) })
        {
            var cross = NewImage("Cross", viewRect, new Color(1, 1, 1, 0.6f)).rectTransform;
            cross.anchorMin = cross.anchorMax = new Vector2(0.5f, 0.5f); cross.sizeDelta = s;
        }
    }

    static Image NewImage(string name, Transform parent, Color c)
    {
        var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = c; img.raycastTarget = false;
        return img;
    }

    static Text NewText(Transform parent, string s, int size, Color c, Font font)
    {
        var t = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size; t.color = c; t.text = s;
        t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false;
        var shadow = t.gameObject.AddComponent<Shadow>();   // 밝은 영상 위에서도 글씨가 또렷하게
        shadow.effectColor = new Color(0, 0, 0, 0.8f); shadow.effectDistance = new Vector2(1.2f, -1.2f);
        var textRect = t.rectTransform; textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        return t;
    }
}