# base image for running
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# build image
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# copy project files (for proper restore + caching)
COPY ["PaperlessREST/Paperless.REST.csproj", "PaperlessREST/"]
COPY ["Paperless.Contracts/Paperless.Contracts.csproj", "Paperless.Contracts/"]

# restore only the REST project (which references Contracts)
RUN dotnet restore "PaperlessREST/Paperless.REST.csproj"

# copy the rest of the source
COPY . .

# publish REST project
WORKDIR /src/PaperlessREST
RUN dotnet publish -c Release -o /app/publish

# final runtime image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Paperless.REST.dll"]
