# Сервер ОТК. Сборка из корня репозитория:
#   docker build -f ARDU_OTK.Server/Dockerfile -t ardu-otk-server .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ARDU_OTK.Shared/ ARDU_OTK.Shared/
COPY ARDU_OTK.Server/ ARDU_OTK.Server/
RUN dotnet publish ARDU_OTK.Server/ARDU_OTK.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# База — в томе: контейнер пересоздаётся при каждом обновлении.
ENV Otk__DataDir=/data \
    ASPNETCORE_URLS=http://0.0.0.0:8080
# Каталог создаётся заранее и отдаётся пользователю app: иначе новый том
# наследует владельца root, и сервер падает на первой записи в базу.
RUN mkdir -p /data && chown app /data
VOLUME /data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ARDU_OTK.Server.dll"]
