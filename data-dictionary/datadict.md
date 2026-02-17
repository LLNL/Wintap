[[_TOC_]]

## Table: all_files
    Master list of filenames seen in the collect. Derived from process executions, dll loads and file usage activity.

| Table     | Column           | Description                                                                                            |
|-----------|------------------|--------------------------------------------------------------------------------------------------------|
| all_files | dll_num_rows     | Number of times used as a DLL                                                                          |
| all_files | file_num_rows    | Number of times referenced in file read/write activity                                                 |
| all_files | filename         | Fully qualified filename                                                                               |
| all_files | num_hosts        | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| all_files | process_num_rows | Number of rows file was executed as a process                                                          |

## Table: files
    Files by hostname with summary information for executions, dlls and file usage.

| Table   | Column              | Description                                                     |
|---------|---------------------|-----------------------------------------------------------------|
| files   | dll_first_seen      | Earliest time seen                                              |
| files   | dll_last_seen       | Latest time seen                                                |
| files   | dll_num_rows        | Number of times used as a DLL                                   |
| files   | file_first_seen     | Earliest time seen                                              |
| files   | file_id             | Unique ID for a file. Hash of hostname+fully qualified filename |
| files   | file_last_seen      | Latest time seen                                                |
| files   | file_num_rows       | Number of times referenced in file read/write activity          |
| files   | filename            | Fully qualified filename                                        |
| files   | hostname            | Hostname data collected from                                    |
| files   | max_process_term    | Maximum value in the interval                                   |
| files   | min_process_started | Minimum value in the interval                                   |
| files   | process_num_rows    | Number of rows file was executed as a process                   |

## Table: host
    Unique entry for every instrumented host.

| Table   | Column              | Description                                                                                            |
|---------|---------------------|--------------------------------------------------------------------------------------------------------|
| host    | Hostname            | Hostname data collected from                                                                           |
| host    | ad_domain           | Active directory host is joined to                                                                     |
| host    | agent_ids           | List of agent ids. A long running host may have several dur to update, reinstalls, etc.                |
| host    | arch                | Chip architecture                                                                                      |
| host    | domain_role         | Host role within the domain. Ex: domain controller, member, etc.                                       |
| host    | etl_version         | Wintap data collection plugin version                                                                  |
| host    | first_seen          | Earliest time seen                                                                                     |
| host    | has_battery         | Does this host have a battery? Indicates laptop if true                                                |
| host    | last_boot           | Time of last boot, in epoch seconds                                                                    |
| host    | last_seen           | Latest time seen                                                                                       |
| host    | num_ad_domain       | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_arch            | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_domain_role     | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_etl_version     | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_has_battery     | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_last_boot       | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_os              | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_os_version      | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_processor_count | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_processor_speed | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | num_rows            | Number of row behind this summary                                                                      |
| host    | num_wintap_version  | Number of unique values in the related column. Typically, the related column should only have 1 value. |
| host    | os                  | Host operating system                                                                                  |
| host    | os_family           | OS Family: windows, linux, osx                                                                         |
| host    | os_version          | Operating System Version                                                                               |
| host    | processor_count     | Number of processors cores                                                                             |
| host    | processor_speed     | Speed of processors                                                                                    |
| host    | wintap_version      | Version Wintap core. Each plugin has its own version.                                                  |

## Table: host_ip
    Network interface information for hosts.

| Table   | Column          | Description                                                                                             |
|---------|-----------------|---------------------------------------------------------------------------------------------------------|
| host_ip | Hostname        | Hostname data collected from                                                                            |
| host_ip | MTU             | Should be max transmition unit, but its currently not collected.                                        |
| host_ip | agent_id        | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install. |
| host_ip | first_seen      | Earliest time seen                                                                                      |
| host_ip | interface       | Should be the interface device name, but its currently not collected.                                   |
| host_ip | ip_addr         | An IP address on the host. Stored as an integer                                                         |
| host_ip | ip_addr_no      | An IP address on the host. In dotted quad notation.                                                     |
| host_ip | last_seen       | Latest time seen                                                                                        |
| host_ip | mac             | Mac address as string (colon delimited)                                                                 |
| host_ip | num_rows        | Number of row behind this summary                                                                       |
| host_ip | os_family       | OS Family: windows, linux, osx                                                                          |
| host_ip | private_gateway | IP of the gateway used by this interface                                                                |

## Table: labels_graph_net_conn
    Summarizes network-related labels by conn_id

| Table                 | Column                     | Description                                                                                                                                                                                                                                                        |
|-----------------------|----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| labels_graph_net_conn | conn_id                    | Hash of "normalized" 5 tuple (ip1, port1, ip2, port2, L4 protocol). To represent A↔B the same as B↔A, the lower IP (as int) along with its port comes first in the hash preimage ordering. When ip1 == ip2, then the pair order is decided by lowest port instead. |
| labels_graph_net_conn | label_num_hits             | Number of unique labels hit                                                                                                                                                                                                                                        |
| labels_graph_net_conn | label_num_sources          | Number of unique sources of labels                                                                                                                                                                                                                                 |
| labels_graph_net_conn | label_num_uniq_annotations | Number of unqiue annotations                                                                                                                                                                                                                                       |
| labels_graph_net_conn | label_source               | For now, only a single source: "networkx"                                                                                                                                                                                                                          |

## Table: labels_graph_nodes
    Parse JSON in NetworkX files into a flat structure representing the nodes

| Table              | Column     | Description                                                                                                                                     |
|--------------------|------------|-------------------------------------------------------------------------------------------------------------------------------------------------|
| labels_graph_nodes | annotation | Annotations are manually added to specific nodes by the person creating the networkx graph of activity. Used to further highlight the activity. |
| labels_graph_nodes | filename   | Fully qualified filename                                                                                                                        |
| labels_graph_nodes | id         | Unique ID for the node in the graph. Often a PID_HASH, CONN_ID or similar.                                                                      |
| labels_graph_nodes | label      | Graph node label. Often a PROCESS_NAME or similar.                                                                                              |
| labels_graph_nodes | node_type  | Type of node: PROCESS, NETWORK, FILE, etc.                                                                                                      |

## Table: labels_graph_process_summary
    Summarizes process-related labels by pid_hash

| Table                        | Column                     | Description                                                               |
|------------------------------|----------------------------|---------------------------------------------------------------------------|
| labels_graph_process_summary | label_num_hits             | Number of unique labels hit                                               |
| labels_graph_process_summary | label_num_sources          | Number of unique sources of labels                                        |
| labels_graph_process_summary | label_num_uniq_annotations | Number of unqiue annotations                                              |
| labels_graph_process_summary | label_source               | For now, only a single source: "networkx"                                 |
| labels_graph_process_summary | pid_hash                   | Globally unique process hash (hash from hostname, os pid, and start_time) |

## Table: labels_networkx
    Import of networkx graph data. Used primarily as base to build summary tables. Data in this table is JSON, tricky to work with directly.

| Table           | Column        | Description              |
|-----------------|---------------|--------------------------|
| labels_networkx | directed      | Directed graph or not.   |
| labels_networkx | filename      | Fully qualified filename |
| labels_networkx | is_multigraph | Multigraph or not        |
| labels_networkx | links         | List of links            |
| labels_networkx | nodes         | List of nodes            |

## Table: lolbas
    Joins process data with LOLBAS (Living Off the Land Binaries and Scripts) data. Summarizes LOLBAS-related activity by pid_hash.

| Table   | Column                | Description                                                                 |
|---------|-----------------------|-----------------------------------------------------------------------------|
| lolbas  | acknowledgements      | Person who submitted the entry. May be more than 1.                         |
| lolbas  | author                | Person or group who submitted the entry.                                    |
| lolbas  | command               | The command with arguments                                                  |
| lolbas  | command_category      | Intent. Ex: EXECUTE, UAC BYPASS, CONCEAL, etc.                              |
| lolbas  | command_description   | Description of the command                                                  |
| lolbas  | command_privileges    | Required priviledges to execute                                             |
| lolbas  | command_usecase       | Description of use case for attacker                                        |
| lolbas  | date                  | Date created                                                                |
| lolbas  | description           | Normal purpose of the command                                               |
| lolbas  | detections            | How to detect exploit: Many are URLs pointing to SIGMA rule, Defender, etc. |
| lolbas  | filename              | Filename of executable. No path.                                            |
| lolbas  | mitre_attck_technique | Specific MITRE attck code. Ex: T1003                                        |
| lolbas  | operating_system      | OSes binary is found on. Some very specific, some general.                  |
| lolbas  | paths                 | Paths where binary is found. More than one are comma separated.             |
| lolbas  | resources             | URL to more info                                                            |
| lolbas  | url                   | URL to LolBAS project about the exploit                                     |

## Table: mitre_labels
    None

| Table        | Column      | Description                                                                                                                                                 |
|--------------|-------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------|
| mitre_labels | analytic_id | Mitre analytic ID. Ex: CAR-2021-05-012                                                                                                                      |
| mitre_labels | entity      | Wintap entity ID (hash). The type of entity (PID_HASH, CONN_ID, etc) is defined in the ENTITY_TYPE column. Note: Currently, only PID_HASHes are identified. |
| mitre_labels | entity_type | Wintap entity type. Determines what kind of hash is in the ENTITY column. Ex: PID_HASH, CONN_ID, FILE_ID                                                    |
| mitre_labels | time        | Timestamp of activity                                                                                                                                       |

## Table: process
    Each row is a process execution. Processes are the central object of this entire data model.

| Table   | Column                   | Description                                                                                                                   |
|---------|--------------------------|-------------------------------------------------------------------------------------------------------------------------------|
| process | agent_id                 | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install.                       |
| process | args                     | Command line arguments                                                                                                        |
| process | commit_charge            | Commit charge is the total amount of virtual memory guaranteed for all processes to fit in physical memory and the page file. |
| process | commit_peak              | Total memory commited                                                                                                         |
| process | cpu_cycle_count          | Always zero (note: more than zero cycles were generally used in a process)                                                    |
| process | cpu_utilization          |                                                                                                                               |
| process | exit_code                | Process exit code                                                                                                             |
| process | file_id                  | Unique ID for a file. Hash of hostname+fully qualified filename                                                               |
| process | file_md5                 | MD5 hash of the binary process/dll                                                                                            |
| process | file_sha2                | SHA2 hash of the file contents                                                                                                |
| process | filename                 | Fully qualified filename                                                                                                      |
| process | first_seen               | Earliest time seen                                                                                                            |
| process | hard_fault_count         | Count of hard page faults                                                                                                     |
| process | hostname                 | Hostname data collected from                                                                                                  |
| process | last_seen                | Latest time seen                                                                                                              |
| process | num_agent_id             | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_args                 | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_file_md5             | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_file_sha2            | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_parent_os_pid        | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_parent_pid_hash      | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_process_name         | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_process_path         | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_process_start        | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_process_stop         | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | num_user_name            | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process | os_family                | OS Family: windows, linux, osx                                                                                                |
| process | os_pid                   | Operating system process id                                                                                                   |
| process | parent_os_pid            | OS Process ID of parent process                                                                                               |
| process | parent_pid_hash          | PID_HASH of parent process                                                                                                    |
| process | pid_hash                 | Globally unique process hash (hash from hostname, os pid, and start_time)                                                     |
| process | process_name             | Filename of executable                                                                                                        |
| process | process_path             | Fully qualified path of the process executable                                                                                |
| process | process_started          | Process start timestamp                                                                                                       |
| process | process_started_seconds  | Process start time in unix epoch seconds                                                                                      |
| process | process_stop_seconds     | Process stop in unix epoch seconds                                                                                            |
| process | process_term             | Process termination timestamp                                                                                                 |
| process | read_operation_count     | Count of number of read operations                                                                                            |
| process | read_transfer_kilobytes  | Number of K bytes read                                                                                                        |
| process | token_elevation_type     |                                                                                                                               |
| process | user_name                | Username. Note that on windows there are many usernames that represent system or other background activity                    |
| process | write_operation_count    | Count of number of write operations                                                                                           |
| process | write_transfer_kilobytes | Number of K bytes written                                                                                                     |

## Table: process_conn_incr
    Network connection increments for each process. (KEY: PID_HASH + CONN_ID + INCR_START_SECS)

| Table             | Column               | Description                                                                                                                                                                                                                                                        |
|-------------------|----------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| process_conn_incr | Hostname             | Hostname data collected from                                                                                                                                                                                                                                       |
| process_conn_incr | agent_id             | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install.                                                                                                                                                            |
| process_conn_incr | conn_id              | Hash of "normalized" 5 tuple (ip1, port1, ip2, port2, L4 protocol). To represent A↔B the same as B↔A, the lower IP (as int) along with its port comes first in the hash preimage ordering. When ip1 == ip2, then the pair order is decided by lowest port instead. |
| process_conn_incr | first_seen           | Earliest time seen                                                                                                                                                                                                                                                 |
| process_conn_incr | incr_start           | Start time of a 1 minute increment used to aggregate the high-volume events                                                                                                                                                                                        |
| process_conn_incr | last_seen            | Latest time seen                                                                                                                                                                                                                                                   |
| process_conn_incr | local_ip_addr        | IP address on the host collecting data. This address is local to the host sensor. In dotted quad notation.                                                                                                                                                         |
| process_conn_incr | local_ip_int         | IP of the host collecting this data as 32-bit int                                                                                                                                                                                                                  |
| process_conn_incr | local_port           | Port of the process on the collecting host                                                                                                                                                                                                                         |
| process_conn_incr | max_10sec_eventcount | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_size             | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_tcp_recv_count   | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_tcp_recv_size    | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_tcp_send_count   | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_tcp_send_size    | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_udp_recv_count   | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_udp_recv_size    | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_udp_send_count   | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | max_udp_send_size    | Maximum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | min_10sec_eventcount | Minimum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | min_size             | Minimum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | min_tcp_recv_size    | Minimum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | min_tcp_send_size    | Minimum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | min_udp_recv_size    | Minimum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | min_udp_send_size    | Minimum value in the interval                                                                                                                                                                                                                                      |
| process_conn_incr | num_raw_rows         | Number of unique values in the related column. Typically, the related column should only have 1 value.                                                                                                                                                             |
| process_conn_incr | os_family            | OS Family: windows, linux, osx                                                                                                                                                                                                                                     |
| process_conn_incr | pid_hash             | Globally unique process hash (hash from hostname, os pid, and start_time)                                                                                                                                                                                          |
| process_conn_incr | process_name         | Filename of executable                                                                                                                                                                                                                                             |
| process_conn_incr | protocol             | Layer 4 protocol (TCP, UDP)                                                                                                                                                                                                                                        |
| process_conn_incr | remote_ip_addr       | IP address the host collecting data is talking to. This address is remote to the host sensor. In dotted quad notation.                                                                                                                                             |
| process_conn_incr | remote_ip_int        | IP of the remote host for this connection increment as 32-bit int                                                                                                                                                                                                  |
| process_conn_incr | remote_port          | Port of the remote host for this connection increment                                                                                                                                                                                                              |
| process_conn_incr | sq_size              | Squared size of bytes in network packet. Ask Chris B!                                                                                                                                                                                                              |
| process_conn_incr | sq_tcp_recv_size     | Square of the values in the interval. (Ask Chris B)                                                                                                                                                                                                                |
| process_conn_incr | sq_tcp_send_size     | Square of the values in the interval. (Ask Chris B)                                                                                                                                                                                                                |
| process_conn_incr | sq_udp_recv_size     | Square of the values in the interval. (Ask Chris B)                                                                                                                                                                                                                |
| process_conn_incr | sq_udp_send_size     | Square of the values in the interval. (Ask Chris B)                                                                                                                                                                                                                |
| process_conn_incr | tcp_accept_count     | Number of TCP ACCEPT events on this connection for the time window                                                                                                                                                                                                 |
| process_conn_incr | tcp_connect_count    | Number of TCP CONNECT events on this connection for the time window                                                                                                                                                                                                |
| process_conn_incr | tcp_disconnect_count | Number of TCP DISCONNECT events on this connection for the time window                                                                                                                                                                                             |
| process_conn_incr | tcp_reconnect_count  | Number of TCP RECONNECT events on this connection for the time window                                                                                                                                                                                              |
| process_conn_incr | tcp_recv_count       | Number of TCP RECV events on this connection for the time window                                                                                                                                                                                                   |
| process_conn_incr | tcp_recv_size        | Number of TCP RECV bytes received on this connection for the time window                                                                                                                                                                                           |
| process_conn_incr | tcp_retransmit_count | Number of TCP RETRANSMIT events on this connection for the time window                                                                                                                                                                                             |
| process_conn_incr | tcp_send_count       | Number of TCP SEND events on this connection for the time window                                                                                                                                                                                                   |
| process_conn_incr | tcp_send_size        | Number of TCP bytes sent for the time window                                                                                                                                                                                                                       |
| process_conn_incr | tcp_tcpcopy_count    | Number of TCP TCPCOPY events on this connection for the time window                                                                                                                                                                                                |
| process_conn_incr | tcp_tcpcopy_size     | Number of TCP TCPCOPY bytes on this connection for the time window                                                                                                                                                                                                 |
| process_conn_incr | total_events         | Sum of events counts for this connection increment                                                                                                                                                                                                                 |
| process_conn_incr | total_size           | Number of bytes observed for this connection increment                                                                                                                                                                                                             |
| process_conn_incr | udp_recv_count       | Number of UDP RECV events on this connection for the time window                                                                                                                                                                                                   |
| process_conn_incr | udp_recv_size        | Number of UDP bytes received for the time window                                                                                                                                                                                                                   |
| process_conn_incr | udp_send_count       | Number of UDP SEND events on this connection for the time window                                                                                                                                                                                                   |
| process_conn_incr | udp_send_size        | Number of UDP bytes sent for the time window                                                                                                                                                                                                                       |

## Table: process_exe_file_summary
    Summary of files used in process executions. Derived from all process executions resulting in a single row per file per host.

| Table                    | Column              | Description                                                     |
|--------------------------|---------------------|-----------------------------------------------------------------|
| process_exe_file_summary | file_id             | Unique ID for a file. Hash of hostname+fully qualified filename |
| process_exe_file_summary | filename            | Fully qualified filename                                        |
| process_exe_file_summary | hostname            | Hostname data collected from                                    |
| process_exe_file_summary | max_process_term    | Maximum value in the interval                                   |
| process_exe_file_summary | min_process_started | Minimum value in the interval                                   |
| process_exe_file_summary | process_num_rows    | Number of rows file was executed as a process                   |
| process_exe_file_summary | source              | Source of file: currently only PROCESS                          |

## Table: process_file
    File activity of processes. Activity is summarized to the PROCESS + FILE level. See RAW_PROCESS_FILE for activity detailed activity.

| Table        | Column          | Description                                                                                             |
|--------------|-----------------|---------------------------------------------------------------------------------------------------------|
| process_file | Hostname        | Hostname data collected from                                                                            |
| process_file | activity_type   | File activity type. Ex: READ, WRITE                                                                     |
| process_file | agent_id        | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install. |
| process_file | bytes_requested | Total bytes requested                                                                                   |
| process_file | event_count     | Total ETW events                                                                                        |
| process_file | file_hash       | MD5 hash of the file                                                                                    |
| process_file | file_id         | Unique ID for a file. Hash of hostname+fully qualified filename                                         |
| process_file | filename        | Name of the file                                                                                        |
| process_file | first_seen      | Earliest time seen                                                                                      |
| process_file | last_seen       | Latest time seen                                                                                        |
| process_file | max_event       | Maximum value in the interval                                                                           |
| process_file | min_event       | Minimum value in the interval                                                                           |
| process_file | num_raw_rows    | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_file | pid_hash        | Globally unique process hash (hash from hostname, os pid, and start_time)                               |
| process_file | process_name    | Filename of executable                                                                                  |

## Table: process_file_summary
    File activity summarize to the process level.

| Table                | Column             | Description                                                                                             |
|----------------------|--------------------|---------------------------------------------------------------------------------------------------------|
| process_file_summary | Close_Events       | Number of file close events                                                                             |
| process_file_summary | Create_Events      | Number of file create events                                                                            |
| process_file_summary | Delete_Events      | Number of file delete events                                                                            |
| process_file_summary | Hostname           | Hostname data collected from                                                                            |
| process_file_summary | Read_Bytes         | File bytes read                                                                                         |
| process_file_summary | Read_Events        | Number of file read events                                                                              |
| process_file_summary | Rename_Events      | Number of file rename events                                                                            |
| process_file_summary | SetInfo_Events     |                                                                                                         |
| process_file_summary | Write_Bytes        | File bytes written                                                                                      |
| process_file_summary | Write_Events       | Number of file write events                                                                             |
| process_file_summary | agent_id           | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install. |
| process_file_summary | first_seen         | Earliest time seen                                                                                      |
| process_file_summary | last_seen          | Latest time seen                                                                                        |
| process_file_summary | num_null_filename  | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_file_summary | num_raw_rows       | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_file_summary | num_uniq_file_hash | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_file_summary | pid_hash           | Globally unique process hash (hash from hostname, os pid, and start_time)                               |
| process_file_summary | process_name       | Filename of executable                                                                                  |

## Table: process_image_load
    DLLs loaded and unloaded by process.

| Table              | Column         | Description                                                                                             |
|--------------------|----------------|---------------------------------------------------------------------------------------------------------|
| process_image_load | agent_id       | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install. |
| process_image_load | build_time     | Build time of DLL                                                                                       |
| process_image_load | checksum       | A checksum on the file contents of the DLL                                                              |
| process_image_load | default_base   | Default base memory address                                                                             |
| process_image_load | file_id        | Unique ID for a file. Hash of hostname+fully qualified filename                                         |
| process_image_load | file_md5       | MD5 hash of the binary process/dll                                                                      |
| process_image_load | filename       | Filename of the loaded code                                                                             |
| process_image_load | first_seen     | Earliest time seen                                                                                      |
| process_image_load | hostname       | Hostname data collected from                                                                            |
| process_image_load | image_base     | Actual base memory address(?)                                                                           |
| process_image_load | last_seen      | Latest time seen                                                                                        |
| process_image_load | max_image_size | Maximum value in the interval                                                                           |
| process_image_load | min_image_size | Minimum value in the interval                                                                           |
| process_image_load | num_load       | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_image_load | num_unload     | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_image_load | pid_hash       | Globally unique process hash (hash from hostname, os pid, and start_time)                               |
| process_image_load | process_name   | Filename of executable                                                                                  |

## Table: process_image_load_summary
    Summarizes DLL and image load activity by process (pid_hash).

| Table                      | Column         | Description                                                                                             |
|----------------------------|----------------|---------------------------------------------------------------------------------------------------------|
| process_image_load_summary | agent_id       | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install. |
| process_image_load_summary | dlls           | List of DDL names as an array                                                                           |
| process_image_load_summary | first_seen     | Earliest time seen                                                                                      |
| process_image_load_summary | hostname       | Hostname data collected from                                                                            |
| process_image_load_summary | last_seen      | Latest time seen                                                                                        |
| process_image_load_summary | num_uniq_files | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_image_load_summary | pid_hash       | Globally unique process hash (hash from hostname, os pid, and start_time)                               |
| process_image_load_summary | process_name   | Filename of executable                                                                                  |

## Table: process_lolbas_summary
    None

| Table                  | Column          | Description                                                               |
|------------------------|-----------------|---------------------------------------------------------------------------|
| process_lolbas_summary | lolbas_cats     | List categories from hits                                                 |
| process_lolbas_summary | lolbas_mitre    | List of Mitre codes from hits                                             |
| process_lolbas_summary | lolbas_num_rows | Number of hits                                                            |
| process_lolbas_summary | lolbas_privs    | List of priviledges from hist                                             |
| process_lolbas_summary | pid_hash        | Globally unique process hash (hash from hostname, os pid, and start_time) |

## Table: process_mitre_summary
    None

| Table                 | Column                    | Description                                                                |
|-----------------------|---------------------------|----------------------------------------------------------------------------|
| process_mitre_summary | mitre_analytic_ids        | List of analytic IDs from hits                                             |
| process_mitre_summary | mitre_analytic_types      | List of ananlytic types from hits                                          |
| process_mitre_summary | mitre_information_domains | List domains from hits. Ex: Analytic, Host, Network                        |
| process_mitre_summary | mitre_num_rows            | Number of hits                                                             |
| process_mitre_summary | mitre_subtypes            | List of subtypes from hits. Ex: Map building, Anomaly, Hostflow.  Process. |
| process_mitre_summary | pid_hash                  | Globally unique process hash (hash from hostname, os pid, and start_time)  |

## Table: process_net_conn
    Network connections for each process. These are summarize to the PROCESS and Network 5-tuple (KEY: PID_HASH + CONN_ID)

| Table            | Column               | Description                                                                                                                                                                                                                                                        |
|------------------|----------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| process_net_conn | Hostname             | Hostname data collected from                                                                                                                                                                                                                                       |
| process_net_conn | agent_id             | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install.                                                                                                                                                            |
| process_net_conn | conn_id              | Hash of "normalized" 5 tuple (ip1, port1, ip2, port2, L4 protocol). To represent A↔B the same as B↔A, the lower IP (as int) along with its port comes first in the hash preimage ordering. When ip1 == ip2, then the pair order is decided by lowest port instead. |
| process_net_conn | first_seen           | Earliest time seen                                                                                                                                                                                                                                                 |
| process_net_conn | last_seen            | Latest time seen                                                                                                                                                                                                                                                   |
| process_net_conn | local_ip_addr        | IP address on the host collecting data. This address is local to the host sensor. In dotted quad notation.                                                                                                                                                         |
| process_net_conn | local_port           | Port of the process on the collecting host                                                                                                                                                                                                                         |
| process_net_conn | num_raw_rows         | Number of unique values in the related column. Typically, the related column should only have 1 value.                                                                                                                                                             |
| process_net_conn | os_family            | OS Family: windows, linux, osx                                                                                                                                                                                                                                     |
| process_net_conn | pid_hash             | Globally unique process hash (hash from hostname, os pid, and start_time)                                                                                                                                                                                          |
| process_net_conn | process_name         | Filename of executable                                                                                                                                                                                                                                             |
| process_net_conn | protocol             | Layer 4 Protocol (TCP, UDP)                                                                                                                                                                                                                                        |
| process_net_conn | remote_ip_addr       | IP address the host collecting data is talking to. This address is remote to the host sensor. In dotted quad notation.                                                                                                                                             |
| process_net_conn | remote_port          | Port of the remote host for this connection increment                                                                                                                                                                                                              |
| process_net_conn | sq_size              | Squared size of bytes in network packet. Ask Chris B!                                                                                                                                                                                                              |
| process_net_conn | sq_tcp_recv_size     | Square of the values in the interval. (Ask Chris B)                                                                                                                                                                                                                |
| process_net_conn | sq_tcp_send_size     | Square of the values in the interval. (Ask Chris B)                                                                                                                                                                                                                |
| process_net_conn | sq_udp_recv_size     | Square of the values in the interval. (Ask Chris B)                                                                                                                                                                                                                |
| process_net_conn | sq_udp_send_size     | Square of the values in the interval. (Ask Chris B)                                                                                                                                                                                                                |
| process_net_conn | tcp_accept_count     | Number of TCP ACCEPT events on this connection                                                                                                                                                                                                                     |
| process_net_conn | tcp_connect_count    | Number of TCP CONNECT events on this connection                                                                                                                                                                                                                    |
| process_net_conn | tcp_disconnect_count | Number of TCP DISCONNECT events on this connection                                                                                                                                                                                                                 |
| process_net_conn | tcp_reconnect_count  | Number of TCP RECONNECT events on this connection                                                                                                                                                                                                                  |
| process_net_conn | tcp_recv_count       | Number of TCP RECV events on this connection                                                                                                                                                                                                                       |
| process_net_conn | tcp_recv_size        | Number of TCP RECV bytes received on this connection                                                                                                                                                                                                               |
| process_net_conn | tcp_retransmit_count | Number of TCP RETRANSMIT events on this connection                                                                                                                                                                                                                 |
| process_net_conn | tcp_send_count       | Number of TCP SEND events on this connection                                                                                                                                                                                                                       |
| process_net_conn | tcp_send_size        | Number of TCP bytes sent                                                                                                                                                                                                                                           |
| process_net_conn | tcp_tcpcopy_count    | Number of TCP TCPCOPY events on this connection                                                                                                                                                                                                                    |
| process_net_conn | tcp_tcpcopy_size     | Number of TCP TCPCOPY bytes on this connection                                                                                                                                                                                                                     |
| process_net_conn | total_events         | Sum of events counts for this connection                                                                                                                                                                                                                           |
| process_net_conn | total_size           | Number of bytes observed for this connection                                                                                                                                                                                                                       |
| process_net_conn | udp_recv_count       | Number of UDP RECV events on this connection                                                                                                                                                                                                                       |
| process_net_conn | udp_recv_size        | Number of UDP bytes received                                                                                                                                                                                                                                       |
| process_net_conn | udp_send_count       | Number of UDP SEND events on this connection                                                                                                                                                                                                                       |
| process_net_conn | udp_send_size        | Number of UDP bytes sent on this connection                                                                                                                                                                                                                        |

## Table: process_net_summary
    Summarizes all network connections for a given process to the process level.

| Table               | Column               | Description                                                                                             |
|---------------------|----------------------|---------------------------------------------------------------------------------------------------------|
| process_net_summary | Hostname             | Hostname data collected from                                                                            |
| process_net_summary | agent_id             | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install. |
| process_net_summary | avg_bytes            | Average bytes in an event                                                                               |
| process_net_summary | avg_packets          | Average packets per session done by the process                                                         |
| process_net_summary | conn_id_count        | Number of unique network connection IDs (5-tuples)                                                      |
| process_net_summary | first_seen           | Earliest time seen                                                                                      |
| process_net_summary | last_seen            | Latest time seen                                                                                        |
| process_net_summary | max_bytes            | Maximum bytes in an event                                                                               |
| process_net_summary | max_packets          | Max packets per session done by the process                                                             |
| process_net_summary | min_bytes            | Minimum bytes in an event                                                                               |
| process_net_summary | min_packets          | Min packets per session done by the process                                                             |
| process_net_summary | net_recv_size        | Total bytes received by the process                                                                     |
| process_net_summary | net_rs_total         | Total bytes sent/received by the process                                                                |
| process_net_summary | net_send_size        | Total bytes sent by the process                                                                         |
| process_net_summary | net_send_vs_recv     | Ratio of bytes sent vs received by the process                                                          |
| process_net_summary | net_total_events     | Total events accross UDP/TCP                                                                            |
| process_net_summary | net_total_size       | Total bytes accross all events                                                                          |
| process_net_summary | num_raw_rows         | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_net_summary | os_family            | OS Family: windows, linux, osx                                                                          |
| process_net_summary | pid_hash             | Globally unique process hash (hash from hostname, os pid, and start_time)                               |
| process_net_summary | process_name         | Filename of executable                                                                                  |
| process_net_summary | sq_size              | Squared size of bytes in network packet. Ask Chris B!                                                   |
| process_net_summary | tcp_accept_count     | Number of TCP Accept for this process                                                                   |
| process_net_summary | tcp_connect_count    | Number of TCP Connects for this process                                                                 |
| process_net_summary | tcp_disconnect_count | Number of TCP Disconnects for this process                                                              |
| process_net_summary | tcp_reconnect_count  | Number of TCP Reconnects for this process                                                               |
| process_net_summary | tcp_recv_count       | Number of TCP packets recevied by the process                                                           |
| process_net_summary | tcp_recv_size        | Number of TCP bytes received by the process                                                             |
| process_net_summary | tcp_retransmit_count | Number of TCP retransmits by the process                                                                |
| process_net_summary | tcp_rs_total         | Total TCP bytes sent/received by the process                                                            |
| process_net_summary | tcp_send_count       | Number of TCP packets sent by the process                                                               |
| process_net_summary | tcp_send_size        | Number of TCP bytes sent by the process                                                                 |
| process_net_summary | tcp_send_vs_recv     | Ratio of TCP bytes sent vs received by the process                                                      |
| process_net_summary | tcp_tcpcopy_count    | Number of TCP copy events during the process                                                            |
| process_net_summary | tcp_tcpcopy_size     | TCP copy bytes                                                                                          |
| process_net_summary | udp_recv_count       | Number of UDP packets received by the process                                                           |
| process_net_summary | udp_recv_size        | Number of UDP bytes received by the process                                                             |
| process_net_summary | udp_rs_total         | Total UDP bytes received by the process                                                                 |
| process_net_summary | udp_send_count       | Number of UDP packets sent by the process                                                               |
| process_net_summary | udp_send_size        | Number of UDP bytes sent by the process                                                                 |
| process_net_summary | udp_send_vs_recv     | Ratio of UDP bytes sent vs received by the process                                                      |

## Table: process_path
    For each process, has the path to its root (parent process).

| Table        | Column            | Description                                                                                                                                      |
|--------------|-------------------|--------------------------------------------------------------------------------------------------------------------------------------------------|
| process_path | agent_id          | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install.                                          |
| process_path | hostname          | Hostname data collected from                                                                                                                     |
| process_path | level             | Distance from the kernel process                                                                                                                 |
| process_path | max_level         | Ignore: The same value level. Artifact of the processing to generate the paths.                                                                  |
| process_path | os_pid            | Operating system process id                                                                                                                      |
| process_path | parent_os_pid     | OS Process ID of parent process                                                                                                                  |
| process_path | parent_pid_hash   | PID_HASH of parent process                                                                                                                       |
| process_path | pid_hash          | Globally unique process hash (hash from hostname, os pid, and start_time)                                                                        |
| process_path | process_name      | Filename of executable                                                                                                                           |
| process_path | process_path      | Fully qualified path of the process executable                                                                                                   |
| process_path | ptree             | Path to kernel using only PROCESS_NAMEs. Order and format is: =process->parent process->...  Ex: =winlogon.exe->smss.exe->smss.exe->ntoskrnl.exe |
| process_path | ptree_list        | Path to kernel using PID_HASHes. Stored as an list. List is order from process to kernel process                                                 |
| process_path | ptree_list_tuples | Path to kernel using named tuples of PID_HASH and PROCESS_NAME (list of maps). List is order from process to kernel process                      |
| process_path | seq               | Ignore: Artifact left over from process. Level+1.                                                                                                |

## Table: process_registry
    Registry activity events. These are aggregated to PROCESS + Registry Key/Value

| Table            | Column        | Description                                                                                             |
|------------------|---------------|---------------------------------------------------------------------------------------------------------|
| process_registry | activity_type | Type of registry activity. Ex: CREATEKEY, READ, WRITE, DELETEVALUE, DELETEKEY                           |
| process_registry | agent_id      | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install. |
| process_registry | event_count   | Total ETW events                                                                                        |
| process_registry | first_seen    | Earliest time seen                                                                                      |
| process_registry | hostname      | Hostname data collected from                                                                            |
| process_registry | last_seen     | Latest time seen                                                                                        |
| process_registry | max_event     | Maximum value in the interval                                                                           |
| process_registry | min_event     | Minimum value in the interval                                                                           |
| process_registry | num_raw_rows  | Number of unique values in the related column. Typically, the related column should only have 1 value.  |
| process_registry | pid_hash      | Globally unique process hash (hash from hostname, os pid, and start_time)                               |
| process_registry | process_name  | Filename of executable                                                                                  |
| process_registry | reg_data      | Registry data, which is the most detailed part of the registry key. Strangely, it isn't data            |
| process_registry | reg_path      | Registry path                                                                                           |
| process_registry | reg_value     | Registry value read or written                                                                          |

## Table: process_registry_summary
    All registry activity events summarized to the PROCESS.

| Table                    | Column               | Description                                                                                             |
|--------------------------|----------------------|---------------------------------------------------------------------------------------------------------|
| process_registry_summary | agent_id             | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install. |
| process_registry_summary | createkeys           | Number of createkeys by process                                                                         |
| process_registry_summary | deletekeys           | Number of deletekeys by process                                                                         |
| process_registry_summary | deletevalues         | Number of deletevalues by process                                                                       |
| process_registry_summary | first_seen           | Earliest time seen                                                                                      |
| process_registry_summary | hostname             | Hostname data collected from                                                                            |
| process_registry_summary | last_seen            | Latest time seen                                                                                        |
| process_registry_summary | pid_hash             | Globally unique process hash (hash from hostname, os pid, and start_time)                               |
| process_registry_summary | process_name         | Filename of executable                                                                                  |
| process_registry_summary | reads                | Number of reads by process                                                                              |
| process_registry_summary | total_activity_types | Total number of ETW events for all registry activity for a process                                      |
| process_registry_summary | writes               | Number of writes by process                                                                             |

## Table: process_summary
    Provides a unified summary of all process-related activities that are directly collected by Wintap. This includes:n  Process metadata (e.g., process_name, hostname, pid_hash).n  Registry activity (e.g., reads, writes, key creation/deletion).n  File activity (e.g., file reads, writes, and other events).n  Network activity (e.g., connections, data sent/received).n  Image loads (e.g., DLLs loaded by the process).n  Host-level metadata (e.g., OS, architecture).

| Table           | Column                   | Description                                                                                                                   |
|-----------------|--------------------------|-------------------------------------------------------------------------------------------------------------------------------|
| process_summary | Close_Events             | Number of file close events                                                                                                   |
| process_summary | Create_Events            | Number of file create events                                                                                                  |
| process_summary | Delete_Events            | Number of file delete events                                                                                                  |
| process_summary | Read_Bytes               | File bytes read                                                                                                               |
| process_summary | Read_Events              | Number of file read events                                                                                                    |
| process_summary | Rename_Events            | Number of file rename events                                                                                                  |
| process_summary | SetInfo_Events           |                                                                                                                               |
| process_summary | Write_Bytes              | File bytes written                                                                                                            |
| process_summary | Write_Events             | Number of file write events                                                                                                   |
| process_summary | agent_id                 | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install.                       |
| process_summary | arch                     | Host architecture                                                                                                             |
| process_summary | args                     | Process arguments. Note: as a simple string, no parsing done                                                                  |
| process_summary | avg_bytes                | Average bytes over the interval                                                                                               |
| process_summary | avg_packets              | Average packets over the interval                                                                                             |
| process_summary | commit_charge            | Commit charge is the total amount of virtual memory guaranteed for all processes to fit in physical memory and the page file. |
| process_summary | commit_peak              | Total memory commited                                                                                                         |
| process_summary | conn_id_count            | Number of unique network connection IDs (5-tuples)                                                                            |
| process_summary | cpu_cycle_count          | Always zero (note: more than zero cycles were generally used in a process)                                                    |
| process_summary | cpu_utilization          |                                                                                                                               |
| process_summary | dll_first_seen           | Earliest time seen                                                                                                            |
| process_summary | dll_last_seen            | Latest time seen                                                                                                              |
| process_summary | dll_num_uniq_files       | Number of unique DDLs by name                                                                                                 |
| process_summary | dlls                     | List of DDL names as an array                                                                                                 |
| process_summary | duration_seconds         | Elapsed process execution in seconds                                                                                          |
| process_summary | exit_code                | Process exit code                                                                                                             |
| process_summary | file_first_seen          | Earliest time seen                                                                                                            |
| process_summary | file_id                  | Unique ID for a file. Hash of hostname+fully qualified filename                                                               |
| process_summary | file_last_seen           | Latest time seen                                                                                                              |
| process_summary | file_md5                 | MD5 hash of the binary process/dll                                                                                            |
| process_summary | file_num_raw_rows        | Number of rows of file activity from the raw data.                                                                            |
| process_summary | file_sha2                | SHA2 hash of the file contents                                                                                                |
| process_summary | filename                 | Fully qualified filename                                                                                                      |
| process_summary | first_seen               | Earliest time seen                                                                                                            |
| process_summary | hard_fault_count         | Count of hard page faults                                                                                                     |
| process_summary | hostname                 | Hostname data collected from                                                                                                  |
| process_summary | last_seen                | Latest time seen                                                                                                              |
| process_summary | max_bytes                | Max bytes in the interval                                                                                                     |
| process_summary | max_packets              | Max packets in the interval                                                                                                   |
| process_summary | min_bytes                | Min bytes in the interval                                                                                                     |
| process_summary | min_packets              | Max packets in the interval                                                                                                   |
| process_summary | net_first_seen           | Earliest time seen                                                                                                            |
| process_summary | net_last_seen            | Latest time seen                                                                                                              |
| process_summary | net_num_raw_rows         | Number of rows in the raw data                                                                                                |
| process_summary | net_recv_size            | Total bytes received in the interval                                                                                          |
| process_summary | net_rs_total             | Total bytes sent/received in the interval                                                                                     |
| process_summary | net_send_size            | Total bytes sent in the interval                                                                                              |
| process_summary | net_send_vs_recv         | Ratio of bytes sent/received in the interval                                                                                  |
| process_summary | net_total_events         | Total network events in the interval                                                                                          |
| process_summary | net_total_size           | Total bytes (send/receive) in the interval                                                                                    |
| process_summary | num_agent_id             | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_args                 | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_file_md5             | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_file_sha2            | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_null_filename        | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_parent_os_pid        | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_parent_pid_hash      | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_process_name         | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_process_path         | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_process_start        | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_process_stop         | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_uniq_file_hash       | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | num_user_name            | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_summary | os                       | Operating System                                                                                                              |
| process_summary | os_family                | OS Family: windows, linux, osx                                                                                                |
| process_summary | os_pid                   | Operating system process id                                                                                                   |
| process_summary | os_version               | Operating System Version                                                                                                      |
| process_summary | parent_os_pid            | OS Process ID of parent process                                                                                               |
| process_summary | parent_pid_hash          | PID_HASH of parent process                                                                                                    |
| process_summary | pid_hash                 | Globally unique process hash (hash from hostname, os pid, and start_time)                                                     |
| process_summary | process_name             | Filename of executable                                                                                                        |
| process_summary | process_path             | Fully qualified path of the process executable                                                                                |
| process_summary | process_started          | Process start timestamp                                                                                                       |
| process_summary | process_started_seconds  | Process start time in unix epoch seconds                                                                                      |
| process_summary | process_stop_seconds     | Process stop in unix epoch seconds                                                                                            |
| process_summary | process_term             | Process termination timestamp                                                                                                 |
| process_summary | read_operation_count     | Count of number of read operations                                                                                            |
| process_summary | read_transfer_kilobytes  | Number of K bytes read                                                                                                        |
| process_summary | reg_createkeys           | Registry keys created                                                                                                         |
| process_summary | reg_deletekeys           | Registry keys deleted                                                                                                         |
| process_summary | reg_deletevalues         | Registry values deleted                                                                                                       |
| process_summary | reg_first_seen           | Earliest time seen                                                                                                            |
| process_summary | reg_last_seen            | Latest time seen                                                                                                              |
| process_summary | reg_reads                | Registry read events                                                                                                          |
| process_summary | reg_totals               | Total registry events                                                                                                         |
| process_summary | reg_writes               | Registry write events                                                                                                         |
| process_summary | sq_size                  | Squared size of bytes in network packet. Ask Chris B!                                                                         |
| process_summary | tcp_accept_count         | TCP Accepts                                                                                                                   |
| process_summary | tcp_connect_count        | TCP connects                                                                                                                  |
| process_summary | tcp_disconnect_count     | TCP disconnects                                                                                                               |
| process_summary | tcp_reconnect_count      | TCP reconnects                                                                                                                |
| process_summary | tcp_recv_count           | TCP packets received                                                                                                          |
| process_summary | tcp_recv_size            | TCP bytes received                                                                                                            |
| process_summary | tcp_retransmit_count     | TCP packets retransmitted                                                                                                     |
| process_summary | tcp_rs_total             | TCP bytes total (send/received)                                                                                               |
| process_summary | tcp_send_count           | TCP bytes sent                                                                                                                |
| process_summary | tcp_send_size            | TCP packets sent                                                                                                              |
| process_summary | tcp_send_vs_recv         | Ratio of TCP bytes sent/received                                                                                              |
| process_summary | tcp_tcpcopy_count        | TCP copy events. We think this is an event that is internal to the network stack on the host.                                 |
| process_summary | tcp_tcpcopy_size         | TCP copy bytes                                                                                                                |
| process_summary | token_elevation_type     |                                                                                                                               |
| process_summary | udp_recv_count           | UDP bytes received                                                                                                            |
| process_summary | udp_recv_size            | UDP events received                                                                                                           |
| process_summary | udp_rs_total             | Total UDP bytes sent/received                                                                                                 |
| process_summary | udp_send_count           | UDP packets sent                                                                                                              |
| process_summary | udp_send_size            | UDP bytes sent                                                                                                                |
| process_summary | udp_send_vs_recv         | Ratio of UDP bytes sent/recieved                                                                                              |
| process_summary | user_name                | Username. Note that on windows there are many usernames that represent system or other background activity                    |
| process_summary | write_operation_count    | Count of number of write operations                                                                                           |
| process_summary | write_transfer_kilobytes | Number of K bytes written                                                                                                     |

## Table: process_uber_summary
    The process_uber_summary view is an enhanced version of the process_summary view, incorporating additional threat intelligence and labeling data. It combines process activity summaries with external threat indicators such as SIGMA rules, MITRE ATT&CK techniques, LOLBAS (Living Off the Land Binaries and Scripts), and NetworkX graph labels. This comprehensive view provides a detailed and enriched dataset for advanced threat analysis and detection.

| Table                | Column                     | Description                                                                                                                   |
|----------------------|----------------------------|-------------------------------------------------------------------------------------------------------------------------------|
| process_uber_summary | Close_Events               | Number of file close events                                                                                                   |
| process_uber_summary | Create_Events              | Number of file create events                                                                                                  |
| process_uber_summary | Delete_Events              | Number of file delete events                                                                                                  |
| process_uber_summary | Read_Bytes                 | File bytes read                                                                                                               |
| process_uber_summary | Read_Events                | Number of file read events                                                                                                    |
| process_uber_summary | Rename_Events              | Number of file rename events                                                                                                  |
| process_uber_summary | SetInfo_Events             |                                                                                                                               |
| process_uber_summary | Write_Bytes                | File bytes written                                                                                                            |
| process_uber_summary | Write_Events               | Number of file write events                                                                                                   |
| process_uber_summary | agent_id                   | Agent ID is a unique id for a Wintap install on a host. It is created on initial startup after install.                       |
| process_uber_summary | arch                       | Host architecture                                                                                                             |
| process_uber_summary | args                       | Process arguments. Note: as a simple string, no parsing done                                                                  |
| process_uber_summary | avg_bytes                  | Average bytes over the interval                                                                                               |
| process_uber_summary | avg_packets                | Average packets over the interval                                                                                             |
| process_uber_summary | commit_charge              | Commit charge is the total amount of virtual memory guaranteed for all processes to fit in physical memory and the page file. |
| process_uber_summary | commit_peak                | Total memory commited                                                                                                         |
| process_uber_summary | conn_id_count              | Number of unique network connection IDs (5-tuples)                                                                            |
| process_uber_summary | cpu_cycle_count            | Always zero (note: more than zero cycles were generally used in a process)                                                    |
| process_uber_summary | cpu_utilization            |                                                                                                                               |
| process_uber_summary | critical_num_sigma_hits    | Number of unique Sigma rules hit                                                                                              |
| process_uber_summary | dll_first_seen             | Earliest time seen                                                                                                            |
| process_uber_summary | dll_last_seen              | Latest time seen                                                                                                              |
| process_uber_summary | dll_num_uniq_files         | Number of unique DDLs by name                                                                                                 |
| process_uber_summary | dlls                       | List of DDL names as an array                                                                                                 |
| process_uber_summary | duration_seconds           | Elapsed process execution in seconds                                                                                          |
| process_uber_summary | exit_code                  | Process exit code                                                                                                             |
| process_uber_summary | file_first_seen            | Earliest time seen                                                                                                            |
| process_uber_summary | file_id                    | Unique ID for a file. Hash of hostname+fully qualified filename                                                               |
| process_uber_summary | file_last_seen             | Latest time seen                                                                                                              |
| process_uber_summary | file_md5                   | MD5 hash of the binary process/dll                                                                                            |
| process_uber_summary | file_num_raw_rows          | Number of rows of file activity from the raw data.                                                                            |
| process_uber_summary | file_sha2                  | SHA2 hash of the file contents                                                                                                |
| process_uber_summary | filename                   | Fully qualified filename                                                                                                      |
| process_uber_summary | first_seen                 | Earliest time seen                                                                                                            |
| process_uber_summary | hard_fault_count           | Count of hard page faults                                                                                                     |
| process_uber_summary | high_num_sigma_hits        | Number of unique Sigma rules hit                                                                                              |
| process_uber_summary | high_num_sigma_rows        | Number of times Sigma rules hit for this entity                                                                               |
| process_uber_summary | hostname                   | Hostname data collected from                                                                                                  |
| process_uber_summary | label_num_hits             | Number of unique labels hit                                                                                                   |
| process_uber_summary | label_num_sources          | Number of unique sources of labels                                                                                            |
| process_uber_summary | label_num_uniq_annotations | Number of unqiue annotations                                                                                                  |
| process_uber_summary | label_source               | For now, only a single source: "networkx"                                                                                     |
| process_uber_summary | last_seen                  | Latest time seen                                                                                                              |
| process_uber_summary | lolbas_cats                | List categories from hits                                                                                                     |
| process_uber_summary | lolbas_mitre               | List of Mitre codes from hits                                                                                                 |
| process_uber_summary | lolbas_num_rows            | Number of hits                                                                                                                |
| process_uber_summary | lolbas_privs               | List of priviledges from hist                                                                                                 |
| process_uber_summary | low_num_sigma_hits         | Number of unique Sigma rules hit                                                                                              |
| process_uber_summary | low_num_sigma_rows         | Number of times Sigma rules hit for this entity                                                                               |
| process_uber_summary | max_bytes                  | Max bytes in the interval                                                                                                     |
| process_uber_summary | max_packets                | Max packets in the interval                                                                                                   |
| process_uber_summary | medium_num_sigma_hits      | Number of unique Sigma rules hit                                                                                              |
| process_uber_summary | medium_num_sigma_rows      | Number of times Sigma rules hit for this entity                                                                               |
| process_uber_summary | min_bytes                  | Min bytes in the interval                                                                                                     |
| process_uber_summary | min_packets                | Max packets in the interval                                                                                                   |
| process_uber_summary | mitre_analytic_ids         | List of analytic IDs from hits                                                                                                |
| process_uber_summary | mitre_analytic_types       | List of ananlytic types from hits                                                                                             |
| process_uber_summary | mitre_information_domains  | List domains from hits. Ex: Analytic, Host, Network                                                                           |
| process_uber_summary | mitre_num_rows             | Number of hits                                                                                                                |
| process_uber_summary | mitre_subtypes             | List of subtypes from hits. Ex: Map building, Anomaly, Hostflow.  Process.                                                    |
| process_uber_summary | net_first_seen             | Earliest time seen                                                                                                            |
| process_uber_summary | net_last_seen              | Latest time seen                                                                                                              |
| process_uber_summary | net_num_raw_rows           | Number of rows in the raw data                                                                                                |
| process_uber_summary | net_recv_size              | Total bytes received in the interval                                                                                          |
| process_uber_summary | net_rs_total               | Total bytes sent/received in the interval                                                                                     |
| process_uber_summary | net_send_size              | Total bytes sent in the interval                                                                                              |
| process_uber_summary | net_send_vs_recv           | Ratio of bytes sent/received in the interval                                                                                  |
| process_uber_summary | net_total_events           | Total network events in the interval                                                                                          |
| process_uber_summary | net_total_size             | Total bytes (send/receive) in the interval                                                                                    |
| process_uber_summary | num_agent_id               | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_args                   | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_file_md5               | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_file_sha2              | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_null_filename          | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_parent_os_pid          | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_parent_pid_hash        | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_process_name           | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_process_path           | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_process_start          | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_process_stop           | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_uniq_file_hash         | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | num_user_name              | Number of unique values in the related column. Typically, the related column should only have 1 value.                        |
| process_uber_summary | os                         | Operating System                                                                                                              |
| process_uber_summary | os_family                  | OS Family: windows, linux, osx                                                                                                |
| process_uber_summary | os_pid                     | Operating system process id                                                                                                   |
| process_uber_summary | os_version                 | Operating System Version                                                                                                      |
| process_uber_summary | parent_os_pid              | OS Process ID of parent process                                                                                               |
| process_uber_summary | parent_pid_hash            | PID_HASH of parent process                                                                                                    |
| process_uber_summary | pid_hash                   | Globally unique process hash (hash from hostname, os pid, and start_time)                                                     |
| process_uber_summary | process_name               | Filename of executable                                                                                                        |
| process_uber_summary | process_path               | Fully qualified path of the process executable                                                                                |
| process_uber_summary | process_started            | Process start timestamp                                                                                                       |
| process_uber_summary | process_started_seconds    | Process start time in unix epoch seconds                                                                                      |
| process_uber_summary | process_stop_seconds       | Process stop in unix epoch seconds                                                                                            |
| process_uber_summary | process_term               | Process termination timestamp                                                                                                 |
| process_uber_summary | read_operation_count       | Count of number of read operations                                                                                            |
| process_uber_summary | read_transfer_kilobytes    | Number of K bytes read                                                                                                        |
| process_uber_summary | reg_createkeys             | Registry keys created                                                                                                         |
| process_uber_summary | reg_deletekeys             | Registry keys deleted                                                                                                         |
| process_uber_summary | reg_deletevalues           | Registry values deleted                                                                                                       |
| process_uber_summary | reg_first_seen             | Earliest time seen                                                                                                            |
| process_uber_summary | reg_last_seen              | Latest time seen                                                                                                              |
| process_uber_summary | reg_reads                  | Registry read events                                                                                                          |
| process_uber_summary | reg_totals                 | Total registry events                                                                                                         |
| process_uber_summary | reg_writes                 | Registry write events                                                                                                         |
| process_uber_summary | sq_size                    | Squared size of bytes in network packet. Ask Chris B!                                                                         |
| process_uber_summary | tcp_accept_count           | TCP Accepts                                                                                                                   |
| process_uber_summary | tcp_connect_count          | TCP connects                                                                                                                  |
| process_uber_summary | tcp_disconnect_count       | TCP disconnects                                                                                                               |
| process_uber_summary | tcp_reconnect_count        | TCP reconnects                                                                                                                |
| process_uber_summary | tcp_recv_count             | TCP packets received                                                                                                          |
| process_uber_summary | tcp_recv_size              | TCP bytes received                                                                                                            |
| process_uber_summary | tcp_retransmit_count       | TCP packets retransmitted                                                                                                     |
| process_uber_summary | tcp_rs_total               | TCP bytes total (send/received)                                                                                               |
| process_uber_summary | tcp_send_count             | TCP bytes sent                                                                                                                |
| process_uber_summary | tcp_send_size              | TCP packets sent                                                                                                              |
| process_uber_summary | tcp_send_vs_recv           | Ratio of TCP bytes sent/received                                                                                              |
| process_uber_summary | tcp_tcpcopy_count          | TCP copy events. We think this is an event that is internal to the network stack on the host.                                 |
| process_uber_summary | tcp_tcpcopy_size           | TCP copy bytes                                                                                                                |
| process_uber_summary | token_elevation_type       |                                                                                                                               |
| process_uber_summary | total_sigma_hits           | Total Sigma hits over all crticality levels                                                                                   |
| process_uber_summary | udp_recv_count             | UDP bytes received                                                                                                            |
| process_uber_summary | udp_recv_size              | UDP events received                                                                                                           |
| process_uber_summary | udp_rs_total               | Total UDP bytes sent/received                                                                                                 |
| process_uber_summary | udp_send_count             | UDP packets sent                                                                                                              |
| process_uber_summary | udp_send_size              | UDP bytes sent                                                                                                                |
| process_uber_summary | udp_send_vs_recv           | Ratio of UDP bytes sent/recieved                                                                                              |
| process_uber_summary | user_name                  | Username. Note that on windows there are many usernames that represent system or other background activity                    |
| process_uber_summary | write_operation_count      | Count of number of write operations                                                                                           |
| process_uber_summary | write_transfer_kilobytes   | Number of K bytes written                                                                                                     |

## Table: sigma_labels
    The sigma_labels table is a key component in the threat detection pipeline, as it links process or entity activity to specific SIGMA rules. SIGMA is a standardized rule format for describing log-based detection patterns, often used for identifying suspicious or malicious behavior in systems. The sigma_labels table provides a mapping between entities (like processes) and the SIGMA rules that matched their activity, along with metadata about the severity and type of the detection.

| Table        | Column      | Description                                                                                                                                                 |
|--------------|-------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------|
| sigma_labels | analytic_id | Specific SIGMA rule hit. Value is a GUID                                                                                                                    |
| sigma_labels | entity      | Wintap entity ID (hash). The type of entity (PID_HASH, CONN_ID, etc) is defined in the ENTITY_TYPE column. Note: Currently, only PID_HASHes are identified. |
| sigma_labels | entity_type | Wintap entity type. Determines what kind of hash is in the ENTITY column. Ex: PID_HASH, CONN_ID, FILE_ID                                                    |
| sigma_labels | time        | Timestamp of activity                                                                                                                                       |

## Table: sigma_labels_summary
    Aggregates data from sigma_labels by PID_HASH and pivots the severity levels into separate columns (e.g., critical_num_sigma_hits, high_num_sigma_hits).

| Table                | Column                  | Description                                                               |
|----------------------|-------------------------|---------------------------------------------------------------------------|
| sigma_labels_summary | critical_num_sigma_hits | Number of unique Sigma rules hit                                          |
| sigma_labels_summary | high_num_sigma_hits     | Number of unique Sigma rules hit                                          |
| sigma_labels_summary | high_num_sigma_rows     | Number of times Sigma rules hit for this entity                           |
| sigma_labels_summary | low_num_sigma_hits      | Number of unique Sigma rules hit                                          |
| sigma_labels_summary | low_num_sigma_rows      | Number of times Sigma rules hit for this entity                           |
| sigma_labels_summary | medium_num_sigma_hits   | Number of unique Sigma rules hit                                          |
| sigma_labels_summary | medium_num_sigma_rows   | Number of times Sigma rules hit for this entity                           |
| sigma_labels_summary | pid_hash                | Globally unique process hash (hash from hostname, os pid, and start_time) |
