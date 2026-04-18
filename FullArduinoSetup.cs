#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <TinyGPSPlus.h>
#include <Firebase_ESP_Client.h>

#include "addons/TokenHelper.h"
#include "addons/RTDBHelper.h"

//////////////////// WIFI ////////////////////

#define WIFI_SSID ""
#define WIFI_PASSWORD ""

//////////////////// TRACKER ////////////////////

const char* TRACKER_ID = "Tracker_1";

//////////////////// NGROK BACKEND ////////////////////

const char* NGROK_BASE_URL = "https://dismount-tactics-parasail.ngrok-free.dev";
const char* TRACKER_ROUTE = "/tracker/register";   

//////////////////// FIREBASE ////////////////////

#define API_KEY "AIzaSyC11Powwf8kRfCg2d8vjS3qi1QPNCKka7Y"
#define PROJECT_ID "car-tracker-iot"

FirebaseData fbdo;
FirebaseAuth auth;
FirebaseConfig config;
bool signupOK = false;

//////////////////// GPS ////////////////////

TinyGPSPlus gps;
HardwareSerial gpsSerial(1);

#define GPS_RX 16
#define GPS_TX 17

//////////////////// TIMERS ////////////////////

unsigned long lastSend = 0;
unsigned long lastGpsPrint = 0;
unsigned long bootTime = 0;

//////////////////// DECLARATIONS ////////////////////

void connectWiFi();
void readGPS();
void sendToFirebase(float lat, float lng);
void sendTrackerInfoToBackend();
String getGpsStatus();
int getSatellites();

//////////////////// SETUP ////////////////////

void setup() {
  Serial.begin(115200);
  delay(1000);

  gpsSerial.begin(9600, SERIAL_8N1, GPS_RX, GPS_TX);

  connectWiFi();

  config.api_key = API_KEY;
  config.token_status_callback = tokenStatusCallback;

  if (Firebase.signUp(&config, &auth, "", "")) {
    signupOK = true;
    Serial.println("FB signup OK");
  } else {
    signupOK = false;
    Serial.println("FB signup FAIL:");
    Serial.println(config.signer.signupError.message.c_str());
  }

  Firebase.begin(&config, &auth);
  Firebase.reconnectWiFi(true);

  bootTime = millis();
  Serial.println("Ready");
}

//////////////////// MAIN LOOP ////////////////////

void loop() {
  if (WiFi.status() != WL_CONNECTED) {
    connectWiFi();
  }

  readGPS();
}

//////////////////// WIFI ////////////////////

void connectWiFi() {
  WiFi.mode(WIFI_STA);
  WiFi.disconnect(true, true);
  delay(1000);

  Serial.print("Connecting to: ");
  Serial.println(WIFI_SSID);

  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  int tries = 0;
  while (WiFi.status() != WL_CONNECTED && tries < 40) {
    delay(500);
    Serial.print(".");
    tries++;
  }

  Serial.println();

  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("WiFi OK");
    Serial.print("IP: ");
    Serial.println(WiFi.localIP());
  } else {
    Serial.print("WiFi failed, status = ");
    Serial.println(WiFi.status());
  }
}

//////////////////// GPS HELPERS ////////////////////

String getGpsStatus() {
  if (!gps.location.isValid()) return "SEARCHING";
  if (getSatellites() >= 4) return "GOOD FIX";
  return "WEAK FIX";
}

int getSatellites() {
  return gps.satellites.isValid() ? gps.satellites.value() : 0;
}

//////////////////// READ GPS ////////////////////

void readGPS() {
  while (gpsSerial.available()) {
    gps.encode(gpsSerial.read());
  }

  if (millis() - lastGpsPrint > 3000) {
    lastGpsPrint = millis();
    Serial.print("GPS:");
    Serial.print(getGpsStatus());
    Serial.print(" SAT:");
    Serial.println(getSatellites());
  }

  if (millis() - lastSend > 20000) {
    lastSend = millis();

    float lat = gps.location.isValid() ? gps.location.lat() : 0.0;
    float lng = gps.location.isValid() ? gps.location.lng() : 0.0;

    bool backendAllowed = (millis() - bootTime > 30000);

    if (Firebase.ready() && signupOK) {
      sendToFirebase(lat, lng);
    } else {
      Serial.println("FB SKIP");
    }

    if (backendAllowed) {
      sendTrackerInfoToBackend();
    } else {
      Serial.println("API WAIT");
    }
  }
}

//////////////////// FIREBASE ////////////////////

void sendToFirebase(float lat, float lng) {
  FirebaseJson content;
  FirebaseJson geo;

  if (gps.location.isValid()) {
    geo.set("latitude", lat);
    geo.set("longitude", lng);
    content.set("fields/location/geoPointValue", geo);
  }

  content.set("fields/isOnline/booleanValue", true);
  content.set("fields/lastUpdated/stringValue", String(millis()));
  content.set("fields/speed/integerValue", "0");
  content.set("fields/gpsStatus/stringValue", getGpsStatus());
  content.set("fields/satellites/integerValue", String(getSatellites()));

  bool ok = Firebase.Firestore.patchDocument(
    &fbdo,
    PROJECT_ID,
    "",
    "CarsList/car_1",
    content.raw(),
    "location,isOnline,lastUpdated,speed,gpsStatus,satellites"
  );

  Serial.println(ok ? "FB OK" : "FB FAIL");
}

//////////////////// TRACKER INFO TO BACKEND ////////////////////

void sendTrackerInfoToBackend() {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("API SKIP");
    return;
  }

  WiFiClientSecure client;
  client.setInsecure();

  HTTPClient http;
  String url = String(NGROK_BASE_URL) + String(TRACKER_ROUTE);

  if (!http.begin(client, url)) {
    Serial.println("API BEGIN FAIL");
    return;
  }

  http.addHeader("Content-Type", "application/json");

  // ✅ CLEAN JSON (MySQL TINYINT SAFE)
  String payload = "{";
  payload += "\"tracker_id\":\"" + String(TRACKER_ID) + "\",";
  payload += "\"is_online\":1,";
  payload += "\"gps_status\":\"" + getGpsStatus() + "\",";
  payload += "\"last_updated\":\"" + String(millis()) + "\"";
  payload += "}";

  // 🔥 DEBUG (VERY IMPORTANT)
  Serial.println("---- SENDING TO BACKEND ----");
  Serial.println(payload);

  int code = http.POST(payload);

  Serial.print("API RESPONSE CODE: ");
  Serial.println(code);

  String response = http.getString();
  Serial.println("RESPONSE:");
  Serial.println(response);

  http.end();
}
dismount-tactics-parasail.ngrok-free.dev
