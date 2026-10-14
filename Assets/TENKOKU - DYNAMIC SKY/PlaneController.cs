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

    void Start()
    {
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
        transform.rotation = Quaternion.Euler(-pitch, -90f, roll);

        // 비행기 정면(X축)으로 이동
        float speed = 10f;
        transform.position += transform.right * speed * Time.deltaTime;
    }

    void OnDestroy()
    {
        receiveThread.Abort();
        udpClient.Close();
    }
}