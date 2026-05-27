FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["ChatServer.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
# Порты: 80 для веб-страницы, 8888 для чата
EXPOSE 80
EXPOSE 8888
ENTRYPOINT ["dotnet", "ChatServer.dll"]