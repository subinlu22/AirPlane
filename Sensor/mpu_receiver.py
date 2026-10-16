import serial
import socket
import time

# ── 설정 (포트가 바뀌면 여기만 고치면 됨) ──────────────
PORT = 'COM7'          # 아두이노 시리얼 포트 (장치 관리자 > 포트에서 확인)
BAUD = 115200          # 아두이노 Serial.begin 값과 같아야 함
UDP_IP = "127.0.0.1"   # 유니티가 같은 PC에서 실행 중
UDP_PORT = 5053        # 유니티 PlaneController.cs 의 port 와 같아야 함 (센서 값)
STATUS_PORT = 5054     # 유니티 SensorHUD.cs 가 듣는 포트 (수신기·센서 상태 표시용)

# 유니티로 UDP 전송 설정
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)


def send_status(state, line=""):
    # HUD 용 상태: "STATUS OK X:.. Y:.. Z:.." / "STATUS NOSENSOR" / "STATUS NODATA"
    sock.sendto(f"STATUS {state} {line}".strip().encode(), (UDP_IP, STATUS_PORT))


def open_serial():
    # 아두이노 시리얼 연결 — 실패하면 1초마다 다시 시도 (그동안 HUD 에는 NO SERIAL DATA)
    warned = False
    while True:
        try:
            s = serial.Serial(PORT, BAUD, timeout=1)
            print(f"[연결] {PORT} 열림")
            return s
        except serial.SerialException as e:
            if not warned:
                print(f"[오류] {PORT} 를 열 수 없습니다: {e}")
                print("  - 아두이노 IDE의 시리얼 모니터가 켜져 있으면 닫으세요.")
                print("  - USB 를 다시 꽂거나, 포트 번호가 바뀌었으면 위의 PORT 값을 고치세요.")
                print("  - 1초마다 다시 연결을 시도합니다...")
                warned = True
            send_status("NODATA")
            time.sleep(1)


print(f"MPU 데이터 수신 시작... ({PORT} → UDP {UDP_IP}:{UDP_PORT}, 상태 → {STATUS_PORT})")
ser = open_serial()

last_warn = 0
while True:
    try:
        line = ser.readline().decode('utf-8', errors='ignore').strip()
    except serial.SerialException as e:
        # USB 가 순간적으로 끊김 (선이 흔들려 보드가 리셋되는 경우 등) — 죽지 않고 다시 연결
        print(f"[끊김] 시리얼 연결이 끊겼습니다: {e}")
        try:
            ser.close()
        except Exception:
            pass
        send_status("NODATA")
        time.sleep(1)
        ser = open_serial()
        continue

    if not line:
        send_status("NODATA")      # 1초 동안 아두이노에서 아무 줄도 안 옴
        continue
    if not line.startswith("X:"):
        continue
    if line == "X:-1 Y:-1 Z:-1":
        send_status("NOSENSOR")    # 센서 응답 없음(접촉 불량) — 값은 보내지 않고 마지막 자세 유지
        if time.time() - last_warn > 2:
            print("[경고] 센서 응답 없음 (X:-1) — SDA/SCL/VCC/GND 연결을 확인하세요")
            last_warn = time.time()
        continue
    print(line)
    sock.sendto(line.encode(), (UDP_IP, UDP_PORT))
    send_status("OK", line)