import AppKit
import Foundation

struct IconSpec {
    let filename: String
    let size: Int
}

let arguments = CommandLine.arguments

guard arguments.count == 3 else {
    fputs("Usage: generate-favicons.swift <source-png> <output-dir>\n", stderr)
    exit(1)
}

let sourceURL = URL(fileURLWithPath: arguments[1])
let outputDirectoryURL = URL(fileURLWithPath: arguments[2], isDirectory: true)

let iconSpecs = [
    IconSpec(filename: "favicon-16x16.png", size: 16),
    IconSpec(filename: "favicon-32x32.png", size: 32),
    IconSpec(filename: "favicon-128x128.png", size: 128),
    IconSpec(filename: "apple-touch-icon.png", size: 180)
]

guard let sourceImage = NSImage(contentsOf: sourceURL) else {
    fputs("Could not load source image at \(sourceURL.path)\n", stderr)
    exit(1)
}

func renderSquareIcon(from image: NSImage, targetSize: Int) -> Data? {
    let canvasSize = NSSize(width: targetSize, height: targetSize)
    let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: targetSize,
        pixelsHigh: targetSize,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
    )

    guard let bitmap else {
        return nil
    }

    bitmap.size = canvasSize

    NSGraphicsContext.saveGraphicsState()
    guard let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
        NSGraphicsContext.restoreGraphicsState()
        return nil
    }

    NSGraphicsContext.current = context
    NSColor.clear.setFill()
    NSRect(origin: .zero, size: canvasSize).fill()

    let imageSize = image.size
    let scale = min(canvasSize.width / imageSize.width, canvasSize.height / imageSize.height)
    let scaledSize = NSSize(width: imageSize.width * scale, height: imageSize.height * scale)
    let drawRect = NSRect(
        x: (canvasSize.width - scaledSize.width) / 2,
        y: (canvasSize.height - scaledSize.height) / 2,
        width: scaledSize.width,
        height: scaledSize.height
    )

    image.draw(in: drawRect, from: .zero, operation: .sourceOver, fraction: 1)
    context.flushGraphics()
    NSGraphicsContext.restoreGraphicsState()

    return bitmap.representation(using: .png, properties: [:])
}

for spec in iconSpecs {
    guard let pngData = renderSquareIcon(from: sourceImage, targetSize: spec.size) else {
        fputs("Failed to render \(spec.filename)\n", stderr)
        exit(1)
    }

    let outputURL = outputDirectoryURL.appendingPathComponent(spec.filename)
    do {
        try pngData.write(to: outputURL, options: .atomic)
    } catch {
        fputs("Failed to write \(outputURL.path): \(error)\n", stderr)
        exit(1)
    }
}
