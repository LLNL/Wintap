import { OnInit } from '@angular/core';
import { Component } from '@angular/core';
import { LayoutService } from './service/app.layout.service';
import { QuerybuilderComponent } from '../querybuilder/querybuilder.component';
import { TreeviewComponent } from '../treeview/treeview.component';

@Component({
    selector: 'app-menu',
    templateUrl: './app.menu.component.html'
})
export class AppMenuComponent implements OnInit {

    model: any[] = [];

    constructor(public layoutService: LayoutService) { }

    ngOnInit() {
        this.model = [
            {
                label: 'Home',
                items: [
                    { label: 'Dashboard', icon: 'pi pi-fw pi-home', routerLink: ['/'] }
                ]
            },
            {
                label: 'Tools',
                items: [
                    { label: 'Esper Workbench', icon: 'pi pi-fw pi-bolt', routerLink: ['/querybuilder'] },
                    { label: 'Process Tree Viewer', icon: 'pi pi-fw pi-sitemap', routerLink: ['/treeview'] },
                    { label: 'ETW Explorer', icon: 'pi pi-fw pi-map-marker', routerLink: ['/etwexplorer'] },
                    { label: 'Chat', icon: 'pi pi-fw pi-comments', routerLink: ['/chat'] },
                ]
            },
            {
                label: 'Reference',
                items: [
                    {
                        label: 'Documentation', icon: 'pi pi-fw pi-question', routerLink: ['/documentation']
                    },
                    {
                        label: 'Code', icon: 'pi pi-fw pi-github', url: ['https://github.com/LLNL/Wintap-Workbench'], target: '_blank'
                    }
                ]
            }
        ];
    }
}
