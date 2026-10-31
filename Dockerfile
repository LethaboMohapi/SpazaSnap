FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SpazaSnap.csproj global.json ./
RUN dotnet restore SpazaSnap.csproj
COPY . .
RUN dotnet publish SpazaSnap.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out ./
RUN mkdir -p /app/App_Data && chown -R app:app /app/App_Data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SpazaSnap.dll"]
