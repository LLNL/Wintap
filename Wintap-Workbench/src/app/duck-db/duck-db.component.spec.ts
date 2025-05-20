import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DuckDbComponent } from './duck-db.component';

describe('DuckDbComponent', () => {
  let component: DuckDbComponent;
  let fixture: ComponentFixture<DuckDbComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ DuckDbComponent ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(DuckDbComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
