# build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files (for better layer caching)
COPY ["PaperlessAI/Paperless.AI.csproj", "PaperlessAI/"]
COPY ["Paperless.Contracts/Paperless.Contracts.csproj", "Paperless.Contracts/"]

# Restore dependencies
RUN dotnet restore "PaperlessAI/Paperless.AI.csproj"

# Copy the rest of the source
COPY . .

# Publish
WORKDIR /src/PaperlessAI
RUN dotnet publish -c Release -o /out

# run
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "Paperless.AI.dll"]
