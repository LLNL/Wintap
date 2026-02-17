CREATE TABLE all_files
(
  filename VARCHAR,
  num_hosts BIGINT,
  process_num_rows DOUBLE,
  dll_num_rows DOUBLE,
  file_num_rows DOUBLE
)
;

CREATE TABLE files
(
  file_id VARCHAR,
  hostname VARCHAR,
  filename VARCHAR,
  process_num_rows DOUBLE,
  dll_num_rows DOUBLE,
  file_num_rows DOUBLE,
  min_process_started TIMESTAMP WITH TIME ZONE,
  max_process_term TIMESTAMP WITH TIME ZONE,
  dll_first_seen TIMESTAMP WITH TIME ZONE,
  dll_last_seen TIMESTAMP WITH TIME ZONE,
  file_first_seen TIMESTAMP WITH TIME ZONE,
  file_last_seen TIMESTAMP WITH TIME ZONE
)
;

CREATE TABLE host_ip
(
  agent_id VARCHAR,
  Hostname VARCHAR,
  os_family VARCHAR,
  private_gateway VARCHAR,
  ip_addr_no VARCHAR,
  mac VARCHAR,
  ip_addr BIGINT,
  interface VARCHAR,
  MTU INTEGER,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  num_rows BIGINT
)
;

CREATE TABLE host
(
  Hostname VARCHAR,
  agent_ids VARCHAR[],
  os_family VARCHAR,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  os VARCHAR,
  num_os BIGINT,
  os_version VARCHAR,
  num_os_version BIGINT,
  arch VARCHAR,
  num_arch BIGINT,
  processor_count INTEGER,
  num_processor_count BIGINT,
  processor_speed BIGINT,
  num_processor_speed BIGINT,
  has_battery BOOLEAN,
  num_has_battery BIGINT,
  ad_domain VARCHAR,
  num_ad_domain BIGINT,
  domain_role VARCHAR,
  num_domain_role BIGINT,
  last_boot BIGINT,
  num_last_boot BIGINT,
  wintap_version VARCHAR,
  num_wintap_version BIGINT,
  etl_version VARCHAR,
  num_etl_version BIGINT,
  num_rows BIGINT
)
;

CREATE TABLE labels_graph_net_conn
(
  conn_id VARCHAR,
  label_source VARCHAR,
  label_num_sources BIGINT,
  label_num_uniq_annotations BIGINT,
  label_num_hits BIGINT
)
;

CREATE TABLE labels_graph_nodes
(
  filename VARCHAR,
  node_type VARCHAR,
  id VARCHAR,
  annotation VARCHAR,
  "label" VARCHAR
)
;

CREATE TABLE labels_graph_process_summary
(
  pid_hash VARCHAR,
  label_source VARCHAR,
  label_num_sources BIGINT,
  label_num_uniq_annotations BIGINT,
  label_num_hits BIGINT
)
;

CREATE TABLE labels_networkx
(
  directed BOOLEAN,
  is_multigraph BOOLEAN,
  nodes STRUCT
("type" VARCHAR, id VARCHAR, ProcessKey STRUCT
(hostName VARCHAR, pid VARCHAR, firstEventTime VARCHAR, pid_hash UUID)[], ProcessExit STRUCT
(exitcode VARCHAR, CpuCycleCount VARCHAR, CpuUtilization VARCHAR, CommitCharge VARCHAR, CommitPeak VARCHAR, ReadOperationCount VARCHAR, WriteOperationCount VARCHAR, ReadTransferKB VARCHAR, WriteTransferKB VARCHAR, HardFaultCount VARCHAR, TokenElevationType VARCHAR)[], ParentProcess STRUCT
(pid_hash UUID)[], "User" STRUCT
(userName VARCHAR)[], ProcessDetails STRUCT
(pid VARCHAR, ProcessName VARCHAR, DisplayName VARCHAR, ProcessPath VARCHAR, MD5 UUID, ProcessStarted VARCHAR, ProcessTerminated VARCHAR, Filename VARCHAR, FileId UUID, pid_hash UUID, Args VARCHAR)[], "label" VARCHAR, FiveTupleKey STRUCT
(ConnId VARCHAR, LocalIp VARCHAR, LocalPort VARCHAR, RemoteIp VARCHAR, RemotePort VARCHAR, protocol VARCHAR)[], TcpConnSummary STRUCT
(TcpAcceptCount VARCHAR, TcpConnectCount VARCHAR, TcpSendCount VARCHAR, TcpSendSize VARCHAR, TcpRecvCount VARCHAR, TcpRecvSize VARCHAR, TcpDisconnectCount VARCHAR, firstSeen VARCHAR, lastSeen VARCHAR)[], FileKey STRUCT
(FileId UUID, Filename VARCHAR)[], FileDetails STRUCT
(Filename VARCHAR, ProcessNumRows VARCHAR, DllNumRows VARCHAR, FileNumRows VARCHAR, min_processstarted VARCHAR, max_processterm VARCHAR, FileId UUID, min_file_activity_time VARCHAR, max_file_activity_time VARCHAR)[], ProcessFileSummary STRUCT
(ProcessName VARCHAR, ActivityType VARCHAR, BytesRequested VARCHAR, EventCount VARCHAR, NumRawRows VARCHAR, firstSeen VARCHAR, lastSeen VARCHAR, PidHash UUID)[], annotation VARCHAR)[],
  links STRUCT
("type" VARCHAR, source UUID, target VARCHAR, ProcessDetails STRUCT
(pid VARCHAR, ProcessName VARCHAR, DisplayName VARCHAR, ProcessPath VARCHAR, MD5 UUID, ProcessStarted VARCHAR, ProcessTerminated VARCHAR, Filename VARCHAR, FileId UUID, pid_hash UUID)[], TcpConnSummary STRUCT
(TcpAcceptCount VARCHAR, TcpConnectCount VARCHAR, TcpSendCount VARCHAR, TcpSendSize VARCHAR, TcpRecvCount VARCHAR, TcpRecvSize VARCHAR, TcpDisconnectCount VARCHAR, firstSeen VARCHAR, lastSeen VARCHAR)[])[],
  filename VARCHAR
)
;

CREATE TABLE lolbas
(
  filename VARCHAR,
  description VARCHAR,
  author VARCHAR,
  date DATE,
  command VARCHAR,
  command_description VARCHAR,
  command_usecase VARCHAR,
  command_category VARCHAR,
  command_privileges VARCHAR,
  mitre_attck_technique VARCHAR,
  operating_system VARCHAR,
  paths VARCHAR,
  detections VARCHAR,
  resources VARCHAR,
  acknowledgements VARCHAR,
  url VARCHAR
)
;

CREATE TABLE mitre_labels
(
  entity VARCHAR,
  analytic_id VARCHAR,
  "time" TIMESTAMP,
  entity_type VARCHAR
)
;

CREATE TABLE process_conn_incr
(
  os_family VARCHAR,
  agent_id VARCHAR,
  Hostname VARCHAR,
  pid_hash VARCHAR,
  process_name VARCHAR,
  conn_id VARCHAR,
  protocol VARCHAR,
  incr_start TIMESTAMP WITH TIME ZONE,
  local_ip_addr VARCHAR,
  local_ip_int BIGINT,
  local_port INTEGER,
  remote_ip_addr VARCHAR,
  remote_ip_int BIGINT,
  remote_port INTEGER,
  total_events DOUBLE,
  total_size DOUBLE,
  num_raw_rows BIGINT,
  tcp_accept_count DOUBLE,
  tcp_connect_count DOUBLE,
  tcp_disconnect_count DOUBLE,
  tcp_reconnect_count DOUBLE,
  tcp_recv_count DOUBLE,
  tcp_recv_size DOUBLE,
  tcp_retransmit_count DOUBLE,
  tcp_send_count DOUBLE,
  tcp_send_size DOUBLE,
  tcp_tcpcopy_count DOUBLE,
  tcp_tcpcopy_size DOUBLE,
  udp_recv_count DOUBLE,
  udp_recv_size DOUBLE,
  udp_send_count DOUBLE,
  udp_send_size DOUBLE,
  min_10sec_eventcount INTEGER,
  max_10sec_eventcount INTEGER,
  min_size BIGINT,
  max_size BIGINT,
  sq_size DOUBLE,
  max_tcp_recv_count INTEGER,
  min_tcp_recv_size BIGINT,
  max_tcp_recv_size BIGINT,
  sq_tcp_recv_size DOUBLE,
  max_tcp_send_count INTEGER,
  min_tcp_send_size BIGINT,
  max_tcp_send_size BIGINT,
  sq_tcp_send_size DOUBLE,
  max_udp_recv_count INTEGER,
  min_udp_recv_size BIGINT,
  max_udp_recv_size BIGINT,
  sq_udp_recv_size DOUBLE,
  max_udp_send_count INTEGER,
  min_udp_send_size BIGINT,
  max_udp_send_size BIGINT,
  sq_udp_send_size DOUBLE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE
)
;

CREATE TABLE process_exe_file_summary
(
  source VARCHAR,
  hostname VARCHAR,
  filename VARCHAR,
  file_id VARCHAR,
  min_process_started TIMESTAMP WITH TIME ZONE,
  max_process_term TIMESTAMP WITH TIME ZONE,
  process_num_rows BIGINT
)
;

CREATE TABLE process_file
(
  agent_id VARCHAR,
  Hostname VARCHAR,
  pid_hash VARCHAR,
  process_name VARCHAR,
  file_id VARCHAR,
  file_hash VARCHAR,
  filename VARCHAR,
  activity_type VARCHAR,
  bytes_requested DOUBLE,
  event_count DOUBLE,
  num_raw_rows BIGINT,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  min_event TIMESTAMP WITH TIME ZONE,
  max_event TIMESTAMP WITH TIME ZONE
)
;

CREATE TABLE process_file_summary
(
  agent_id VARCHAR,
  Hostname VARCHAR,
  process_name VARCHAR,
  pid_hash VARCHAR,
  Close_Events DOUBLE,
  Create_Events DOUBLE,
  Delete_Events DOUBLE,
  Rename_Events DOUBLE,
  SetInfo_Events DOUBLE,
  Read_Bytes DOUBLE,
  Read_Events DOUBLE,
  Write_Bytes DOUBLE,
  Write_Events DOUBLE,
  num_raw_rows DOUBLE,
  num_uniq_file_hash BIGINT,
  num_null_filename DOUBLE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE
)
;

CREATE TABLE process_image_load
(
  pid_hash VARCHAR,
  filename VARCHAR,
  agent_id VARCHAR,
  hostname VARCHAR,
  process_name VARCHAR,
  file_id VARCHAR,
  file_md5 VARCHAR,
  build_time BIGINT,
  checksum INTEGER,
  default_base VARCHAR,
  image_base VARCHAR,
  min_image_size INTEGER,
  max_image_size INTEGER,
  num_load DOUBLE,
  num_unload DOUBLE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE
)
;

CREATE TABLE process_image_load_summary
(
  agent_id VARCHAR,
  hostname VARCHAR,
  pid_hash VARCHAR,
  process_name VARCHAR,
  dlls VARCHAR[],
  num_uniq_files BIGINT,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE
)
;

CREATE TABLE process_lolbas_summary
(
  pid_hash VARCHAR,
  lolbas_privs VARCHAR[],
  lolbas_cats VARCHAR[],
  lolbas_mitre VARCHAR[],
  lolbas_num_rows BIGINT
)
;

CREATE TABLE process_mitre_summary
(
  pid_hash VARCHAR,
  mitre_analytic_ids VARCHAR[],
  mitre_information_domains VARCHAR[],
  mitre_subtypes VARCHAR[][],
  mitre_analytic_types VARCHAR[][],
  mitre_num_rows BIGINT
)
;

CREATE TABLE process_net_conn
(
  os_family VARCHAR,
  agent_id VARCHAR,
  Hostname VARCHAR,
  pid_hash VARCHAR,
  process_name VARCHAR,
  conn_id VARCHAR,
  protocol VARCHAR,
  local_ip_addr VARCHAR,
  local_port INTEGER,
  remote_ip_addr VARCHAR,
  remote_port INTEGER,
  total_events DOUBLE,
  total_size DOUBLE,
  sq_size DOUBLE,
  num_raw_rows DOUBLE,
  tcp_accept_count DOUBLE,
  tcp_connect_count DOUBLE,
  tcp_disconnect_count DOUBLE,
  tcp_reconnect_count DOUBLE,
  tcp_recv_count DOUBLE,
  tcp_recv_size DOUBLE,
  sq_tcp_recv_size DOUBLE,
  tcp_retransmit_count DOUBLE,
  tcp_send_count DOUBLE,
  tcp_send_size DOUBLE,
  sq_tcp_send_size DOUBLE,
  tcp_tcpcopy_count DOUBLE,
  tcp_tcpcopy_size DOUBLE,
  udp_recv_count DOUBLE,
  udp_recv_size DOUBLE,
  sq_udp_recv_size DOUBLE,
  udp_send_count DOUBLE,
  udp_send_size DOUBLE,
  sq_udp_send_size DOUBLE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE
)
;

CREATE TABLE process_net_summary
(
  os_family VARCHAR,
  pid_hash VARCHAR,
  process_name VARCHAR,
  agent_id VARCHAR,
  Hostname VARCHAR,
  conn_id_count BIGINT,
  net_total_events DOUBLE,
  net_total_size DOUBLE,
  num_raw_rows DOUBLE,
  tcp_accept_count DOUBLE,
  tcp_connect_count DOUBLE,
  tcp_disconnect_count DOUBLE,
  tcp_reconnect_count DOUBLE,
  tcp_recv_count DOUBLE,
  tcp_recv_size DOUBLE,
  tcp_retransmit_count DOUBLE,
  tcp_send_count DOUBLE,
  tcp_send_size DOUBLE,
  tcp_tcpcopy_count DOUBLE,
  tcp_tcpcopy_size DOUBLE,
  udp_recv_count DOUBLE,
  udp_recv_size DOUBLE,
  udp_send_count DOUBLE,
  udp_send_size DOUBLE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  net_recv_size DOUBLE,
  net_send_size DOUBLE,
  net_rs_total DOUBLE,
  net_send_vs_recv DOUBLE,
  tcp_rs_total DOUBLE,
  tcp_send_vs_recv DOUBLE,
  udp_rs_total DOUBLE,
  udp_send_vs_recv DOUBLE,
  min_bytes DOUBLE,
  max_bytes DOUBLE,
  avg_bytes DOUBLE,
  min_packets DOUBLE,
  max_packets DOUBLE,
  avg_packets DOUBLE,
  sq_size DOUBLE
)
;

CREATE TABLE process
(
  pid_hash VARCHAR,
  os_family VARCHAR,
  agent_id VARCHAR,
  num_agent_id BIGINT,
  hostname VARCHAR,
  os_pid INTEGER,
  process_name VARCHAR,
  num_process_name BIGINT,
  args VARCHAR,
  num_args BIGINT,
  user_name VARCHAR,
  num_user_name BIGINT,
  parent_pid_hash VARCHAR,
  num_parent_pid_hash BIGINT,
  parent_os_pid INTEGER,
  num_parent_os_pid BIGINT,
  process_path VARCHAR,
  num_process_path BIGINT,
  filename VARCHAR,
  file_id VARCHAR,
  file_md5 VARCHAR,
  num_file_md5 BIGINT,
  file_sha2 VARCHAR,
  num_file_sha2 BIGINT,
  process_started_seconds DOUBLE,
  process_started TIMESTAMP WITH TIME ZONE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  num_process_start DOUBLE,
  process_stop_seconds DOUBLE,
  process_term TIMESTAMP WITH TIME ZONE,
  cpu_cycle_count BIGINT,
  cpu_utilization INTEGER,
  commit_charge BIGINT,
  commit_peak BIGINT,
  read_operation_count BIGINT,
  write_operation_count BIGINT,
  read_transfer_kilobytes BIGINT,
  write_transfer_kilobytes BIGINT,
  hard_fault_count INTEGER,
  token_elevation_type INTEGER,
  exit_code BIGINT,
  num_process_stop DOUBLE
)
;

CREATE TABLE process_path
(
  agent_id VARCHAR,
  hostname VARCHAR,
  pid_hash VARCHAR,
  os_pid INTEGER,
  process_name VARCHAR,
  process_path VARCHAR,
  "level" INTEGER,
  parent_pid_hash VARCHAR,
  parent_os_pid INTEGER,
  seq INTEGER,
  ptree VARCHAR,
  ptree_list VARCHAR[],
  ptree_list_tuples STRUCT
(pid_hash VARCHAR, process_name VARCHAR)[],
  max_level INTEGER
)
;

CREATE TABLE process_registry
(
  agent_id VARCHAR,
  hostname VARCHAR,
  pid_hash VARCHAR,
  process_name VARCHAR,
  reg_path VARCHAR,
  reg_value VARCHAR,
  activity_type VARCHAR,
  reg_data VARCHAR,
  event_count DOUBLE,
  num_raw_rows BIGINT,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  min_event TIMESTAMP WITH TIME ZONE,
  max_event TIMESTAMP WITH TIME ZONE
)
;

CREATE TABLE process_registry_summary
(
  agent_id VARCHAR,
  hostname VARCHAR,
  pid_hash VARCHAR,
  process_name VARCHAR,
  reads DOUBLE,
  writes DOUBLE,
  createkeys DOUBLE,
  deletekeys DOUBLE,
  deletevalues DOUBLE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  total_activity_types DOUBLE
)
;

CREATE TABLE process_summary
(
  pid_hash VARCHAR,
  os_family VARCHAR,
  agent_id VARCHAR,
  num_agent_id BIGINT,
  hostname VARCHAR,
  os_pid INTEGER,
  process_name VARCHAR,
  num_process_name BIGINT,
  args VARCHAR,
  num_args BIGINT,
  user_name VARCHAR,
  num_user_name BIGINT,
  parent_pid_hash VARCHAR,
  num_parent_pid_hash BIGINT,
  parent_os_pid INTEGER,
  num_parent_os_pid BIGINT,
  process_path VARCHAR,
  num_process_path BIGINT,
  filename VARCHAR,
  file_id VARCHAR,
  file_md5 VARCHAR,
  num_file_md5 BIGINT,
  file_sha2 VARCHAR,
  num_file_sha2 BIGINT,
  process_started_seconds DOUBLE,
  process_started TIMESTAMP WITH TIME ZONE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  num_process_start DOUBLE,
  process_stop_seconds DOUBLE,
  process_term TIMESTAMP WITH TIME ZONE,
  cpu_cycle_count BIGINT,
  cpu_utilization INTEGER,
  commit_charge BIGINT,
  commit_peak BIGINT,
  read_operation_count BIGINT,
  write_operation_count BIGINT,
  read_transfer_kilobytes BIGINT,
  write_transfer_kilobytes BIGINT,
  hard_fault_count INTEGER,
  token_elevation_type INTEGER,
  exit_code BIGINT,
  num_process_stop DOUBLE,
  duration_seconds DOUBLE,
  reg_totals DOUBLE,
  reg_reads DOUBLE,
  reg_writes DOUBLE,
  reg_createkeys DOUBLE,
  reg_deletekeys DOUBLE,
  reg_deletevalues DOUBLE,
  reg_first_seen TIMESTAMP WITH TIME ZONE,
  reg_last_seen TIMESTAMP WITH TIME ZONE,
  Close_Events DOUBLE,
  Create_Events DOUBLE,
  Delete_Events DOUBLE,
  Rename_Events DOUBLE,
  SetInfo_Events DOUBLE,
  Read_Bytes DOUBLE,
  Read_Events DOUBLE,
  Write_Bytes DOUBLE,
  Write_Events DOUBLE,
  file_num_raw_rows DOUBLE,
  num_uniq_file_hash BIGINT,
  num_null_filename DOUBLE,
  file_first_seen TIMESTAMP WITH TIME ZONE,
  file_last_seen TIMESTAMP WITH TIME ZONE,
  conn_id_count BIGINT,
  net_total_events DOUBLE,
  net_total_size DOUBLE,
  net_num_raw_rows DOUBLE,
  tcp_accept_count DOUBLE,
  tcp_connect_count DOUBLE,
  tcp_disconnect_count DOUBLE,
  tcp_reconnect_count DOUBLE,
  tcp_recv_count DOUBLE,
  tcp_recv_size DOUBLE,
  tcp_retransmit_count DOUBLE,
  tcp_send_count DOUBLE,
  tcp_send_size DOUBLE,
  tcp_tcpcopy_count DOUBLE,
  tcp_tcpcopy_size DOUBLE,
  udp_recv_count DOUBLE,
  udp_recv_size DOUBLE,
  udp_send_count DOUBLE,
  udp_send_size DOUBLE,
  net_recv_size DOUBLE,
  net_send_size DOUBLE,
  net_rs_total DOUBLE,
  net_send_vs_recv DOUBLE,
  tcp_rs_total DOUBLE,
  tcp_send_vs_recv DOUBLE,
  udp_rs_total DOUBLE,
  udp_send_vs_recv DOUBLE,
  min_bytes DOUBLE,
  max_bytes DOUBLE,
  avg_bytes DOUBLE,
  min_packets DOUBLE,
  max_packets DOUBLE,
  avg_packets DOUBLE,
  sq_size DOUBLE,
  net_first_seen TIMESTAMP WITH TIME ZONE,
  net_last_seen TIMESTAMP WITH TIME ZONE,
  dlls VARCHAR[],
  dll_num_uniq_files BIGINT,
  dll_first_seen TIMESTAMP WITH TIME ZONE,
  dll_last_seen TIMESTAMP WITH TIME ZONE,
  os VARCHAR,
  os_version VARCHAR,
  arch VARCHAR
)
;

CREATE TABLE process_uber_summary
(
  pid_hash VARCHAR,
  os_family VARCHAR,
  agent_id VARCHAR,
  num_agent_id BIGINT,
  hostname VARCHAR,
  os_pid INTEGER,
  process_name VARCHAR,
  num_process_name BIGINT,
  args VARCHAR,
  num_args BIGINT,
  user_name VARCHAR,
  num_user_name BIGINT,
  parent_pid_hash VARCHAR,
  num_parent_pid_hash BIGINT,
  parent_os_pid INTEGER,
  num_parent_os_pid BIGINT,
  process_path VARCHAR,
  num_process_path BIGINT,
  filename VARCHAR,
  file_id VARCHAR,
  file_md5 VARCHAR,
  num_file_md5 BIGINT,
  file_sha2 VARCHAR,
  num_file_sha2 BIGINT,
  process_started_seconds DOUBLE,
  process_started TIMESTAMP WITH TIME ZONE,
  first_seen TIMESTAMP WITH TIME ZONE,
  last_seen TIMESTAMP WITH TIME ZONE,
  num_process_start DOUBLE,
  process_stop_seconds DOUBLE,
  process_term TIMESTAMP WITH TIME ZONE,
  cpu_cycle_count BIGINT,
  cpu_utilization INTEGER,
  commit_charge BIGINT,
  commit_peak BIGINT,
  read_operation_count BIGINT,
  write_operation_count BIGINT,
  read_transfer_kilobytes BIGINT,
  write_transfer_kilobytes BIGINT,
  hard_fault_count INTEGER,
  token_elevation_type INTEGER,
  exit_code BIGINT,
  num_process_stop DOUBLE,
  duration_seconds DOUBLE,
  reg_totals DOUBLE,
  reg_reads DOUBLE,
  reg_writes DOUBLE,
  reg_createkeys DOUBLE,
  reg_deletekeys DOUBLE,
  reg_deletevalues DOUBLE,
  reg_first_seen TIMESTAMP WITH TIME ZONE,
  reg_last_seen TIMESTAMP WITH TIME ZONE,
  Close_Events DOUBLE,
  Create_Events DOUBLE,
  Delete_Events DOUBLE,
  Rename_Events DOUBLE,
  SetInfo_Events DOUBLE,
  Read_Bytes DOUBLE,
  Read_Events DOUBLE,
  Write_Bytes DOUBLE,
  Write_Events DOUBLE,
  file_num_raw_rows DOUBLE,
  num_uniq_file_hash BIGINT,
  num_null_filename DOUBLE,
  file_first_seen TIMESTAMP WITH TIME ZONE,
  file_last_seen TIMESTAMP WITH TIME ZONE,
  conn_id_count BIGINT,
  net_total_events DOUBLE,
  net_total_size DOUBLE,
  net_num_raw_rows DOUBLE,
  tcp_accept_count DOUBLE,
  tcp_connect_count DOUBLE,
  tcp_disconnect_count DOUBLE,
  tcp_reconnect_count DOUBLE,
  tcp_recv_count DOUBLE,
  tcp_recv_size DOUBLE,
  tcp_retransmit_count DOUBLE,
  tcp_send_count DOUBLE,
  tcp_send_size DOUBLE,
  tcp_tcpcopy_count DOUBLE,
  tcp_tcpcopy_size DOUBLE,
  udp_recv_count DOUBLE,
  udp_recv_size DOUBLE,
  udp_send_count DOUBLE,
  udp_send_size DOUBLE,
  net_recv_size DOUBLE,
  net_send_size DOUBLE,
  net_rs_total DOUBLE,
  net_send_vs_recv DOUBLE,
  tcp_rs_total DOUBLE,
  tcp_send_vs_recv DOUBLE,
  udp_rs_total DOUBLE,
  udp_send_vs_recv DOUBLE,
  min_bytes DOUBLE,
  max_bytes DOUBLE,
  avg_bytes DOUBLE,
  min_packets DOUBLE,
  max_packets DOUBLE,
  avg_packets DOUBLE,
  sq_size DOUBLE,
  net_first_seen TIMESTAMP WITH TIME ZONE,
  net_last_seen TIMESTAMP WITH TIME ZONE,
  dlls VARCHAR[],
  dll_num_uniq_files BIGINT,
  dll_first_seen TIMESTAMP WITH TIME ZONE,
  dll_last_seen TIMESTAMP WITH TIME ZONE,
  os VARCHAR,
  os_version VARCHAR,
  arch VARCHAR,
  high_num_sigma_hits BIGINT,
  high_num_sigma_rows BIGINT,
  low_num_sigma_hits BIGINT,
  low_num_sigma_rows BIGINT,
  medium_num_sigma_hits BIGINT,
  medium_num_sigma_rows BIGINT,
  critical_num_sigma_hits DECIMAL
(18,3),
  total_sigma_hits DECIMAL
(25,3),
  label_source VARCHAR,
  label_num_sources BIGINT,
  label_num_uniq_annotations BIGINT,
  label_num_hits BIGINT,
  lolbas_privs VARCHAR[],
  lolbas_cats VARCHAR[],
  lolbas_mitre VARCHAR[],
  lolbas_num_rows BIGINT,
  mitre_analytic_ids VARCHAR[],
  mitre_information_domains VARCHAR[],
  mitre_subtypes VARCHAR[][],
  mitre_analytic_types VARCHAR[][],
  mitre_num_rows BIGINT
)
;

CREATE TABLE sigma_labels
(
  entity VARCHAR,
  analytic_id VARCHAR,
  "time" TIMESTAMP,
  entity_type VARCHAR
)
;

CREATE TABLE sigma_labels_summary
(
  pid_hash VARCHAR,
  high_num_sigma_hits BIGINT,
  high_num_sigma_rows BIGINT,
  low_num_sigma_hits BIGINT,
  low_num_sigma_rows BIGINT,
  medium_num_sigma_hits BIGINT,
  medium_num_sigma_rows BIGINT,
  critical_num_sigma_hits DECIMAL
(18,3)
)
;

