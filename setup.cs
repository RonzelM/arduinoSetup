  // ✅ CLEAN JSON (MySQL TINYINT SAFE)
  String payload = "{";
  payload += "\"tracker_id\":\"" + String(TRACKER_ID) + "\",";
  payload += "\"is_online\":1,";
  payload += "\"gps_status\":\"" + getGpsStatus() + "\",";
  payload += "\"last_updated\":\"" + String(millis()) + "\"";
  payload += "}";
