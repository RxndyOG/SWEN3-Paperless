import { Component, OnInit, OnDestroy, inject, ChangeDetectorRef } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HttpClient, HttpClientModule } from '@angular/common/http';
import { JsonPipe, CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

interface Document {
  id: number;
  fileName: string;
  content: string;
  createdAt: string;
  summary?: string;
  tag?: number;
  tagName?: string;
  tagClass?: string;
  currentVersionId?: number;
  versionNumber?: number;
  sizeBytes?: number;
  changeSummary?: string;
}

interface DocumentVersion {
  id?: number;
  versionNumber?: number;
  sizeBytes?: number;
  tag?: number;
  changeSummary?: string;
  summarizedContent?: string;
  contentType?: string;
  diffBaseVersionId?: number;
}

interface DocumentWithVersions extends Document {
  versions?: DocumentVersion[];
  allVersionsLoaded?: boolean;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, JsonPipe, CommonModule, FormsModule, HttpClientModule],
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

  documents: DocumentWithVersions[] = [];
  filteredDocuments: DocumentWithVersions[] = [];
  isLoadingDocuments = false;
  searchQuery = '';
  currentView: 'home' | 'documents' = 'home';
  selectedDocument?: DocumentWithVersions;
  selectedVersion?: DocumentVersion;
  showVersionHistory = false;

  //Polling helpers for summary generation
  private summaryPollers: Record<number, number> = {}; // docId -> intervalId
  private readonly pollIntervalMs = 3000;
  private readonly maxPollAttempts = 30; // ~90s max
  //Debounce timer for search input
  private searchDebounceTimer?: number;

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
      next: (res: unknown) => this.result = res,
      error: (err: any) => this.result = err?.message ?? err
    });
  }

  toggleMenu(): void {
    this.isMenuOpen = !this.isMenuOpen;
  }

  addDocument(): void {
    console.log('Add document clicked');
    //Trigger file input click
    const fileInput = document.getElementById('fileInput') as HTMLInputElement;
    if (fileInput) {
      fileInput.click();
    }
  }

  viewSettings(): void {
     console.log('Settings clicked');
     // TODO: Implement settings view
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];

      if (file.size > this.maxUploadBytes) {
        this.uploadStatus = 'File too large. Maximum allowed size is 10 MB.';
        //clear input so user can pick another file
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

    this.http.post('/api/documents', formData).subscribe({
      next: (response: any) => {
        this.isUploading = false;
        this.uploadStatus = 'File uploaded successfully!';
        this.selectedFileName = '';
        console.log('Upload response:', response);

        //If server returned the created document, normalize and insert/update
        const created = this.normalizeDocument(response);
        const createdId = created?.id ?? response?.id ?? response?.Id;
        if (created && createdId) {
          //add to lists if not present, or merge if present
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
          //fallback: reload list
          this.loadDocuments();
        }

        //Clear status after 3 seconds
        setTimeout(() => {
          this.uploadStatus = '';
        }, 3000);
      },
      error: (error: any) => {
        this.isUploading = false;
        this.uploadStatus = `Upload failed: ${error.error?.message || error.message || 'Unknown error'}`;
        this.selectedFileName = '';
        console.error('Upload error:', error);
        
        //Clear status after 5 seconds
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

  showAllDocuments(): void {
    this.searchQuery = '';
    this.filteredDocuments = [...this.documents];
    this.currentView = 'documents';
  }

  showHome(): void {
    this.currentView = 'home';
    this.selectedDocument = undefined;
  }

  downloadDocument(doc: Document): void {
    if (!doc?.id) return;
    const url = `/api/documents/${doc.id}/download`;
    this.http.get(url, { responseType: 'blob' }).subscribe({
      next: (blob: Blob) => {
        const objectUrl = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = objectUrl;
        link.download = doc.fileName || 'document.pdf';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(objectUrl);
      },
      error: (error: any) => {
        console.error('Download failed:', error);
        this.uploadStatus = `Download failed: ${error.error?.message || error.message || 'Unknown error'}`;
        setTimeout(() => this.uploadStatus = '', 4000);
      }
    });
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
      next: (documentsRaw: any) => {
        //normalize incoming docs so summary mapping is consistent
        const docs = (documentsRaw || []).map((d: any) => this.normalizeDocument(d));
        this.documents = docs;
        this.filteredDocuments = [...this.documents];
        this.isLoadingDocuments = false;
        console.log('Loaded documents:', docs);

        //Start polling for any documents that don't have a summary yet
        this.startPollingMissingSummaries();
      },
      error: (error: any) => {
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
    if (this.summaryPollers[docId]) return;

    let attempts = 0;
    const intervalId = window.setInterval(() => {
      attempts++;
      const url = `/api/documents/${docId}?_=${Date.now()}`;
      this.http.get<any>(url).subscribe({
        next: (updatedRaw: any) => {
          const updated = this.normalizeDocument(updatedRaw);
          if (!updated) return;

          const mergeInto = (arr: Document[]) => {
            const idx = arr.findIndex((d) => d.id === docId);
            if (idx >= 0) {
              const merged = { ...arr[idx], ...updated };
              arr[idx] = merged;
            }
          };

          mergeInto(this.documents);
          mergeInto(this.filteredDocuments);

          //also update selected if it is this doc
          if (this.selectedDocument?.id === docId) {
            this.selectedDocument = {
              ...this.selectedDocument,
              ...updated,
            };
          }

          this.documents = [...this.documents];
          this.filteredDocuments = [...this.filteredDocuments];
          try { this.cdr.detectChanges(); } catch {}

          if (updated?.summary) {
            clearInterval(this.summaryPollers[docId]);
            delete this.summaryPollers[docId];
          }
        },
        error: (err: any) => console.error(`Error polling document ${docId} for summary:`, err),
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
    const query = this.searchQuery.toLowerCase();
    this.filteredDocuments = this.documents.filter(doc => 
      doc.fileName.toLowerCase().includes(query) ||
      (doc.content && doc.content.toLowerCase().includes(query))
    );
  }

  performRemoteSearch(query: string): void {
    this.isLoadingDocuments = true;
    const url = `/api/documents/search?query=${encodeURIComponent(query)}`;
    this.http.get<Array<{ documentId: number; summary?: string }>>(url).subscribe({
      next: (results: Array<{ documentId: number; summary?: string }>) => {
        const ids = (results || []).map(r => Number(r.documentId)).filter(Boolean);
        for (const r of results || []) {
          const id = Number(r.documentId);
          const existing = this.documents.find(d => d.id === id);
          if (existing) {
            existing.summary = r.summary ?? existing.summary;
          }
        }
        const missing = ids.filter(id => !this.documents.some(d => d.id === id));
        if (missing.length === 0) {
          this.filteredDocuments = this.documents.filter(d => ids.includes(d.id));
          this.isLoadingDocuments = false;
          return;
        }
        const fetches = missing.map(id => this.http.get<any>(`/api/documents/${id}`).toPromise());
        Promise.all(fetches).then(fetched => {
          for (const raw of fetched) {
            const norm = this.normalizeDocument(raw);
            const searchHit = (results || []).find(r => Number(r.documentId) === norm.id);
            if (searchHit && searchHit.summary) norm.summary = searchHit.summary;
            this.documents.push(norm);
          }
          this.filteredDocuments = this.documents.filter(d => ids.includes(d.id));
          this.documents = [...this.documents];
          this.filteredDocuments = [...this.filteredDocuments];
          this.isLoadingDocuments = false;
        }).catch(err => {
          console.error('Error fetching missing documents:', err);
          this.filteredDocuments = this.documents.filter(d => ids.includes(d.id));
          this.isLoadingDocuments = false;
        });
      },
      error: (err: any) => {
        console.error('Remote search error:', err);
        this.performSearch();
        this.isLoadingDocuments = false;
      }
    });
  }

  deleteDocument(doc: Document): void {
    if (confirm(`Are you sure you want to delete "${doc.fileName}"?`)) {
      this.http.delete(`/api/documents/${doc.id}`, { 
        responseType: 'text' 
      }).subscribe({
        next: (response: string) => {
           this.loadDocuments();
           this.uploadStatus = response || 'Document deleted successfully!';
           setTimeout(() => {
             this.uploadStatus = '';
           }, 3000);
         },
        error: (error: any) => {
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

  formatMarkdown(text?: string): SafeHtml {
    if (!text) return '' as unknown as SafeHtml;

    const escaped = String(text)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    const html = escaped.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');

    return this.sanitizer.bypassSecurityTrustHtml(html);
  }

  //helper: normalize server payloads (handles PascalCase or different field names)
  private normalizeDocument(serverDoc: any): Document {
    if (!serverDoc) return serverDoc;

    //extract current version if present
    const current: DocumentVersion | undefined = serverDoc.current ?? serverDoc.Current;
    const currentAny = current as any;

    //tag mapping
    const rawTag = serverDoc.tag ?? serverDoc.Tag ?? currentAny?.tag ?? currentAny?.Tag ?? 0;
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

    const summary =
      serverDoc.summary ??
      serverDoc.Summary ??
      serverDoc.summarizedContent ??
      serverDoc.SummarizedContent ??
      currentAny?.summarizedContent ??
      currentAny?.SummarizedContent;

    const changeSummary =
      serverDoc.changeSummary ??
      serverDoc.ChangeSummary ??
      currentAny?.changeSummary ??
      currentAny?.ChangeSummary;

    const versionNumber =
      serverDoc.versionNumber ??
      serverDoc.VersionNumber ??
      currentAny?.versionNumber ??
      currentAny?.VersionNumber;

    const sizeBytes =
      serverDoc.sizeBytes ??
      serverDoc.SizeBytes ??
      currentAny?.sizeBytes ??
      currentAny?.SizeBytes;

    const id =
      serverDoc.id ??
      serverDoc.Id ??
      serverDoc.documentId ??
      serverDoc.DocumentId;

    const fileName =
      serverDoc.fileName ??
      serverDoc.FileName ??
      serverDoc.file_name ??
      serverDoc.Name;

    const content =
      serverDoc.content ??
      serverDoc.Content ??
      serverDoc.ContentText ??
      '';

    const createdAt =
      serverDoc.createdAt ??
      serverDoc.CreatedAt ??
      serverDoc.created_at ??
      new Date().toISOString();

    return {
      id: Number(id),
      fileName: fileName ?? `Document_${id}`,
      content,
      createdAt,
      summary: summary ?? undefined,
      tag: tagNum,
      tagName: tagInfo.name,
      tagClass: tagInfo.cls,
      currentVersionId: serverDoc.currentVersionId ?? serverDoc.CurrentVersionId,
      versionNumber: versionNumber ? Number(versionNumber) : undefined,
      sizeBytes: sizeBytes ? Number(sizeBytes) : undefined,
      changeSummary: changeSummary ?? undefined,
    };
  }

  selectDocument(doc: DocumentWithVersions): void {
    this.selectedDocument = doc;
    this.showVersionHistory = false;
    this.selectedVersion = undefined;
    
    // Load versions if not already loaded
    if (!doc.allVersionsLoaded) {
      this.loadDocumentVersions(doc.id);
    }
  }

  loadDocumentVersions(docId: number): void {
    const url = `/api/documents/${docId}/versions`;
    console.log('Loading versions from:', url);
    
    this.http.get<any>(url).subscribe({
      next: (response: any) => {
        console.log('Versions loaded:', response);
        const versions = response.Versions || response.versions || response || [];
        
        const updateDoc = (arr: DocumentWithVersions[]) => {
          const idx = arr.findIndex(d => d.id === docId);
          if (idx >= 0) {
            arr[idx] = {
              ...arr[idx],
              versions: Array.isArray(versions) ? versions : [],
              allVersionsLoaded: true,
              currentVersionId: response.CurrentVersionId || response.currentVersionId || response.id
            };
          }
        };
        
        updateDoc(this.documents);
        updateDoc(this.filteredDocuments);
        
        if (this.selectedDocument?.id === docId) {
          this.selectedDocument = {
            ...this.selectedDocument,
            versions: Array.isArray(versions) ? versions : [],
            allVersionsLoaded: true,
            currentVersionId: response.CurrentVersionId || response.currentVersionId || response.id
          };
        }
        
        this.documents = [...this.documents];
        this.filteredDocuments = [...this.filteredDocuments];
        try { this.cdr.detectChanges(); } catch {}
      },
      error: (err: any) => {
        console.error('Failed to load versions from', url, ':', err);
        console.error('Full error:', JSON.stringify(err, null, 2));
        
        //Mark as loaded but empty to prevent infinite retries
        const updateDoc = (arr: DocumentWithVersions[]) => {
          const idx = arr.findIndex(d => d.id === docId);
          if (idx >= 0) {
            arr[idx] = {
              ...arr[idx],
              versions: [],
              allVersionsLoaded: true
            };
          }
        };
        updateDoc(this.documents);
        updateDoc(this.filteredDocuments);
        
        this.uploadStatus = `Versions endpoint not available: ${err.status} ${err.statusText}`;
        setTimeout(() => this.uploadStatus = '', 5000);
      }
    });
  }

  toggleVersionHistory(): void {
    this.showVersionHistory = !this.showVersionHistory;
    if (this.showVersionHistory && this.selectedDocument && !this.selectedDocument.allVersionsLoaded) {
      this.loadDocumentVersions(this.selectedDocument.id);
    }
  }

  selectVersion(version: DocumentVersion): void {
    this.selectedVersion = version;
  }

  setCurrentVersion(docId: number, versionId: number): void {
    this.http.put(`/api/documents/${docId}/currentVersion/${versionId}`, {}).subscribe({
      next: () => {
        // Update local state
        const updateDoc = (arr: DocumentWithVersions[]) => {
          const idx = arr.findIndex(d => d.id === docId);
          if (idx >= 0) {
            arr[idx] = { ...arr[idx], currentVersionId: versionId };
          }
        };
        
        updateDoc(this.documents);
        updateDoc(this.filteredDocuments);
        
        if (this.selectedDocument?.id === docId) {
          this.selectedDocument = {
            ...this.selectedDocument,
            currentVersionId: versionId
          };
        }
        
        this.documents = [...this.documents];
        this.filteredDocuments = [...this.filteredDocuments];
        
        this.uploadStatus = 'Current version updated successfully!';
        setTimeout(() => this.uploadStatus = '', 3000);
        
        // Reload document to get updated details
        this.loadDocuments();
      },
      error: (err: any) => {
        console.error('Failed to set current version:', err);
        this.uploadStatus = `Failed to update version: ${err.error?.message || err.message || 'Unknown error'}`;
        setTimeout(() => this.uploadStatus = '', 4000);
      }
    });
  }

  downloadVersion(versionId: number, versionNumber?: number): void {
    const url = `/api/documents/versions/${versionId}/file`;
    this.http.get(url, { responseType: 'blob' }).subscribe({
      next: (blob: Blob) => {
        const objectUrl = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = objectUrl;
        link.download = `${this.selectedDocument?.fileName || 'document'}_v${versionNumber || versionId}.pdf`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(objectUrl);
      },
      error: (error: any) => {
        console.error('Download failed:', error);
        this.uploadStatus = `Download failed: ${error.error?.message || error.message || 'Unknown error'}`;
        setTimeout(() => this.uploadStatus = '', 4000);
      }
    });
  }

  getVersionOcr(versionId: number): void {
    this.http.get<any>(`/api/documents/versions/${versionId}/ocr`).subscribe({
      next: (response: any) => {
        console.log('OCR Text:', response.OcrText || response.ocrText);
        alert(`OCR Text:\n\n${response.OcrText || response.ocrText}`);
      },
      error: (err: any) => {
        console.error('Failed to load OCR:', err);
      }
    });
  }

  formatVersionDate(version: DocumentVersion): string {
    return `Version ${version.versionNumber || version.id}`;
  }

  formatBytes(bytes?: number): string {
    if (!bytes) return '0 KB';
    const kb = bytes / 1024;
    if (kb < 1024) return `${kb.toFixed(1)} KB`;
    const mb = kb / 1024;
    return `${mb.toFixed(1)} MB`;
  }

}
