FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

COPY AuthorsBooksService.slnx ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY global.json ./
COPY src/ ./src/

RUN dotnet restore src/AuthorsBooks.Api/AuthorsBooks.Api.csproj
RUN dotnet publish src/AuthorsBooks.Api/AuthorsBooks.Api.csproj -c Release -r linux-x64 -o /app/publish /p:UseAppHost=true /p:PublishAot=true

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["./AuthorsBooks.Api"]
