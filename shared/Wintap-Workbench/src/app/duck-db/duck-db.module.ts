import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { DuckDbComponent } from './duck-db.component';
import { DuckDbRoutingModule } from './duck-db-routing.module';

// PrimeNG imports for directive-based approach
import { ButtonModule } from 'primeng/button';
import { RippleModule } from 'primeng/ripple';
import { TooltipModule } from 'primeng/tooltip';

@NgModule({
    declarations: [
        DuckDbComponent
    ],
    imports: [
        // Angular modules
        CommonModule,
        FormsModule,
        HttpClientModule,
        DuckDbRoutingModule,

        // PrimeNG modules
        ButtonModule,
        RippleModule,
        TooltipModule
    ]
})
export class DuckDbModule { }
