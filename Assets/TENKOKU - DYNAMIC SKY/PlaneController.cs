using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class PlaneController : MonoBehaviour
{
    // UDP 수신 설정
    UdpClient udpClient;
    Thread receiveThread;
    int port = 5053;

    // 센서값 저장 변수
    float rotX = 0f;
    float rotY = 0f;
    float rotZ = 0f;
    public float smoothSpeed = 4f;   // 클수록 빠르게 반응, 작을수록 부드러움
    public bool invertRoll = true;   // 좌우 기울기 방향 뒤집기 (손을 왼쪽으로 꺾으면 드론도 왼쪽)
    public bool invertPitch = false; // 앞뒤 기울기 방향 뒤집기

    // 비행 설정
    public float speed = 18f;          // 전진 속도 (m/s)
    public float turnRate = 1.2f;      // 좌우로 1° 기울일 때 초당 선회 각도 (30° 기울이면 초당 36°)
    public float turnDeadzone = 3f;    // 이 각도 이하로 기울이면 선회하지 않고 직진 (센서 떨림 무시)
    public float minAltitude = 120f;   // 고도 제한 (빌딩에 박히지 않게)
    public float maxAltitude = 600f;

    float heading;                     // 현재 진행 방향 (Y 회전, 도)
    float curBank, curNose;            // 부드럽게 따라가는 현재 기울기

    void Start()
    {
        heading = transform.eulerAngles.y;   // 씬에 놓인 방향에서 출발

        // UDP 소켓 열기
        udpClient = new UdpClient(port);
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void ReceiveData()
    {
        while (true)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);

                // "X:100 Y:200 Z:300" 파싱
                string[] parts = text.Split(' ');
                foreach (string part in parts)
                {
                    string[] kv = part.Split(':');
                    if (kv[0] == "X") rotX = float.Parse(kv[1]);
                    if (kv[0] == "Y") rotY = float.Parse(kv[1]);
                    if (kv[0] == "Z") rotZ = float.Parse(kv[1]);
                }
            }
            catch { }
        }
    }

    void Update()
    {
        // 평평할 때 기준값 빼주기 (오프셋 보정)
        float pitch = (rotX - (-2300)) / 16384f * 45f;
        float roll = (rotY - 0) / 16384f * 45f;

        // 비행기 회전 적용
        // pitch(센서 X) → 드론 좌우 기울기, roll(센서 Y) → 드론 앞뒤 기울기
        float bank = invertRoll ? pitch : -pitch;
        float nose = invertPitch ? -roll : roll;
        // 센서 값이 0.5초마다 오므로 목표 기울기로 부드럽게 따라감
        float follow = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        curBank = Mathf.Lerp(curBank, bank, follow);
        curNose = Mathf.Lerp(curNose, nose, follow);

        // 기울인 쪽으로 선회 (왼쪽으로 기울이면 왼쪽으로 돌아감)
        float turnBank = Mathf.Abs(curBank) > turnDeadzone ? curBank - Mathf.Sign(curBank) * turnDeadzone : 0f;
        heading -= turnBank * turnRate * Time.deltaTime;
        transform.rotation = Quaternion.Euler(curBank, heading, curNose);

        // 기수 방향(X축)으로 이동 — 기수를 들면 상승, 숙이면 하강
        transform.position += transform.right * speed * Time.deltaTime;
        Vector3 pos = transform.position;
        pos.y = Mathf.Clamp(pos.y, minAltitude, maxAltitude);
        transform.position = pos;
        
    }

    void OnDestroy()
    {
        receiveThread.Abort();
        udpClient.Close();
    }
}