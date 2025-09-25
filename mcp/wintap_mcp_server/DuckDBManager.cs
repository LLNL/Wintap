using DuckDB.NET.Data;
using System.Text;

public static class DuckDBManager
{
    private static DuckDBConnection _connection;

    public static void Initialize()
    {
        if (_connection == null)
        {
            _connection = new DuckDBConnection("Data Source=:memory:");
            _connection.Open();
            CreateOrReplaceProcessViews();
        }
    }

    private static void CreateOrReplaceProcessViews()
    {
        string parquetDir = Path.Combine(@"c:\programdata\wintap", "parquet", "merged");
        string processParquet = Path.Combine(parquetDir, "*+raw_process+*.parquet");
        string tcpParquet = Path.Combine(parquetDir, "*+raw_tcp_process_conn_incr+*.parquet");
        string udpParquet = Path.Combine(parquetDir, "*+raw_udp_process_conn_incr+*.parquet");
        string registryParquet = Path.Combine(parquetDir, "*+raw_registry+*.parquet");
        string fileParquet = Path.Combine(parquetDir, "*+raw_file+*.parquet");
        string wmiParquet = Path.Combine(parquetDir, "*+raw_wmi+*.parquet");
        string apiParquet = Path.Combine(parquetDir, "*+raw_apicall+*.parquet");
        string memoryParquet = Path.Combine(parquetDir, "*+raw_memorymap+*.parquet");
        string ilParquet = Path.Combine(parquetDir, "*+raw_imageload+*.parquet");
        string fcParquet = Path.Combine(parquetDir, "*+raw_focuschange+*.parquet");
        string cpuParquet = Path.Combine(parquetDir, "*+raw_cputrigger+*.parquet");

        string parquetGlobSql = processParquet.Replace(@"\", @"\\");
        string sqlProcess = $@"
            CREATE OR REPLACE VIEW Wintap_Process AS
            SELECT * FROM read_parquet('{parquetGlobSql}');
        ";

        string parquetPath = tcpParquet.Replace(@"\", @"\\");
        string sqlTcp = $@"
            CREATE OR REPLACE VIEW Wintap_Tcp AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = udpParquet.Replace(@"\", @"\\");
        string sqlUdp = $@"
            CREATE OR REPLACE VIEW Wintap_Udp AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = registryParquet.Replace(@"\", @"\\");
        string sqlRegistry = $@"
            CREATE OR REPLACE VIEW Wintap_Registry AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = fileParquet.Replace(@"\", @"\\");
        string sqlFile = $@"
            CREATE OR REPLACE VIEW Wintap_File AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = wmiParquet.Replace(@"\", @"\\");
        string sqlWmi = $@"
            CREATE OR REPLACE VIEW Wintap_WMI AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = apiParquet.Replace(@"\", @"\\");
        string sqlApi = $@"
            CREATE OR REPLACE VIEW Wintap_APICalls AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = memoryParquet.Replace(@"\", @"\\");
        string sqlMemory = $@"
            CREATE OR REPLACE VIEW Wintap_Memory AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = ilParquet.Replace(@"\", @"\\");
        string sqlIl = $@"
            CREATE OR REPLACE VIEW Wintap_ImageLoad AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = fcParquet.Replace(@"\", @"\\");
        string sqlFc = $@"
            CREATE OR REPLACE VIEW Wintap_FocusChange AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        parquetPath = cpuParquet.Replace(@"\", @"\\");
        string sqlCpu = $@"
            CREATE OR REPLACE VIEW Wintap_CpuTrigger AS
            SELECT * FROM read_parquet('{parquetPath}');
        ";

        using (var command = _connection.CreateCommand())
        {
            try
            {
                command.CommandText = sqlProcess;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }

            try
            {
                command.CommandText = sqlTcp;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }

            try
            {
                command.CommandText = sqlFile;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }


            try
            {
                command.CommandText = sqlRegistry;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }

            try
            {
                command.CommandText = sqlIl;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }


            try
            {
                command.CommandText = sqlApi;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }

            try
            {
                command.CommandText = sqlWmi;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {

            }

            try
            {
                command.CommandText = sqlMemory;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }

            try
            {
                command.CommandText = sqlFc;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }

            try
            {
                command.CommandText = sqlCpu;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // to do:  log the error
            }
        }
    }

    public static string ExecuteSQL(string sqlCmd, CancellationToken cancellationToken)
    {
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = sqlCmd;

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException();

            try
            {
                using (var reader = command.ExecuteReader())
                {
                    var sb = new StringBuilder();

                    // Write column headers
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        if (i > 0) sb.Append('\t');
                        sb.Append(reader.GetName(i));
                    }
                    sb.AppendLine();

                    // Write rows
                    while (reader.Read())
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            if (i > 0) sb.Append('\t');
                            sb.Append(reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString());
                        }
                        sb.AppendLine();
                    }

                    return sb.ToString();
                }
            }
            catch (DuckDBException ex) when (ex.Message.Contains("no results"))
            {
                int affected = command.ExecuteNonQuery();
                return $"Command executed successfully. Rows affected: {affected}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}