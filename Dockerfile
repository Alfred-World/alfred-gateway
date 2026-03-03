# ============================================
# Build Stage
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY ["src/Alfred.Gateway/Alfred.Gateway.csproj", "Alfred.Gateway/"]
RUN --mount=type=cache,id=nuget-gateway,target=/root/.nuget/packages \
    dotnet restore "Alfred.Gateway/Alfred.Gateway.csproj"

COPY src/ .

# ============================================
# Publish Stage (publish already compiles — no separate build step needed)
# ============================================
FROM build AS publish
WORKDIR "/src/Alfred.Gateway"
RUN --mount=type=cache,id=nuget-gateway,target=/root/.nuget/packages \
    dotnet publish "Alfred.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ============================================
# Final Stage
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

RUN apk --no-cache add curl libgcc libstdc++ icu-libs

RUN addgroup -S -g 1001 alfred && adduser -S -u 1001 -G alfred -H alfred

COPY --from=publish --chown=alfred:alfred /app/publish .

USER alfred

EXPOSE 8000
EXPOSE 8001

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8000/health || exit 1

ENV ASPNETCORE_URLS=http://+:8000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Alfred.Gateway.dll"]
