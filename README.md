# Glimpse

Never lose a screenshot again. Watches, indexes, and lets you search your screen captures by text content.

![Glimpse Screenshot Search](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)
![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?style=flat-square&logo=docker)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)

## Features

- 📁 **Auto-watch** - Monitors your screenshot folder for new images
- 🔍 **OCR Search** - Extracts text from images using Tesseract and makes it searchable
- ⚡ **Instant Results** - Full-text search across all your screenshots
- 📋 **One-click Copy** - Copy any screenshot to clipboard directly from the browser
- 🎨 **Modern UI** - Clean, dark-themed interface built with Tailwind CSS

## Quick Start

### Using Docker (Recommended)

```bash
# Clone the repository
git clone https://github.com/yourusername/glimpse.git
cd glimpse

# Run with Docker Compose
SCREENSHOTS_PATH="$HOME/Pictures/Screenshots" docker compose up -d

# Open in browser
open http://localhost:5123
```

### Configuration

Set `SCREENSHOTS_PATH` to the folder you want to watch:

```bash
SCREENSHOTS_PATH="/path/to/your/screenshots" docker compose up -d
```

## How It Works

1. **Watch** - Monitors your screenshot directory using FileSystemWatcher
2. **OCR** - New images are processed with Tesseract to extract text
3. **Index** - Text and metadata stored in SQLite database
4. **Search** - Web UI lets you search by any text in your screenshots

## Tech Stack

- **.NET 8** - ASP.NET Core MVC
- **SQLite** - Local database with Entity Framework Core
- **Tesseract** - OCR engine for text extraction
- **Tailwind CSS** - Modern styling via CDN
- **Docker** - Containerized deployment

## Project Structure

```
glimpse/
├── src/Glimpse/
│   ├── Controllers/     # MVC controllers
│   ├── Data/            # EF Core DbContext
│   ├── Models/          # Data models
│   ├── Services/        # Background services (OCR, file watcher)
│   ├── Views/           # Razor views
│   └── Program.cs       # Application entry point
├── docker-compose.yml
├── Dockerfile
└── README.md
```

## Development

```bash
# Run locally (requires Tesseract installed)
cd src/Glimpse
dotnet run

# Build Docker image
docker compose build
```

## License

MIT
