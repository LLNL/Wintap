import streamlit as st

#  potential alternative (and simpler) approach to workbench

# Set page config for a modern look
st.set_page_config(page_title="Wintap Workbench", layout="wide")

# Initialize session state
if "sidebar_initialized" not in st.session_state:
    st.session_state.sidebar_initialized = True
    st.session_state.page = "dashboard"
    with st.sidebar:
        st.title("Wintap Workbench")
        st.header("Home")
        if st.button("Dashboard"): st.switch_page("pages/dashboard.py")
        st.header("Tools")
        if st.button("Esper Workbench"): st.switch_page("pages/esper_workbench.py")
        if st.button("DuckDB Workbench"): st.switch_page("pages/duckdb_workbench.py")
        if st.button("Process Tree Viewer"): st.switch_page("pages/process_tree_viewer.py")
        if st.button("ETW Explorer"): st.switch_page("pages/etw_explorer.py")
        if st.button("Chat"): st.switch_page("pages/chat.py")
        st.header("Reference")
        if st.button("Documentation"): st.switch_page("pages/documentation.py")
        if st.button("Code"): st.switch_page("pages/code.py")

# Default content if no page is selected
st.title("Wintap Workbench")
st.write("Select a page from the sidebar to begin.")