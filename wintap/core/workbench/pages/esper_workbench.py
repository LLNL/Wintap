import streamlit as st
import requests
import time
import pandas as pd
from signalrcore.hub_connection_builder import HubConnectionBuilder
import threading

# Query templates (defined here)
query_templates = [
    {"name": "Process Creation", "query": 'SELECT * FROM WintapMessage WHERE MessageType = "PROCESS" AND ActivityType = "Start"'},
    {"name": "Registry Modifications", "query": 'SELECT * FROM WintapMessage WHERE MessageType = "REGISTRY" AND ActivityType IN ("Write", "CreateKey", "DeleteKey", "DeleteValue")'},
    {"name": "Network Connections", "query": 'SELECT * FROM WintapMessage WHERE MessageType = "TCP_CONNECTION" AND ActivityType = "TcpIpConnect"'},
    {"name": "File Operations", "query": 'SELECT * FROM WintapMessage WHERE MessageType = "FILE"'},
]

# Initialize session state
if "epl_listing" not in st.session_state: st.session_state.epl_listing = []
if "esper_results" not in st.session_state: st.session_state.esper_results = []
if "query_text" not in st.session_state: st.session_state.query_text = ""
if "query_name" not in st.session_state: st.session_state.query_name = ""
if "show_confirm" not in st.session_state: st.session_state.show_confirm = False
if "show_error" not in st.session_state: st.session_state.show_error = False
if "query_error" not in st.session_state: st.session_state.query_error = ""
if "hub_connected" not in st.session_state: st.session_state.hub_connected = False
if "hub_connection" not in st.session_state: st.session_state.hub_connection = None

# API functions
def fetch_epl_listing():
    try: return requests.get("http://localhost:5000/api/streams", timeout=5).json().get("response", [])
    except requests.RequestException: return st.session_state.epl_listing

def add_stream(name, query, state):
    try:
        body = {"Name": name, "Id": str(time.time()), "Query": query, "State": state}
        response = requests.post("http://localhost:5000/api/streams", json=body, timeout=5)
        response.raise_for_status()
        st.success(f"Query '{name}' {('started' if state == 0 else 'stopped' if state == 1 else 'deleted')} successfully")
        return True
    except requests.RequestException as e:
        st.error(f"Failed to update query: {e}")
        return False

def delete_all_streams():
    try:
        response = requests.delete("http://localhost:5000/api/Streams/", timeout=5)
        response.raise_for_status()
        st.success("All queries deleted successfully")
        return True
    except requests.RequestException as e:
        st.error(f"Failed to delete queries: {e}")
        return False

# SignalR connection in a separate thread
def start_hub_connection():
    connection = HubConnectionBuilder() \
        .with_url("http://localhost:5000/signalr/workbenchHub") \
        .build()
    
    def on_receive_message(message, status):
        enhanced_result = {"result": message, "timestamp": time.strftime("%H:%M:%S"), "id": str(time.time()), "type": "Event"}
        st.session_state.esper_results.insert(0, enhanced_result)
        st.rerun()

    connection.on("ReceiveMessage", on_receive_message)
    
    try:
        connection.start()
        st.session_state.hub_connected = True
        st.success("SignalR connection established")
    except Exception as e:
        st.error(f"SignalR connection error: {e}")
        st.session_state.hub_connected = False
    
    while True:
        time.sleep(1)  # Keep thread alive to listen for messages

if not st.session_state.hub_connected and st.session_state.hub_connection is None:
    hub_thread = threading.Thread(target=start_hub_connection, daemon=True)
    hub_thread.start()
    st.session_state.hub_connection = hub_thread

# Main content
st.title("Wintap Workbench - Esper Workbench")

st.subheader("Saved Queries")
search_query = st.text_input("Search Queries", key="query_search")
if st.button("New Query"): 
    st.session_state.query_text = "SELECT * FROM WintapMessage"
    st.session_state.query_name = ""
if st.button("Delete All"):
    st.session_state.show_confirm = True
epl_df = pd.DataFrame(st.session_state.epl_listing, columns=["Name", "State", "Actions"])
epl_df = epl_df[epl_df["Name"].str.contains(search_query, case=False, na=False)]
if not epl_df.empty:
    for index, row in epl_df.iterrows():
        col1, col2, col3 = st.columns([2, 1, 2])
        with col1: st.write(row["Name"])
        with col2: st.write(f":{'green[●]' if row['State'] == 'ACTIVE' else 'yellow[●]' if row['State'] == 'STOPPED' else 'red[●]'}")
        with col3:
            if st.button("Start", key=f"start_{index}"): add_stream(row["Name"], row["query"], 0); st.rerun()
            if st.button("Stop", key=f"stop_{index}"): add_stream(row["Name"], row["query"], 1); st.rerun()
            if st.button("Edit", key=f"edit_{index}"): 
                st.session_state.query_name = row["Name"]
                st.session_state.query_text = row["query"]
            if st.button("Delete", key=f"del_{index}"): add_stream(row["Name"], row["query"], 2); st.rerun()
else:
    st.write("No saved queries found.")

st.subheader("Query Editor")
st.session_state.query_name = st.text_input("Query Name", value=st.session_state.query_name)
st.session_state.query_text = st.text_area("Query", value=st.session_state.query_text, height=200)
template = st.selectbox("Insert Template", [""] + [t["name"] for t in query_templates], key="template_select")
if template and st.button("Insert"): 
    st.session_state.query_text = next(t["query"] for t in query_templates if t["name"] == template)
if st.button("Execute"): 
    if st.session_state.query_name and st.session_state.query_text:
        add_stream(st.session_state.query_name, st.session_state.query_text, 0)
        st.rerun()
    else:
        st.warning("Please enter a query name and text.")
if st.button("Save"): 
    if st.session_state.query_name and st.session_state.query_text:
        add_stream(st.session_state.query_name, st.session_state.query_text, 0)
        st.rerun()
    else:
        st.warning("Please enter a query name and text.")

st.subheader("Query Results")
search_results = st.text_input("Filter Results", key="result_search")
esper_df = pd.DataFrame(st.session_state.esper_results, columns=["Time", "Type", "PID", "Process", "Details"])
esper_df = esper_df[esper_df["Details"].str.contains(search_results, case=False, na=False)]
st.dataframe(esper_df, hide_index=True)
if st.button("Clear Results"): st.session_state.esper_results = []

# Confirmation dialog
if st.session_state.show_confirm:
    st.warning("This will delete ALL saved queries. Are you sure?")
    col1, col2 = st.columns(2)
    with col1:
        if st.button("Cancel"):
            st.session_state.show_confirm = False
            st.rerun()
    with col2:
        if st.button("Delete All"):
            delete_all_streams()
            st.session_state.show_confirm = False
            st.rerun()

# Error dialog
if st.session_state.show_error:
    st.error(f"Query Execution Failed: {st.session_state.query_error}")
    if st.button("OK"):
        st.session_state.show_error = False
        st.rerun()

# Auto-update (fallback until hub is stable)
if not st.session_state.hub_connected:
    time.sleep(2)
    st.rerun()