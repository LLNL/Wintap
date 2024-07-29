// chat.component.ts



import {
  Component, ViewChild, AfterViewInit, OnInit, ElementRef, QueryList
} from '@angular/core';
import { Table } from 'primeng/table';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ChangeDetectorRef } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { HttpHeaders } from '@angular/common/http';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import * as internal from 'stream';

declare var $: any;

interface ChatMessage {
  text: string;
  timestamp: number;
}

interface Inference {
  prompt: string;
  response: string;
  tokensUsed: number;
}

interface ChatHistory {
  [key: string]: ChatMessage[];
}

@Component({
  selector: 'app-chat',
  templateUrl: './chat.component.html',
  styleUrls: ['./chat.component.scss']
})
export class ChatComponent implements OnInit {
  userPrompt: string;
  results: string[] = [];
  llmOutput: string = '';
  errorMessage: string = '';
  selectedChatHistory: string = '';
  chatHistories: string[] = [];
  chatHistory: ChatHistory = {};
  @ViewChild('prompt') prompt!: ElementRef<HTMLTextAreaElement>;
  isSpinnerHidden: boolean = true;
  isPromptDisabled: boolean = false;
  tokensUsed: string = "0";
  connection: any;
  
  constructor(private http: HttpClient, private cd: ChangeDetectorRef, private elementRef: ElementRef) { 
    this.userPrompt = '';
  }


  ngOnInit(): void {
    // Initialize chat histories
    this.chatHistories = ['History 1', 'History 2', 'History 3'];
    this.chatHistory = {};
    
    this.connection = new HubConnectionBuilder()
    .withUrl('/signalr/inferenceHub')
    .withAutomaticReconnect([0, 2000, 10000, 30000])
    .build();

    this.connection
      .start()
      .catch(console.error('error'));

      console.log(this.connection.state); // Check the state here

      const fullyQualifiedUrl = `${window.location.protocol}//${window.location.hostname}`;

      console.log(`URL:  ${fullyQualifiedUrl}`);

    this.connection.on('ReceiveMessage', (message: Inference) => {
      this.isSpinnerHidden = true;
      this.isPromptDisabled = false;
      console.log(message);
      this.results.push(message.response);
      //this.tokensUsed = message.tokensUsed.toString();
    });
  }

  sendPrompt(): void {
    this.isSpinnerHidden = false;
    this.isPromptDisabled = true;
    const prompt = this.prompt.nativeElement.value;
    this.userPrompt = prompt;
    this.prompt.nativeElement.value = '';
    console.log('attempting to send prompt: ' + prompt);
    const headers = new HttpHeaders().set('Content-Type', 'application/json');
    this.http.put('/api/LLM/Inference', JSON.stringify({ prompt: prompt }), { headers: headers })
      .subscribe((response: any) => {
        console.log('inference request sent successfully');

      }, (error: any) => {
        this.errorMessage = error.message;
        console.log('inference error: ' + error.message);
      });
  }



performSetup(): void {
  // create new chat session and context, clear results
  this.http.post('/api/LLM/Clear', {})
    .subscribe((response: any) => {
        this.prompt.nativeElement.value = '';
        this.results = [];
    }, (error: any) => {
      console.log(JSON.stringify(this.errorMessage));
    });
}


}