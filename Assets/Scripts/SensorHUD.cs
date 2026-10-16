using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

// 화면 하단 HUD — 수신기·센서 연결 상태, 센서 원값, 자세(롤/피치), 좌표, 속도를 표시
// 연결 상태는 mpu_receiver.py 가 UDP 5054 로 보내는 "STATUS OK|NOSENSOR|NODATA ..." 로 판단
// (드론을 움직이는 센서 값은 PlaneController.cs 가 5053 으로 따로 받음 — 여기서는 건드리지 않음)
public class SensorHUD : MonoBehaviour
{
    public Transform drone;                 // 센서로 움직이는 DroneRig (비우면 이름으로 찾음)
    public int statusPort = 5054;
    public float barHeight = 132f;
    public float bottomMargin = 46f;        // 하단 Google/Cesium 출처 표시를 가리지 않게 띄움

    // ── 상태 수신 (별도 스레드) ──────────────────────
    UdpClient udp;
    Thread thread;
    readonly object lockObj = new object();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    string lastState = "";
    string lastLine = "";
    double lastStatusTime = -1;
    double lastOkTime = -1;
    int okCount;

    // ── UI ─────────────────────────────────────────
    static readonly Color Panel  = new Color(0.03f, 0.05f, 0.08f, 0.80f);
    static readonly Color Accent = new Color(0.35f, 0.85f, 1f, 0.9f);
    static readonly Color Green  = new Color(0.30f, 0.95f, 0.55f);
    static readonly Color Amber  = new Color(1f, 0.72f, 0.25f);
    static readonly Color Red    = new Color(1f, 0.35f, 0.35f);
    static readonly Color Dim    = new Color(0.55f, 0.63f, 0.74f);
    const string DimHex = "#8C9FB8";

    Font font;
    Image recvDot, sensorDot;
    Text recvText, sensorText, hintText, linkText, rawText, attText, posText, flightText;
    RectTransform horizonRoot, horizon;

    CesiumForUnity.CesiumGeoreference geo;
    float hz, hzTimer, speed;
    int prevOk;
    Vector3 prevPos;

    void Start()
    {
        if (drone == null) { var g = GameObject.Find("DroneRig"); if (g) drone = g.transform; }
        geo = FindFirstObjectByType<CesiumForUnity.CesiumGeoreference>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
        if (drone) prevPos = drone.position;

        try
        {
            udp = new UdpClient(statusPort);
            thread = new Thread(Receive) { IsBackground = true };
            thread.Start();
        }
        catch (Exception e) { Debug.LogWarning($"[SensorHUD] 상태 포트 {statusPort} 를 열 수 없음: {e.Message}"); }
    }

    void Receive()
    {
        while (true)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                string msg = Encoding.UTF8.GetString(udp.Receive(ref ep));
                if (!msg.StartsWith("STATUS")) continue;
                string[] parts = msg.Split(new[] { ' ' }, 3);
                double now = clock.Elapsed.TotalSeconds;
                lock (lockObj)
                {
                    lastState = parts.Length > 1 ? parts[1] : "";
                    if (parts.Length > 2) lastLine = parts[2];
                    lastStatusTime = now;
                    if (lastState == "OK") { okCount++; lastOkTime = now; }
                }
            }
            catch { break; }
        }
    }

    void Update()
    {
        string st, line; double lastStatus, lastOk; int ok;
        lock (lockObj) { st = lastState; line = lastLine; lastStatus = lastStatusTime; lastOk = lastOkTime; ok = okCount; }
        double now = clock.Elapsed.TotalSeconds;
        bool online = lastStatus >= 0 && now - lastStatus < 2.5;
        float pulse = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));

        hzTimer += Time.unscaledDeltaTime;
        if (hzTimer >= 1f) { hz = (ok - prevOk) / hzTimer; prevOk = ok; hzTimer = 0f; }

        // 수신기 / 센서 상태
        SetDot(recvDot, online ? Green : Red, online ? pulse : 1f);
        recvText.text = $"<color={DimHex}>RECEIVER</color>   {(online ? "ONLINE" : "OFFLINE")}";
        if (!online)
        {
            SetDot(sensorDot, Dim, 0.5f);
            sensorText.text = $"<color={DimHex}>SENSOR</color>       —";
            hintText.text = "START  run_sensor.bat";
            hintText.color = Amber;
        }
        else if (st == "OK")
        {
            SetDot(sensorDot, Green, pulse);
            sensorText.text = $"<color={DimHex}>SENSOR</color>       OK";
            hintText.text = "MPU-9265  ·  COM7";
            hintText.color = Dim;
        }
        else if (st == "NOSENSOR")
        {
            SetDot(sensorDot, Red, pulse);
            sensorText.text = $"<color={DimHex}>SENSOR</color>       NO RESPONSE";
            hintText.text = "CHECK WIRING  SDA·SCL·VCC·GND";
            hintText.color = Red;
        }
        else
        {
            SetDot(sensorDot, Amber, pulse);
            sensorText.text = $"<color={DimHex}>SENSOR</color>       NO SERIAL DATA";
            hintText.text = "CHECK ARDUINO USB";
            hintText.color = Amber;
        }

        // 링크
        string age = lastOk < 0 ? "—" : $"{now - lastOk:0.0} s";
        linkText.text = $"<color={DimHex}>RATE</color>   {hz:0.0} Hz\n<color={DimHex}>LAST</color>   {age}\n<color={DimHex}>UDP</color>    5053 · 5054";

        // 센서 원값
        int x = 0, y = 0, z = 0;
        foreach (var kv in line.Split(' '))
        {
            var p = kv.Split(':');
            if (p.Length != 2) continue;
            int.TryParse(p[1], out int v);
            if (p[0] == "X") x = v; else if (p[0] == "Y") y = v; else if (p[0] == "Z") z = v;
        }
        rawText.text = line.Length == 0
            ? $"<color={DimHex}>X</color>   —\n<color={DimHex}>Y</color>   —\n<color={DimHex}>Z</color>   —"
            : $"<color={DimHex}>X</color>   {x,7:+0;-0;0}\n<color={DimHex}>Y</color>   {y,7:+0;-0;0}\n<color={DimHex}>Z</color>   {z,7:+0;-0;0}";

        if (drone == null) return;

        // 자세 (기수 = DroneRig 의 +X)
        Vector3 nose = drone.right, up = drone.up;
        Vector3 wingRight = Vector3.Cross(up, nose);
        float pitch = Mathf.Asin(Mathf.Clamp(nose.y, -1f, 1f)) * Mathf.Rad2Deg;
        float roll = Mathf.Atan2(-wingRight.y, up.y) * Mathf.Rad2Deg;
        attText.text = $"<color={DimHex}>ROLL</color>    {roll,6:+0.0;-0.0;0.0}°\n<color={DimHex}>PITCH</color>   {pitch,6:+0.0;-0.0;0.0}°";
        horizonRoot.localRotation = Quaternion.Euler(0, 0, roll);
        horizon.anchoredPosition = new Vector2(0, -pitch * 1.6f);

        // 속도 / 방위
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float inst = (drone.position - prevPos).magnitude / dt;
        speed = Mathf.Lerp(speed, inst, 0.1f);
        prevPos = drone.position;
        float hdg = (Mathf.Atan2(nose.x, nose.z) * Mathf.Rad2Deg + 360f) % 360f;

        // 좌표 (Cesium: 유니티 좌표 → 지구 중심 좌표 → 위도·경도·고도)
        if (geo != null)
        {
            double3 ecef = geo.TransformUnityPositionToEarthCenteredEarthFixed(new double3(drone.position.x, drone.position.y, drone.position.z));
            double3 llh = CesiumForUnity.CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
            posText.text = $"<color={DimHex}>LAT</color>   {Math.Abs(llh.y):0.00000}° {(llh.y >= 0 ? "N" : "S")}\n" +
                           $"<color={DimHex}>LON</color>   {Math.Abs(llh.x):0.00000}° {(llh.x >= 0 ? "E" : "W")}\n" +
                           $"<color={DimHex}>ALT</color>   {llh.z:0.0} m";
        }
        flightText.text = $"<color={DimHex}>SPD</color>   {speed:0.0} m/s\n<color={DimHex}>HDG</color>   {hdg:000}°\n<color={DimHex}>MODE</color>  SENSOR";
    }

    void OnDestroy()
    {
        try { udp?.Close(); } catch { }
    }

    // ── UI 만들기 ───────────────────────────────────
    void BuildUI()
    {
        var canvasGO = new GameObject("SensorHUD Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var bar = NewImage("Bar", canvasGO.transform, Panel).rectTransform;
        bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
        bar.offsetMin = new Vector2(24, bottomMargin); bar.offsetMax = new Vector2(-24, bottomMargin + barHeight);

        var accent = NewImage("AccentLine", bar, Accent).rectTransform;
        accent.anchorMin = new Vector2(0, 1); accent.anchorMax = new Vector2(1, 1); accent.pivot = new Vector2(0.5f, 1);
        accent.sizeDelta = new Vector2(0, 2);

        var row = NewRect("Sections", bar);
        row.anchorMin = Vector2.zero; row.anchorMax = Vector2.one; row.offsetMin = Vector2.zero; row.offsetMax = new Vector2(0, -2);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;

        // 1. 연결 상태
        var s1 = Section(row, "LINK STATUS", 1.5f);
        recvDot = DotRow(s1, out recvText);
        sensorDot = DotRow(s1, out sensorText);
        hintText = NewText(s1, "", 14, Dim);
        Divider(row);
        // 2. 통신
        linkText = NewText(Section(row, "TELEMETRY", 1.0f), "", 17, Color.white);
        Divider(row);
        // 3. 센서 원값
        rawText = NewText(Section(row, "RAW ACCEL  (MPU)", 1.0f), "", 17, Color.white);
        Divider(row);
        // 4. 자세
        var s4 = Section(row, "ATTITUDE", 1.4f);
        var attRow = NewRect("AttRow", s4);
        var ah = attRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        ah.spacing = 16; ah.childAlignment = TextAnchor.MiddleLeft;
        ah.childControlWidth = true; ah.childControlHeight = true; ah.childForceExpandWidth = false; ah.childForceExpandHeight = false;
        attRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 78;
        BuildIndicator(attRow);
        attText = NewText(attRow, "", 18, Color.white);
        Divider(row);
        // 5. 좌표
        posText = NewText(Section(row, "POSITION  (WGS84)", 1.4f), "", 17, Color.white);
        Divider(row);
        // 6. 비행
        flightText = NewText(Section(row, "FLIGHT", 1.0f), "", 17, Color.white);
    }

    // 자세계: 하늘/땅이 롤만큼 돌고 피치만큼 위아래로 움직임, 가운데 노란 기체 표시는 고정
    void BuildIndicator(Transform parent)
    {
        var frame = NewImage("Indicator", parent, new Color(1, 1, 1, 0.25f));
        var le = frame.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 78; le.preferredHeight = 78; le.flexibleWidth = 0;
        var inner = NewImage("Inner", frame.transform, Color.black).rectTransform;
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one; inner.offsetMin = new Vector2(1, 1); inner.offsetMax = new Vector2(-1, -1);
        inner.gameObject.AddComponent<RectMask2D>();

        horizonRoot = NewRect("HorizonRoot", inner);
        horizonRoot.sizeDelta = new Vector2(240, 240);
        horizon = NewRect("Horizon", horizonRoot);
        horizon.sizeDelta = new Vector2(240, 240);
        var sky = NewImage("Sky", horizon, new Color(0.13f, 0.32f, 0.52f)).rectTransform;
        sky.anchorMin = new Vector2(0, 0.5f); sky.anchorMax = Vector2.one; sky.offsetMin = sky.offsetMax = Vector2.zero;
        var ground = NewImage("Ground", horizon, new Color(0.40f, 0.26f, 0.14f)).rectTransform;
        ground.anchorMin = Vector2.zero; ground.anchorMax = new Vector2(1, 0.5f); ground.offsetMin = ground.offsetMax = Vector2.zero;
        NewImage("HorizonLine", horizon, Color.white).rectTransform.sizeDelta = new Vector2(240, 2);
        for (int i = -2; i <= 2; i++)          // 피치 눈금 (10° 간격)
        {
            if (i == 0) continue;
            var tick = NewImage("Tick", horizon, new Color(1, 1, 1, 0.6f)).rectTransform;
            tick.sizeDelta = new Vector2(i % 2 == 0 ? 26 : 14, 1);
            tick.anchoredPosition = new Vector2(0, i * 10 * 1.6f);
        }
        foreach (float xPos in new[] { -18f, 18f })
        {
            var w = NewImage("Wing", inner, Amber).rectTransform;
            w.sizeDelta = new Vector2(20, 3); w.anchoredPosition = new Vector2(xPos, 0);
        }
        NewImage("Center", inner, Amber).rectTransform.sizeDelta = new Vector2(5, 5);
    }

    RectTransform Section(Transform parent, string title, float flex)
    {
        var rt = NewRect(title, parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = flex; le.minWidth = 120;
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(20, 12, 14, 10); v.spacing = 5;
        v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        NewText(rt, title, 12, Accent).fontStyle = FontStyle.Bold;
        return rt;
    }

    void Divider(Transform parent)
    {
        var le = NewImage("Divider", parent, new Color(1, 1, 1, 0.08f)).gameObject.AddComponent<LayoutElement>();
        le.minWidth = 1; le.preferredWidth = 1; le.flexibleWidth = 0;
    }

    Image DotRow(Transform parent, out Text label)
    {
        var r = NewRect("Row", parent);
        var hl = r.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 10; hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        var dot = NewImage("Dot", r, Dim);
        var le = dot.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 10; le.preferredHeight = 10; le.flexibleWidth = 0;
        label = NewText(r, "", 18, Color.white);
        return dot;
    }

    static void SetDot(Image dot, Color c, float alpha) { c.a = alpha; dot.color = c; }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Image NewImage(string name, Transform parent, Color c)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    Text NewText(Transform parent, string s, int size, Color c)
    {
        var t = NewRect("Text", parent).gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.color = c; t.text = s;
        t.supportRichText = true; t.raycastTarget = false;
        t.alignment = TextAnchor.UpperLeft;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.lineSpacing = 1.1f;
        return t;
    }
}