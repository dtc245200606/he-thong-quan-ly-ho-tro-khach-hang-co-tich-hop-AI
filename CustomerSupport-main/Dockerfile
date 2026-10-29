# CustomerSupport: ASP.NET Core MVC (.NET 10) + SQLite. All state lives in /data (mount a volume there).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CustomerSupportAI.csproj ./
RUN dotnet restore CustomerSupportAI.csproj
COPY . .
RUN dotnet publish CustomerSupportAI.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# Database, encrypted AI settings, Data Protection key ring and dev mail outbox all on the /data volume.
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__DefaultConnection="Data Source=/data/customer-support.db" \
    AiSettings__StorageDirectory=/data/settings \
    DataProtection__KeysDirectory=/data/keys \
    Mail__OutboxDirectory=/data/mail-outbox
RUN mkdir -p /data && chown -R app:app /data
USER app
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "CustomerSupportAI.dll"]
