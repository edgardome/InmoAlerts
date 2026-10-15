# InmoAlerts 🏠🔔

### Real-Time Idealista Scraper & Automated Alert Engine

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-239120?logo=c-sharp&logoColor=white)](https://docs.microsoft.com/dotnet/csharp/)
[![Playwright](https://img.shields.io/badge/Playwright-Automated_Scraping-2EAD33?logo=playwright&logoColor=white)](https://playwright.dev/)
[![SQLite](https://img.shields.io/badge/SQLite-Deduplication_%26_Cache-003B57?logo=sqlite&logoColor=white)](https://www.sqlite.org/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker&logoColor=white)](https://www.docker.com/)

**InmoAlerts** is an automated real estate monitoring and instant notification engine built with **.NET 8** and **Playwright**. It continuously scans **Idealista**, detects newly published listings and price drops across configured geographic zones, filters properties according to custom criteria, and broadcasts instant alerts via **Telegram** and **Email (SMTP)**.

---

## 🚀 Key Features

* **Anti-Bot Resilient Idealista Scraping:** Uses **Microsoft Playwright** browser automation and **HtmlAgilityPack** to reliably navigate and extract property data, evading anti-scraping defenses and dynamic client-side rendering.
* **Background Worker Service:** Runs continuously as an asynchronous `BackgroundService` (`IHostedService`), managing scheduling cycles, configurable intervals, exponential backoff retries, and rate limiting.
* **Instant Multi-Channel Alerts:**
  * **Telegram Bot:** Real-time push messages with price, pictures, location, and direct link to the Idealista ad.
  * **Email (SMTP):** Formatted HTML digest messages with comprehensive listing details.
* **Smart Deduplication & Cache:** Embedded **SQLite** database stores visited properties to eliminate duplicate alerts and track price variation history.
* **Live Web Dashboard:** Integrated web interface served via ASP.NET Core (`wwwroot`) displaying execution logs, scraper health metrics, and cycle status in real time.
* **Containerized Deployment:** Includes an optimized multi-stage `Dockerfile` ready for 24/7 cloud deployment.

---

## 🏗️ Architecture & Project Structure

```
InmoAlerts/
├── Configuration/              # Strongly-typed configuration options (Email, Telegram, Scraper, Dashboard)
│   ├── DashboardSettings.cs
│   ├── DatabaseSettings.cs
│   ├── EmailSettings.cs
│   ├── ScraperSettings.cs
│   └── TelegramSettings.cs
├── Helpers/                    # Utility classes and logging formatters
├── Models/                     # Core domain entities (Property, Location, LocationsConfig, AppUser)
│   ├── AppUser.cs
│   ├── Location.cs
│   ├── LocationsConfig.cs
│   └── Property.cs
├── Services/                   # Business logic and background processes
│   ├── LogService.cs                # Thread-safe log collection for UI/Dashboard
│   ├── PropertyService.cs           # SQLite persistence, queries, and deduplication
│   ├── ScraperBackgroundService.cs  # Main Idealista polling loop & lifecycle
│   └── ScraperState.cs              # Real-time state metrics for observability
├── Resources/
│   └── locations.json          # Target search URLs, coordinates, and filters
├── wwwroot/                    # Web dashboard frontend (HTML/CSS/JS)
├── Dockerfile                  # Multi-stage container definition
├── Program.cs                  # ASP.NET Core entry point, DI container & pipeline
└── appsettings.json            # Application configuration template
```

---

## 🛠️ Tech Stack

* **Runtime:** .NET 8 (C#)
* **Web Framework:** ASP.NET Core Web API / Minimal Endpoints
* **Browser Automation:** `Microsoft.Playwright` (v1.58.0)
* **HTML Parsing:** `HtmlAgilityPack`
* **Persistence:** `Microsoft.Data.Sqlite.Core` / SQLite
* **HTTP Client:** `RestSharp` & `HttpClient`
* **Serialization:** `Newtonsoft.Json`

---

## ⚙️ Configuration

Copy or update `appsettings.json` with your credentials:

```json
{
  "EmailSettings": {
    "SmtpServer": "smtp.gmail.com",
    "Port": 587,
    "Username": "your_email@gmail.com",
    "Password": "YOUR_GMAIL_APP_PASSWORD",
    "FromEmail": "your_email@gmail.com",
    "Recipients": [
      "your_email@gmail.com"
    ]
  },
  "TelegramSettings": {
    "BotToken": "YOUR_TELEGRAM_BOT_TOKEN"
  },
  "ScraperSettings": {
    "DelayBetweenRequests": 2000,
    "MaxConcurrentRequests": 5,
    "MinutesBetweenCycles": 5,
    "MaxRetries": 3,
    "TimeoutSeconds": 30,
    "AlertVenta": true,
    "AlertAlquiler": true
  }
}
```

---

## 📦 Getting Started

### 1. Prerequisites
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [PowerShell / Terminal]

### 2. Run Locally

```bash
# Clone the repository
git clone https://github.com/edgardome/InmoAlerts.git
cd InmoAlerts

# Restore dependencies
dotnet restore InmoAlerts.sln

# Install Playwright browser dependencies (first run only)
pwsh bin/Debug/net8.0/playwright.ps1 install

# Run application
dotnet run --project InmoAlerts.csproj
```

Open your browser at `http://localhost:5000` to view the **Live Scraper Dashboard**.

---

### 3. Run with Docker 🐳

```bash
# Build Docker image
docker build -t inmoalerts .

# Run container
docker run -d -p 5000:5000 --name inmoalerts-runner inmoalerts
```

---

## 📄 License
This project is open-source and available under the [MIT License](LICENSE).
