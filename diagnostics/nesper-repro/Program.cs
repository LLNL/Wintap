using com.espertech.esper.common.client;
using com.espertech.esper.common.client.configuration;
using com.espertech.esper.compat;
using com.espertech.esper.compiler.client;
using com.espertech.esper.runtime.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using System.Diagnostics;
using System.Threading;

if (args.Contains("--cache-benchmark"))
{
    int operations = ReadIntArgument(args, "--operations", 20_000);
    int keys = ReadIntArgument(args, "--keys", 10_000);
    int capacity = ReadIntArgument(args, "--capacity", keys);
    int missPenaltyUs = ReadIntArgument(args, "--miss-penalty-us", 250);
    RunCacheBenchmark(operations, keys, capacity, missPenaltyUs);
    return;
}

if (args.Contains("--benchmark"))
{
    int eventCount = ReadIntArgument(args, "--events", 200_000);
    int cardinality = ReadIntArgument(args, "--cardinality", 10_000);
    int rounds = ReadIntArgument(args, "--rounds", 1);
    string? scenario = ReadStringArgument(args, "--scenario");
    RunBenchmark(eventCount, cardinality, rounds, scenario);
    return;
}

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

static int ReadIntArgument(string[] args, string name, int defaultValue)
{
    int index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value) || value <= 0)
    {
        return defaultValue;
    }

    return value;
}

static void RunCacheBenchmark(int operations, int keys, int capacity, int missPenaltyUs)
{
    Console.WriteLine("PROCESS_CACHE_BENCHMARK_BEGIN");
    foreach (int hitPercent in new[] { 100, 90, 75, 50, 0 })
    {
        var cache = new BoundedEventTimeCache<int>(capacity);
        DateTime start = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMinutes(1);
        for (int key = 0; key < keys; key++)
        {
            cache.Set(key, "instance-" + key, start, end, key);
        }
        cache.TakeCounters(out _, out _, out long setupEvictions, out _);

        int observedHits = 0;
        int observedMisses = 0;
        Stopwatch timer = Stopwatch.StartNew();
        for (int i = 0; i < operations; i++)
        {
            bool shouldHit = i % 100 < hitPercent;
            int retainedKeys = Math.Min(keys, capacity);
            int key = shouldHit
                ? keys - 1 - (i % retainedKeys)
                : keys + (i % keys);
            if (cache.TryGet(key, start.AddSeconds(30), out _))
            {
                observedHits++;
            }
            else
            {
                observedMisses++;
                BusyWaitMicroseconds(missPenaltyUs);
            }
        }
        timer.Stop();
        cache.TakeCounters(out _, out _, out long runtimeEvictions, out int entries);

        Console.WriteLine(
            $"PROCESS_CACHE_RESULT|hit_target={hitPercent}|capacity={capacity}|keys={keys}|hits={observedHits}|misses={observedMisses}|entries={entries}|setup_evictions={setupEvictions}|runtime_evictions={runtimeEvictions}|elapsed_ms={timer.Elapsed.TotalMilliseconds:F2}|ops_per_sec={operations / timer.Elapsed.TotalSeconds:F0}|miss_penalty_us={missPenaltyUs}");
    }
    Console.WriteLine("PROCESS_CACHE_BENCHMARK_END");
}

static void BusyWaitMicroseconds(int microseconds)
{
    long ticks = Math.Max(1, microseconds * Stopwatch.Frequency / 1_000_000L);
    long end = Stopwatch.GetTimestamp() + ticks;
    while (Stopwatch.GetTimestamp() < end)
    {
        Thread.SpinWait(32);
    }
}

static string? ReadStringArgument(string[] args, string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void RunBenchmark(int eventCount, int cardinality, int rounds, string? selectedScenario)
{
    cardinality = Math.Min(eventCount, cardinality);
    WintapMessage[] events = CreateFileEvents(eventCount, cardinality, out long expectedEventCount, out long expectedBytes);

    Console.WriteLine("NESPER_BENCHMARK_BEGIN");
    Console.WriteLine($"BENCHMARK_INPUT|events={eventCount}|cardinality={cardinality}|expected_event_count={expectedEventCount}|expected_bytes={expectedBytes}");

    var scenarios = new[]
    {
        (Name: "baseline-no-epl", Epl: (string?)null, Broad: false, Outbound: false, ConcurrentExpiry: false, Context: false),
        (Name: "file-cast", Epl: (string?)NesperBenchmarkQueries.FileCast, Broad: false, Outbound: false, ConcurrentExpiry: false, Context: false),
        (Name: "file-native-enum", Epl: (string?)NesperBenchmarkQueries.FileNativeEnum, Broad: false, Outbound: false, ConcurrentExpiry: false, Context: false),
        (Name: "file-cast-plus-broad", Epl: (string?)NesperBenchmarkQueries.FileCast, Broad: true, Outbound: false, ConcurrentExpiry: false, Context: false),
        (Name: "file-cast-outbound-1", Epl: (string?)NesperBenchmarkQueries.FileCast, Broad: false, Outbound: true, ConcurrentExpiry: false, Context: false),
        (Name: "file-cast-concurrent-expiry", Epl: (string?)NesperBenchmarkQueries.FileCast, Broad: false, Outbound: false, ConcurrentExpiry: true, Context: false),
        (Name: "file-cast-outbound-1-concurrent-expiry", Epl: (string?)NesperBenchmarkQueries.FileCast, Broad: false, Outbound: true, ConcurrentExpiry: true, Context: false),
        (Name: "file-context", Epl: (string?)NesperBenchmarkQueries.FileContext, Broad: false, Outbound: false, ConcurrentExpiry: false, Context: true),
        (Name: "file-context-concurrent-expiry", Epl: (string?)NesperBenchmarkQueries.FileContext, Broad: false, Outbound: false, ConcurrentExpiry: true, Context: true)
    };

    foreach (var scenario in scenarios)
    {
        if (selectedScenario == null && scenario.Name == "file-context-concurrent-expiry")
        {
            continue;
        }

        if (selectedScenario == null || string.Equals(selectedScenario, scenario.Name, StringComparison.OrdinalIgnoreCase))
        {
            for (int round = 1; round <= rounds; round++)
            {
                RunBenchmarkScenario($"{scenario.Name}-r{round}", events, expectedEventCount, expectedBytes, scenario.Epl, scenario.Broad, scenario.Outbound, scenario.ConcurrentExpiry, scenario.Context);
            }
        }
    }

    Console.WriteLine("NESPER_BENCHMARK_END");
}

static void RunBenchmarkScenario(
    string scenario,
    WintapMessage[] events,
    long expectedEventCount,
    long expectedBytes,
    string? fileEpl,
    bool deployBroadStatement,
    bool outboundThreading,
    bool concurrentExpiry,
    bool useContext)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    Configuration configuration = new Configuration();
    configuration.Common.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;
    configuration.Common.AddEventType(typeof(WintapMessage));
    configuration.Compiler.ByteCode.IsAllowSubscriber = true;
    configuration.Compiler.ByteCode.SetAccessModifiersPublic();
    configuration.Compiler.ByteCode.BusModifierEventType = com.espertech.esper.common.client.util.EventTypeBusModifier.BUS;
    configuration.Runtime.Threading.IsInternalTimerEnabled = false;

    if (outboundThreading)
    {
        configuration.Runtime.Threading.IsThreadPoolOutbound = true;
        configuration.Runtime.Threading.ThreadPoolOutboundNumThreads = 1;
        configuration.Runtime.Threading.ThreadPoolOutboundCapacity = 1024;
        configuration.Runtime.Threading.IsListenerDispatchPreserveOrder = true;
    }

    EPRuntime runtime = EPRuntimeProvider.GetRuntime("benchmark-" + scenario + "-" + Guid.NewGuid(), configuration);
    runtime.EventService.AdvanceTime(0);

    long actualEventCount = 0;
    long actualBytes = 0;
    int outputRows = 0;
    int broadRows = 0;
    using ManualResetEventSlim batchDelivered = new ManualResetEventSlim(fileEpl == null);

    if (fileEpl != null)
    {
        if (useContext)
        {
            CompileDeploy(runtime, configuration, NesperBenchmarkQueries.Context, scenario + "-context");
        }
        EPStatement statement = CompileDeploy(runtime, configuration, fileEpl, scenario + "-file");
        statement.Events += (_, eventArgs) =>
        {
            foreach (EventBean row in eventArgs.NewEvents)
            {
                Interlocked.Add(ref actualEventCount, Convert.ToInt64(row["eventCount"]));
                Interlocked.Add(ref actualBytes, Convert.ToInt64(row["bytesRequested"]));
                Interlocked.Increment(ref outputRows);
            }
            batchDelivered.Set();
        };
    }

    if (deployBroadStatement)
    {
        EPStatement broad = CompileDeploy(
            runtime,
            configuration,
            "SELECT * FROM WintapMessage WHERE CAST(MessageType, string) <> 'ProcessPartial'",
            scenario + "-broad");
        broad.Events += (_, eventArgs) => Interlocked.Add(ref broadRows, eventArgs.NewEvents.Length);
    }

    Stopwatch sendTimer = Stopwatch.StartNew();
    int splitIndex = concurrentExpiry ? events.Length / 2 : events.Length;
    for (int i = 0; i < splitIndex; i++)
    {
        runtime.EventService.SendEventBean(events[i], "WintapMessage");
    }

    Task? expiryTask = null;
    if (concurrentExpiry)
    {
        using ManualResetEventSlim expiryStarted = new ManualResetEventSlim(false);
        expiryTask = Task.Run(() =>
        {
            expiryStarted.Set();
            runtime.EventService.AdvanceTime(10_001);
        });
        expiryStarted.Wait();

        for (int i = splitIndex; i < events.Length; i++)
        {
            runtime.EventService.SendEventBean(events[i], "WintapMessage");
        }
    }
    sendTimer.Stop();

    Stopwatch deliveryTimer = Stopwatch.StartNew();
    expiryTask?.Wait();
    runtime.EventService.AdvanceTime(concurrentExpiry ? 20_002 : 10_001);
    bool delivered = batchDelivered.Wait(TimeSpan.FromSeconds(30)) &&
        (fileEpl == null || SpinWait.SpinUntil(
            () => Interlocked.Read(ref actualEventCount) == expectedEventCount,
            TimeSpan.FromSeconds(30)));
    deliveryTimer.Stop();

    bool fidelityPass = fileEpl == null ||
        (delivered && actualEventCount == expectedEventCount && actualBytes == expectedBytes);
    double eventsPerSecond = events.Length / sendTimer.Elapsed.TotalSeconds;

    Console.WriteLine(
        $"BENCHMARK_RESULT|scenario={scenario}|send_ms={sendTimer.Elapsed.TotalMilliseconds:F2}|events_per_sec={eventsPerSecond:F0}|delivery_ms={deliveryTimer.Elapsed.TotalMilliseconds:F2}|output_rows={outputRows}|broad_rows={broadRows}|actual_event_count={actualEventCount}|actual_bytes={actualBytes}|fidelity={(fidelityPass ? "PASS" : "FAIL")}");

    runtime.Destroy();
}

static EPStatement CompileDeploy(EPRuntime runtime, Configuration configuration, string epl, string name)
{
    CompilerArguments compilerArguments = new CompilerArguments(configuration);
    compilerArguments.GetPath().Add(runtime.RuntimePath);
    EPCompiled compiled = EPCompilerProvider.Compiler.Compile($"@name('{name}') {epl}", compilerArguments);
    return runtime.DeploymentService.Deploy(compiled).Statements[0];
}

static WintapMessage[] CreateFileEvents(int eventCount, int cardinality, out long expectedEventCount, out long expectedBytes)
{
    WintapMessage[] events = new WintapMessage[eventCount];
    expectedEventCount = 0;
    expectedBytes = 0;
    DateTime eventTime = DateTime.UtcNow;

    for (int i = 0; i < eventCount; i++)
    {
        int representedEvents = (i % 3) + 1;
        int bytes = representedEvents * 4096;
        int identity = i % cardinality;
        var message = new WintapMessage(eventTime, identity % 64 + 1000, WintapMessage.MessageTypeEnum.File)
        {
            PidHash = "pid-" + (identity % 64),
            ProcessName = "benchmark",
            AgentId = "benchmark-agent",
            ActivityType = (WintapMessage.ActivityTypeEnum)(
                (int)WintapMessage.ActivityTypeEnum.Read + (identity % 2)),
            File = new WintapMessage.FileActivityObject
            {
                Path = "/benchmark/path-" + identity,
                BytesRequested = bytes,
                EventCount = representedEvents,
                FirstSeenEventTime = eventTime.ToFileTimeUtc(),
                LastSeenEventTime = eventTime.ToFileTimeUtc()
            }
        };

        events[i] = message;
        expectedEventCount += representedEvents;
        expectedBytes += bytes;
    }

    return events;
}

public static class NesperBenchmarkQueries
{
    public const string Context = "create context Every10Seconds initiated @now and pattern [every timer:interval(10 seconds)] terminated after 10 seconds";

    public const string FileCast = """
select istream
  file.path as path,
  sum(file.bytesRequested) as bytesRequested,
  PID,
  PidHash,
  ProcessName,
  CAST(activityType, string) as activityType,
  min(case when file.firstSeenEventTime > 0 then file.firstSeenEventTime else eventTime end) as firstSeen,
  max(case when file.lastSeenEventTime > 0 then file.lastSeenEventTime else eventTime end) as lastSeen,
  sum(file.eventCount) as eventCount,
  AgentId
from WintapMessage(CAST(messageType, string) = 'File').win:time_batch(10 sec)
group by file.path, PidHash, PID, activityType, ProcessName, AgentId
""";

    public const string FileNativeEnum = """
select istream
  file.path as path,
  sum(file.bytesRequested) as bytesRequested,
  PID,
  PidHash,
  ProcessName,
  activityType as activityType,
  min(case when file.firstSeenEventTime > 0 then file.firstSeenEventTime else eventTime end) as firstSeen,
  max(case when file.lastSeenEventTime > 0 then file.lastSeenEventTime else eventTime end) as lastSeen,
  sum(file.eventCount) as eventCount,
  AgentId
from WintapMessage(messageType = gov.llnl.wintap.collect.models.WintapMessage$MessageTypeEnum.File).win:time_batch(10 sec)
group by file.path, PidHash, PID, activityType, ProcessName, AgentId
""";

    public const string FileContext = """
context Every10Seconds select istream
  file.path as path,
  sum(file.bytesRequested) as bytesRequested,
  PID,
  PidHash,
  ProcessName,
  CAST(activityType, string) as activityType,
  min(case when file.firstSeenEventTime > 0 then file.firstSeenEventTime else eventTime end) as firstSeen,
  max(case when file.lastSeenEventTime > 0 then file.lastSeenEventTime else eventTime end) as lastSeen,
  sum(file.eventCount) as eventCount,
  AgentId
from WintapMessage(CAST(messageType, string) = 'File')
group by file.path, PidHash, PID, activityType, ProcessName, AgentId
output snapshot when terminated
""";
}

public class SimpleEvent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
