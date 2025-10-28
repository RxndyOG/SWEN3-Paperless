import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { JsonPipe, CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

interface Document {
  id: number;
  fileName: string;
  content: string;
  createdAt: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, JsonPipe, CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css'],
})
export class AppComponent implements OnInit {
  private http = inject(HttpClient);
  result: unknown = {};
  isMenuOpen = false;
  uploadStatus: string = '';
  isUploading = false;
  selectedFileName = '';
  isDragOver = false;
  
  // Document management properties
  documents: Document[] = [];
  filteredDocuments: Document[] = [];
  isLoadingDocuments = false;
  searchQuery = '';
  currentView: 'home' | 'documents' = 'home';

  ngOnInit(): void {
    this.result = {};
    this.loadDocuments();
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
    // Trigger file input click
    const fileInput = document.getElementById('fileInput') as HTMLInputElement;
    if (fileInput) {
      fileInput.click();
    }
  }

  // Spoof helper for testing UI without calling the backend
  addFakeDocument(): void {
    // Create minimal JSON payload (no Id - server will generate it)
    const payload = {
      fileName: `FakeDocument_${Date.now()}.pdf`,
      content: 'This is a fake document used for UI testing. It contains sample text to exercise search and listing features.',
      createdAt: new Date().toISOString()
    } as Partial<Document>;

    this.isUploading = true;
    this.uploadStatus = 'Posting fake document...';

    this.http.post<Document>('/api/documents', payload).subscribe({
      next: (created) => {
        // Insert server-returned document at front
        this.documents = [created, ...this.documents];
        this.filteredDocuments = [created, ...this.filteredDocuments];
        this.currentView = 'documents';
        this.uploadStatus = `Added document: ${created.fileName}`;
        this.isUploading = false;
        setTimeout(() => this.uploadStatus = '', 3000);
      },
      error: (err) => {
        this.isUploading = false;
        this.uploadStatus = `Failed to add fake document: ${err?.message ?? err}`;
        setTimeout(() => this.uploadStatus = '', 4000);
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      
      // Check if file is PDF
      if (file.type !== 'application/pdf') {
        this.uploadStatus = 'Please select a PDF file only.';
        return;
      }

      // Check file size (max 10MB)
      const maxSize = 10 * 1024 * 1024; // 10MB
      if (file.size > maxSize) {
        this.uploadStatus = 'File size must be less than 10MB.';
        return;
      }

      this.selectedFileName = file.name;
      this.uploadFile(file);
    }
  }

  uploadFile(file: File): void {
    this.isUploading = true;
    this.uploadStatus = 'Uploading...';

    const formData = new FormData();
    formData.append('file', file);
    // The API expects just the file, based on the response structure

    this.http.post('/api/documents/upload', formData).subscribe({
      next: (response) => {
        this.isUploading = false;
        this.uploadStatus = 'File uploaded successfully!';
        this.selectedFileName = '';
        console.log('Upload response:', response);
        
        // Clear status after 3 seconds
        setTimeout(() => {
          this.uploadStatus = '';
        }, 3000);
        
        // Reload documents after successful upload
        this.loadDocuments();
      },
      error: (error) => {
        this.isUploading = false;
        this.uploadStatus = `Upload failed: ${error.error?.message || error.message || 'Unknown error'}`;
        this.selectedFileName = '';
        console.error('Upload error:', error);
        
        // Clear status after 5 seconds
        setTimeout(() => {
          this.uploadStatus = '';
        }, 5000);
      }
    });
  }

  searchDocuments(): void {
    this.currentView = 'documents';
    this.performSearch();
  }

  viewSettings(): void {
    console.log('Settings clicked');
    // TODO: Implement settings view
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = false;
  }

  onFileDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = false;

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      const file = files[0];
      
      // Check if file is PDF
      if (file.type !== 'application/pdf') {
        this.uploadStatus = 'Please select a PDF file only.';
        return;
      }

      // Check file size (max 10MB)
      const maxSize = 10 * 1024 * 1024; // 10MB
      if (file.size > maxSize) {
        this.uploadStatus = 'File size must be less than 10MB.';
        return;
      }

      this.selectedFileName = file.name;
      this.uploadFile(file);
    }
  }

  loadDocuments(): void {
    this.isLoadingDocuments = true;
    this.http.get<Document[]>('/api/documents').subscribe({
      next: (documents) => {
        this.documents = documents || [];
        this.filteredDocuments = [...this.documents];
        this.isLoadingDocuments = false;
        console.log('Loaded documents:', documents);
      },
      error: (error) => {
        this.isLoadingDocuments = false;
        console.error('Error loading documents:', error);
        this.documents = [];
        this.filteredDocuments = [];
      }
    });
  }

  onSearchInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.searchQuery = input.value;
    this.performSearch();
  }

  performSearch(): void {
    if (!this.searchQuery.trim()) {
      this.filteredDocuments = [...this.documents];
      return;
    }

    const query = this.searchQuery.toLowerCase();
    this.filteredDocuments = this.documents.filter(doc => 
      doc.fileName.toLowerCase().includes(query) ||
      (doc.content && doc.content.toLowerCase().includes(query))
    );
  }

  showAllDocuments(): void {
    this.currentView = 'documents';
    this.searchQuery = '';
    this.filteredDocuments = [...this.documents];
  }

  showHome(): void {
    this.currentView = 'home';
  }

  downloadDocument(doc: Document): void {
    if (doc.id) {
      // Create download link
      const link = document.createElement('a');
      link.href = `/api/documents/${doc.id}/download`;
      link.download = doc.fileName;
      link.click();
    }
  }

  deleteDocument(doc: Document): void {
    if (confirm(`Are you sure you want to delete "${doc.fileName}"?`)) {
      this.http.delete(`/api/documents/${doc.id}`, { 
        responseType: 'text' 
      }).subscribe({
        next: (response) => {
          this.loadDocuments(); // Reload the list
          this.uploadStatus = response || 'Document deleted successfully!';
          setTimeout(() => {
            this.uploadStatus = '';
          }, 3000);
        },
        error: (error) => {
          this.uploadStatus = `Delete failed: ${error.error?.message || error.message || 'Unknown error'}`;
          setTimeout(() => {
            this.uploadStatus = '';
          }, 5000);
        }
      });
    }
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
