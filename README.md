graph LR
    subgraph Client
        UI[Web UI]
        CURL[curl / Postman]
    end

    subgraph Backend
        REST[Paperless REST API]
        OCRW[OCR Worker]
        AIW[AI Worker]
        RESTC["REST Consumer\n(RabbitMQ)"]
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
    AIW -->|"msg: MessageTransferObject\n(ocrText, summary, tag)"| RABBIT

    RABBIT -->|queue: genai_finished| RESTC
    RESTC -->|update summary + tag| PG
    RESTC -->|index doc| ES

    REST -->|search via ElasticService| ES
