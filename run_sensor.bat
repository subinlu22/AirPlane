@echo off
chcp 65001 >nul
rem 아두이노 MPU 센서 -> 유니티(UDP 5053) 전송기 실행
rem 실행 전: 아두이노 USB 연결, 아두이노 IDE 시리얼 모니터 닫기
cd /d "%~dp0Sensor"
"C:\Users\dev\venv311\Scripts\python.exe" mpu_receiver.py
pause
