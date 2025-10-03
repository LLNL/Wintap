import streamlit as st
import requests
import time

# Initialize session state
if "kpi" not in st.session_state: st.session_state.kpi = None
if "wintap_settings" not in st.session_state:
    st.session_state.wintap_settings = {"Tcp": False, "Udp": False, "File": False, "ImageLoad": False, "Registry": False, "MemoryMap": False, "ApiCall": False, "DeveloperMode": False}
if "chart_data" not in st.session_state: st.session_state.chart_data = {"data": [0] * 60}
if "apply_button_disabled" not in st.session_state: st.session_state.apply_button_disabled = True
if "last_update" not in st.session_state: st.session_state.last_update = 0

# API functions
def fetch_kpi():
    try: return requests.get("http://localhost:5000/api/EsperService/", timeout=5).json()
    except requests.RequestException: return st.session_state.kpi or {"totalEvents": 0, "maxEventsPerSecond": 0, "maxEventTime": "", "eventsPerSecond": 0, "runtime": "00:00:00", "wintapOK": False, "collectorOK": False}

def update_wintap_settings(settings):
    try: requests.post("http://localhost:5000/api/WintapService", json=settings, timeout=5); st.success("Settings updated")
    except requests.RequestException: st.error("Failed to update settings")

# Update data
def update_data():
    kpi = fetch_kpi()
    st.session_state.kpi = kpi
    if kpi and len(st.session_state.chart_data["data"]) >= 60:
        st.session_state.chart_data["data"].pop(0)
    if kpi: st.session_state.chart_data["data"].append(kpi.get("eventsPerSecond", 0))

# Main content
st.title("Wintap Workbench Dashboard")

# Update data every 2 seconds
if st.session_state.kpi is None or time.time() - st.session_state.last_update > 2:
    update_data()
    st.session_state.last_update = time.time()

kpi = st.session_state.kpi

# KPIs
col1, col2, col3, col4 = st.columns(4)
with col1: st.metric("Total Events", f"{kpi.get('totalEvents', 0):,}")
with col2: st.metric("Max Events/Sec", f"{kpi.get('maxEventsPerSecond', 0):,}")
with col3: st.metric("Events/Sec", f"{kpi.get('eventsPerSecond', 0):,}", delta_color="inverse" if kpi.get("eventsPerSecond", 0) > 100 else "normal")
with col4: st.metric("Uptime", kpi.get("runtime", "00:00:00"))

# Chart
st.subheader("Current Activity")
st.line_chart(st.session_state.chart_data["data"])

# Sensor Health
st.subheader("Sensor Health")
wintap_ok = kpi.get("wintapOK", False)
collector_ok = kpi.get("collectorOK", False)
st.markdown(f"**Wintap Sensor:** :{'green[●]' if wintap_ok else 'red[●]'}")
st.markdown(f"**Data Collect Plugin:** :{'green[●]' if collector_ok else 'red[●]'}")

# Sensor Settings
st.subheader("Sensor Settings")
settings = st.session_state.wintap_settings
for key in settings:
    settings[key] = st.toggle(key, settings[key], help=f"{key} activity")
if st.button("Apply", disabled=st.session_state.apply_button_disabled):
    update_wintap_settings(settings)
    st.session_state.apply_button_disabled = True
if any(settings[key] != st.session_state.wintap_settings.get(key, False) for key in settings):
    st.session_state.apply_button_disabled = False
st.session_state.wintap_settings = settings.copy()

# Auto-update
time.sleep(2)
st.rerun()