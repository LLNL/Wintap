import { Component, ViewChild, AfterViewInit, OnInit, ElementRef, OnDestroy } from '@angular/core';
import { Table } from 'primeng/table';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { ChangeDetectorRef } from '@angular/core';
import { CodemirrorComponent } from '@ctrl/ngx-codemirror';
import * as CodeMirror from 'codemirror';
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs/operators';
import { takeUntil } from 'rxjs/operators';
import { Subject } from 'rxjs';

// Import codemirror addons
import 'codemirror/mode/sql/sql';
import 'codemirror/addon/mode/overlay';
import 'codemirror/addon/hint/show-hint';
import 'codemirror/addon/edit/closebrackets';
import 'codemirror/addon/edit/matchbrackets';
import './wintapmessage';

@Component({
  selector: 'app-querybuilder',
  templateUrl: './querybuilder.component.html',
  styleUrls: ['./querybuilder.component.scss'],
  providers: [MessageService]
})
export class QuerybuilderComponent implements AfterViewInit, OnInit, OnDestroy {
  @ViewChild('editor') codeEditor!: CodemirrorComponent;
  @ViewChild('nameField') nameField!: ElementRef;
  @ViewChild('dt1') dt1!: Table;
  @ViewChild('resultsTable') resultsTable!: Table;

  // Component state
  private destroy$ = new Subject<void>();
  loading = false;
  eplListing: Statement[] = [];
  esperResults: EnhancedEsperResult[] = [];
  selectedStatement: any = null;
  selectedResultView: any = { label: 'Table View', value: 'table' };
  showConfirmDialog = false;
  showInvalidQueryDialog = false;
  queryError = '';
  helpDialogVisible = false;
  
  // Hub connection for real-time updates
  private connection!: HubConnection;
  
  // Dropdown options
  resultViewOptions = [
    { label: 'Table View', value: 'table' },
    { label: 'JSON View', value: 'json' },
    { label: 'Card View', value: 'card' }
  ];

  constructor(
    private http: HttpClient,
    private cd: ChangeDetectorRef,
    private messageService: MessageService
  ) {}

  ngOnInit() {
    this.initSignalRConnection();
    this.fetchEplListing();
  }

  ngAfterViewInit() {
    this.initCodeMirror();
  }

  ngOnDestroy() {
    // Clean up resources
    if (this.connection) {
      this.connection.stop().catch(err => console.error('Error stopping connection:', err));
    }
    this.destroy$.next();
    this.destroy$.complete();
  }

// Update your SignalR connection handler with this corrected version

/**
 * Set up the SignalR connection for real-time query results
 */
private initSignalRConnection() {
  this.connection = new HubConnectionBuilder()
    .withUrl('/signalr/workbenchHub')
    .withAutomaticReconnect([0, 2000, 10000, 30000])
    .build();

  this.connection.start()
    .then(() => {
      console.log('SignalR connection established');
    })
    .catch(error => {
      console.error('SignalR connection error:', error);
      this.showToast('error', 'Connection Error', 'Failed to connect to real-time updates');
    });

  this.connection.on('ReceiveMessage', (message: EsperResult, status: string) => {
    console.log('Message received:', message, 'Status:', status);
    
    // Ensure we have a valid result
    if (message && message.result) {
      // Create a new enhanced result object
      const enhancedResult: EnhancedEsperResult = {
        result: message.result,
        timestamp: new Date(),
        id: Date.now().toString(),
        type: this.getEventType(message.result)
      };
      
      console.log('Enhanced result created:', enhancedResult);
      
      // Add to the start of our results array
      this.esperResults = [enhancedResult, ...this.esperResults];
      
      // Log the current state
      console.log(`Results array now has ${this.esperResults.length} items`);
      
      // Force Angular to update the view
      this.cd.markForCheck();
      this.cd.detectChanges();
    } else {
      console.error('Received invalid message format:', message);
    }
  });
}
  // Helper function to extract PID from result string
extractPID(resultStr: string): string {
  if (!resultStr) return '';
  
  const pidMatch = resultStr.match(/PID=(\d+)/i);
  return pidMatch && pidMatch[1] ? pidMatch[1] : '';
}

// Helper function to extract ProcessName from result string
extractProcessName(resultStr: string): string {
  if (!resultStr) return '';
  
  const processMatch = resultStr.match(/ProcessName=([^,]+)/i);
  return processMatch && processMatch[1] ? processMatch[1] : '';
}

  // Initialize CodeMirror with custom options
  private initCodeMirror() {
    if (!this.codeEditor || !this.codeEditor.codeMirror) {
      setTimeout(() => this.initCodeMirror(), 100);
      return;
    }

    const editor = this.codeEditor.codeMirror;
    
    // Configure autocomplete
    editor.setOption('extraKeys', {
      'Ctrl-Space': 'autocomplete'
    });

    editor.setOption('hintOptions', {
      hint: this.customHintFunction
    });
    
    // Set some useful defaults
    if (editor) {
      editor.setValue(`SELECT * FROM WintapMessage 
WHERE MessageType = "Process"`);
    }
  }

  // Custom hint function for autocomplete
  private customHintFunction(editor: any): any {
    interface ISuggestion {
      text: string;
      displayText: string;
    }
    
    const cursor = editor.getCursor();
    const lineContent = editor.getLine(cursor.line);
    const textBeforeCursor = lineContent.substring(0, cursor.ch);
    
    // Create suggestions based on context
    const suggestions: ISuggestion[] = [];
    
    // Default suggestions if none are found
    if (suggestions.length === 0) {
      // Add basic Esper EPL keywords
      ['SELECT', 'FROM', 'WHERE', 'GROUP BY', 'HAVING', 'ORDER BY', 'INSERT INTO'].forEach(keyword => {
        suggestions.push({ text: keyword, displayText: keyword });
      });
      
      // Add WintapMessage properties
      ['WintapMessage', 'Process', 'TcpConnection', 'UdpPacket', 'ImageLoad', 'File', 'Registry'].forEach(type => {
        suggestions.push({ text: type, displayText: type });
      });
    }
    
    return {
      list: suggestions,
      from: CodeMirror.Pos(cursor.line, cursor.ch),
      to: CodeMirror.Pos(cursor.line, cursor.ch)
    };
  }

  // Fetch saved EPL statements
  fetchEplListing() {
    this.loading = true;
    this.http.get<ApiResponse>('/api/streams')
      .pipe(
        finalize(() => this.loading = false)
      )
      .subscribe(
        response => {
          this.eplListing = response.response;
        },
        error => {
          console.error('Error fetching EPL statements:', error);
          this.showToast('error', 'Error', 'Failed to load saved queries');
        }
      );
  }
  
  // Handler for selecting a statement from the list
  selectStatement(statement: Statement) {
    this.selectedStatement = statement;
  }

  // Execute a new EPL statement
  activateEpl(eplName: string) {
    if (!eplName || eplName.trim() === '') {
      this.showToast('warn', 'Query Name Required', 'Please provide a name for your query');
      return;
    }

    if (!this.codeEditor || !this.codeEditor.codeMirror) {
      this.showToast('error', 'Editor Not Ready', 'The query editor is not ready. Please try again.');
      return;
    }

    const editor = this.codeEditor.codeMirror;
    const editorContent = editor?.getValue() || '';
    
    if (!editorContent || editorContent.trim() === '') {
      this.showToast('warn', 'Empty Query', 'Please enter a query to execute');
      return;
    }

    this.loading = true;
    
    // Log the raw content
    console.log('Query content:', editorContent);
    
    // Do NOT encode the query content
    this.addStream(eplName, editorContent, "ACTIVE")
      .pipe(
        finalize(() => this.loading = false)
      )
      .subscribe(
        response => {
          this.showToast('success', 'Query Activated', `Query "${eplName}" has been successfully activated`);
          this.fetchEplListing();
        },
        error => {
          console.error('Error activating query:', error);
          
          // Show detailed error information
          const errorMsg = error.error?.Message || error.message || error.error || 'Unknown error occurred';
          console.log('Detailed error information:', error);
          
          this.queryError = errorMsg;
          this.showInvalidQueryDialog = true;
        }
      );
  }

  // Start a selected EPL statement
  startEpl(statement = this.selectedStatement) {
    if (!statement) {
      this.showToast('warn', 'No Query Selected', 'Please select a query to start');
      return;
    }

    this.loading = true;
    const encodedContent = encodeURIComponent(statement.query);
    
    this.addStream(statement.name, encodedContent, "START")
      .pipe(
        finalize(() => this.loading = false)
      )
      .subscribe(
        response => {
          this.showToast('success', 'Query Started', `Query "${statement.name}" has been started`);
          this.fetchEplListing();
        },
        error => {
          console.error('Error starting query:', error);
          this.queryError = error.error.Message || 'Unknown error occurred';
          this.showInvalidQueryDialog = true;
        }
      );
  }

  // Stop a selected EPL statement
  stopEpl(statement = this.selectedStatement) {
    if (!statement) {
      this.showToast('warn', 'No Query Selected', 'Please select a query to stop');
      return;
    }

    this.loading = true;
    const encodedContent = encodeURIComponent(statement.query);
    
    this.addStream(statement.name, encodedContent, "STOP")
      .pipe(
        finalize(() => this.loading = false)
      )
      .subscribe(
        response => {
          this.showToast('success', 'Query Stopped', `Query "${statement.name}" has been stopped`);
          this.fetchEplListing();
        },
        error => {
          console.error('Error stopping query:', error);
          this.queryError = error.error.Message || 'Unknown error occurred';
          this.showInvalidQueryDialog = true;
        }
      );
  }

  // Edit a selected EPL statement
  editEpl(statement = this.selectedStatement) {
    if (!statement) {
      this.showToast('warn', 'No Query Selected', 'Please select a query to edit');
      return;
    }

    // Load the query into the editor
    // Check if query is URL encoded and decode if necessary
    let queryText = statement.query;
    try {
      // Test if it's URL encoded by attempting to decode
      const decoded = decodeURIComponent(statement.query);
      // If it contains special characters typically encoded in URLs, it was likely encoded
      if (decoded.includes('%') || decoded.includes('&') || decoded.includes('=') || 
          decoded.includes('+') || decoded.includes('/')) {
        queryText = decoded;
      }
    } catch (error) {
      // If error in decoding, assume it's not encoded
      console.log('Query is not URL encoded:', error);
    }

    this.codeEditor.codeMirror!.setValue(queryText);
    this.nameField.nativeElement.value = statement.name;
    
    // Scroll the editor into view
    setTimeout(() => {
      const editorElement = document.querySelector('.query-editor-container');
      if (editorElement) {
        editorElement.scrollIntoView({ behavior: 'smooth' });
      }
    }, 100);
  }

  // Delete a single EPL statement
  deleteOneEpl(statement = this.selectedStatement) {
    if (!statement) {
      this.showToast('warn', 'No Query Selected', 'Please select a query to delete');
      return;
    }

    this.loading = true;
    const encodedContent = encodeURIComponent(statement.query);
    
    this.addStream(statement.name, encodedContent, "DELETE")
      .pipe(
        finalize(() => this.loading = false)
      )
      .subscribe(
        response => {
          this.showToast('success', 'Query Deleted', `Query "${statement.name}" has been deleted`);
          this.fetchEplListing();
          this.selectedStatement = null;
        },
        error => {
          console.error('Error deleting query:', error);
          this.queryError = error.error.Message || 'Unknown error occurred';
          this.showInvalidQueryDialog = true;
        }
      );
  }

  // Delete all EPL statements (shows confirmation dialog)
  deleteEpl() {
    this.showConfirmDialog = true;
  }

  // Confirm deletion of all EPL statements
  deleteConfirmed() {
    this.loading = true;
    
    this.http.delete('/api/Streams/')
      .pipe(
        finalize(() => {
          this.loading = false;
          this.showConfirmDialog = false;
        })
      )
      .subscribe(
        response => {
          this.showToast('success', 'All Queries Deleted', 'All queries have been successfully deleted');
          this.fetchEplListing();
          this.selectedStatement = null;
        },
        error => {
          console.error('Error deleting all queries:', error);
          this.showToast('error', 'Error', 'Failed to delete all queries');
        }
      );
  }

  // Clear query results
  clearResults(table: Table) {
    this.esperResults = [];
    if (table) {
      table.clear();
    }
    this.showToast('info', 'Results Cleared', 'Query results have been cleared');
  }

  // Helper method to add a stream - rewritten to match the EsperQuery class definition
  addStream(shortName: string, queryString: string, stateString: string) {
    console.log(`Adding/updating stream: ${shortName}, state: ${stateString}`);
    
    // Create body to match the EsperQuery class definition
    // Important: The enum must be sent as a numeric value, not a string
    // And we should NOT URL-encode the query string
    const body = {
      Name: shortName,
      Id: shortName, // Using Name as Id since they appear to be the same
      Query: queryString, // No encoding here - send the raw query string
      State: this.getEnumValueFromString(stateString)
    };
    
    console.log('Request body:', JSON.stringify(body));
    
    const headers = new HttpHeaders({
      'Content-Type': 'application/json'
    });
    
    return this.http.post('/api/streams', body, { headers });
  }
  
  // Helper method to convert string enum values to their numeric representation
  private getEnumValueFromString(enumString: string): number {
    switch (enumString.toUpperCase()) {
      case 'ACTIVE':
        return 0; // EsperState.ACTIVE = 0
      case 'STOPPED':
        return 1; // EsperState.STOPPED = 1
      case 'DELETED':
        return 2; // EsperState.DELETED = 2
      default:
        return 0; // Default to ACTIVE
    }
  }

  // Table filter methods
  onGlobalFilter(table: Table, event: Event) {
    table.filterGlobal((event.target as HTMLInputElement).value, 'contains');
  }

  onGlobalFilter2(table: Table, event: Event) {
    table.filterGlobal((event.target as HTMLInputElement).value, 'contains');
  }

  // Get severity class for status tag
  getStatusSeverity(status: string | number): string {
    if (status === undefined || status === null) return 'info';
    
    // Handle both string and numeric status values
    const statusStr = typeof status === 'number' 
      ? this.getEnumStringFromValue(status)
      : status.toString().toUpperCase();
    
    switch (statusStr) {
      case 'ACTIVE':
      case 'STARTED':
        return 'success';
      case 'STOPPED':
        return 'warning';
      case 'DELETED':
        return 'danger';
      default:
        return 'info';
    }
  }
  
  // Convert numeric enum value back to string - made public for use in template
  getEnumStringFromValue(enumValue: number | string | null | undefined): string {
    if (enumValue === undefined || enumValue === null) return 'UNKNOWN';
    
    // If it's already a string, return it
    if (typeof enumValue === 'string') {
      return enumValue.toUpperCase();
    }
    
    // Convert numeric value to string
    switch (enumValue) {
      case 0:
        return 'ACTIVE';
      case 1:
        return 'STOPPED';
      case 2:
        return 'DELETED';
      default:
        return 'UNKNOWN';
    }
  }

  // Extract event type from result string
  getEventType(resultStr: string): string {
    if (!resultStr) return 'Unknown';
    
    // Try to extract type from result string
    // This is a simple implementation - you may need to adjust based on actual data format
    const typeMatches = resultStr.match(/"MessageType"\s*:\s*"([^"]+)"/i) || 
                        resultStr.match(/MessageType\s*=\s*"([^"]+)"/i);
    
    if (typeMatches && typeMatches[1]) {
      return typeMatches[1];
    }
    
    return 'Event';
  }

  // Get icon for event type
  getEventTypeIcon(resultStr: string): string {
    const type = this.getEventType(resultStr);
    
    switch (type.toLowerCase()) {
      case 'process':
        return 'pi-desktop';
      case 'tcpconnection':
        return 'pi-link';
      case 'udppacket':
        return 'pi-send';
      case 'imageload':
        return 'pi-file';
      case 'file':
        return 'pi-folder';
      case 'registry':
        return 'pi-key';
      default:
        return 'pi-info-circle';
    }
  }

  // Show toast notification
  private showToast(severity: string, summary: string, detail: string) {
    this.messageService.add({
      severity,
      summary,
      detail,
      life: 3000
    });
    }

    // Add these properties to your component class:
    sidebarCollapsed = false;

    toggleSidebar() {
        this.sidebarCollapsed = !this.sidebarCollapsed;
    }

    createNewQuery() {
        // Clear the editor and set focus
        if (this.codeEditor && this.codeEditor.codeMirror) {
            this.codeEditor.codeMirror.setValue('');
            this.codeEditor.codeMirror.focus();
        }

        if (this.nameField) {
            this.nameField.nativeElement.value = '';
        }

        // Expand sidebar if collapsed
        if (this.sidebarCollapsed) {
            this.sidebarCollapsed = false;
        }
    }

    getQueryMenuItems(statement: Statement) {
        return [
            {
                label: 'Start',
                icon: 'pi pi-play',
                command: () => this.startEpl(statement)
            },
            {
                label: 'Stop',
                icon: 'pi pi-stop',
                command: () => this.stopEpl(statement)
            },
            {
                label: 'Edit',
                icon: 'pi pi-pencil',
                command: () => this.editEpl(statement)
            },
            {
                separator: true
            },
            {
                label: 'Delete',
                icon: 'pi pi-trash',
                command: () => this.deleteOneEpl(statement)
            }
        ];
    }

// Add these helper methods to the QueryBuilder component
// These help users craft queries using string literals

// MessageType enum values and descriptions
messageTypes = [
  { value: 'PROCESS', description: 'Process creation, termination, or refresh' },
  { value: 'TCP_CONNECTION', description: 'TCP network connection activity' },
  { value: 'UDP_PACKET', description: 'UDP network packet activity' },
  { value: 'FILE', description: 'File system operations' },
  { value: 'REGISTRY', description: 'Registry operations' },
  { value: 'IMAGE_LOAD', description: 'DLL/module loading' },
  { value: 'FOCUS_CHANGE', description: 'Window focus change events' },
  { value: 'SESSION_CHANGE', description: 'User session events' },
  { value: 'WAIT_CURSOR', description: 'UI wait cursor events' },
  { value: 'WMI', description: 'Windows Management Instrumentation events' },
  { value: 'THREAD', description: 'Thread creation and termination' },
  { value: 'GENERIC_MESSAGE', description: 'Generic event messages' },
  { value: 'MICROSOFT_WINDOWS_CPU_TRIGGER', description: 'High CPU usage events' },
  { value: 'MICROSOFT_WINDOWS_GROUP_POLICY', description: 'Group policy events' },
  { value: 'MEMORY_MAP', description: 'Memory mapping events' },
  { value: 'KERNEL_API_CALL', description: 'Kernel API call events' },
  { value: 'EVENT_LOG_EVENT', description: 'Windows event log entries' },
  { value: 'SYSDIG', description: 'Sysdig events' },
  { value: 'WINTAP_ALERT', description: 'Wintap internal alerts' }
];

// ActivityType enum values and descriptions
activityTypes = [
  { value: 'Start', description: 'Process or activity started' },
  { value: 'Stop', description: 'Process or activity stopped' },
  { value: 'Refresh', description: 'Refresh event' },
  { value: 'Read', description: 'Read operation' },
  { value: 'Write', description: 'Write operation' },
  { value: 'CreateKey', description: 'Registry key creation' },
  { value: 'DeleteKey', description: 'Registry key deletion' },
  { value: 'DeleteValue', description: 'Registry value deletion' },
  { value: 'Load', description: 'Module/image load' },
  { value: 'Unload', description: 'Module/image unload' },
  { value: 'HighCpuUsage', description: 'High CPU usage event' },
  { value: 'TcpIpConnect', description: 'TCP connection established' },
  { value: 'TcpIpSend', description: 'TCP data sent' },
  { value: 'TcpIpRecv', description: 'TCP data received' },
  { value: 'UdpIpSend', description: 'UDP data sent' },
  { value: 'UdpIpRecv', description: 'UDP data received' }
  // Add other activity types as needed
];

// Common query templates to help users get started
queryTemplates = [
  {
    name: 'Process Creation',
    query: `SELECT * FROM WintapMessage 
WHERE MessageType = "PROCESS" 
AND ActivityType = "Start"`,
    description: 'Shows all new processes that start'
  },
  {
    name: 'Registry Modifications',
    query: `SELECT * FROM WintapMessage 
WHERE MessageType = "REGISTRY" 
AND ActivityType IN ("Write", "CreateKey", "DeleteKey", "DeleteValue")`,
    description: 'Shows all registry changes'
  },
  {
    name: 'Network Connections',
    query: `SELECT * FROM WintapMessage 
WHERE MessageType = "TCP_CONNECTION" 
AND ActivityType = "TcpIpConnect"`,
    description: 'Shows all new TCP connections'
  },
  {
    name: 'File Operations',
    query: `SELECT * FROM WintapMessage 
WHERE MessageType = "FILE"`,
    description: 'Shows all file operations'
  },
  {
    name: 'Process + File Pattern',
    query: `SELECT * FROM PATTERN [
  every p=WintapMessage(MessageType="PROCESS", ActivityType="Start") ->
  f=WintapMessage(MessageType="FILE", PID=p.PID)
  WHERE timer:within(5 sec)
]`,
    description: 'Shows file operations within 5 seconds of a process start'
  }
];

// Function to insert a query template into the editor
insertTemplate(template: any) {
  if (this.codeEditor && this.codeEditor.codeMirror) {
    this.codeEditor.codeMirror.setValue(template.query);
    this.showToast('info', 'Template Inserted', `Inserted "${template.name}" template`);
  }
}

// Function to insert a MessageType into the current query at cursor position
insertMessageType(type: any) {
  if (this.codeEditor && this.codeEditor.codeMirror) {
    const editor = this.codeEditor.codeMirror;
    editor.replaceSelection(`"${type.value}"`);
    this.showToast('info', 'Type Inserted', `Inserted MessageType "${type.value}"`);
  }
}

// Function to insert an ActivityType into the current query at cursor position
insertActivityType(type: any) {
  if (this.codeEditor && this.codeEditor.codeMirror) {
    const editor = this.codeEditor.codeMirror;
    editor.replaceSelection(`"${type.value}"`);
    this.showToast('info', 'Type Inserted', `Inserted ActivityType "${type.value}"`);
  }
}

// Add a help button dialog that shows users how to write queries
showQueryHelp() {
  // Implementation would display a dialog with query guidance
  this.helpDialogVisible = true;
}


}

// Interface definitions
export interface ApiResponse {
  response: Statement[];
}

export interface Statement {
  name: string;
  query: string;
  statementType: string | null;
  state: string | null;
  createDate: number;
}

export interface EsperResult {
  result: string;
}

// Enhanced version of EsperResult with additional properties
export interface EnhancedEsperResult extends EsperResult {
  id: string;
  timestamp: Date;
  type: string;
}

