# Alfred Gateway Makefile - Development & Production

# Variables
PROJECT_NAME = Alfred.Gateway
STARTUP = src/$(PROJECT_NAME)
DOCKER_IMAGE = alfred-gateway
DOCKER_TAG = latest

.PHONY: help restore build run watch test clean docker-build docker-build-nc docker-clean

# Default target
help:
	@echo "======================================"
	@echo "Alfred Gateway - Available Commands"
	@echo "======================================"
	@echo ""
	@echo "🏗️  Build & Run:"
	@echo "  make restore           Restore NuGet packages"
	@echo "  make build             Build the project"
	@echo "  make run               Run the gateway"
	@echo "  make watch             Run with hot reload"
	@echo "  make test              Run tests"
	@echo "  make clean             Clean build artifacts"
	@echo ""
	@echo "🐳 Docker:"
	@echo "  make docker-build      Build Docker image"
	@echo "  make docker-build-nc   Build Docker image (no cache)"
	@echo "  make docker-clean      Remove old images"
	@echo ""
	@echo "🚀 Production:"
	@echo "  make prod-deploy       Build image for production deployment"
	@echo "  Use 'cd ../alfred-infra && make prod-*' to manage production services"

# Restore dependencies
restore:
	@echo "🔄 Restoring NuGet packages..."
	dotnet restore
	@echo "✅ Restore complete!"

# Build project
build: restore
	@echo "🔨 Building $(PROJECT_NAME)..."
	dotnet build --configuration Release --no-restore
	@echo "✅ Build complete!"

# Run application
run:
	@echo "🚀 Running $(PROJECT_NAME)..."
	dotnet run --project "$(STARTUP)"

# Run with hot reload
watch:
	@echo "👀 Running $(PROJECT_NAME) with hot reload..."
	dotnet watch --project "$(STARTUP)"

# Run tests (currently no test project for gateway)
test:
	@echo "🧪 Running tests..."
	dotnet test
	@echo "✅ Tests complete!"

# Clean build artifacts
clean:
	@echo "🧹 Cleaning build artifacts..."
	dotnet clean
	@find . -type d -name "bin" -o -type d -name "obj" | xargs rm -rf 2>/dev/null || true
	@echo "✅ Clean complete!"

# Build Docker image
docker-build:
	@echo "🐳 Building Docker image: $(DOCKER_IMAGE):$(DOCKER_TAG)..."
	docker build -t $(DOCKER_IMAGE):$(DOCKER_TAG) .
	@echo "✅ Docker image built: $(DOCKER_IMAGE):$(DOCKER_TAG)"

# Build Docker image (no cache)
docker-build-nc:
	@echo "🐳 Building Docker image (no cache): $(DOCKER_IMAGE):$(DOCKER_TAG)..."
	docker build --no-cache -t $(DOCKER_IMAGE):$(DOCKER_TAG) .
	@echo "✅ Docker image built: $(DOCKER_IMAGE):$(DOCKER_TAG)"

# Remove old image
docker-clean:
	@echo "🧹 Cleaning Docker images..."
	@docker rmi $(DOCKER_IMAGE):$(DOCKER_TAG) 2>/dev/null || true
	@docker system prune -f
	@echo "✅ Cleanup complete!"

# Production: build image then redirect to alfred-infra
prod-deploy: docker-build-nc
	@echo "🚀 Docker image built. To deploy, run:"
	@echo "  cd ../alfred-infra && make prod-deploy"
	@echo "✅ Image ready for deployment!"
