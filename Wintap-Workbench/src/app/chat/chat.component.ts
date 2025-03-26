import {
    Component, AfterViewChecked, ViewChild, AfterViewInit, OnInit, ElementRef, QueryList
} from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { ChangeDetectorRef } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
// @ts-ignore
import * as Prism from 'prismjs';

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
export class ChatComponent implements OnInit, AfterViewChecked {
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
    results: string[] = [];

    constructor(
        private http: HttpClient,
        private cd: ChangeDetectorRef,
        private elementRef: ElementRef,
        private sanitizer: DomSanitizer
    ) {
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

    ngAfterViewChecked() {
        // Apply syntax highlighting to all code blocks
        this.highlightCodeBlocks();
    }

    highlightCodeBlocks(): void {
        // Use setTimeout to ensure DOM is fully updated
        setTimeout(() => {
            // Apply Prism highlighting to all code elements
            if (typeof Prism !== 'undefined') {
                Prism.highlightAll();

                // Add additional PowerShell specific styling
                this.applyCustomPowershellStyling();
            }
        }, 10);
    }

    applyCustomPowershellStyling(): void {
        // Get all PowerShell code blocks
        const powershellBlocks = document.querySelectorAll('code.language-powershell');

        powershellBlocks.forEach(block => {
            // Add special styling for PowerShell commands
            const content = block.innerHTML;
            let enhancedContent = content;

            // PowerShell commands
            const commands = [
                'Write-Host', 'Get-Computer', 'Select-Object', 'Get-ItemProperty',
                'Where-Object', 'Get-', 'Set-', 'New-', 'Remove-', 'Import-', 'Export-',
                'Start-', 'Stop-', 'Restart-', 'Test-', 'Invoke-', 'Format-', 'Out-',
                'ConvertTo-', 'ConvertFrom-'
            ];

            commands.forEach(cmd => {
                const regex = new RegExp(`(${cmd.replace('-', '\\-')})`, 'g');
                enhancedContent = enhancedContent.replace(
                    regex,
                    '<span class="ps-command">$1</span>'
                );
            });

            // PowerShell variables (starting with $)
            enhancedContent = enhancedContent.replace(
                /(\$\w+)/g,
                '<span class="ps-variable">$1</span>'
            );

            // PowerShell strings (in quotes)
            enhancedContent = enhancedContent.replace(
                /(&quot;|")([^"&]*?)(&quot;|")/g,
                '<span class="token string">$1$2$3</span>'
            );

            // Windows paths (drive letter followed by colon and backslash)
            enhancedContent = enhancedContent.replace(
                /([A-Z]:(?:\\[^\\<>:"\/|?*]+)+)/g,
                '<span class="token path">$1</span>'
            );

            // Registry paths
            enhancedContent = enhancedContent.replace(
                /(HKLM:\\[^\\<>:"\/|?*\s]+(?:\\[^\\<>:"\/|?*\s]+)*)/g,
                '<span class="token path">$1</span>'
            );

            // Update the content with enhanced styling
            block.innerHTML = enhancedContent;
        });
    }

    formatMessageText(text: string): SafeHtml {
        if (!text) return this.sanitizer.bypassSecurityTrustHtml('');

        // Check if this is a PowerShell output message
        const isPowerShellOutput = text.includes('```powershell') ||
            text.includes('```ps') ||
            (text.includes('```') && text.includes('Write-Host'));

        // Format code blocks with language specification (```language code```)
        let formattedText = text.replace(/```(\w*)\s*([\s\S]*?)```/g, (match, language, codeContent) => {
            // Default to powershell if the language is not specified but looks like PowerShell
            let lang = language ? language.toLowerCase() : '';
            if (!lang && (codeContent.includes('Write-Host') ||
                codeContent.includes('Get-') ||
                codeContent.includes('Select-Object'))) {
                lang = 'powershell';
            }

            const langClass = lang ? ` class="language-${lang}"` : '';

            // Special formatting for PowerShell code blocks
            if (lang === 'powershell') {
                return `<div class="powershell-terminal">
                            <div class="terminal-header">Windows PowerShell</div>
                            <pre class="code-block"><code${langClass}>${this.escapeHtml(codeContent.trim())}</code></pre>
                        </div>`;
            }

            return `<pre class="code-block"><code${langClass}>${this.escapeHtml(codeContent.trim())}</code></pre>`;
        });

        // Format inline code (`code`)
        formattedText = formattedText.replace(/`([^`]+)`/g, (match, codeContent) => {
            return `<code>${this.escapeHtml(codeContent)}</code>`;
        });

        // Format bold text (**text**)
        formattedText = formattedText.replace(/\*\*([^*]+)\*\*/g, (match, content) => {
            return `<strong>${content}</strong>`;
        });

        // Format italic text (*text*)
        formattedText = formattedText.replace(/\*([^*]+)\*/g, (match, content) => {
            return `<em>${content}</em>`;
        });

        // Handle special formatting for PowerShell prompt 
        if (isPowerShellOutput) {
            // Highlight PowerShell commands
            const powershellCommands = ['Write-Host', 'Get-', 'Set-', 'Select-Object', 'Where-Object'];
            powershellCommands.forEach(cmd => {
                const regex = new RegExp(`(${cmd.replace('-', '\\-')})`, 'g');
                formattedText = formattedText.replace(regex, `<span class="ps-command">$1</span>`);
            });

            // Highlight variables
            formattedText = formattedText.replace(/(\$\w+)/g, '<span class="ps-variable">$1</span>');
        }

        // Convert line breaks to <br> tags
        formattedText = formattedText.replace(/\n/g, '<br>');

        return this.sanitizer.bypassSecurityTrustHtml(formattedText);
    }

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

        // Allow the DOM to update and then apply highlighting
        setTimeout(() => {
            this.highlightCodeBlocks();
            this.cd.detectChanges();
        }, 50);
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

    onFileSelected(event: any) {
        const file = event.target.files[0];
        if (file) {
            const formData = new FormData();
            formData.append('file', file);

            this.http.post('/api/LLM/Upload', formData).subscribe(
                (response) => {
                    console.log('File uploaded successfully');
                    this.messages.push({
                        text: `Uploaded file: ${file.name}`,
                        isUser: true,
                        timestamp: new Date()
                    });
                },
                (error) => {
                    console.error('File upload failed', error);
                    this.errorMessage = 'File upload failed';
                }
            );
        }
    }
}
