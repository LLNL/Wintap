import { NgModule } from '@angular/core';
import { HashLocationStrategy, LocationStrategy } from '@angular/common';
import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { AppComponent } from './app.component';
import { AppRoutingModule } from './app-routing.module';
import { AppLayoutModule } from './layout/app.layout.module';
import { NotfoundComponent } from './notfound/notfound.component';
import { HttpClientModule } from '@angular/common/http';
import { ChartModule } from 'primeng/chart';
import { TagModule } from 'primeng/tag';
import { QuerybuilderComponent } from './querybuilder/querybuilder.component';
import { FormsModule } from '@angular/forms';
import { CodemirrorModule } from '@ctrl/ngx-codemirror';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { RippleModule } from 'primeng/ripple';
import { InputTextareaModule } from "primeng/inputtextarea";
import { InputTextModule } from "primeng/inputtext";
import { DatePipe } from '@angular/common';
import { ChipModule } from "primeng/chip";
import { DialogModule } from 'primeng/dialog';
import { CommonModule } from '@angular/common';
import { TreeviewComponent } from './treeview/treeview.component';
import { TreeTableModule } from 'primeng/treetable';
import { BadgeModule } from 'primeng/badge';
import { CardModule } from 'primeng/card';
import { DividerModule } from 'primeng/divider';
import { DropdownModule } from 'primeng/dropdown';
import { ScrollPanelModule } from 'primeng/scrollpanel';
import { InputSwitchModule } from 'primeng/inputswitch';
import { EtwExplorerComponent } from './etw-explorer/etw-explorer.component';
import { ChatComponent } from './chat/chat.component';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { SliderModule } from 'primeng/slider'; // Add this for temperature slider
// Add these PrimeNG imports
import { AccordionModule } from 'primeng/accordion';
import { TooltipModule } from 'primeng/tooltip';
import { CheckboxModule } from 'primeng/checkbox';
import { MenuModule } from 'primeng/menu';
import { SplitterModule } from 'primeng/splitter';


@NgModule({
    declarations: [
        AppComponent,
        NotfoundComponent,
        QuerybuilderComponent,
        TreeviewComponent,
        EtwExplorerComponent,
        ChatComponent
    ],
    imports: [
        BrowserModule,
        BrowserAnimationsModule,
        AppRoutingModule,
        AppLayoutModule,
        HttpClientModule,
        ChartModule,
        TagModule,
        FormsModule,
        CodemirrorModule,
        TableModule,
        ButtonModule,
        RippleModule,
        InputTextareaModule,
        InputTextModule,
        ChipModule,
        DialogModule,
        TreeTableModule,
        CommonModule,
        BadgeModule,
        CardModule,
        DividerModule,
        DropdownModule,
        InputSwitchModule,
        ScrollPanelModule,
        ProgressSpinnerModule,
        ToastModule,
        SliderModule,
        AccordionModule,
        ToastModule,
        ChipModule,
        TagModule,
        TooltipModule,
        DropdownModule,
        CheckboxModule,
        MenuModule,
        SplitterModule
    ],
    providers: [
        DatePipe,
        MessageService,
        HubConnectionBuilder,
        { provide: LocationStrategy, useClass: HashLocationStrategy },
    ],
    bootstrap: [AppComponent]
})
export class AppModule { }
