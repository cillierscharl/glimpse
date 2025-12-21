# Glimpse

Never lose a screenshot again. Watches, indexes, and lets you search your screen captures by text content.

![Glimpse Screenshot](images/image.png)

## Quick Start

```bash
git clone https://github.com/cillierscharl/glimpse.git
cd glimpse
SCREENSHOTS_PATH="$HOME/Pictures/Screenshots" docker compose up -d
```

Open http://localhost:5123

The first run downloads the minicpm-v vision model (~5GB) and starts indexing your screenshots.

## Features

- **Auto-watch** - Monitors your screenshot folder for new images
- **Local OCR** - Extracts text using minicpm-v vision model (fully offline)
- **Search** - Find screenshots by text content or date
- **Notes** - Add personal notes to screenshots
- **Real-time** - New screenshots are prioritized and indexed immediately

## How It Works

1. Watches your screenshot directory for new images
2. Extracts text using minicpm-v vision model (via Ollama)
3. Stores text and metadata in SQLite with full-text search
4. Web UI lets you search by content or date (e.g., "Nov 26", "error message")

## Privacy

All processing happens locally. Screenshots never leave your machine.
