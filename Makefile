# Makefile for Alfred Gateway

.PHONY: help build run test clean docker-build docker-run restore

# Variables
PROJECT_NAME = Alfred.Gateway
DOCKER_IMAGE = alfred-gateway
DOCKER_TAG = latest

# Default target
help:
	@echo "Alfred Gateway - Available Commands:"
	@echo "  make restore       - Restore NuGet packages"
	@echo "  make build         - Build the project"
	@echo "  make run           - Run the application"
	@echo "  make watch         - Run with hot reload"
	@echo "  make clean         - Clean build artifacts"
	@echo "  make docker-build  - Build Docker image"
	@echo "  make docker-run    - Run Docker container"
	@echo "  make docker-stop   - Stop Docker container"

# Restore dependencies
restore:
	@echo "Restoring NuGet packages..."
	dotnet restore

# Build project
build: restore
	@echo "Building $(PROJECT_NAME)..."
	dotnet build --configuration Release --no-restore

# Run application
run:
	@echo "Running $(PROJECT_NAME)..."
	cd src/$(PROJECT_NAME) && dotnet run

# Run with hot reload
watch:
	@echo "Running $(PROJECT_NAME) with hot reload..."
	cd src/$(PROJECT_NAME) && dotnet watch run

# Clean build artifacts
clean:
	@echo "Cleaning build artifacts..."
	dotnet clean
	find . -type d -name "bin" -exec rm -rf {} +
	find . -type d -name "obj" -exec rm -rf {} +

# Build Docker image
docker-build:
	@echo "Building Docker image..."
	docker build -t $(DOCKER_IMAGE):$(DOCKER_TAG) .

# Run Docker container
docker-run:
	@echo "Running Docker container..."
	docker-compose up -d

# Stop Docker container
docker-stop:
	@echo "Stopping Docker container..."
	docker-compose down

# Run in production mode
docker-prod:
	@echo "Running in production mode..."
	docker-compose -f docker-compose.prod.yml up -d
