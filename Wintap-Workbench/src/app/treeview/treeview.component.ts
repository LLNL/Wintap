import { TreeNode } from 'primeng/api';
import { BadgeModule } from 'primeng/badge';
import {
  Component, ViewChild, AfterViewInit, OnInit, ElementRef, QueryList
} from '@angular/core';
import { Table } from 'primeng/table';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Component({
  selector: 'app-treeview',
  templateUrl: './treeview.component.html',
    styleUrls: ['./treeview.component.css']
})
export class TreeviewComponent implements OnInit {
  nodes: TreeNode[] = [];
  cols: any[] = [];
  public nodeCount: number = 0;
  public nodeCountStr: string = '';

  constructor(private http: HttpClient) {

  }

  ngOnInit() {
    this.fetchProcessTree();
    
  this.cols = [
    { field: 'processname', header: 'Name' },
    // { field: 'processpath', header: 'Size' },
    { field: 'pid', header: 'PID' },
    { field: 'pidhash', header: 'PidHash' },
  ];

    this.countTreeNodes();

  }


  fetchProcessTree() {
    this.http.get<any>('/api/Tree').subscribe(response => {
        console.log(JSON.stringify(response.response));
        this.nodes = JSON.parse(response.response);
        console.log("nodes: " + this.nodes);
    });
  }

  expandAll(nodes: TreeNode[]) {
  nodes.forEach(node => {
    node.expanded = true;
    this.nodeCount++;
    if (node.children) {
      this.expandAll(node.children);
    }
  });
  }

  countTreeNodes() {
    console.log('node count: ' + this.nodeCount);
    //let nodeCount = this.countNodes(this.nodes);
    //this.nodeCountStr = nodeCount.toString();
  }

  countNodes(nodes: TreeNode[]): number {
    let count = 0;
    for(let node of nodes){
      count++;
      if(node.children){
        count += this.countNodes(node.children);
      }
    }
    return count;
  }
  

}
