# Produce markdown files for the data dicitonary
import duckdb
from wintappy.datautils import rawutil as ru

# Create objects to be documented
def create_target(con, schema='stdview', ddl_file='ACME4-stdview-ddl.sql'):
    con.sql(f'create schema {schema}')
    con.sql(f'use {schema}')
    ru.run_sql_no_args(con, 'ACME4-stdview-ddl.sql')
    con.sql(f'use main')

def load_comments(con, datafile='md_desc.parquet'):
    con.sql(f"create table md_comments as from '{datafile}'")

# Create Reporting Views
def create_views(con):
    ru.run_sql_no_args(con, 'datadictddl.sql')

def get_tables(con):
    return con.sql('select table_name, description from md_table_comments order by table_name').fetchall()

def publish(con, table):
    df = con.sql(f"select table_name, column_name, description from md_column_comments where table_name ilike '{table}' order by all").fetchdf()
    # Rename columns
    df.rename(columns={'table_name':'Table','column_name':'Column','description':'Description'},inplace=True)
    # Remove the pandas index. Its just the row number in this case
    print(df.to_markdown(index=False, tablefmt="github"))

def main(argv=None):
    con=duckdb.connect()
    create_target(con)
    load_comments(con)
    create_views(con)
    tables = get_tables(con)
    print('[[_TOC_]]')
    for table, desc in tables:
        print(f'\n## Table: {table}\n    {desc}\n')
        publish(con, table)

if __name__ == "__main__":
    main(argv=None)
