using ModelContextProtocol.Server;
using System.ComponentModel;

namespace gov.llnl.wintap.ai.mcp
{
    [McpServerToolType]
    internal class mcp_tools
    {

        internal mcp_tools()
        {

        }

        [McpServerTool(Name = "TellTime"), Description("Tells the current local date and time")]
        public static string TellTime(CancellationToken cancellationToken)
        {
            return DateTime.Now.ToString();
        }

        [McpServerTool(Name = "RunSQL"), Description("Allows you to run arbitrary sql commands on the Wintap telemetry database")]
        public static string RunSQL(string sqlCmd, CancellationToken cancellationToken)
        {
            return DuckDBManager.ExecuteSQL(sqlCmd, cancellationToken);
        }
    }

}
