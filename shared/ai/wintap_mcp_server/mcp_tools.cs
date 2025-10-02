/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using ModelContextProtocol.Server;
using System.ComponentModel;

namespace gov.llnl.wintap.ai.mcp
{
    /// <summary>
    /// Provides Model Context Protocol (MCP) tools for AI agent integration with Wintap telemetry data.
    /// </summary>
    /// <remarks>
    /// <para><strong>⚠️ RESEARCH / PROOF-OF-CONCEPT IMPLEMENTATION ⚠️</strong></para>
    /// <para>
    /// This implementation is currently in an early research/proof-of-concept phase and is NOT suitable
    /// for production use. It is designed for research experimentation and may lack
    /// production-grade error handling, input validation, access controls, and security hardening.
    /// Use only in controlled research environments.
    /// </para>
    /// 
    /// <para>
    /// This class exposes Wintap's telemetry querying capabilities to AI models through the Model Context Protocol,
    /// enabling AI agents (local Ollama models or remote OpenAI-compatible APIs) to analyze cybersecurity data
    /// using natural language queries that are translated into SQL operations.
    /// </para>
    /// 
    /// <para><strong>Architecture:</strong></para>
    /// <list type="bullet">
    /// <item><description>Integrates with Microsoft Semantic Kernel's MCP tool calling framework</description></item>
    /// <item><description>Decorated with <c>[McpServerToolType]</c> for automatic tool registration</description></item>
    /// <item><description>Each method marked with <c>[McpServerTool]</c> becomes callable by AI agents</description></item>
    /// <item><description>Provides cancellation support for long-running AI operations</description></item>
    /// </list>
    /// 
    /// <para><strong>Available Tools:</strong></para>
    /// <list type="bullet">
    /// <item>
    ///     <description><strong>TellTime</strong> - Returns current local date and time, simple test case.
    ///     <para>Use case: Provides temporal context for time-based security queries</para>
    ///     </description>
    /// </item>
    /// <item>
    ///     <description><strong>RunSQL</strong> - Executes arbitrary SQL queries against Wintap telemetry database
    ///     <para>Use case: Enables AI agents to query process, network, file, registry, and other system activity data</para>
    ///     <para>Data sources: All Wintap_* views (Process, Tcp, Udp, Registry, File, WMI, APICalls, Memory, ImageLoad, etc.)</para>
    ///     </description>
    /// </item>
    /// </list>
    /// 
    /// <para><strong>AI Integration Flow:</strong></para>
    /// <code>
    /// User: "Show me all chrome.exe processes from the last hour"
    ///   ↓
    /// AI Model (via MCP): Calls RunSQL with appropriate query
    ///   ↓
    /// mcp_tools.RunSQL(): Executes query via DuckDBManager
    ///   ↓
    /// Results returned to AI model for interpretation
    ///   ↓
    /// AI presents findings to user in natural language
    /// </code>
    /// 
    /// <para><strong>Security Considerations:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>CRITICAL:</strong> RunSQL allows arbitrary SQL execution with NO input validation or sanitization</description></item>
    /// <item><description><strong>CRITICAL:</strong> No authentication, authorization, or access control currently implemented</description></item>
    /// <item><description>Suitable ONLY for single-user research environments or controlled testing</description></item>
    /// </list>
    /// 
    /// <para><strong>Known Limitations:</strong></para>
    /// <list type="bullet">
    /// <item><description>No query timeout enforcement beyond CancellationToken</description></item>
    /// <item><description>No result set size limits - large queries may cause memory issues</description></item>
    /// <item><description>Error messages returned directly to AI without sanitization</description></item>
    /// <item><description>No query logging or audit trail</description></item>
    /// </list>
    /// 
    /// <para><strong>Usage Context:</strong></para>
    /// <para>
    /// This class is part of Wintap's AI integration layer, designed to make cybersecurity research data
    /// accessible through conversational interfaces. It supports both local inference (Ollama) and remote
    /// API calls (OpenAI-compatible endpoints) via Microsoft's Semantic Kernel abstractions.
    /// </para>
    /// 
    /// <para><strong>Example AI Interactions:</strong></para>
    /// <list type="bullet">
    /// <item><description>"What processes have made outbound network connections in the last 10 minutes?"</description></item>
    /// <item><description>"Show me all registry modifications by suspicious processes"</description></item>
    /// <item><description>"Find processes that loaded unusual DLLs"</description></item>
    /// <item><description>"Analyze TCP connection patterns for potential data exfiltration"</description></item>
    /// </list>
    /// 
    /// <para><strong>Cancellation Support:</strong></para>
    /// <para>
    /// All tools accept a <c>CancellationToken</c> to allow graceful termination of long-running
    /// AI operations or queries. This is particularly important for complex SQL queries that may
    /// scan large amounts of telemetry data.
    /// </para>
    /// 
    /// <para><strong>Extension and Future Work:</strong></para>
    /// <para>
    /// Additional tools can be added by creating new static methods with the <c>[McpServerTool]</c>
    /// attribute. Consider adding tools for common security analysis patterns, threat hunting queries,
    /// or integration with external threat intelligence sources. Production-ready implementations should
    /// include proper security controls, query validation, and monitoring capabilities.
    /// </para>
    /// </remarks>
    /// <seealso cref="DuckDBManager"/>
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
