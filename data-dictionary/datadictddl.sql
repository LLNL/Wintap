-- Mapping function to simplify type description.
-- Note only types that change are listed in here. Some, such as BOOLEAN, DATE, INTEGER, are the same.
create function normalize_datatype(duckdb_type) as
case 
  when duckdb_type='BIGINT' then 'INT64'
  when duckdb_type like 'DECIMAL%' THEN 'DOUBLE'
  when duckdb_type like 'STRUCT%' THEN 'STRUCT'
  when duckdb_type like 'TIMESTAMP%' THEN 'TIMESTAMP'
  when duckdb_type like 'VARCHAR%' THEN replace(duckdb_type, 'VARCHAR', 'STRING')
  else duckdb_type end
;

-- Create views on DuckDB metadata tables
CREATE OR REPLACE VIEW md_columns AS SELECT schema_name, table_name, column_name, normalize_datatype(data_type) data_type FROM duckdb_columns where schema_name='stdview'
;

CREATE OR REPLACE VIEW md_tables AS SELECT schema_name, table_name FROM duckdb_tables where schema_name='stdview'
;

/**

Join comments data with table metadata.

Use a series of outer joins for the different types of matches.

**/
create or replace view md_column_comments
as
select t.*, ifnull(c1.description,ifnull(c2.description,c3.description)) description
  from md_columns t
  left outer join md_comments c1 on c1.column_name ilike t.column_name and c1.table_name ilike t.table_name
  left outer join md_comments c2 on t.column_name ilike c2.column_name and c2.table_name is null and not contains(c2.column_name,'%')
  left outer join md_comments c3 on t.column_name ilike c3.column_name and c3.table_name is null and contains(c3.column_name,'%')
;

-- Create a view that is for Table level comments
create or replace view md_table_comments
as
select t.*, c2.description
  from md_tables t
  left outer join (from md_comments where column_name is null) c2 on c2.table_name ilike t.table_name
;
