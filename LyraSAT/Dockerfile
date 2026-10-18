FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["LyraSAT.csproj", "./"]
RUN dotnet restore "LyraSAT.csproj"
COPY . .
RUN dotnet build "LyraSAT.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "LyraSAT.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LyraSAT.dll"]