import { Injectable } from '@angular/core';
import {
    Component, ViewChild, AfterViewInit, OnInit, ElementRef, QueryList
} from '@angular/core';
import { Table } from 'primeng/table';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ChangeDetectorRef } from '@angular/core';
import 'codemirror/mode/sql/sql';
import { CodemirrorComponent } from '@ctrl/ngx-codemirror';
import * as CodeMirror from 'codemirror';
import 'codemirror/addon/mode/overlay';
import './wintapmessage';
import { HubConnectionBuilder } from '@microsoft/signalr';
import 'codemirror/addon/hint/show-hint';


declare var $: any;

interface expandedRows {
    [key: string]: boolean;
}

@Injectable({
    providedIn: 'root'
})

@Component({
  selector: 'app-querybuilder',
  templateUrl: './querybuilder.component.html',
    styleUrls: ['./querybuilder.component.css']
})
export class QuerybuilderComponent implements AfterViewInit, OnInit {

    @ViewChild('editor') codeEditor!: CodemirrorComponent;
    @ViewChild('nameField') nameField!: ElementRef;
    @ViewChild('dt1') table!: Table;
    @ViewChild('resultsContainer') resultsContainer!: ElementRef;

    ngAfterViewInit(): void {
        configureCodeMirror();
        setTimeout(() => {
            if (this.codeEditor && this.codeEditor.codeMirror) {
              const editor = this.codeEditor.codeMirror;
              console.log('CodeMirror is initialized:', this.codeEditor.codeMirror);
              editor.setOption('extraKeys', {
                "Ctrl-Space": "autocomplete"
              });
        
              editor.setOption('hintOptions', {
                hint: this.customHintFunction
              });
            } else {
              console.error('CodeMirror instance is not available.');
            }
        }, 500);  

        this.fetchEplListing();
    }

    eplListing: Statement[] = [];
    selectedStatement: any = null;
    loading: boolean = true;
    showConfirmDialog = false;
    showInvalidQueryDialog = false;
    private connection: any;
    queryResults: string[] = [];
    queryError: string = '';

    esperResults: EsperResult[] = [];
    esperResult!: EsperResult;
    selectedResult: any = null;

    constructor(private http: HttpClient, private cd: ChangeDetectorRef) {
        this.connection = $.hubConnection('/signalr');
        const hubProxy = this.connection.createHubProxy('workbenchHub');
    }

    ngOnInit() {
        this.loading = false;

        this.connection = new HubConnectionBuilder()
        .withUrl('/signalr/workbenchHub')
        .withAutomaticReconnect([0, 2000, 10000, 30000])
        .build();

      console.log('starting hub connect');
      this.connection.start().catch(console.error('error'));

      console.log("wp1");
      console.log(this.connection.state); // Check the state here

      const fullyQualifiedUrl = `${window.location.protocol}//${window.location.hostname}`;

      console.log(`URL:  ${fullyQualifiedUrl}`);
      console.log("wp2");
    this.connection.on('ReceiveMessage', (message: EsperResult) => {
        console.log('esper query result: ' + message.result);

      this.esperResults.push(message);
          this.cd.detectChanges();  
    });
    }

    private customHintFunction(editor: any): any {

        interface IObjectProperties {
            [key: string]: string[];
          }

        interface IActivityProperties {
            [key: string]: string;
        }
          
          interface ISuggestion {
            text: string;
            displayText: string;
          }
          
          const objectProperties: IObjectProperties = {
            'WintapMessage': ['PID (number)', 'EventTime (long)', 'ProcessName (string)', 'MessageType (string)', 'ReceiveTime (long)', 'PidHash (string)', 'ActivityType (string)', 'CorrelationId (string)', 'ActivityId (string)'],
            'Process': ['ParentPID (number)', 'ParentPidHash (string)', 'ParentProcessName (string)', 'Name (string)', 'Path (lower string)', 'CommandLine (lower string)', 'Arguments (lower string)', 'User (string)', 'ExitCode (number)', 'CPUCycleCount (number)', 'CPUUtilization (number)', 'CommitCharge (number)', 'CommitPeak (number)', 'ReadOperationCount (number)', 'WriteOperationCount (number)', 'ReadTransferKiloBytes (number)', 'WriteTransferKiloBytes (number)', 'HardFaultCount (number)', 'TokenElevationType (number)', 'PID (number)', 'UniqueProcessKey (string)', 'MD5 (string)', 'SHA2 (string)'],
            'TcpConnection': ['Direction (string)', 'SourceAddress (string)', 'SourcePort (number)', 'DestinationAddress (string)', 'DestinationPort (number)', 'State (string)', 'MaxSegSize (number)', 'RcvWin', 'RcvWinScale', 'SackOpt', 'SeqNo', 'PacketSize', 'SendWinScale', 'TimestampOption', 'WinScaleOption', 'EndTime', 'StartTime', 'FailureCode', 'PID'],
            'UdpPacket': ['SourceAddress (string)', 'SourcePort (number)', 'DestinationAddress (string)', 'DestinationPort (number)', 'PacketSize (number)'],
            'ImageLoad': ['FileName (string)', 'BuildTime (number)', 'ImageChecksum (number)', 'ImageSize (number)', 'DefaultBase (string)', 'ImageBase (string)', 'MD5 (string)'],
            'File': ['Path (string)', 'BytesRequested (number)'],
            'Registry': ['Path (string)', 'DataType (string)', 'ValueName (string)', 'Data (string)',],
            'FocusChange': ['OldProcessId (number)', 'FocusChangeSessionId (number)'],
            'WaitCursor': ['SessionId (number)', 'DisplayTimeMS (number)'],
            'GenericMessage': ['Provider (string)', 'EventName (string)', 'Payload (string)'],
            'WmiActivity': ['OperationId (number)', 'Operation (string)', 'User (string)', 'IsLocal (number)', 'ResultCode (number)'],
            'EventlogEvent': ['LogName (string)', 'LogSource (string)', 'EventId (number)', 'EventMessage (string)'],
            'KernelApiCall': ['ProviderName (string)', 'TargetPid (number)', 'DesiredAccess (number)', 'ReturnCode (number)', 'LinkSourceName (string)', 'LinkTargetName (string)', 'NotifyRoutineAddress (number)', 'TargetThreatId (number)'],
            'MemoryMap': ['Description (string)', 'BaseAddress (string)', 'AllocationBaseAddress (string)', 'AllocationProtect (string)', 'RegionSize (number)', 'PageProtect (string)', 'PageType (string)'],
          };

          const activityProperties: IActivityProperties = {
            'start': 'start',
            'stop': 'stop',
            'refresh': 'refresh',
            'WRITE': 'WRITE',
            'READ': 'READ',
            'CLOSE': 'CLOSE'
          }

          let suggestions: ISuggestion[] = [];

          const cursor = editor.getCursor();
          const lineContent = editor.getLine(cursor.line);
          const textBeforeCursor = lineContent.substring(0, cursor.ch);
        
          // Regular expression to match "MessageType =" or "MessageType="
          const messageTypeRegex = /messagetype\s*=\s*$/i;
          const activityTypeRegex = /activitytype\s*=\s*$/i;
        
          if (messageTypeRegex.test(textBeforeCursor)) {
            console.log("MessageType context recognized");
            suggestions = Object.keys(objectProperties).map(key => {
              let displayText = key;
              let text = key;
              if (key.toLowerCase() === 'process' || key.toLowerCase() === 'tcpconnection' || key.toLowerCase() === 'udppacket' || key.toLowerCase() == 'imageload'|| key.toLowerCase() == 'file'|| key.toLowerCase() == 'registry' || key.toLowerCase() == 'focuschange' || key.toLowerCase() == 'waitcursor' || key.toLowerCase() == 'genericmessage' || key.toLowerCase() == 'wmiactivity'|| key.toLowerCase() == 'eventlogevent'|| key.toLowerCase() == 'kernelapicall'|| key.toLowerCase() == 'memorymap' ) {
                text = `"${key}"`;
              }
              return { text, displayText };
            });
          } else if (activityTypeRegex.test(textBeforeCursor)) {
            console.log("ActivityType context recognized");
            suggestions = Object.keys(activityProperties).map(key => {
              let displayText = key;
              let text = `"${key}"`;
              return { text, displayText };
            });
          } else {
            const match = textBeforeCursor.match(/(\w+)\.$/i);
            if (match) {
              const key = match[1].toLowerCase(); 
              const lowerCaseProperties: IObjectProperties = {};
              // Convert all keys in objectProperties to lowercase
              Object.keys(objectProperties).forEach(currentKey => {
                lowerCaseProperties[currentKey.toLowerCase()] = objectProperties[currentKey];
              });
        
              if (key in lowerCaseProperties) {
                suggestions = lowerCaseProperties[key].map(prop => ({ text: extractStringBeforeParen(prop), displayText: prop }));
              }
            }
          }
        
          return {
            list: suggestions,
            from: CodeMirror.Pos(cursor.line, cursor.ch),
            to: CodeMirror.Pos(cursor.line, cursor.ch)
          };
        }      

    onSort() {
        // TODO
    }

    onGlobalFilter(table: Table, event: Event) {
        table.filterGlobal((event.target as HTMLInputElement).value, 'contains');
    }

    onGlobalFilter2(table: Table, event: Event) {
      table.filterGlobal((event.target as HTMLInputElement).value, 'contains');
  }

    clear(table: Table) {
        this.deleteEpl() ;
    }

    clearResults(table: Table) {
      this.esperResults = [];
    }

    activateEpl(eplName: string) {
        this.queryResults = [];
        const codeMirrorInstance = this.codeEditor.codeMirror;
        const editorContent = codeMirrorInstance!.getValue();
        const encodedContent = encodeURIComponent(editorContent);
        console.log('attempting to activate: ' + eplName + '   content: ' + encodedContent);
        this.addStream(eplName, encodedContent, "ACTIVE").subscribe(
            (response) => {
                console.log('Success:', response);
                this.fetchEplListing();
            },
            (error) => {
                console.log('Error:' + JSON.stringify(error));
                this.queryError = error.error.Message;;
                this.showInvalidQueryDialog = true;
            }
        );
    }

    startEpl() {
        const selectedRow = this.table.selection;
        const eplName = selectedRow.Name;
        const queryString = selectedRow.Query;
        const encodedContent = encodeURIComponent(queryString);
        this.addStream(eplName, encodedContent, "START").subscribe(
          (response) => {
              console.log('Success:', response);
              this.fetchEplListing();
          },
          (error) => {
              console.log('Error:' + JSON.stringify(error));
              this.queryError = error.error.Message;;
              this.showInvalidQueryDialog = true;
          }
      );
    }

    stopEpl() {
        const selectedRow = this.table.selection;
        const eplName = selectedRow.Name;
        const queryString = selectedRow.Query;
        const encodedContent = encodeURIComponent(queryString);
        this.addStream(eplName, encodedContent, "STOP").subscribe(
          (response) => {
              console.log('Success:', response);
              this.fetchEplListing();
          },
          (error) => {
              console.log('Error:' + JSON.stringify(error));
              this.queryError = error.error.Message;;
              this.showInvalidQueryDialog = true;
          }
      );
    }

    deleteOneEpl() {
      const selectedRow = this.table.selection;
      const eplName = selectedRow.Name;
      const queryString = selectedRow.Query;
      const encodedContent = encodeURIComponent(queryString);
      this.addStream(eplName, encodedContent, "DELETE").subscribe(
        (response) => {
            console.log('Success:', response);
            this.fetchEplListing();
        },
        (error) => {
            console.log('Error:' + JSON.stringify(error));
            this.queryError = error.error.Message;;
            this.showInvalidQueryDialog = true;
        }
    );
  }

    editEpl() {
        const selectedRow = this.table.selection;
        console.log('editing selected row: ' + JSON.stringify(selectedRow));
        const name = selectedRow.name;
        console.log('name: ' + name);
        const queryString = selectedRow.query;
        var decodedString = queryString;
        console.log('decoded string: ' + decodedString);
        try{
            decodedString = decodeURIComponent(queryString);
        }
        catch(error) {
            console.log(error);
        }
        this.codeEditor.codeMirror!.setValue(decodedString);
        this.nameField.nativeElement.value = name;
    }

    deleteEpl() {  
        this.confirmDelete(); // Display the confirmation dialog
    }

    deleteConfirmed() {
        const apiUrl = `/api/Streams/`;
        this.http.delete(apiUrl).toPromise()
          .then(() => {
            this.fetchEplListing();
            this.showConfirmDialog = false;
            this.cd.detectChanges();
          });
      }

    confirmDelete() {
        this.showConfirmDialog = true;
      }

      addStream(shortName: string, queryString: string, stateString: string): Observable<any> {
        const apiUrl = `/api/streams`;
        const body = {
            name: shortName,
            query: queryString,
            state: stateString
        };
        const headers = new HttpHeaders({
          'Content-Type': 'application/json'
        });
          console.log('body of request: ' + JSON.stringify(body) + ' headers: ' + JSON.stringify(headers));
          return this.http.post(apiUrl, body, { headers });
    }
    

    fetchEplListing() {
        this.http.get<ApiResponse>('/api/streams').subscribe(response => {
            this.eplListing = response.response;
            console.log(JSON.stringify(this.eplListing))
        });
    }

    private scrollToBottom() {
        const container = this.resultsContainer.nativeElement;
        container.scrollTop = container.scrollHeight;
      }
      
}

function configureCodeMirror() {
    CodeMirror.defineMode('wintapOverlay', (config, parserConfig) => {
      const esperMode = CodeMirror.getMode(config, 'text/x-esper');
      const myOverlayMode = CodeMirror.getMode(config, 'wintapmessage');
      return CodeMirror.overlayMode(esperMode, myOverlayMode);
    });
  }
  
  function extractStringBeforeParen(input: string): string {
    // Find the index of the open parenthesis
    const indexOfOpenParen = input.indexOf('(');

    // If there's no open parenthesis, return the original string
    if (indexOfOpenParen === -1) {
        return input;
    }

    // Extract and return the substring before the open parenthesis
    return input.substring(0, indexOfOpenParen).trim();
}

export interface ApiResponse {
    response: Statement[];
}

export interface Statement {
    Name: string;
    Query: string;
    StatementType: string | null;
    State: string | null;
    CreateDate: number;
}

export interface EsperResult {
  result: string;
}
