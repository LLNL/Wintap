#!/usr/bin/env python3
"""
Produce LaTeX files for the data dictionary from DuckDB schema
"""
import duckdb
from wintappy.datautils import rawutil as ru


def create_target(con, schema='stdview', ddl_file='ACME4-stdview-ddl.sql'):
    """Create the target schema to be documented"""
    con.sql(f'create schema {schema}')
    con.sql(f'use {schema}')
    ru.run_sql_no_args(con, ddl_file)
    con.sql(f'use main')


def load_comments(con, datafile='md_desc.parquet'):
    """Load table and column descriptions"""
    con.sql(f"create table md_comments as from '{datafile}'")


def create_views(con):
    """Create reporting views for metadata"""
    ru.run_sql_no_args(con, 'datadictddl.sql')


def get_tables(con):
    """Get list of tables with descriptions"""
    return con.sql('select table_name, description from md_table_comments order by table_name').fetchall()


def escape_tex(text):
    """Escape special LaTeX characters"""
    if text is None:
        return ''
    
    result = str(text)
    
    # Order matters! Do backslash first, then others
    result = result.replace('\\', r'\textbackslash{}')
    result = result.replace('&', r'\&')
    result = result.replace('%', r'\%')
    result = result.replace('$', r'\$')
    result = result.replace('#', r'\#')
    result = result.replace('_', r'\_')
    result = result.replace('{', r'\{')
    result = result.replace('}', r'\}')
    result = result.replace('~', r'\textasciitilde{}')
    result = result.replace('^', r'\textasciicircum{}')
    
    return result


def write_tex_header(f):
    """Write LaTeX document header"""
    f.write(r'''\documentclass[conference,compsoc]{IEEEtran}
\usepackage{longtable}
\usepackage{booktabs}
\usepackage{array}
\usepackage{textcomp}
\usepackage{needspace}

\begin{document}
\onecolumn

\section{Table Definitions}

% Suppress warnings about missing aux file on first compile
\IfFileExists{\jobname.aux}{\tableofcontents\clearpage}{}

''')


def write_tex_footer(f):
    """Write LaTeX document footer"""
    f.write(r'\twocolumn')
    f.write(r'\end{document}')


def publish_table_tex(con, table, f):
    """Write a single table's data dictionary to LaTeX"""
    # Get table description
    table_desc = con.sql(
        f"select description from md_table_comments where table_name = '{table}'"
    ).fetchone()
    
    desc_text = escape_tex(table_desc[0]) if table_desc and table_desc[0] else ''
    
    # Write section header with space requirement
    f.write('\\needspace{4\\baselineskip}\n')  # Ensure space for header + 1 row
    f.write(f'\\subsection{{{escape_tex(table)}}}\n')
    f.write(f'\\label{{sec:{table.lower()}}}\n\n')
    
    if desc_text:
        f.write(f'{desc_text}\n\n')
    
    # Get columns for this table
    df = con.sql(f"""
        select column_name, description, data_type 
        from md_column_comments 
        where table_name = '{table}' 
        order by column_name
    """).fetchdf()
    
    if df.empty:
        f.write('\\textit{No column information available.}\n\n')
        return
    
    # Write longtable with fixed column widths
    f.write('\\begin{longtable}{|p{5cm}|p{2cm}|p{9cm}|}\n')
    f.write('\\hline\n')
    f.write('\\textbf{Column} & \\textbf{Data Type} & \\textbf{Description} \\\\\n')
    f.write('\\hline\n')
    f.write('\\endfirsthead\n\n')
    
    # Continuation header
    f.write('\\multicolumn{3}{c}%\n')
    f.write(f'{{\\tablename\\ \\thetable\\ -- Continued from previous page}} \\\\\n')
    f.write('\\hline\n')
    f.write('\\textbf{Column} & \\textbf{Data Type} & \\textbf{Description} \\\\\n')
    f.write('\\hline\n')
    f.write('\\endhead\n\n')
    
    # Continuation footer
    f.write('\\hline\n')
    f.write('\\multicolumn{3}{r}{Continued on next page} \\\\\n')
    f.write('\\endfoot\n\n')
    
    # Final footer
    f.write('\\hline\n')
    f.write('\\endlastfoot\n\n')
    
    # Write data rows
    for _, row in df.iterrows():
        col_name = escape_tex(row['column_name'])
        col_type = escape_tex(row['data_type'])
        col_desc = escape_tex(row['description'])
        f.write(f'{col_name} & {col_type} & {col_desc} \\\\\n')
    
    f.write('\\hline\n')
    f.write('\\end{longtable}\n\n')


def main(argv=None, output_file='data_dictionary.tex'):
    """Main function to generate LaTeX data dictionary"""
    con = duckdb.connect()
    
    # Setup database
    create_target(con)
    load_comments(con)
    create_views(con)
    
    # Get all tables
    tables = get_tables(con)
    
    # Write LaTeX file
    with open(output_file, 'w', encoding='utf-8') as f:
        write_tex_header(f)
        
        for table, desc in tables:
            publish_table_tex(con, table, f)
        
        write_tex_footer(f)
    
    print(f'LaTeX data dictionary written to {output_file}')
    print(f'Documented {len(tables)} tables')
    
    con.close()


if __name__ == "__main__":
    main()