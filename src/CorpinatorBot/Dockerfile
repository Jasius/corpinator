FROM microsoft/dotnet:2.1-runtime AS base
WORKDIR /app

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY CorpinatorBot/CorpinatorBot.csproj CorpinatorBot/
RUN dotnet restore CorpinatorBot/CorpinatorBot.csproj
COPY . .
WORKDIR /src/CorpinatorBot
RUN dotnet build CorpinatorBot.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish CorpinatorBot.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "CorpinatorBot.dll"]
