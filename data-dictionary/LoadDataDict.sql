create OR replace table md_comments as from '~/git/LLNL/Wintap/data-dictionary/md_desc.parquet'
;

-- Use this to export the in-memory data to parquet. Note that it intentionally doesn't overwite the original source file.
copy md_comments to '~/git/LLNL/Wintap/data-dictionary/md_desc-test.parquet'
;

-- Create all the objects we'll be commenting. Put them in their own schema, just to help organize a little.
create schema stdview;

use memory.stdview;
/** Go run the ACME ddl script here. Once run, the stdview schema will be used as ground truth for all tables and columns in the data dictionary.
.read ACME4-stdview-ddl.sql
 */
use memory.main;

/** Go run the DDL to create the base tables/views. Shared by publish scripts.
.read datadictddl.sql
**/
-- QA

-- Multi-table, wildcarded comments
select * from md_comments
where table_name is null  and contains(column_name,'%')
;

-- Full outer join allows us to see any tables defined in the ground truth schema with comments and comments with no table in ground truth.
select t.*, c2.description, c2.table_name, c2.*
from md_tables t
full outer join (from md_comments where column_name is null) c2 on c2.table_name ilike t.table_name
;

-- Columns without descriptions. Should be none!
select * from md_column_comments where description is null order by table_name


select table_name, description from md_table_comments order by all

select * from md_column_comments where column_name ilike 'token%' order by table_name


select * from md_column_comments where table_name like '%mitre%' order by table_name, column_name


select * from md_comments where description like '%summarize%' and table_name ilike 'process_conn_incr'


-- Add Table comment:

insert into md_comments(table_name, column_name, column_type, description )
values 
('mitre_labelsn',null,null,'The mitre_labels table maps process or entity activity to matched MITRE ATT&CK rules in the threat detection pipeline. MITRE ATT&CK (Adversarial Tactics, Techniques, and Common Knowledge) is a globally accessible knowledge base of adversary tactics and techniques based on real-world observations of cyberattacks. It provides a comprehensive framework for understanding attacker behavior across the attack lifecycle. This table links entities to triggered MITRE ATT&CK rules and includes associated metadata such as technique IDs, tactic categories, severity levels, and detection types, enabling security analysts to contextualize suspicious activity within the broader landscape of known adversary behaviors.')


update md_comments
set table_name='process_net_summary' where description like '%Summarizes%' and table_name ilike 'process_net_conn'

update md_comments
set description=replace(description,'↔',' to ') where description like '%↔%'

-- Delete based on a pattern
delete from md_comments where column_name like 'MY PATTERN HERE%'

update md_comments
set table_name='mitre_labels' where table_name='mitre_labelsn'


SELECT schema_name, table_name, column_name, data_type FROM duckdb_columns where schema_name='stdview' and table_name='process'

 