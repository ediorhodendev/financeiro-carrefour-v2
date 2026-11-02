FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Financial.slnx
ARG PROJECT=src/Ledger/Ledger.Api/Ledger.Api.csproj
RUN dotnet publish "$PROJECT" -c Release --no-restore -o /out /p:UseAppHost=false
FROM build AS tests
ENTRYPOINT ["dotnet","test","Financial.slnx","-c","Release","--no-restore"]
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
COPY --from=build /out .
ARG APP_DLL=Ledger.Api.dll
ENV APP_DLL=$APP_DLL
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["/bin/sh","-c","exec dotnet \"$APP_DLL\" \"$@\"","--"]
