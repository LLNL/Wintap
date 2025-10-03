import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Component({
    selector: 'app-duck-db',
    templateUrl: './duck-db.component.html',
    styleUrls: ['./duck-db.component.scss']
})
export class DuckDbComponent implements OnInit {
    duckDbUrl = 'http://localhost:4213';
    isAvailable = false;
    isChecking = false;

    constructor(private http: HttpClient) { }

    ngOnInit(): void {
        // Check if DuckDB is available
        this.checkDuckDbAvailability();
    }

    /**
     * Check if DuckDB UI is available
     */
    checkDuckDbAvailability(): void {
        this.isChecking = true;

        // Simple GET request to check if the service is running
        this.http.get(this.duckDbUrl, { observe: 'response' })
            .subscribe({
                next: () => {
                    this.isAvailable = true;
                    this.isChecking = false;
                },
                error: () => {
                    this.isAvailable = true;  // todo: fix this check
                    this.isChecking = false;
                }
            });
    }

    /**
     * Open DuckDB in a new tab
     */
    openDuckDb(): void {
        if (this.isAvailable) {
            window.open(this.duckDbUrl, '_blank');
        }
    }
}
