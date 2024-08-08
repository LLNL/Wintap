import {
    Component, ViewChild, AfterViewInit, OnInit, ElementRef, QueryList
} from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { ChangeDetectorRef } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { HubConnectionBuilder } from '@microsoft/signalr';

interface ChatMessage {
    text: string;
    isUser: boolean;
    timestamp: Date;
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
    messages: ChatMessage[] = [];
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
            this.appendOrAddMessage(message.response);
            //this.tokensUsed = message.tokensUsed.toString();
        });
    }

    sendPrompt(): void {
        this.isSpinnerHidden = false;
        this.isPromptDisabled = true;
        const prompt = this.prompt.nativeElement.value;
        this.addMessage(prompt, true);
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

    addMessage(text: string, isUser: boolean): void {
        const message: ChatMessage = {
            text: text,
            isUser: isUser,
            timestamp: new Date()
        };
        this.messages.push(message);
        this.scrollToBottom();
    }

    appendOrAddMessage(text: string): void {
        if (this.messages.length > 0 && !this.messages[this.messages.length - 1].isUser) {
            // Append to the last AI message
            this.messages[this.messages.length - 1].text += text;
        } else {
            // Add a new AI message
            this.addMessage(text, false);
        }
        this.scrollToBottom();
    }

    adjustTextareaHeight(event: Event): void {
        const textarea = event.target as HTMLTextAreaElement;
        textarea.style.height = 'auto';
        textarea.style.height = `${textarea.scrollHeight}px`;
    }

    scrollToBottom(): void {
        setTimeout(() => {
            const resultsWindow = this.elementRef.nativeElement.querySelector('.results-window');
            if (resultsWindow) {
                resultsWindow.scrollTop = resultsWindow.scrollHeight;
            }
        }, 100);
    }

    performSetup(): void {
        // create new chat session and context, clear results
        this.http.post('/api/LLM/Clear', {})
            .subscribe((response: any) => {
                this.prompt.nativeElement.value = '';
                this.messages = [];
            }, (error: any) => {
                console.log(JSON.stringify(this.errorMessage));
            });
    }
}
