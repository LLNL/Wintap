using com.espertech.esper.common.client;
using com.espertech.esper.common.client.configuration;
using com.espertech.esper.compat;
using com.espertech.esper.compiler.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;

Console.WriteLine("NESPER_REPRO_BEGIN");
Console.WriteLine($"DOTNET_VERSION|{Environment.Version}");
Console.WriteLine($"OS|{System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine($"ARCH|{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

RunScenario("simple-event", typeof(SimpleEvent), new[]
{
    "SELECT * FROM SimpleEvent",
    "SELECT Id, Name FROM SimpleEvent WHERE Id = 1"
});

RunScenario("wintap-message", typeof(WintapMessage), new[]
{
    "SELECT * FROM WintapMessage",
    "SELECT * FROM WintapMessage WHERE CAST(MessageType, string) = 'Process'",
    "SELECT * FROM WintapMessage WHERE CAST(MessageType, string) <> 'ProcessPartial'",
    "@Name(\"Every10Seconds Context DDL\") create context Every10Seconds initiated @now and pattern [every timer:interval(10 seconds)] terminated after 10 seconds"
});

Console.WriteLine("NESPER_REPRO_END");

static void RunScenario(string scenario, Type eventType, string[] queries)
{
    Console.WriteLine($"SCENARIO_BEGIN|{scenario}|{eventType.FullName}");

    Configuration configuration = new Configuration();
    configuration.Common.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;
    configuration.Common.AddEventType(eventType);
    configuration.Compiler.ByteCode.IsAllowSubscriber = true;
    configuration.Compiler.ByteCode.SetAccessModifiersPublic();
    configuration.Compiler.ByteCode.BusModifierEventType = com.espertech.esper.common.client.util.EventTypeBusModifier.BUS;

    EPRuntime runtime = EPRuntimeProvider.GetRuntime("repro-" + scenario + "-" + Guid.NewGuid(), configuration);

    for (int i = 0; i < queries.Length; i++)
    {
        string name = scenario + "_query_" + i;
        string epl = "@name('" + name + "') " + queries[i];
        try
        {
            CompilerArguments compilerArguments = new CompilerArguments(configuration);
            compilerArguments.GetPath().Add(runtime.RuntimePath);

            var module = EPCompilerProvider.Compiler.ParseModule(epl);
            EPCompilerProvider.Compiler.SyntaxValidate(module, compilerArguments);
            EPCompiled compiled = EPCompilerProvider.Compiler.Compile(epl, compilerArguments);
            runtime.DeploymentService.Deploy(compiled);

            Console.WriteLine($"NESPER_REPRO_RESULT|{scenario}|{i}|OK|{OneLine(queries[i])}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NESPER_REPRO_RESULT|{scenario}|{i}|FAIL|{ex.GetType().FullName}|{OneLine(ex.Message)}|{OneLine(queries[i])}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"NESPER_REPRO_INNER|{scenario}|{i}|{ex.InnerException.GetType().FullName}|{OneLine(ex.InnerException.Message)}");
            }
        }
    }

    runtime.Destroy();
    Console.WriteLine($"SCENARIO_END|{scenario}");
}

static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');

public class SimpleEvent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
