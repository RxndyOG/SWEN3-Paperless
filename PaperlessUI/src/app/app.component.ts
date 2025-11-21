import { Component, OnInit, OnDestroy, inject, ChangeDetectorRef } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { JsonPipe, CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

interface Document {
  id: number;
  fileName: string;
  content: string;
  createdAt: string;
  summary?: string; // added optional summary
  tag?: number;
  tagName?: string;
  tagClass?: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, JsonPipe, CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css'],
})
export class AppComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  private cdr = inject(ChangeDetectorRef);
  private sanitizer = inject(DomSanitizer);
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

  // Polling helpers for summary generation
  private summaryPollers: Record<number, number> = {}; // docId -> intervalId
  private readonly pollIntervalMs = 3000;
  private readonly maxPollAttempts = 30; // ~90s max
  // Debounce timer for search input
  private searchDebounceTimer?: number;

  // Increase max upload size to 10 MB
  private readonly maxUploadBytes = 10 * 1024 * 1024; // 10 MB

  ngOnInit(): void {
    this.result = {};
    this.loadDocuments();
  }

  ngOnDestroy(): void {
    // clear any running poll intervals
    for (const id of Object.values(this.summaryPollers)) {
      clearInterval(id);
    }
    this.summaryPollers = {};
    // clear search debounce timer if present
    if (this.searchDebounceTimer) {
      clearTimeout(this.searchDebounceTimer);
      this.searchDebounceTimer = undefined;
    }
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

  // Use the legacy JSON create endpoint so the server will persist the record
  this.http.post<Document>('/api/documents/create', payload).subscribe({
      next: (createdRaw) => {
        const created = this.normalizeDocument(createdRaw);
        // Insert server-returned document at front
        this.documents = [created, ...this.documents];
        this.filteredDocuments = [created, ...this.filteredDocuments];
        this.currentView = 'documents';
        this.uploadStatus = `Added document: ${created.fileName}`;
        this.isUploading = false;
        setTimeout(() => this.uploadStatus = '', 3000);

        // start polling for summary if needed
        if (created?.id && !created.summary) {
          this.startPollingForSummary(created.id);
        }
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

      if (file.size > this.maxUploadBytes) {
        this.uploadStatus = 'File too large. Maximum allowed size is 10 MB.';
        // clear input so user can pick another file
        input.value = '';
        setTimeout(() => (this.uploadStatus = ''), 5000);
        return;
      }

      this.uploadFile(file);
    }
  }

  uploadFile(file: File): void {
    this.isUploading = true;
    this.uploadStatus = 'Uploading...';

    const formData = new FormData();
    formData.append('file', file);
    // The API expects just the file, based on the response structure

    this.http.post('/api/documents', formData).subscribe({
      next: (response: any) => {
        this.isUploading = false;
        this.uploadStatus = 'File uploaded successfully!';
        this.selectedFileName = '';
        console.log('Upload response:', response);

        // If server returned the created document, normalize and insert/update
        const created = this.normalizeDocument(response);
        const createdId = created?.id ?? response?.id ?? response?.Id;
        if (created && createdId) {
          // add to lists if not present, or merge if present
          const upsert = (arr: Document[]) => {
            const idx = arr.findIndex(d => d.id === createdId);
            if (idx === -1) arr.unshift(created);
            else arr[idx] = { ...arr[idx], ...created };
          };
          upsert(this.documents);
          upsert(this.filteredDocuments);
          this.currentView = 'documents';
          if (!created.summary) {
            this.startPollingForSummary(createdId);
          }
        } else {
          // fallback: reload list
          this.loadDocuments();
        }

        // Clear status after 3 seconds
        setTimeout(() => {
          this.uploadStatus = '';
        }, 3000);
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
    const q = this.searchQuery?.trim();
    if (!q) {
      this.showAllDocuments();
      return;
    }
    this.performRemoteSearch(q);
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

      if (file.size > this.maxUploadBytes) {
        this.uploadStatus = 'File too large. Maximum allowed size is 10 MB.';
        setTimeout(() => (this.uploadStatus = ''), 5000);
        return;
      }

      this.uploadFile(file);
    }
  }

  loadDocuments(): void {
    this.isLoadingDocuments = true;
    this.http.get<Document[]>('/api/documents').subscribe({
      next: (documentsRaw) => {
        // normalize incoming docs so summary mapping is consistent
        const docs = (documentsRaw || []).map((d: any) => this.normalizeDocument(d));
        this.documents = docs;
        this.filteredDocuments = [...this.documents];
        this.isLoadingDocuments = false;
        console.log('Loaded documents:', docs);

        // Start polling for any documents that don't have a summary yet
        this.startPollingMissingSummaries();
      },
      error: (error) => {
        this.isLoadingDocuments = false;
        console.error('Error loading documents:', error);
        this.documents = [];
        this.filteredDocuments = [];
      }
    });
  }

  private startPollingMissingSummaries(): void {
    for (const doc of this.documents) {
      if (!doc.summary && !this.summaryPollers[doc.id]) {
        this.startPollingForSummary(doc.id);
      }
    }
  }

  private startPollingForSummary(docId: number): void {
    if (this.summaryPollers[docId]) return; // already polling

    let attempts = 0;
    const intervalId = window.setInterval(() => {
      attempts++;

      // add cache-buster to avoid stale/proxy responses
      const url = `/api/documents/${docId}?_=${Date.now()}`;

      this.http.get<any>(url).subscribe({
        next: (updatedRaw) => {
          const updated = this.normalizeDocument(updatedRaw);
          if (!updated) return;

          // Merge update into existing document entries. Update the object so bindings update.
          const mergeInto = (arr: Document[]) => {
            const idx = arr.findIndex(d => d.id === docId);
            if (idx >= 0) {
              // preserve same object reference where possible (helps bindings), then merge
              const existing = arr[idx];
              const merged = { ...existing, ...updated };
              arr[idx] = merged;
            }
          };

          mergeInto(this.documents);
          mergeInto(this.filteredDocuments);

          // Replace array references so ngFor picks up changes under all change-detection strategies
          this.documents = [...this.documents];
          this.filteredDocuments = [...this.filteredDocuments];

          // Re-apply current search/filter so updated fields (like summary) appear
          try {
            this.performSearch();
          } catch {
            // ignore if performSearch not available in some test contexts
          }

          // ensure Angular runs change detection immediately
          try { this.cdr.detectChanges(); } catch { /* ignore in SSR/test env */ }

          if (updated?.summary) {
            console.log(`Summary arrived for doc ${docId}`);
            // stop polling once summary arrives
            clearInterval(this.summaryPollers[docId]);
            delete this.summaryPollers[docId];
          }
         },
         error: (err) => {
           console.error(`Error polling document ${docId} for summary:`, err);
         }
       });

       if (attempts >= this.maxPollAttempts) {
         clearInterval(this.summaryPollers[docId]);
         delete this.summaryPollers[docId];
       }
     }, this.pollIntervalMs);

     this.summaryPollers[docId] = intervalId;
   }

  onSearchInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.searchQuery = input.value;

    // debounce remote search: wait 500ms after the user's last keystroke
    if (this.searchDebounceTimer) {
      clearTimeout(this.searchDebounceTimer);
    }

    this.searchDebounceTimer = window.setTimeout(() => {
      const q = this.searchQuery?.trim();
      if (!q) {
        this.showAllDocuments();
        return;
      }
      this.performRemoteSearch(q);
      this.searchDebounceTimer = undefined;
    }, 500);
  }

  performSearch(): void {
    if (!this.searchQuery.trim()) {
      this.filteredDocuments = [...this.documents];
      return;
    }
    // keep local fallback behaviour when explicitly invoked
    const query = this.searchQuery.toLowerCase();
    this.filteredDocuments = this.documents.filter(doc => 
      doc.fileName.toLowerCase().includes(query) ||
      (doc.content && doc.content.toLowerCase().includes(query))
    );
  }

  /**
   * Call the server-side search endpoint and restrict displayed documents
   * to the ids returned by the backend. The server returns objects like
   * [{ documentId: 11, summary: "..." }, ...]
   */
  performRemoteSearch(query: string): void {
    this.isLoadingDocuments = true;
    const url = `/api/documents/search?query=${encodeURIComponent(query)}`;
    this.http.get<Array<{ documentId: number; summary?: string }>>(url).subscribe({
      next: (results) => {
        const ids = (results || []).map(r => Number(r.documentId)).filter(Boolean);

        // merge summaries into existing documents (if present)
        for (const r of results || []) {
          const id = Number(r.documentId);
          const existing = this.documents.find(d => d.id === id);
          if (existing) {
            existing.summary = r.summary ?? existing.summary;
          }
        }

        // find ids that we don't have locally and fetch them
        const missing = ids.filter(id => !this.documents.some(d => d.id === id));
        if (missing.length === 0) {
          // simply filter local docs
          this.filteredDocuments = this.documents.filter(d => ids.includes(d.id));
          this.isLoadingDocuments = false;
          return;
        }

        // fetch missing documents in parallel
        const fetches = missing.map(id => this.http.get<any>(`/api/documents/${id}`).toPromise());
        Promise.all(fetches).then(fetched => {
          for (const raw of fetched) {
            const norm = this.normalizeDocument(raw);
            // attach summary from search results if present
            const searchHit = (results || []).find(r => Number(r.documentId) === norm.id);
            if (searchHit && searchHit.summary) norm.summary = searchHit.summary;
            // add to lists
            this.documents.push(norm);
          }

          // now filter
          this.filteredDocuments = this.documents.filter(d => ids.includes(d.id));
          // ensure arrays are replaced so bindings update
          this.documents = [...this.documents];
          this.filteredDocuments = [...this.filteredDocuments];
          this.isLoadingDocuments = false;
        }).catch(err => {
          console.error('Error fetching missing documents:', err);
          // still show what we can
          this.filteredDocuments = this.documents.filter(d => ids.includes(d.id));
          this.isLoadingDocuments = false;
        });
      },
      error: (err) => {
        console.error('Remote search error:', err);
        // fallback to local search when server search fails
        this.performSearch();
        this.isLoadingDocuments = false;
      }
    });
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

  /**
   * Convert markdown-like bold markers **text** into safe HTML with <strong> tags.
   * Returns sanitized SafeHtml for binding with [innerHTML].
   */
  formatMarkdown(text?: string): SafeHtml {
    if (!text) return '' as unknown as SafeHtml;

    // Escape basic HTML to avoid injection, then replace **bold** markers.
    const escaped = String(text)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    // Replace **bold** occurrences with <strong> tags (non-greedy)
    const html = escaped.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');

    return this.sanitizer.bypassSecurityTrustHtml(html);
  }

  // helper: normalize server payloads (handles PascalCase or different field names)
  private normalizeDocument(serverDoc: any): Document {
     if (!serverDoc) return serverDoc;
     const id = serverDoc.id ?? serverDoc.Id ?? serverDoc.documentId ?? serverDoc.DocumentId ?? serverDoc.DocumentId;
     const fileName = serverDoc.fileName ?? serverDoc.FileName ?? serverDoc.file_name ?? serverDoc.Name;
     const content = serverDoc.content ?? serverDoc.Content ?? serverDoc.ContentText ?? serverDoc.FullText;
     const createdAt = serverDoc.createdAt ?? serverDoc.CreatedAt ?? serverDoc.created_at ?? new Date().toISOString();
     const summary =
       serverDoc.summary ??
       serverDoc.Summary ??
       serverDoc.summarizedContent ??
       serverDoc.SummarizedContent ??
       serverDoc.SummarizedContentText;
    
    // Tag mapping (server returns numeric tag, e.g. "tag": 4)
    const rawTag = serverDoc.tag ?? serverDoc.Tag ?? serverDoc.TagId ?? 0;
    const tagNum = Number(rawTag) || 0;
    const tagMap: Record<number, { name: string; cls: string }> = {
      0: { name: 'Default', cls: 'tag-default' },
      1: { name: 'Invoice', cls: 'tag-invoice' },
      2: { name: 'Contract', cls: 'tag-contract' },
      3: { name: 'Personal', cls: 'tag-personal' },
      4: { name: 'Education', cls: 'tag-education' },
      5: { name: 'Medical', cls: 'tag-medical' },
      6: { name: 'Finance', cls: 'tag-finance' },
      7: { name: 'Legal', cls: 'tag-legal' },
      8: { name: 'Other', cls: 'tag-other' },
    };
    const tagInfo = tagMap[tagNum] ?? tagMap[0];
 
     return {
       id: Number(id),
       fileName: fileName ?? `Document_${id}`,
       content: content ?? '',
       createdAt: createdAt,
       summary: summary ?? undefined,
       tag: tagNum,
       tagName: tagInfo.name,
       tagClass: tagInfo.cls
     } as Document;
    }
 }
