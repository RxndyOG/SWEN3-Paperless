# build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["PaperlessOCR/Paperless.OCR.csproj", "PaperlessOCR/"]
COPY ["Paperless.Contracts/Paperless.Contracts.csproj", "Paperless.Contracts/"]

# Restore dependencies
RUN dotnet restore "PaperlessOCR/Paperless.OCR.csproj"

# Copy the rest of the source
COPY . .

# Publish OCR project
WORKDIR /src/PaperlessOCR
RUN dotnet publish -c Release -o /out

# run
FROM mcr.microsoft.com/dotnet/runtime:9.0
RUN apt-get update && \
    apt-get install -y --no-install-recommends tesseract-ocr poppler-utils && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "Paperless.OCR.dll"]
