import {
  Component, ViewChild, AfterViewInit, OnInit, ElementRef, QueryList
} from '@angular/core';
import { Table } from 'primeng/table';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ChangeDetectorRef } from '@angular/core';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { HttpHeaders } from '@angular/common/http';

declare var $: any;

interface expandedRows {
    [key: string]: boolean;
}

@Component({
  selector: 'app-etw-explorer',
  templateUrl: './etw-explorer.component.html',
  styleUrls: ['./etw-explorer.component.scss']
})
export class EtwExplorerComponent implements AfterViewInit, OnInit {
  @ViewChild('dt2') eventDataTable!: Table;
  @ViewChild('resultsContainer') resultsContainer!: ElementRef;

  ngAfterViewInit(): void {
    this.fetchProviderListing();
}

  private connection: any;
  providerListing: EtwProvider[] = [];
  etwSamples: ETWSample[] = [];
  selectedSample: any = null;
  etwSample!: ETWSample;
  selectedProvider: any = null;
  loading: boolean = true;
  queryResults: string[] = [];
  filteredRowCount: number = 0;
  eventNameOptions!: any[];
  uniqueEventNames: Set<string> = new Set();
  //cd: ChangeDetectorRef;

  constructor(private http: HttpClient, private cd: ChangeDetectorRef) {
    this.connection = $.hubConnection('/signalr');
    const hubProxy = this.connection.createHubProxy('explorerHub');
}

    ngOnInit() {

        this.connection = new HubConnectionBuilder()
            .withUrl('/signalr/explorerHub')
            .withAutomaticReconnect([0, 2000, 10000, 30000])
            .build();

        this.connection.start().catch(console.error('error'));

        console.log('connection state: ' + this.connection.state); // Check the state here

        const fullyQualifiedUrl = `${window.location.protocol}//${window.location.hostname}`;

        console.log(`URL:  ${fullyQualifiedUrl}`);

        this.connection.on('ReceiveMessage', (data: string) => {
            // Decode known HTML entities with string replacement
            const decodedData = data.replace(/&quot;/g, '"')
                .replace(/&lt;/g, '<')
                .replace(/&gt;/g, '>')
                .replace(/&apos;/g, "'")

            // Then parse the JSON
            let parsedData = JSON.parse(decodedData);
            this.etwSamples.push(parsedData);
            this.uniqueEventNames.add(parsedData.EventName);
            this.updateEventNameOptions();
            this.eventDataTable.totalRecords++;
            this.cd.detectChanges();
        });


    this.loading = false;
    this.eventNameOptions = [
      { label: 'Event 1', value: 'Event 1' },
      { label: 'Event 2', value: 'Event 2' },
  ];
}

fetchProviderListing() {
  this.http.get<ApiResponse>('/api/EtwExplorer').subscribe(response => {
      this.providerListing = response.response;
      console.log('providers: ' + JSON.stringify(this.providerListing))
  });
}

startProvider() {
  const selectedRow = this.eventDataTable.selection;
    const providerName = this.selectedProvider.providerName;
    console.log('starting provider: ' + providerName);
        this.enableProvider(providerName).subscribe(
          (response) => {
              console.log('Success:', response);
              // Clear the eventNameOptions array
                this.eventNameOptions = [];
          },
          (error) => {
              console.log('Error:' + JSON.stringify(error));
          }
      );
}

stopProvider() {
  this.disableProvider().subscribe(
    (response) => {
        console.log('Success:', response);
    },
    (error) => {
        console.log('Error:' + JSON.stringify(error));
    }
);
}

enableProvider(providerName: string): Observable<any> {
  const apiUrl = `/api/EtwExplorer?providerName=${providerName}`;
  return this.http.put(apiUrl, null);
}

disableProvider(): Observable<any> {
  const stopApiUrl = '/api/EtwExplorer';
  return this.http.post(stopApiUrl, null);
}

onGlobalFilter(table: Table, event: Event) {
  table.filterGlobal((event.target as HTMLInputElement).value, 'contains');
}

onGlobalFilter2(table: Table, event: Event) {
  table.filterGlobal((event.target as HTMLInputElement).value, 'contains');
}

clear(table: Table) {
  this.etwSamples = [];
  this.eventNameOptions = [];
  this.eventDataTable.totalRecords = 0;
}

// Function to update eventNameOptions
updateEventNameOptions() {
  this.eventNameOptions = Array.from(this.uniqueEventNames).map(eventName => {
      return { label: eventName, value: eventName };
  });
}

}

export interface ApiResponse {
  response: EtwProvider[];
}

export interface  EtwProvider {
  providerName: string;
}

export interface ETWSample {
  ComputerName: string;
  ProviderName: string;
  ProviderGuid: string;
  EventName: string;
  Message: string;
  ActivityId: string;
  TimeStamp: string;
  PID: number;
  ProcessName: string;
  SampleId: string;
}
