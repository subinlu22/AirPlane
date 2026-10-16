#include <Wire.h>

#define MPU_ADDR 0x68  // 방금 찾은 주소

void setup() {
  Serial.begin(115200);
  delay(1000);
  Wire.begin();
  
  // MPU 슬립 모드 해제
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(0x6B);
  Wire.write(0);
  Wire.endTransmission();
  
  Serial.println("MPU-9265 시작");
}

void loop() {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(0x3B);
  Wire.endTransmission(false);
  Wire.requestFrom(MPU_ADDR, 6, true);

  int16_t ax = Wire.read() << 8 | Wire.read();
  int16_t ay = Wire.read() << 8 | Wire.read();
  int16_t az = Wire.read() << 8 | Wire.read();

  Serial.print("X:"); Serial.print(ax);
  Serial.print(" Y:"); Serial.print(ay);
  Serial.print(" Z:"); Serial.println(az);

  delay(500);
}