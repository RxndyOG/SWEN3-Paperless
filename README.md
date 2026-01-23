# Paperless – Distributed Document Processing System

Paperless is a small **distributed document-processing system** built for the **SWEN3 course**.

It demonstrates a service-oriented architecture with asynchronous processing, OCR, AI-based summarization, full-text search, batch processing, and automated testing.

---

## Features

- Upload PDF documents via **REST API** or **Web UI**
- Store original files in **MinIO** (S3-compatible object storage)
- Persist document metadata and versions in **PostgreSQL**
- Perform **OCR (Tesseract)** in a dedicated worker
- Summarize and auto-tag documents using **Google Gemini** in a second worker
- Index documents in **Elasticsearch**
- Search documents by content via REST API and UI
- Process **daily access statistics** via a **batch worker** (XML input)
- Fully containerized using **Docker Compose**
- Automated **unit tests, integration tests, and CI pipeline**

---

## High-Level Architecture

``` mermaid
graph LR

%% Clients
subgraph Client
  UI[Web UI]
  CURL[curl / Postman]
end

%% Core Backend
subgraph Backend
  direction LR
  REST[REST API]
  OCRW[OCR Worker]
  AIW[AI Worker]
  RESTC[REST Consumer]
end

%% Messaging
subgraph Messaging
  RABBIT[(RabbitMQ)]
end

%% Infrastructure
subgraph Storage
  PG[(PostgreSQL)]
  MINIO[(MinIO)]
  ES[(Elasticsearch)]
end

%% Batch
subgraph Batch
  FS[(XML Input / Archive)]
  BATCH[Access Batch Worker]
end

%% Client to API
UI --> REST
CURL --> REST

%% Upload flow
REST --> MINIO
REST --> PG
REST --> RABBIT

RABBIT --> OCRW
OCRW --> RABBIT
RABBIT --> AIW
AIW --> RABBIT
RABBIT --> RESTC

RESTC --> PG
RESTC --> ES

%% Search
REST --> ES

%% Batch processing
FS --> BATCH
BATCH --> PG
BATCH --> FS


```

## Components

### REST API

- Upload documents

- Manage document versions

- Download documents

- Search documents via Elasticsearch

- Publishes events to RabbitMQ

### OCR Worker

- Listens for uploaded document events

- Extracts text using Tesseract

- Emits OCR results asynchronously

### AI Worker

- Uses Google Gemini to summarize and classify OCR text

- Emits enriched document metadata

### Batch Worker (Access Statistics)

- Runs as a separate application

- Reads daily XML files from a configurable input folder

- Aggregates access counts per document and day

- Stores results in PostgreSQL

- Archives processed XML files

### Infrastructure

- PostgreSQL – metadata, versions, access statistics

- MinIO – document storage

- RabbitMQ – asynchronous messaging

- Elasticsearch – full-text search

---

## Batch Processing – Access Statistics

### XML Input Format

Example file: access-2026-01-21.xml

``` XML
<accessStatistics date="2026-01-21">
  <document id="1" count="5" />
  <document id="2" count="12" />
</accessStatistics>
```

### Processing Rules

- One file per day

- Each entry contains:

    - documentId

    - accessCount

- esults are stored in table AccessStatistics

- Unique constraint: (documentId, accessDate)

- Files are archived after processing to prevent duplicate processing

## Scheduling

- Production: intended to run daily (e.g. 01:00 AM)

- Development/testing: configurable polling interval (e.g. every minute)

## Database Schema (Excerpt)

- Documents

    - ```Id, FileName, CreatedAt, CurrentVersionId, CurrentVersion, Versions```

- DocumentVersions
    - ``` Id DocumentId, Document, DiffBaseVersionId, DiffBaseVersion, Content, SummarizedContent, ChangeSummmary, Tag ```

- AccessStatistics
    - ``` Id, DocumentId, AccessDate, AccessCount```

---

# Running the Project Locally

## Prerequisites

- Docker & Docker Compose

- .NET SDK (for local development and testing)

```Start all services
docker compose up --build 
```

This starts:

- REST API

- Web UI

- PostgreSQL

- MinIO

- RabbitMQ

- Elasticsearch

- OCR Worker

- AI Worker

- Batch Worker

### Access URLs

- REST API: http://localhost:5000

- Web UI: http://localhost:4200

- MinIO Console: http://localhost:9001

- Elasticsearch: http://localhost:9200

## Configuration

Configuration is provided via:

- .env file

- Docker Compose service definitions

Includes:

- Database credentials

- MinIO endpoint & credentials

- RabbitMQ credentials

- Elasticsearch endpoint

- Batch worker input & archive folders

## Testing

### Unit Tests

- Business logic

- Services

- Workers

- Mocked dependencies (MQ, Elastic, AI)

### Integration Tests

- End-to-end document upload use case

- Real PostgreSQL & MinIO using Testcontainers

- Verifies:

    - REST upload

    - database persistence

    - object storage

    - document versioning

## Run tests locally

``` C#
dotnet test Paperless.sln 
```

## CI/CD Pipeline

- Implemented using GitHub Actions

- Triggered on push and pull request

- Pipeline steps:

    - Restore dependencies

    - Build solution

    - Apply EF migrations

    - Run unit and integration tests

- External dependencies are provisioned using Testcontainers

## Software Architecture & Design Principles

- Layered architecture (API, Services, Workers, DAL)

- Loose coupling via interfaces

- Dependency Injection throughout

- Asynchronous communication via RabbitMQ

- Clean Code and SOLID principles applied

- Clear separation of concerns between components

## Project Workflow

- Git-based version control

- Feature branches and pull requests

- Issue tracking and task board used for coordination

- CI pipeline ensures build and test stability
