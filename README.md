# Paperless – OCR, AI Summarization & Document Search

Paperless is a small distributed document-processing system built for the SWEN3 course.

It allows you to:

- Upload PDF documents via a web UI or REST API
- Store the original file in MinIO and metadata in PostgreSQL
- Run OCR (Tesseract) in a dedicated worker
- Summarize the extracted text & auto-tag documents using Google Gemini in a second worker
- Index documents in Elasticsearch
- Search for documents by content via the REST API and UI

---

## High-Level Architecture

```mermaid
graph LR
    subgraph Client
        UI[Web UI]
        CURL[curl / Postman]
    end

    subgraph Backend
        REST[Paperless REST API]
        OCRW[OCR Worker]
        AIW[AI Worker]
        RESTC[REST Consumer<br/>(RabbitMQ)]
    end

    subgraph Infra
        PG[(PostgreSQL)]
        MINIO[(MinIO)]
        RABBIT[(RabbitMQ)]
        ES[(Elasticsearch)]
    end

    UI -->|HTTP: upload/search| REST
    CURL -->|HTTP: upload/search| REST

    REST -->|store file| MINIO
    REST -->|store metadata| PG
    REST -->|msg: UploadedDocMessage| RABBIT

    RABBIT -->|queue: documents| OCRW
    OCRW -->|read object| MINIO
    OCRW -->|OCR text| RABBIT

    RABBIT -->|queue: ocr_finished| AIW
    AIW -->|Gemini summarize + classify| AIW
    AIW -->|msg: MessageTransferObject<br/>(ocrText, summary, tag)| RABBIT

    RABBIT -->|queue: genai_finished| RESTC
    RESTC -->|update summary + tag| PG
    RESTC -->|index doc| ES

    REST -->|search via ElasticService| ES
