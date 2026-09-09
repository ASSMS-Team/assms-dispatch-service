ARG BUILDPLATFORM
ARG TARGETARCH

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY ["src/DispatchService/DispatchService.csproj", "src/DispatchService/"]
RUN dotnet restore "src/DispatchService/DispatchService.csproj" --arch $TARGETARCH

COPY . .
WORKDIR "/src/src/DispatchService"
RUN dotnet publish "DispatchService.csproj" --configuration Release --output /app/publish --no-restore --arch $TARGETARCH /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Staging

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DispatchService.dll"]
