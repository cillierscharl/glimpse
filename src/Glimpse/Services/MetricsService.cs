using Prometheus;

namespace Glimpse.Services;

public static class GlimpseMetrics
{
    public static readonly Counter ScreenshotsDetected = Metrics
        .CreateCounter("glimpse_screenshots_detected_total", "Total screenshots detected by watcher");

    public static readonly Counter ScreenshotsProcessed = Metrics
        .CreateCounter("glimpse_screenshots_processed_total", "Total screenshots processed by OCR",
            new CounterConfiguration { LabelNames = ["status"] });

    public static readonly Gauge ScreenshotsTotal = Metrics
        .CreateGauge("glimpse_screenshots_total", "Total screenshots in database");

    public static readonly Gauge ScreenshotsPending = Metrics
        .CreateGauge("glimpse_screenshots_pending", "Screenshots pending OCR");

    public static readonly Histogram OcrDuration = Metrics
        .CreateHistogram("glimpse_ocr_duration_seconds", "OCR processing duration",
            new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(1, 2, 8) }); // 1s to 128s

    public static readonly Gauge QueueSize = Metrics
        .CreateGauge("glimpse_queue_size", "Current OCR queue size",
            new GaugeConfiguration { LabelNames = ["queue"] });
}
