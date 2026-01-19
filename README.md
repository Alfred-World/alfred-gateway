# 🚀 Alfred API Gateway

**Enterprise-grade API Gateway** cho hệ thống A.L.F.R.E.D sử dụng [YARP (Yet Another Reverse Proxy)](https://microsoft.github.io/reverse-proxy/) trên .NET 8.

## 📋 Tổng quan

Alfred Gateway là điểm vào duy nhất (Single Entry Point) cho toàn bộ hệ thống microservices/modular monolith của A.L.F.R.E.D. Gateway đảm nhiệm các chức năng:

- ✅ **Routing & Load Balancing** - Điều hướng request tới đúng service
- 🔒 **Authentication & Authorization** - Xác thực JWT Token tại cổng
- 🛡️ **Rate Limiting** - Chống spam và DDoS
- 🌐 **CORS Management** - Quản lý Cross-Origin requests
- 📊 **Health Checks** - Monitoring trạng thái các service
- 🐛 **Global Error Handling** - Xử lý lỗi tập trung

---

## 🏗️ Kiến trúc

```
alfred-gateway/
├── src/
│   └── Alfred.Gateway/
│       ├── Configurations/          # YARP routing configuration
│       │   └── yarp.json           # Route & Cluster definitions
│       │
│       ├── Extensions/              # Service registration extensions
│       │   ├── YarpExtensions.cs   # YARP & Rate Limiting
│       │   ├── AuthExtensions.cs   # JWT Authentication
│       │   └── CorsExtensions.cs   # CORS policies
│       │
│       ├── Middlewares/            # Custom middlewares
│       │   └── GlobalExceptionMiddleware.cs
│       │
│       ├── appsettings.json        # Application settings
│       └── Program.cs              # Entry point
│
├── Dockerfile                       # Production-ready container
├── docker-compose.yml              # Development environment
└── Makefile                        # Common commands
```

---

## 🚀 Quick Start

### Yêu cầu hệ thống

- .NET 8.0 SDK hoặc cao hơn
- Docker & Docker Compose (optional)

### 1. Restore & Build

```bash
# Sử dụng Makefile
make restore
make build

# Hoặc dùng dotnet CLI
dotnet restore
dotnet build
```

### 2. Chạy ứng dụng

```bash
# Development mode với hot reload
make watch

# Hoặc chạy thông thường
make run

# Hoặc dotnet CLI
cd src/Alfred.Gateway
dotnet run
```

Gateway sẽ chạy tại: **http://localhost:5000**

### 3. Chạy với Docker

```bash
# Build Docker image
make docker-build

# Run với docker-compose
make docker-run

# Stop
make docker-stop
```

---

## ⚙️ Cấu hình

### 1. YARP Routes (`Configurations/yarp.json`)

Định nghĩa các route và cluster đích:

```json
{
  "ReverseProxy": {
    "Routes": {
      "identity-route": {
        "ClusterId": "identity-cluster",
        "AuthorizationPolicy": "Anonymous",
        "Match": {
          "Path": "/auth/{**remainder}"
        }
      },
      "core-route": {
        "ClusterId": "core-cluster",
        "AuthorizationPolicy": "Authenticated",
        "RateLimiterPolicy": "fixed-window",
        "Match": {
          "Path": "/api/{**remainder}"
        }
      }
    },
    "Clusters": {
      "identity-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://localhost:5001"
          }
        }
      },
      "core-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://localhost:5002"
          }
        }
      }
    }
  }
}
```

### 2. Application Settings (`appsettings.json`)

```json
{
  "Auth": {
    "Authority": "http://localhost:5001",
    "ValidIssuer": "Alfred.Identity",
    "RequireHttpsMetadata": false
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:5173"
    ]
  },
  "RateLimit": {
    "Window": "00:01:00",
    "PermitLimit": 100,
    "QueueLimit": 2
  }
}
```

---

## 🔒 Authentication Flow

```
┌─────────┐           ┌─────────┐           ┌──────────┐
│ Client  │──────────▶│ Gateway │──────────▶│ Identity │
│         │  Request  │         │  Verify   │ Service  │
│         │           │         │  Token    │          │
└─────────┘           └─────────┘           └──────────┘
                           │
                           │ ✅ Token Valid
                           ▼
                      ┌─────────┐
                      │  Core   │
                      │ Service │
                      └─────────┘
```

1. Client gửi request kèm JWT Token trong header `Authorization: Bearer <token>`
2. Gateway verify token signature và claims
3. Nếu hợp lệ, forward request tới service backend
4. Nếu không hợp lệ, trả về 401 Unauthorized

---

## 🛠️ Development

### Available Commands

```bash
make help           # Show all available commands
make restore        # Restore NuGet packages
make build          # Build project
make run            # Run application
make watch          # Run with hot reload
make clean          # Clean build artifacts
make docker-build   # Build Docker image
make docker-run     # Run with Docker Compose
```

### Testing Endpoints

```bash
# Check Gateway health
curl http://localhost:5000/health

# Gateway info
curl http://localhost:5000/

# Test authentication (cần có token)
curl -H "Authorization: Bearer <your-token>" \
     http://localhost:5000/api/users
```

---

## 📦 Docker Deployment

### Development

```bash
docker-compose up -d
```

### Production

```bash
docker-compose -f docker-compose.prod.yml up -d
```

---

## 🔧 Mở rộng

### Thêm Route mới

Chỉnh sửa file `Configurations/yarp.json`:

```json
{
  "Routes": {
    "new-service-route": {
      "ClusterId": "new-cluster",
      "AuthorizationPolicy": "Authenticated",
      "Match": {
        "Path": "/new-service/{**remainder}"
      }
    }
  },
  "Clusters": {
    "new-cluster": {
      "Destinations": {
        "destination1": {
          "Address": "http://new-service:8080"
        }
      }
    }
  }
}
```

### Thêm Authorization Policy

Chỉnh sửa `Extensions/AuthExtensions.cs`:

```csharp
options.AddPolicy("CustomPolicy", policy => 
    policy.RequireClaim("permission", "special-access"));
```

---

## 📚 Tài liệu tham khảo

- [YARP Documentation](https://microsoft.github.io/reverse-proxy/)
- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [JWT Authentication](https://jwt.io/)

---

## 👨‍💻 Maintainer

**Alfred Development Team**

---

## 📝 License

Private - A.L.F.R.E.D System