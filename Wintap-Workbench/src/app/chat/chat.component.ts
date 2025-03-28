import { Component, OnInit, ViewChild, ElementRef, AfterViewInit, OnDestroy } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr';
import { MessageService } from 'primeng/api';

declare var Prism: any;

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

@Component({
    selector: 'app-chat',
    templateUrl: './chat.component.html',
    styleUrls: ['./chat.component.scss'],
    providers: [MessageService]
})
export class ChatComponent implements OnInit, AfterViewInit, OnDestroy {
    @ViewChild('messagesContainer') private messagesContainer!: ElementRef;
    @ViewChild('messageInput') private messageInput!: ElementRef;

    userPrompt: string = '';
    messages: ChatMessage[] = [];
    isResponding: boolean = false;
    connection!: HubConnection;

    // Variables to handle message chunking and scrolling
    private manuallyScrolled: boolean = false;
    private lastScrollPosition: number = 0;
    private responseTimeout: any = null;
    private lastUserPrompt: string = '';
    private assistantResponseStarted: boolean = false;
    private lastAssistantMessageIndex: number = -1;

    constructor(
        private http: HttpClient,
        private sanitizer: DomSanitizer,
        private messageService: MessageService
    ) { }

    ngOnInit(): void {
        console.log('Chat component initialized');
        // Initialize SignalR connection
        this.connection = new HubConnectionBuilder()
            .withUrl('/signalr/inferenceHub')
            .withAutomaticReconnect([0, 2000, 10000, 30000])
            .build();

        // Start the connection
        this.connection.start()
            .then(() => {
                console.log('SignalR connection established');
            })
            .catch(error => {
                console.error('SignalR connection error:', error);
                this.showError('Failed to connect to the server');
            });

        // Handle incoming messages
        this.connection.on('ReceiveMessage', (inference: Inference) => {
            console.log('Received inference message:', inference);
            this.handleInferenceResponse(inference);

            // Reset the response timeout to ensure we detect the end of the response
            if (this.responseTimeout) {
                clearTimeout(this.responseTimeout);
            }

            this.responseTimeout = setTimeout(() => {
                console.log('Response timeout triggered - marking response as complete');
                this.isResponding = false;
            }, 1500);
        });
    }

    ngAfterViewInit(): void {
        // Set up the scroll event listener
        if (this.messagesContainer) {
            const element = this.messagesContainer.nativeElement;
            element.addEventListener('scroll', () => {
                // Check if user has manually scrolled up
                const maxScroll = element.scrollHeight - element.clientHeight;
                const currentScroll = element.scrollTop;

                // If we're not at the bottom and scrolling up
                if (maxScroll - currentScroll > 50 && currentScroll < this.lastScrollPosition) {
                    this.manuallyScrolled = true;
                }

                // If we've scrolled to the bottom, enable auto-scrolling again
                if (maxScroll - currentScroll < 20) {
                    this.manuallyScrolled = false;
                }

                this.lastScrollPosition = currentScroll;
            });
        }
    }

    ngOnDestroy(): void {
        // Close the connection when component is destroyed
        if (this.connection) {
            this.connection.stop().catch(err => console.error('Error stopping connection:', err));
        }

        // Clear any pending timeouts
        if (this.responseTimeout) {
            clearTimeout(this.responseTimeout);
        }
    }

    // Handle response from SignalR
    handleInferenceResponse(inference: Inference): void {
        // Ignore empty responses
        if (!inference.response || inference.response.trim() === '') {
            return;
        }

        console.log('Handling inference response:', inference.response);

        // If this is the first message after a user prompt
        if (!this.assistantResponseStarted) {
            this.assistantResponseStarted = true;
            this.isResponding = true;

            // Create a new assistant message
            this.messages.push({
                text: inference.response,
                isUser: false,
                timestamp: new Date()
            });

            this.lastAssistantMessageIndex = this.messages.length - 1;

            console.log('New assistant message created at index:', this.lastAssistantMessageIndex);

            // Scroll to the new message if not manually scrolled
            if (!this.manuallyScrolled) {
                setTimeout(() => this.scrollToBottom(), 100);
            }
        } else {
            // This is a continuation of the previous response
            if (this.lastAssistantMessageIndex >= 0 && this.lastAssistantMessageIndex < this.messages.length) {
                // Append to the existing message
                this.messages[this.lastAssistantMessageIndex].text += inference.response;
                console.log('Appended to existing message at index:', this.lastAssistantMessageIndex);

                // Scroll if not manually scrolled
                if (!this.manuallyScrolled) {
                    setTimeout(() => this.scrollToBottom(), 100);
                }
            }
        }

        // Apply syntax highlighting
        setTimeout(() => this.highlightCodeBlocks(), 50);

        // Check for end of response markers
        const endOfResponseMarkers = ['\n\n', '</code></pre>', '```\n\n'];
        for (const marker of endOfResponseMarkers) {
            if (inference.response.includes(marker)) {
                console.log('End of response marker detected:', marker);
                setTimeout(() => {
                    this.isResponding = false;
                }, 200);
                break;
            }
        }
    }

    // Format message content with simple HTML formatting
    formatMessageContent(text: string): SafeHtml {
        if (!text) return this.sanitizer.bypassSecurityTrustHtml('');

        // Convert markdown-style code blocks to HTML
        let html = text.replace(/```(\w*)([\s\S]*?)```/g, (match, language, code) => {
            const langClass = language ? ` class="language-${language}"` : '';
            return `<pre><code${langClass}>${this.escapeHtml(code.trim())}</code></pre>`;
        });

        // Convert inline code
        html = html.replace(/`([^`]+)`/g, (match, code) => {
            return `<code>${this.escapeHtml(code)}</code>`;
        });

        // Convert bold text
        html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

        // Convert italic text
        html = html.replace(/\*([^*]+)\*/g, '<em>$1</em>');

        // Convert line breaks
        html = html.replace(/\n/g, '<br>');

        return this.sanitizer.bypassSecurityTrustHtml(html);
    }

    // Helper function to escape HTML special characters
    escapeHtml(text: string): string {
        const htmlEntities: { [key: string]: string } = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        };

        return text.replace(/[&<>"']/g, (match) => htmlEntities[match]);
    }

    // Highlight code blocks after DOM updates
    highlightCodeBlocks(): void {
        if (typeof Prism !== 'undefined') {
            const codeBlocks = document.querySelectorAll('pre code:not(.prism-highlighted)');
            codeBlocks.forEach((block: any) => {
                Prism.highlightElement(block);
                block.classList.add('prism-highlighted');
            });
        }
    }

    // Send a message to the server
    sendMessage(): void {
        if (!this.userPrompt.trim() || this.isResponding) return;

        // Add user message to the chat
        this.addMessage(this.userPrompt, true);

        // Get the message and reset the input
        const prompt = this.userPrompt;
        this.lastUserPrompt = prompt;
        this.userPrompt = '';

        // Reset state for new message sequence
        this.assistantResponseStarted = false;
        this.lastAssistantMessageIndex = -1;
        this.manuallyScrolled = false;

        // Adjust the textarea height
        if (this.messageInput) {
            this.adjustTextareaHeight({ target: this.messageInput.nativeElement });
        }

        // Send the request to the server
        const headers = new HttpHeaders().set('Content-Type', 'application/json');
        this.http.put('/api/LLM/Inference', { prompt }, { headers })
            .subscribe({
                next: () => {
                    console.log('Inference request sent successfully');
                },
                error: (error) => {
                    this.isResponding = false;
                    this.showError('Failed to send message: ' + (error.message || 'Unknown error'));
                    console.error('Inference error:', error);
                }
            });
    }

    // Add a new message to the chat
    addMessage(text: string, isUser: boolean): void {
        this.messages.push({
            text,
            isUser,
            timestamp: new Date()
        });

        // Automatically scroll to bottom for new messages
        if (!this.manuallyScrolled) {
            setTimeout(() => this.scrollToBottom(), 100);
        }

        // Highlight code blocks in the new message
        setTimeout(() => this.highlightCodeBlocks(), 150);
    }

    // Clear the chat history
    clearChat(): void {
        this.http.post('/api/LLM/Clear', {})
            .subscribe({
                next: () => {
                    this.messages = [];
                    this.isResponding = false;
                    this.assistantResponseStarted = false;
                    this.lastAssistantMessageIndex = -1;
                    this.manuallyScrolled = false;
                    this.lastUserPrompt = '';
                    console.log('Chat cleared successfully');
                },
                error: (error) => {
                    this.showError('Failed to clear chat: ' + (error.message || 'Unknown error'));
                    console.error('Clear chat error:', error);
                }
            });
    }

    // Handle file uploads
    onFileSelected(event: any): void {
        const file = event.target.files[0];
        if (!file) return;

        const formData = new FormData();
        formData.append('file', file);

        // Show uploading message
        this.addMessage(`Uploading file: ${file.name}...`, true);

        this.http.post('/api/LLM/Upload', formData)
            .subscribe({
                next: () => {
                    this.addMessage(`File "${file.name}" uploaded successfully. You can now ask questions about its content.`, false);
                },
                error: (error) => {
                    this.showError('File upload failed: ' + (error.message || 'Unknown error'));
                    console.error('File upload error:', error);
                }
            });

        // Reset the file input
        event.target.value = '';
    }

    // Auto-resize the textarea based on content
    adjustTextareaHeight(event: any): void {
        const textarea = event.target;
        textarea.style.height = 'auto';
        textarea.style.height = textarea.scrollHeight + 'px';
    }

    // Scroll to the bottom of the chat
    scrollToBottom(): void {
        if (this.messagesContainer) {
            const element = this.messagesContainer.nativeElement;
            element.scrollTop = element.scrollHeight;
        }
    }

    // Show error notification
    showError(message: string): void {
        this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: message,
            life: 5000
        });
    }
}
