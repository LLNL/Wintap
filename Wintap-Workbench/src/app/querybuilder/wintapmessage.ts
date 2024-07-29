import * as CodeMirror from 'codemirror';
import 'codemirror/addon/mode/overlay';

// Create an overlay mode for the SQL mode
const wintapmessage: CodeMirror.Mode<any> = {
    token: (stream: CodeMirror.StringStream, state: any): string | null => {
    const prevChar = stream.string.charAt(stream.start - 1);
    const validPrevChar = prevChar === "." || prevChar === " ";

    // syntax highlighting rules
    // top-level
      if (stream.match(/^WintapMessage/, true)) {
        return "atom";
      }
      if (stream.match(/^ProcessName\b/, true)) {
        return "string";
      }
      if (stream.match(/^PID\b/, true)) {
        return "number";
      }
      if (stream.match(/^PidHash\b/, true)) {
        return "string";
      }
      if (stream.match(/^MessageType\b/, true)) {
        return "string";
      }
      if (stream.match(/^ActivityType\b/, true)) {
        return "string";
      }
      if (stream.match(/^EventTime\b/, true)) {
        return "number";
      }
      // Process
      if (stream.match(/^Process./, true)) {
        return "atom";
      }  
      if (stream.match(/^ParentPID\b/, true)) {
        return "number";
      }
      if (stream.match(/^ParentPidHash\b/, true)) {
        return "string";
      }
      if (stream.match(/^Name\b/, true)) {
        return "string";
      }
      if (stream.match(/^Path\b/, true)) {
        return "string";
      }
      if (stream.match(/^CommandLine\b/, true)) {
        return "string";
      }
      if (stream.match(/^Arguments\b/, true)) {
        return "string";
      }
      if (stream.match(/^User\b/, true)) {
        return "string";
      }
      if (stream.match(/^ExitCode\b/, true)) {
        return "number";
      }
      // TCP
      if (stream.match(/^TcpConnection\b/, true) && validPrevChar) {
        return "atom";
      }  
      if (stream.match(/^SourceAddress\b/, true)) {
        return "string";
      }
      if (stream.match(/^SourcePort\b/, true)) {
        return "number";
      }
      if (stream.match(/^DestinationAddress\b/, true)) {
        return "string";
      }
      if (stream.match(/^DestinationPort\b/, true)) {
        return "number";
      }
      // UDP
      if (stream.match(/^UdpPacket\b/, true) && validPrevChar) {
        return "atom";
      }  
      if (stream.match(/^SourceAddress\b/, true)) {
        return "string";
      }
      if (stream.match(/^SourcePort\b/, true)) {
        return "number";
      }
      if (stream.match(/^DestinationAddress\b/, true)) {
        return "string";
      }
      if (stream.match(/^DestinationPort\b/, true)) {
        return "number";
      }
      if (stream.match(/^PacketSize\b/, true)) {
        return "number";
      }
      if (stream.match(/^FailureCode\b/, true)) {
        return "string";
      }
       // ImageLoad
       if (stream.match(/^ImageLoad\b/, true) && validPrevChar) {
        return "atom";
      }  
      if (stream.match(/^FileName\b/, true)) {
        return "string";
      }
      if (stream.match(/^BuildTime\b/, true)) {
        return "number";
      }
      if (stream.match(/^ImageChecksum\b/, true)) {
        return "number";
      }
      if (stream.match(/^ImageSize\b/, true)) {
        return "number";
      }
      if (stream.match(/^DefaultBase\b/, true)) {
        return "string";
      }
      if (stream.match(/^ImageBase\b/, true)) {
        return "string";
      }
      if (stream.match(/^MD5\b/, true)) {
        return "string";
      }
      // File
      if (stream.match(/^FilActivity\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^BytesRequested\b/, true)) {
        return "number";
      }
      // Registry
      if (stream.match(/^RegActivity\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^DataType\b/, true)) {
        return "string";
      }
      if (stream.match(/^ValueName\b/, true)) {
        return "string";
      }
      if (stream.match(/^Data\b/, true)) {
        return "string";
      }
      // FocusChange
      if (stream.match(/^FocusChange\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^OldProcessId\b/, true)) {
        return "number";
      }
      if (stream.match(/^FocusChangeSessionId\b/, true)) {
        return "number";
      }
      // WaitCursor
      if (stream.match(/^WaitCursor\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^SessionId\b/, true)) {
        return "number";
      }
      if (stream.match(/^DisplayTimeMS\b/, true)) {
        return "number";
      }
      // GenericMessage
      if (stream.match(/^GenericMessage\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^Provider\b/, true)) {
        return "string";
      }
      if (stream.match(/^EventName\b/, true)) {
        return "string";
      }
      if (stream.match(/^Payload\b/, true)) {
        return "string";
      }
      // WmiActivity
      if (stream.match(/^WmiActivity\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^OperationId\b/, true)) {
        return "string";
      }
      if (stream.match(/^Operation\b/, true)) {
        return "string";
      }
      if (stream.match(/^User\b/, true)) {
        return "string";
      }
      if (stream.match(/^IsLocal\b/, true)) {
        return "string";
      }
      if (stream.match(/^ResultCode\b/, true)) {
        return "number";
      }
      // EventlogEvent
      if (stream.match(/^EventlogEvent\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^LogName\b/, true)) {
        return "string";
      }
      if (stream.match(/^LogSource\b/, true)) {
        return "string";
      }
      if (stream.match(/^EventId\b/, true)) {
        return "number";
      }
      if (stream.match(/^EventMessage\b/, true)) {
        return "string";
      }
      // KernelApiCall
      if (stream.match(/^KernelApiCall\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^ProviderName\b/, true)) {
        return "string";
      }
      if (stream.match(/^TargetPid\b/, true)) {
        return "number";
      }
      if (stream.match(/^DesiredAccess\b/, true)) {
        return "number";
      }
      if (stream.match(/^ReturnCode\b/, true)) {
        return "number";
      }
      if (stream.match(/^LinkSourceName\b/, true)) {
        return "string";
      }
      if (stream.match(/^LinkTargetName\b/, true)) {
        return "string";
      }
      if (stream.match(/^NotifyRoutineAddress\b/, true)) {
        return "number";
      }
      if (stream.match(/^TargetThreatId\b/, true)) {
        return "number";
      }
      // MemoryMap
      if (stream.match(/^MemoryMap\b/, true)) {
        return "atom";
      }  
      if (stream.match(/^Description\b/, true)) {
        return "string";
      }
      if (stream.match(/^BaseAddress\b/, true)) {
        return "string";
      }
      if (stream.match(/^AllocationBaseAddress\b/, true)) {
        return "string";
      }
      if (stream.match(/^AllocationProtect\b/, true)) {
        return "string";
      }
      if (stream.match(/^RegionSize\b/, true)) {
        return "number";
      }
      if (stream.match(/^PageProtect\b/, true)) {
        return "string";
      }
      if (stream.match(/^PageSize\b/, true)) {
        return "number";
      }
      stream.next();
      return null;
    },
  };  

// Register the overlay mode with CodeMirror..
CodeMirror.defineMode('wintapmessage', () => {
    console.log('registered wintapmessage with codemirror')
    return wintapmessage;
  });

export { wintapmessage };
