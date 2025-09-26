import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { JsonPipe, CommonModule } from '@angular/common';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, JsonPipe, CommonModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css'],
})
export class AppComponent implements OnInit {
  private http = inject(HttpClient);
  result: unknown = {};
  isMenuOpen = false;

  ngOnInit(): void {
    this.result = {};
  }

  testApi(): void {
    this.http.get('/api/documents').subscribe({
      next: (res) => this.result = res,
      error: (err) => this.result = err?.message ?? err
    });
  }

  toggleMenu(): void {
    this.isMenuOpen = !this.isMenuOpen;
  }

  addDocument(): void {
    console.log('Add document clicked');
    // TODO: Implement document upload
  }

  searchDocuments(): void {
    console.log('Search documents clicked');
    // TODO: Implement search functionality
  }

  viewSettings(): void {
    console.log('Settings clicked');
    // TODO: Implement settings view
  }
}
