# Glimpse

Never lose a screenshot again. Watches, indexes, and lets you search your screen captures by text content.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?style=flat-square&logo=docker)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)

![Glimpse Screenshot](images/image.png)

## Features

- 📁 **Auto-watch** - Monitors your screenshot folder for new images
- 🔍 **Local Vision OCR** - Extracts text using Ollama (fully offline, no cloud)
- 📅 **Date Search** - Search by date (e.g., "Nov 26", "2024-11-26")
- 📋 **Copy to Clipboard** - Copy screenshots or extracted text

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

The first run will download the Ollama vision model (~5GB) and start indexing your screenshots.

### GPU Acceleration (NVIDIA)

For much faster OCR processing, enable GPU support:

```bash
# Install NVIDIA Container Toolkit
./scripts/install-nvidia-docker.sh

# Restart containers
docker compose down && docker compose up -d
```

### Configuration

Set `SCREENSHOTS_PATH` to the folder you want to watch:

```bash
SCREENSHOTS_PATH="/path/to/your/screenshots" docker compose up -d
```

## How It Works

1. **Watch** - Monitors your screenshot directory for new images
2. **OCR** - Images processed with local Ollama vision model (minicpm-v)
3. **Index** - Text and metadata stored in SQLite database
4. **Search** - Web UI lets you search by text content or date

## Tech Stack

- .NET 10 / ASP.NET Core MVC
- SQLite + Entity Framework Core  
- Ollama (minicpm-v vision model)
- Docker

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
├── scripts/             # Setup scripts
├── docker-compose.yml
├── Dockerfile
└── README.md
```

## Privacy

All processing happens locally on your machine. Screenshots never leave your computer - the Ollama vision model runs entirely offline.

## License

MIT
