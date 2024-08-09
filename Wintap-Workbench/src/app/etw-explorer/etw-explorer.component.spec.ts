import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EtwExplorerComponent } from './etw-explorer.component';

describe('EtwExplorerComponent', () => {
  let component: EtwExplorerComponent;
  let fixture: ComponentFixture<EtwExplorerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ EtwExplorerComponent ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(EtwExplorerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
