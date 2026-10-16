import serial
import socket

# ── 설정 (포트가 바뀌면 여기만 고치면 됨) ──────────────
PORT = 'COM7'          # 아두이노 시리얼 포트 (장치 관리자 > 포트에서 확인)
BAUD = 115200          # 아두이노 Serial.begin 값과 같아야 함
UDP_IP = "127.0.0.1"   # 유니티가 같은 PC에서 실행 중
UDP_PORT = 5053        # 유니티 PlaneController.cs 의 port 와 같아야 함

# 아두이노 시리얼 연결
try:
    ser = serial.Serial(PORT, BAUD, timeout=1)
except serial.SerialException as e:
    print(f"[오류] {PORT} 를 열 수 없습니다: {e}")
    print("  - 아두이노 IDE의 시리얼 모니터가 켜져 있으면 닫고 다시 실행하세요.")
    print("  - 포트 번호가 바뀌었으면 위의 PORT 값을 고치세요 (장치 관리자 > 포트).")
    raise SystemExit(1)

# 유니티로 UDP 전송 설정
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

print(f"MPU 데이터 수신 시작... ({PORT} → UDP {UDP_IP}:{UDP_PORT})")

while True:
    line = ser.readline().decode('utf-8', errors='ignore').strip()
    if line and line.startswith("X:"):
        if line == "X:-1 Y:-1 Z:-1":
            continue   # 센서 응답 없음(접촉 불량) — 보내지 않고 마지막 자세 유지
        print(line)
        sock.sendto(line.encode(), (UDP_IP, UDP_PORT))
