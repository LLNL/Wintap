import { Component, OnInit, OnDestroy, ViewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { MenuItem } from 'primeng/api';
import { Subscription } from 'rxjs';
import { LayoutService } from 'src/app/layout/service/app.layout.service';
import { UIChart } from "primeng/chart";
import { trigger, state, style, animate, transition } from '@angular/animations';
import { ChangeDetectorRef } from '@angular/core';

@Component({
    templateUrl: './dashboard.component.html',
    styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {
    loading = true;
    public kpi?: KPI;
    items!: MenuItem[];
    chartData: any;
    chartOptions: any;
    subscription!: Subscription;
    updateInterval: NodeJS.Timer;
    counter: number;
    applyButtonDisabled: boolean = true; 
    wintapSettings: { [key: string]: boolean } = {
        Tcp: false,
        Udp: false,
        File: false,
        ImageLoad: false,
        Registry: false,
        MemoryMap: false,
        ApiCall: false,
        DeveloperMode: false
      };
    @ViewChild('chart') lineChart: UIChart | undefined;

    constructor(public layoutService: LayoutService, private http: HttpClient, private cdr: ChangeDetectorRef) {
        this.subscription = this.layoutService.configUpdate$.subscribe(() => {
            this.initChart();
        });
        http.get<KPI>('/api/EsperService/').subscribe(result => {
            this.kpi = result;
        }, error => console.error(error));
        this.updateInterval = setInterval(() => this.addData(), 1000);
        this.counter = 1;
    }

    ngOnInit() {
        this.initChart();
        this.getWintapSettings();
    }

    initChart() {
        const documentStyle = getComputedStyle(document.documentElement);
        const textColor = documentStyle.getPropertyValue('--text-color');
        const textColorSecondary = documentStyle.getPropertyValue('--text-color-secondary');
        const surfaceBorder = documentStyle.getPropertyValue('--surface-border');

        this.chartData = {
            labels: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            datasets: [
                {
                    label: 'Events Per Second',
                    data: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
                    fill: false,
                    backgroundColor: documentStyle.getPropertyValue('--bluegray-700'),
                    borderColor: documentStyle.getPropertyValue('--bluegray-700'),
                    tension: .4
                }
            ]
        };

        this.chartOptions = {
            scales: {
                x: {
                    ticks: {
                        display: false
                    }
                }
            }
        };
    }

    ngOnDestroy() {
        if (this.subscription) {
            this.subscription.unsubscribe();
        }
    }

    addData() {

        this.http.get<KPI>('/api/EsperService/').subscribe(result => {
            this.kpi = result;
        }, error => console.error(error));
        if (this.chartData.datasets[0].data.length > 59) {
            this.chartData.datasets[0].data.shift();
        }
        if (this.chartData.labels.length > 59) {
            this.chartData.labels.shift();
        }

        this.chartData.datasets[0].data.push(this.kpi?.eventsPerSecond);
        this.chartData.labels.push(this.counter++);
        this.lineChartComponent.refresh();
        this.counter++;
    }

    get lineChartComponent(): UIChart {
        return this.lineChart!;
    }

    getWintapSettings() {
        this.http.get('/api/WintapService').subscribe(
          (response: any) => {
            this.wintapSettings = response.response;
          },
          (error) => {
            console.error('Error fetching collector settings:', error);
          }
        );
      }

    setWintapSettings(): void {
        this.http.post('/api/WintapService', this.wintapSettings).subscribe(response => {
         });
    
        this.applyButtonDisabled = true; 
      }
    
      onToggleChange(): void {
        this.applyButtonDisabled = false;
      }
}

interface KPI {
    runtime: string,
    eventsPerSecond: number,
    maxEventsPerSecond: number,
    maxEventTime: Date,
    totalEvents: number,
    wintapOK: boolean,
    collectorOK: boolean
}

