# build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet add package Google_GenerativeAI
RUN dotnet publish -c Release -o /out

# run
FROM mcr.microsoft.com/dotnet/runtime:9.0
RUN apt-get update && \
	rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "PaperlessAI.dll"]