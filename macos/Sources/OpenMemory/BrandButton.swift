import AppKit

@MainActor final class BrandButton: NSButton {
    var primary = false
    override func draw(_ dirtyRect: NSRect) {
        let blue = NSColor(calibratedRed: 0, green: 0.435, blue: 1, alpha: 1)
        let path = NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 9, yRadius: 9)
        let fill = !isEnabled ? NSColor(calibratedWhite: 0.93, alpha: 1) : primary ? blue : NSColor.white
        fill.setFill(); path.fill()
        (primary && isEnabled ? blue : NSColor(calibratedWhite: 0.81, alpha: 1)).setStroke(); path.lineWidth = 1; path.stroke()
        let color = !isEnabled ? NSColor.secondaryLabelColor : primary ? NSColor.white : NSColor.labelColor
        let text = NSAttributedString(string: title, attributes: [.font: NSFont.systemFont(ofSize: 14, weight: .medium), .foregroundColor: color])
        let size = text.size(); text.draw(at: NSPoint(x: (bounds.width - size.width) / 2, y: (bounds.height - size.height) / 2))
    }
}
