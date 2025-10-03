import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DashboardComponent } from './dashboard.component';
import { ChartModule } from 'primeng/chart';
import { MenuModule } from 'primeng/menu';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { StyleClassModule } from 'primeng/styleclass';
import { PanelMenuModule } from 'primeng/panelmenu';
import { InputSwitchModule } from 'primeng/inputswitch';
import { DashboardsRoutingModule } from './dashboard-routing.module';
import { TooltipModule } from 'primeng/tooltip';
import {ProgressSpinnerModule} from 'primeng/progressspinner';

@NgModule({
    imports: [
        CommonModule,
        FormsModule,
        ChartModule,
        MenuModule,
        TableModule,
        StyleClassModule,
        PanelMenuModule,
        ButtonModule,
        DashboardsRoutingModule,
        InputSwitchModule,
        TooltipModule,
        ProgressSpinnerModule
    ],
    declarations: [DashboardComponent]
})
export class DashboardModule { }
