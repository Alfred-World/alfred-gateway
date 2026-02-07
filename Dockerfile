# ============================================
# Build Stage - Compile ứng dụng
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj và restore dependencies (tận dụng Docker layer caching)
COPY ["src/Alfred.Gateway/Alfred.Gateway.csproj", "Alfred.Gateway/"]
RUN dotnet restore "Alfred.Gateway/Alfred.Gateway.csproj"

# Copy toàn bộ source code
COPY src/ .

# Build ứng dụng
WORKDIR "/src/Alfred.Gateway"
RUN dotnet build "Alfred.Gateway.csproj" -c Release -o /app/build

# ============================================
# Publish Stage - Tạo artifact để deploy
# ============================================
FROM build AS publish
RUN dotnet publish "Alfred.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ============================================
# Final Stage - Image runtime siêu nhẹ
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Tạo non-root user để bảo mật
RUN addgroup --system --gid 1001 alfred && \
    adduser --system --uid 1001 --ingroup alfred alfred

# Copy artifact từ publish stage
COPY --from=publish /app/publish .

# Đổi ownership cho user alfred
RUN chown -R alfred:alfred /app

# Switch sang user alfred (không dùng root)
USER alfred

# Expose port
EXPOSE 8000
EXPOSE 8001

# Health check using wget (available by default in aspnet image)
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8000/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8000
ENV ASPNETCORE_ENVIRONMENT=Production

# Entry point
ENTRYPOINT ["dotnet", "Alfred.Gateway.dll"]
