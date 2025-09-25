import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { JsonPipe } from '@angular/common';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, JsonPipe],
  template: `
    <h1>PaperlessUI</h1>
    <button (click)="testApi()">Test API</button>
    <pre>{{ result | json }}</pre>
  `,
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  private http = inject(HttpClient);
  result: unknown = {};

  ngOnInit(): void {
    this.result = {};
  }

  testApi(): void {
    this.http.get('/api/documents').subscribe({
      next: (res) => this.result = res,
      error: (err) => this.result = err?.message ?? err
    });
  }
}
