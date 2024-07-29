import { RouterModule } from '@angular/router';
import { NgModule } from '@angular/core';
import { NotfoundComponent } from './notfound/notfound.component';
import { AppLayoutComponent } from "./layout/app.layout.component";
import { QuerybuilderComponent } from './querybuilder/querybuilder.component';
import { TreeviewComponent } from './treeview/treeview.component';
import { EtwExplorerComponent } from './etw-explorer/etw-explorer.component';
import { ChatComponent } from './chat/chat.component';

@NgModule({
    imports: [
        RouterModule.forRoot([
            {
                path: '', component: AppLayoutComponent,
                children: [
                    { path: '', loadChildren: () => import('./dashboard/dashboard.module').then(m => m.DashboardModule) },
                    { path: 'querybuilder', component: QuerybuilderComponent },
                    { path: 'treeview', component: TreeviewComponent },
                    { path: 'etwexplorer', component: EtwExplorerComponent },
                    { path: 'chat', component: ChatComponent },
                    { path: 'documentation', loadChildren: () => import('./documentation/documentation.module').then(m => m.DocumentationModule) },
                ]
            },
            { path: 'notfound', component: NotfoundComponent },
            { path: '**', redirectTo: '/notfound' },
        ], { scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled', onSameUrlNavigation: 'reload' })
    ],
    exports: [RouterModule]
})
export class AppRoutingModule {
}
